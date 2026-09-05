import {
  canvasAlignment,
  cssColor,
  drawGridLines,
  drawOperation,
} from "../../js/circuit-scene/drawing.js";
import {
  contains,
  expandRect,
  gestureMoved,
  gridPoint,
  gridToWorld,
  hitPriority,
  intersects,
  netFromHit,
  orthogonalDragRoute,
  rectFromPoints,
  selectionModeFromModifiers,
  terminalFromSource,
  terminalWireRoutes,
  translateComponentPlacement,
  translateGridPoint,
  translateRect,
  validPoint,
} from "../../js/circuit-scene/geometry.js";
import {
  BrowserPolicyError,
  browserPolicyDimensionTokens,
  buildSourceIndex,
  buildSpatialIndex,
  compareOrdinal,
  decodeBase64,
  deepFreeze,
  encodedJsonBytes,
  interopEnvelopeBytes,
  isAlignment,
  isDigest,
  isDirection,
  isLocale,
  isTextRole,
  isToken,
  packagedFontSupports,
  sourceKey,
  spatialCellKey,
  validTool,
  validatePatch,
  validatePolicy,
  validateRecoveryState,
  validateReplacement,
} from "../../js/circuit-scene/protocol.js";

const mountedHandles = new WeakMap();
const textEncoder = new TextEncoder();
const contextRestoreTimeoutMilliseconds = 2_000;
const maximumAutomaticGridStepCssPixels = 16;

export function mount(host, buildFingerprint, policy, dotnetSink, recoveryState = null) {
  const existing = mountedHandles.get(host);
  if (existing && existing.buildFingerprint === buildFingerprint && !existing.destroyed) {
    return existing;
  }

  existing?.destroy();
  const handle = new CircuitSceneHandle(
    host,
    buildFingerprint,
    validatePolicy(policy),
    dotnetSink,
    recoveryState,
  );
  mountedHandles.set(host, handle);
  return handle;
}

class CircuitSceneHandle {
  constructor(host, buildFingerprint, policy, dotnetSink, recoveryState) {
    this.host = host;
    this.buildFingerprint = buildFingerprint;
    this.policy = policy;
    this.dotnetSink = dotnetSink;
    this.canvas = host.querySelector("[data-scene-canvas]");
    this.context = this.canvas?.getContext("2d", { alpha: false }) ?? null;
    this.published = null;
    this.spatialIndex = new Map();
    this.sourcesByKey = new Map();
    this.targetsBySource = new Map();
    this.viewport = { x: 0, y: 0, zoom: 1 };
    this.savedViewports = validateRecoveryState(recoveryState, policy);
    this.viewportIsUserControlled = false;
    this.primarySelectionSource = null;
    this.hoveredSource = null;
    this.selectedSources = new Set();
    this.gesture = null;
    this.activeTool = Object.freeze({ kind: "select" });
    this.activeToolKey = '{"kind":"select"}';
    this.pendingIntent = null;
    this.spacePan = false;
    this.connected = true;
    this.pendingFrame = 0;
    this.dirty = false;
    this.cssWidth = 0;
    this.cssHeight = 0;
    this.density = 1;
    this.fontFingerprint = null;
    this.symbolFontFamily = null;
    this.transfers = new Map();
    this.destroyed = false;
    this.abortController = new AbortController();
    this.resizeObserver = null;
    this.densityMedia = null;
    this.removalObserver = null;
    this.contextIsLost = false;
    this.contextRestoreTimer = 0;
    this.installRemovalObserver();

    if (!this.canvas || !this.context) {
      this.failClosed();
      void this.notifyFailure("contextUnavailable");
      return;
    }

    this.installListeners();
    this.installRenderObservers();
    this.resize();
  }

  async measureText(requests) {
    this.ensureLive();
    if (!Array.isArray(requests)) {
      throw new Error("invalid text measurement request batch");
    }

    const seen = new Set();
    for (const request of requests) {
      if (
        !request ||
        typeof request.key !== "string" ||
        seen.has(request.key) ||
        typeof request.text !== "string" ||
        !isTextRole(request.fontRole) ||
        !isAlignment(request.alignment) ||
        !isLocale(request.locale) ||
        !isDirection(request.direction)
      ) {
        throw new Error("invalid text measurement request");
      }
      seen.add(request.key);
    }

    if (requests.some((request) => !packagedFontSupports(request.text))) {
      this.failClosed();
      await this.notifyFailure("fontUnavailable");
      throw new Error("the packaged symbol font does not cover the requested text");
    }

    const styles = getComputedStyle(this.canvas);
    const family = styles.getPropertyValue("--ll-scene-font-family").trim();
    const assetFingerprint = styles.getPropertyValue("--ll-scene-font-asset").trim();
    if (family !== "Atkinson Hyperlegible Next" || !isDigest(assetFingerprint)) {
      this.failClosed();
      await this.notifyFailure("assetFingerprintMismatch");
      throw new Error("symbol font asset fingerprint is invalid");
    }

    const font = `400 100px "${family}"`;
    let faces;
    try {
      faces = await document.fonts.load(font, "Ag0");
    } catch {
      faces = [];
    }
    const exactFaceLoaded = faces.some(
      (face) => face.status === "loaded" && face.family.replaceAll('"', "") === family,
    );
    // FontFaceSet.check() only reports whether a future load/font swap is needed; it
    // explicitly may return true when fallback renders the text, so it cannot prove
    // glyph coverage. https://www.w3.org/TR/css-font-loading/#font-face-set-check
    if (!exactFaceLoaded) {
      this.failClosed();
      await this.notifyFailure("fontUnavailable");
      throw new Error("symbol font is unavailable");
    }
    this.symbolFontFamily = `"${family}"`;

    const measurements = [];
    for (const request of requests) {
      this.context.save();
      this.context.font = `100px ${this.symbolFontFamily}`;
      this.context.textAlign = canvasAlignment(request.alignment, request.direction);
      this.context.direction = request.direction;
      const metrics = this.context.measureText(request.text);
      this.context.restore();
      const measurement = {
        key: request.key,
        advanceWidth: checkedInteger(Math.ceil(metrics.width)),
        inkLeft: checkedInteger(Math.floor(-metrics.actualBoundingBoxLeft)),
        inkTop: checkedInteger(Math.floor(-metrics.actualBoundingBoxAscent)),
        inkRight: checkedInteger(Math.ceil(metrics.actualBoundingBoxRight)),
        inkBottom: checkedInteger(Math.ceil(metrics.actualBoundingBoxDescent)),
      };
      if (
        measurement.advanceWidth < 0 ||
        measurement.inkRight < measurement.inkLeft ||
        measurement.inkBottom < measurement.inkTop
      ) {
        throw new Error("invalid text metrics");
      }

      measurements.push(measurement);
    }

    const canonical = measurements
      .slice()
      .sort((left, right) => compareOrdinal(left.key, right.key))
      .map(
        (value) =>
          `${value.key}:${value.advanceWidth}:${value.inkLeft}:${value.inkTop}:${value.inkRight}:${value.inkBottom}`,
      )
      .join("\n");
    const fontFingerprint = await sha256(
      textEncoder.encode(`logiclab-browser-font-v1\n${family}\n${assetFingerprint}\n${canonical}`),
    );
    return { fontFamily: family, assetFingerprint, fontFingerprint, measurements };
  }

  commitTextMeasurements(fontFingerprint) {
    this.ensureLive();
    if (!isDigest(fontFingerprint)) {
      throw new Error("invalid text measurement fingerprint");
    }
    this.fontFingerprint = fontFingerprint;
  }

  beginTransfer(transferId, kind, byteLength, digest) {
    this.ensureLive();
    if (
      !isToken(transferId) ||
      !["replacement", "patch"].includes(kind) ||
      !Number.isSafeInteger(byteLength) ||
      byteLength <= 0 ||
      BigInt(byteLength) > BigInt(this.policy.candidateTransferBytes) ||
      !isDigest(digest) ||
      this.transfers.has(transferId)
    ) {
      this.rejectBatch("invalid scene transfer envelope");
    }

    this.transfers.set(transferId, {
      kind,
      byteLength,
      digest,
      nextOrdinal: 0,
      received: 0,
      chunks: [],
    });
  }

  appendTransfer(transferId, ordinal, base64Chunk) {
    this.ensureLive();
    const transfer = this.transfers.get(transferId);
    if (
      !transfer || transfer.committing ||
      ordinal !== transfer.nextOrdinal || typeof base64Chunk !== "string"
    ) {
      this.transfers.delete(transferId);
      this.rejectBatch("invalid scene transfer batch");
    }
    if (
      BigInt(textEncoder.encode(base64Chunk).byteLength) + interopEnvelopeBytes >
      BigInt(this.policy.interopBatchBytes)
    ) {
      this.transfers.delete(transferId);
      this.rejectBatch("scene transfer batch policy exhausted");
    }

    let chunk;
    try {
      chunk = decodeBase64(base64Chunk);
    } catch {
      this.transfers.delete(transferId);
      this.rejectBatch("invalid scene transfer encoding");
    }
    transfer.received += chunk.byteLength;
    if (transfer.received > transfer.byteLength) {
      this.transfers.delete(transferId);
      this.rejectBatch("scene transfer exceeds its envelope");
    }

    transfer.chunks.push(chunk);
    transfer.nextOrdinal++;
  }

  async commitTransfer(transferId) {
    this.ensureLive();
    const transfer = this.transfers.get(transferId);
    if (transfer?.committing) return false;
    if (!transfer || transfer.received !== transfer.byteLength) {
      this.transfers.delete(transferId);
      await this.rejectCandidate("invalidBatch");
      return false;
    }
    // Keep the candidate registered while hashing so abort/destroy can cancel it.
    transfer.committing = true;

    const bytes = new Uint8Array(transfer.byteLength);
    let offset = 0;
    for (const chunk of transfer.chunks) {
      bytes.set(chunk, offset);
      offset += chunk.byteLength;
    }

    let candidate;
    try {
      const digest = await sha256(bytes);
      if (this.transfers.get(transferId) !== transfer) return false;
      if (digest !== transfer.digest) {
        throw new Error("scene transfer digest mismatch");
      }

      candidate = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes));
    } catch {
      if (this.transfers.get(transferId) !== transfer) return false;
      await this.rejectCandidate("invalidBatch");
      return false;
    } finally {
      if (this.transfers.get(transferId) === transfer) this.transfers.delete(transferId);
    }

    if (candidate?.buildFingerprint !== this.buildFingerprint) {
      await this.dotnetSink?.invokeMethodAsync("SceneBuildMismatchAsync").catch(() => {});
      this.destroy();
      return false;
    }

    try {
      if (transfer.kind === "replacement") {
        this.replace(candidate);
      } else {
        const replacement = validatePatch(
          candidate,
          this.published,
          this.buildFingerprint,
          this.fontFingerprint,
          this.policy,
        );
        if (!replacement) {
          throw new Error("invalid scene patch");
        }
        this.replace(replacement);
      }
      return true;
    } catch (error) {
      if (error instanceof BrowserPolicyError) {
        await this.reportPolicyFailure(error);
        return false;
      }
      await this.rejectCandidate(transfer.kind === "patch" ? "invalidPatch" : "invalidSnapshot");
      return false;
    }
  }

  abortTransfer(transferId) {
    this.transfers.delete(transferId);
  }

  rejectBatch(message) {
    void this.rejectCandidate("invalidBatch");
    throw new Error(message);
  }

  setConnected(isConnected) {
    this.ensureLive();
    this.connected = Boolean(isConnected);
    if (!this.connected && this.gesture?.tool?.kind !== "pan") {
      this.cancelGesture();
    }
  }

  setTool(tool) {
    this.ensureLive();
    if (!validTool(tool)) {
      throw new Error("invalid Scene tool");
    }

    const key = JSON.stringify(tool);
    if (key === this.activeToolKey) {
      return;
    }

    this.cancelGesture();
    this.activeTool = deepFreeze(structuredClone(tool));
    this.activeToolKey = key;
  }

  captureRecoveryState() {
    this.ensureLive();
    this.rememberPublishedViewport();
    const saved = [...this.savedViewports];
    const viewports = [];
    let encodedBytes = encodedJsonBytes({ viewports });
    for (
      let index = saved.length - 1;
      index >= 0 && BigInt(viewports.length) < BigInt(this.policy.sceneSnapshotRecordCount);
      index--
    ) {
      const [circuitDefinitionId, viewport] = saved[index];
      const candidate = {
        circuitDefinitionId,
        translateX: viewport.x,
        translateY: viewport.y,
        zoom: viewport.zoom,
      };
      const separatorBytes = viewports.length === 0 ? 0n : 1n;
      const nextBytes = encodedBytes + separatorBytes + encodedJsonBytes(candidate);
      if (nextBytes + interopEnvelopeBytes > BigInt(this.policy.interopBatchBytes)) {
        break;
      }
      viewports.push(candidate);
      encodedBytes = nextBytes;
    }
    viewports.reverse();
    return { viewports };
  }

  destroy() {
    if (this.destroyed) {
      return;
    }

    this.destroyed = true;
    this.cancelGesture();
    this.abortController.abort();
    this.resizeObserver?.disconnect();
    this.densityMedia?.removeEventListener("change", this.onDensityChange);
    this.removalObserver?.disconnect();
    if (this.pendingFrame) {
      cancelAnimationFrame(this.pendingFrame);
      this.pendingFrame = 0;
    }
    if (this.contextRestoreTimer) {
      clearTimeout(this.contextRestoreTimer);
      this.contextRestoreTimer = 0;
    }

    this.transfers.clear();
    this.savedViewports.clear();
    this.selectedSources.clear();
    this.primarySelectionSource = null;
    this.hoveredSource = null;
    this.published = null;
    this.spatialIndex.clear();
    this.sourcesByKey.clear();
    this.targetsBySource.clear();
    this.context = null;
    this.dotnetSink = null;
    if (mountedHandles.get(this.host) === this) {
      mountedHandles.delete(this.host);
    }
  }

  replace(candidate) {
    const validated = validateReplacement(
      candidate,
      this.buildFingerprint,
      this.fontFingerprint,
      this.policy,
    );
    const sourceIndex =
      validated.kind === "snapshot"
        ? buildSourceIndex(validated.value, this.policy)
        : { sourcesByKey: new Map(), targetsBySource: new Map(), observedBytes: 0n };
    const spatialIndex =
      validated.kind === "snapshot"
        ? buildSpatialIndex(validated.value, this.policy, sourceIndex.observedBytes)
        : new Map();
    this.cancelGesture();
    this.hoveredSource = null;
    if (
      this.pendingIntent &&
      (validated.value.projectionVersion !== this.pendingIntent.projectionVersion ||
        validated.value.sceneVersion > this.pendingIntent.sceneVersion)
    ) {
      this.pendingIntent = null;
    }
    this.rememberPublishedViewport();

    if (validated.kind === "unavailable") {
      this.published = null;
      this.spatialIndex = spatialIndex;
      this.sourcesByKey = sourceIndex.sourcesByKey;
      this.targetsBySource = sourceIndex.targetsBySource;
      this.primarySelectionSource = null;
      this.selectedSources.clear();
      this.clearCanvas();
      this.canvas.dataset.sceneLocalUnavailable = "";
      return;
    }

    this.published = validated.value;
    delete this.canvas.dataset.sceneLocalUnavailable;
    this.spatialIndex = spatialIndex;
    this.sourcesByKey = sourceIndex.sourcesByKey;
    this.targetsBySource = sourceIndex.targetsBySource;
    const saved = this.savedViewports.get(validated.value.circuitDefinitionId);
    if (saved) {
      this.viewport = { ...saved };
      this.viewportIsUserControlled = true;
    } else {
      this.fitViewport();
    }

    const selectionOverlays = validated.value.overlays.filter(
      (overlay) => overlay.kind === "selection",
    );
    this.selectedSources = new Set(selectionOverlays.map((overlay) => sourceKey(overlay.source)));
    const primary = selectionOverlays.find((overlay) => overlay.role === "primary");
    this.primarySelectionSource = primary ? sourceKey(primary.source) : null;
    this.invalidate();
  }

  apply(patch) {
    try {
      const candidate = validatePatch(
        patch,
        this.published,
        this.buildFingerprint,
        this.fontFingerprint,
        this.policy,
      );
      if (!candidate) {
        void this.rejectCandidate("invalidPatch");
        return;
      }

      this.replace(candidate);
    } catch (error) {
      if (error instanceof BrowserPolicyError) {
        void this.reportPolicyFailure(error);
        return;
      }
      throw error;
    }
  }

  installListeners() {
    const signal = this.abortController.signal;
    this.canvas.addEventListener("pointerdown", (event) => this.pointerDown(event), { signal });
    this.canvas.addEventListener("pointermove", (event) => this.pointerMove(event), { signal });
    this.canvas.addEventListener("pointerup", (event) => this.pointerUp(event), { signal });
    this.canvas.addEventListener("pointercancel", (event) => this.cancelPointer(event), { signal });
    this.canvas.addEventListener("lostpointercapture", (event) => this.cancelPointer(event), {
      signal,
    });
    this.canvas.addEventListener("pointerleave", () => this.clearHover(), { signal });
    this.canvas.addEventListener("wheel", (event) => this.wheel(event), { passive: false, signal });
    this.canvas.addEventListener("keydown", (event) => this.keyDown(event), { signal });
    document.addEventListener("keyup", (event) => this.keyUp(event), { signal });
    window.addEventListener(
      "blur",
      () => {
        this.spacePan = false;
        this.cancelGesture();
      },
      { signal },
    );
    this.canvas.addEventListener("contextlost", () => this.contextLost(), { signal });
    this.canvas.addEventListener("contextrestored", () => this.contextRestored(), { signal });
    for (const control of this.host.querySelectorAll("[data-scene-zoom]")) {
      control.addEventListener("click", () => this.zoomControl(control.dataset.sceneZoom), {
        signal,
      });
    }

    this.reconnectModal = document.getElementById("components-reconnect-modal");
    // https://learn.microsoft.com/aspnet/core/blazor/fundamentals/signalr?view=aspnetcore-10.0#reflect-the-server-side-connection-state-in-the-ui
    this.reconnectModal?.addEventListener(
      "components-reconnect-state-changed",
      (event) => this.reconnectStateChanged(event),
      { signal },
    );
  }

  installRenderObservers() {
    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(this.host);
    this.onDensityChange = () => {
      this.armDensityListener();
      this.resize();
    };
    this.armDensityListener();
  }

  installRemovalObserver() {
    // Blazor DOM cleanup guidance requires observing outside the component subtree:
    // https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0#dom-cleanup-tasks-during-component-disposal
    const stableAncestor = this.host.closest("[data-browser-host-ancestor]");
    if (stableAncestor) {
      this.removalObserver = new MutationObserver(() => {
        if (!this.host.isConnected) {
          this.destroy();
        }
      });
      this.removalObserver.observe(stableAncestor, { childList: true, subtree: true });
    }
  }

  armDensityListener() {
    this.densityMedia?.removeEventListener("change", this.onDensityChange);
    this.densityMedia = matchMedia(`(resolution: ${window.devicePixelRatio}dppx)`);
    this.densityMedia.addEventListener("change", this.onDensityChange, { once: true });
  }

  resize() {
    if (this.destroyed || !this.canvas || !this.context || this.contextIsLost) {
      return;
    }

    const rect = this.canvas.getBoundingClientRect();
    if (
      !Number.isFinite(rect.width) ||
      !Number.isFinite(rect.height) ||
      rect.width <= 0 ||
      rect.height <= 0
    ) {
      this.cssWidth = 0;
      this.cssHeight = 0;
      return;
    }

    const maximumDensity = Number(this.policy.effectiveDensityMillionths) / 1_000_000;
    const density = Math.min(Math.max(1, window.devicePixelRatio || 1), maximumDensity);
    const width = Math.ceil(rect.width * density);
    const height = Math.ceil(rect.height * density);
    const pixels = BigInt(width) * BigInt(height);
    if (pixels > BigInt(this.policy.canvasBitmapPixels)) {
      void this.reportPolicyFailure(new BrowserPolicyError("canvasBitmapPixels", pixels));
      return;
    }

    const selectionAnchor = this.viewportIsUserControlled ? this.selectionResizeAnchor() : null;
    const center =
      this.viewportIsUserControlled && !selectionAnchor && this.cssWidth > 0 && this.cssHeight > 0
        ? this.screenToWorld({ x: this.cssWidth / 2, y: this.cssHeight / 2 })
        : null;
    this.cssWidth = rect.width;
    this.cssHeight = rect.height;
    this.density = density;
    if (this.canvas.width !== width || this.canvas.height !== height) {
      this.canvas.width = width;
      this.canvas.height = height;
      this.context = this.canvas.getContext("2d", { alpha: false });
      if (!this.context) {
        this.failClosed();
        void this.notifyFailure("contextUnavailable");
        return;
      }
    }

    if (!this.viewportIsUserControlled && this.published) {
      this.fitViewport();
    } else if (selectionAnchor) {
      this.viewport.x = selectionAnchor.screen.x - selectionAnchor.world.x * this.viewport.zoom;
      this.viewport.y = selectionAnchor.screen.y - selectionAnchor.world.y * this.viewport.zoom;
    } else if (center) {
      this.viewport.x = this.cssWidth / 2 - center.x * this.viewport.zoom;
      this.viewport.y = this.cssHeight / 2 - center.y * this.viewport.zoom;
    }

    this.invalidate();
  }

  invalidate() {
    if (this.destroyed || this.contextIsLost) {
      return;
    }

    this.dirty = true;
    if (!this.pendingFrame) {
      this.pendingFrame = requestAnimationFrame(() => this.render());
    }
  }

  render() {
    this.pendingFrame = 0;
    if (
      this.destroyed ||
      this.contextIsLost ||
      !this.dirty ||
      !this.context ||
      !this.cssWidth ||
      !this.cssHeight
    ) {
      return;
    }

    this.dirty = false;
    const context = this.context;
    const styles = getComputedStyle(this.host);
    context.setTransform(1, 0, 0, 1, 0, 0);
    context.fillStyle = cssColor(styles, "--ll-canvas", "#ffffff");
    context.fillRect(0, 0, this.canvas.width, this.canvas.height);
    if (!this.published) {
      return;
    }

    context.setTransform(
      this.density * this.viewport.zoom,
      0,
      0,
      this.density * this.viewport.zoom,
      this.density * this.viewport.x,
      this.density * this.viewport.y,
    );
    const visible = expandRect(this.visibleWorldRect(), 4 / this.viewport.zoom);
    this.drawGrid(context, styles, visible);
    for (const item of this.published.items) {
      if (!item.hasDrawableTarget) {
        continue;
      }
      const bounds = translateRect(item.bounds, item.origin);
      if (!intersects(bounds, visible)) {
        continue;
      }

      context.save();
      context.translate(item.origin.x, item.origin.y);
      for (const operation of item.operations) {
        drawOperation(context, operation, styles, this.symbolFontFamily);
      }
      context.restore();
    }

    this.drawOverlays(context, styles);
    this.drawTransientPreview(context, styles);
  }

  drawGrid(context, styles, visible) {
    const snapStep = this.published.gridStepPlanUnits * this.published.snapStepGridUnits;
    if (!Number.isSafeInteger(snapStep) || snapStep <= 0) {
      return;
    }

    const minimumSpacingCssPixels = 12;
    const intervalMultiplier = Math.max(
      1,
      Math.ceil(minimumSpacingCssPixels / (snapStep * this.viewport.zoom)),
    );
    const interval = snapStep * intervalMultiplier;
    if (!Number.isSafeInteger(interval)) {
      return;
    }

    drawGridLines(
      context,
      visible,
      interval,
      cssColor(styles, "--ll-grid", "rgb(16 42 51 / 7%)"),
      1 / this.viewport.zoom,
    );
    const strongInterval = interval * 5;
    if (Number.isSafeInteger(strongInterval)) {
      drawGridLines(
        context,
        visible,
        strongInterval,
        cssColor(styles, "--ll-grid-strong", "rgb(8 120 140 / 13%)"),
        1.25 / this.viewport.zoom,
      );
    }
  }

  drawOverlays(context, styles) {
    const selected = this.selectedSources;
    const primary = this.primarySelectionSource;
    const probePoints = new Map(
      this.published.overlays
        .filter((overlay) => overlay.kind === "probeAnchor")
        .map((overlay) => [sourceKey(overlay.source), overlay.point]),
    );

    for (const overlay of this.published.overlays) {
      if (overlay.kind === "liveNetValue") {
        const point = probePoints.get(sourceKey(overlay.source));
        if (point) {
          drawLiveNetValue(
            context,
            styles,
            this.symbolFontFamily,
            point,
            overlay.value,
            this.viewport.zoom,
          );
        }
      } else if (overlay.kind === "probeAnchor") {
        context.save();
        const color = cssColor(styles, `--ll-probe-${overlay.appearanceOrdinal % 4}`, "#08788c");
        const dash =
          overlay.pattern === "dash"
            ? [8, 5]
            : overlay.pattern === "dot"
              ? [2, 4]
              : overlay.pattern === "dashDot"
                ? [9, 4, 2, 4]
                : [];
        context.strokeStyle = color;
        context.fillStyle = cssColor(styles, "--ll-canvas", "#ffffff");
        context.lineWidth = 2 / this.viewport.zoom;
        context.setLineDash(dash.map((length) => length / this.viewport.zoom));
        context.beginPath();
        context.arc(overlay.point.x, overlay.point.y, 7 / this.viewport.zoom, 0, Math.PI * 2);
        context.fill();
        context.stroke();
        context.fillStyle = color;
        context.beginPath();
        context.arc(overlay.point.x, overlay.point.y, 2.5 / this.viewport.zoom, 0, Math.PI * 2);
        context.fill();
        context.restore();
      }
    }

    for (const item of this.published.items) {
      const targets = item.hasDrawableTarget ? [{ source: item.source, bounds: item.bounds }] : [];
      for (const region of item.hitRegions) {
        if (region.targetSource) {
          targets.push({ source: region.targetSource, bounds: region.bounds });
        }
      }

      for (const target of targets) {
        const key = sourceKey(target.source);
        const isPrimary = key === primary;
        if (!isPrimary && !selected.has(key)) {
          continue;
        }

        const bounds = translateRect(target.bounds, item.origin);
        context.save();
        context.strokeStyle = cssColor(styles, "--ll-signal", "#08788c");
        context.lineWidth = 3 / this.viewport.zoom;
        context.setLineDash(isPrimary ? [8, 5] : []);
        context.strokeRect(
          bounds.left,
          bounds.top,
          bounds.right - bounds.left,
          bounds.bottom - bounds.top,
        );
        context.restore();
      }
    }
    for (const overlay of this.published.overlays) {
      if (overlay.kind !== "diagnosticMarker") {
        continue;
      }
      const target = this.targetBySource(overlay.source);
      if (!target) {
        continue;
      }
      context.save();
      context.strokeStyle =
        overlay.severity === "error"
          ? cssColor(styles, "--ll-danger", "#b42318")
          : cssColor(styles, "--ll-transition", "#a85d00");
      context.lineWidth = 4 / this.viewport.zoom;
      context.setLineDash([6 / this.viewport.zoom, 4 / this.viewport.zoom]);
      context.beginPath();
      context.moveTo(target.bounds.left, target.bounds.bottom + 5 / this.viewport.zoom);
      context.lineTo(target.bounds.right, target.bounds.bottom + 5 / this.viewport.zoom);
      context.stroke();
      context.restore();
    }
  }

  drawTransientPreview(context, styles) {
    const hoverSource = this.hoveredSource ? this.sourceByKey(this.hoveredSource) : null;
    const hoverTarget = hoverSource ? this.targetBySource(hoverSource) : null;
    if (hoverTarget && !this.gesture) {
      context.save();
      context.strokeStyle = cssColor(styles, "--ll-signal", "#08788c");
      context.globalAlpha = 0.65;
      context.lineWidth = 2 / this.viewport.zoom;
      context.setLineDash([5 / this.viewport.zoom, 4 / this.viewport.zoom]);
      context.strokeRect(
        hoverTarget.bounds.left,
        hoverTarget.bounds.top,
        hoverTarget.bounds.right - hoverTarget.bounds.left,
        hoverTarget.bounds.bottom - hoverTarget.bounds.top,
      );
      context.restore();
    }

    const gesture = this.gesture;
    const snapshot = this.published;
    if (!gesture || !snapshot || gesture.tool.kind === "pan") {
      return;
    }

    const start = gridPoint(gesture.startWorld, snapshot, gesture.disableSnap);
    const end = gridPoint(gesture.currentWorld, snapshot, gesture.disableSnap);
    if (!start || !end) {
      return;
    }
    const startPoint = gridToWorld(start, snapshot);
    let endPoint = gridToWorld(end, snapshot);
    const previewColor = cssColor(styles, "--ll-signal", "#08788c");
    context.save();
    context.strokeStyle = previewColor;
    context.fillStyle = previewColor;
    context.lineWidth = 3 / this.viewport.zoom;
    context.setLineDash([8 / this.viewport.zoom, 5 / this.viewport.zoom]);

    if (gesture.tool.kind === "select" && !gesture.hit && gestureMoved(gesture)) {
      const marquee = rectFromPoints(gesture.startWorld, gesture.currentWorld);
      context.globalAlpha = 0.12;
      context.fillRect(
        marquee.left,
        marquee.top,
        marquee.right - marquee.left,
        marquee.bottom - marquee.top,
      );
      context.globalAlpha = 0.8;
      context.strokeRect(
        marquee.left,
        marquee.top,
        marquee.right - marquee.left,
        marquee.bottom - marquee.top,
      );
    } else if (
      gesture.tool.kind === "select" &&
      gesture.hit?.item?.hasDrawableTarget &&
      (start.x !== end.x || start.y !== end.y)
    ) {
      const item = gesture.hit.item;
      const translateX = endPoint.x - startPoint.x;
      const translateY = endPoint.y - startPoint.y;
      context.globalAlpha = 0.55;
      context.translate(item.origin.x + translateX, item.origin.y + translateY);
      for (const operation of item.operations) {
        drawOperation(context, operation, styles, this.symbolFontFamily);
      }
    } else if (gesture.tool.kind === "placeComponent") {
      const halfSize = Math.max(snapshot.gridStepPlanUnits * 0.35, 12 / this.viewport.zoom);
      context.globalAlpha = 0.75;
      context.strokeRect(endPoint.x - halfSize, endPoint.y - halfSize, halfSize * 2, halfSize * 2);
    } else if (gesture.tool.kind === "wire" && gesture.hit) {
      const endHit = this.hitTest(gesture.currentWorld);
      const route =
        terminalFromSource(gesture.hit.source) || terminalFromSource(endHit?.source)
          ? terminalWireRoutes(
              snapshot,
              gesture.hit,
              endHit,
              gesture.startWorld,
              gesture.currentWorld,
              gesture.disableSnap,
            )?.[0]
          : orthogonalDragRoute(start, end);
      if (route) {
        const points = route.points.map((point) => gridToWorld(point, snapshot));
        endPoint = points.at(-1);
        context.beginPath();
        context.moveTo(points[0].x, points[0].y);
        for (const point of points.slice(1)) {
          context.lineTo(point.x, point.y);
        }
        context.stroke();
      }
    }

    if (["select", "placeComponent", "wire"].includes(gesture.tool.kind)) {
      const markerSize = 8 / this.viewport.zoom;
      context.setLineDash([]);
      context.beginPath();
      context.moveTo(endPoint.x - markerSize, endPoint.y);
      context.lineTo(endPoint.x + markerSize, endPoint.y);
      context.moveTo(endPoint.x, endPoint.y - markerSize);
      context.lineTo(endPoint.x, endPoint.y + markerSize);
      context.stroke();
    }
    context.restore();
  }

  pointerDown(event) {
    if (
      this.destroyed ||
      this.contextIsLost ||
      !event.isPrimary ||
      event.button !== 0 ||
      this.gesture
    ) {
      return;
    }

    const screen = this.pointerScreen(event);
    const world = this.screenToWorld(screen);
    const tool = this.spacePan || !this.connected ? { kind: "pan" } : this.activeTool;
    if (tool.kind !== "pan" && this.pendingIntent) {
      return;
    }
    if (tool.kind !== "pan" && !this.published) {
      return;
    }
    if (tool.kind !== "pan") {
      this.viewportIsUserControlled = true;
    }
    const hit = tool.kind !== "pan" ? this.hitTest(world) : null;
    this.hoveredSource = null;
    this.gesture = {
      pointerId: event.pointerId,
      tool,
      hit,
      start: screen,
      last: screen,
      startWorld: world,
      currentWorld: world,
      disableSnap: event.altKey,
      selectionMode: selectionModeFromModifiers(event),
      sceneVersion: this.published?.sceneVersion ?? 0,
      projectionVersion: this.published?.projectionVersion ?? 0,
    };
    this.canvas.setPointerCapture(event.pointerId);
    event.preventDefault();
  }

  pointerMove(event) {
    const gesture = this.gesture;
    if (!gesture || gesture.pointerId !== event.pointerId) {
      if (!gesture && this.published) {
        const hit = this.hitTest(this.screenToWorld(this.pointerScreen(event)));
        const hoveredSource = hit ? sourceKey(hit.source) : null;
        if (hoveredSource !== this.hoveredSource) {
          this.hoveredSource = hoveredSource;
          this.invalidate();
        }
      }
      return;
    }

    const screen = this.pointerScreen(event);
    gesture.currentWorld = this.screenToWorld(screen);
    gesture.disableSnap = event.altKey;
    gesture.selectionMode = selectionModeFromModifiers(event);
    if (gesture.tool.kind === "pan") {
      const deltaX = screen.x - gesture.last.x;
      const deltaY = screen.y - gesture.last.y;
      if (deltaX !== 0 || deltaY !== 0) {
        this.viewportIsUserControlled = true;
        this.viewport.x += deltaX;
        this.viewport.y += deltaY;
      }
      gesture.last = screen;
    }
    this.invalidate();
  }

  pointerUp(event) {
    const gesture = this.gesture;
    if (!gesture || gesture.pointerId !== event.pointerId) {
      return;
    }

    this.releaseCapture(event.pointerId);
    this.gesture = null;
    gesture.currentWorld = this.screenToWorld(this.pointerScreen(event));
    gesture.disableSnap = event.altKey;
    gesture.selectionMode = selectionModeFromModifiers(event);
    this.invalidate();
    if (
      this.published &&
      this.connected &&
      gesture.sceneVersion === this.published.sceneVersion &&
      gesture.projectionVersion === this.published.projectionVersion
    ) {
      this.commitGesture(gesture, gesture.disableSnap);
    }
  }

  cancelPointer(event) {
    if (this.gesture?.pointerId === event.pointerId) {
      this.cancelGesture();
    }
  }

  clearHover() {
    if (!this.gesture && this.hoveredSource !== null) {
      this.hoveredSource = null;
      this.invalidate();
    }
  }

  cancelGesture() {
    const gesture = this.gesture;
    this.gesture = null;
    if (gesture) {
      this.releaseCapture(gesture.pointerId);
      this.invalidate();
    }
    return gesture !== null;
  }

  releaseCapture(pointerId) {
    try {
      if (this.canvas?.hasPointerCapture(pointerId)) {
        this.canvas.releasePointerCapture(pointerId);
      }
    } catch {
      // Capture may already have ended because the host was removed.
    }
  }

  wheel(event) {
    if (!this.published) {
      return;
    }

    event.preventDefault();
    this.cancelGesture();
    this.zoomAt(this.pointerScreen(event), this.viewport.zoom * Math.exp(-event.deltaY * 0.001));
  }

  zoomControl(action) {
    if (!this.published) {
      return;
    }
    this.cancelGesture();
    if (action === "fit") {
      this.forgetPublishedViewport();
      this.fitViewport();
      this.invalidate();
      return;
    }
    if (action === "in" || action === "out") {
      const factor = action === "in" ? 1.25 : 0.8;
      this.zoomAt({ x: this.cssWidth / 2, y: this.cssHeight / 2 }, this.viewport.zoom * factor);
    }
  }

  zoomAt(anchor, requestedZoom) {
    const world = this.screenToWorld(anchor);
    const minimum = Number(this.policy.zoomMillionthsMinimum) / 1_000_000;
    const maximum = Number(this.policy.zoomMillionthsMaximum) / 1_000_000;
    const zoom = Math.min(maximum, Math.max(minimum, requestedZoom));
    this.viewportIsUserControlled = true;
    this.viewport.zoom = zoom;
    this.viewport.x = anchor.x - world.x * zoom;
    this.viewport.y = anchor.y - world.y * zoom;
    this.invalidate();
  }

  keyDown(event) {
    const canvasIsTarget = event.target === this.canvas;
    if (event.key === "Escape") {
      const cancelledGesture = this.cancelGesture();
      const cancelledPreview = cancelledGesture || this.spacePan;
      this.spacePan = false;
      if (!cancelledPreview && this.selectedSources.size > 0 && this.published) {
        const committed = this.emitIntent("selectSources", this.published, {
          sources: [],
          selectionMode: "replace",
        });
        if (committed) {
          this.updateSelection([], "replace");
          this.invalidate();
        }
      }
      event.preventDefault();
      return;
    }

    if (event.key === " " && canvasIsTarget) {
      if (!this.spacePan) {
        this.cancelGesture();
      }
      this.spacePan = true;
      event.preventDefault();
      return;
    }
  }

  keyUp(event) {
    if (event.key === " ") {
      this.spacePan = false;
    }
  }

  reconnectStateChanged(event) {
    const state = event.detail?.state;
    if (["show", "paused", "retrying", "failed", "rejected"].includes(state)) {
      this.setConnected(false);
    } else if (state === "hide") {
      this.setConnected(true);
      void this.dotnetSink?.invokeMethodAsync("SceneConnectionChangedAsync", true).catch(() => {});
    }
  }

  // Cancelling contextlost prevents Canvas 2D backing-store restoration.
  // https://html.spec.whatwg.org/multipage/webappapis.html#context-lost-steps
  contextLost() {
    this.contextIsLost = true;
    this.cancelGesture();
    if (this.pendingFrame) {
      cancelAnimationFrame(this.pendingFrame);
      this.pendingFrame = 0;
    }
    if (this.contextRestoreTimer) {
      clearTimeout(this.contextRestoreTimer);
    }
    this.contextRestoreTimer = setTimeout(() => {
      this.contextRestoreTimer = 0;
      if (this.contextIsLost && !this.destroyed) {
        this.failClosed();
        void this.notifyFailure("contextLost");
      }
    }, contextRestoreTimeoutMilliseconds);
  }

  contextRestored() {
    if (this.contextRestoreTimer) {
      clearTimeout(this.contextRestoreTimer);
      this.contextRestoreTimer = 0;
    }
    this.contextIsLost = false;
    this.context = this.canvas.getContext("2d", { alpha: false });
    if (!this.context) {
      this.contextIsLost = true;
      this.failClosed();
      void this.notifyFailure("contextLost");
      return;
    }

    this.resize();
    this.invalidate();
  }

  selectSource(source, selectionMode) {
    this.selectSources([source], selectionMode);
  }

  selectSources(sources, selectionMode) {
    const snapshot = this.published;
    if (
      !snapshot ||
      this.contextIsLost ||
      !this.connected ||
      !Array.isArray(sources) ||
      (sources.length === 0 && selectionMode !== "replace")
    ) {
      return;
    }

    const committed = this.emitIntent(
      "selectSources",
      {
        sceneVersion: snapshot.sceneVersion,
        projectionVersion: snapshot.projectionVersion,
      },
      {
        sources,
        selectionMode,
      },
    );
    if (committed) {
      const keys = sources.map(sourceKey);
      this.updateSelection(keys, selectionMode);
      this.invalidate();
    }
  }

  commitGesture(gesture, disableSnap) {
    const hit = gesture.hit;
    const snapshot = this.published;
    if (!snapshot) {
      return;
    }
    if (gesture.tool.kind === "select") {
      if (!hit) {
        const sources = gestureMoved(gesture)
          ? this.sourcesInRect(rectFromPoints(gesture.startWorld, gesture.currentWorld))
          : [];
        this.selectSources(sources, gesture.selectionMode);
        return;
      }
      const start = gridPoint(gesture.startWorld, snapshot, disableSnap);
      const end = gridPoint(gesture.currentWorld, snapshot, disableSnap);
      if (!start || !end) {
        return;
      }
      const moved = start.x !== end.x || start.y !== end.y;
      const interaction = hit.item.interaction;
      if (moved && interaction?.interactionKind === "component") {
        const placement = translateComponentPlacement(interaction.placement, start, end);
        if (!placement) {
          return;
        }
        this.emitIntent("moveComponents", gesture, {
          moves: [
            {
              component: hit.item.source,
              placement,
            },
          ],
          snapModifier: disableSnap ? "disableSnap" : "none",
        });
      } else if (moved && interaction?.interactionKind === "definitionPort") {
        const position = translateGridPoint(interaction.placement.position, start, end);
        if (!position) {
          return;
        }
        this.emitIntent("moveDefinitionPorts", gesture, {
          moves: [
            {
              port: hit.item.source,
              placement: {
                position,
                facing: interaction.placement.facing,
              },
            },
          ],
          snapModifier: disableSnap ? "disableSnap" : "none",
        });
      } else if (moved && interaction?.interactionKind === "annotation") {
        const position = translateGridPoint(interaction.position, start, end);
        if (!position) {
          return;
        }
        this.emitIntent("moveAnnotations", gesture, {
          moves: [
            {
              annotation: hit.item.source,
              position,
            },
          ],
          snapModifier: disableSnap ? "disableSnap" : "none",
        });
      } else {
        this.selectSource(hit.source, gesture.selectionMode);
      }
      return;
    }
    if (gesture.tool.kind === "placeComponent") {
      const origin = gridPoint(gesture.currentWorld, snapshot, disableSnap);
      if (!origin) {
        return;
      }
      const committed = this.emitIntent("placeComponent", gesture, {
        target: gesture.tool.target,
        parameters: gesture.tool.parameters,
        placement: {
          origin,
          quarterTurnsClockwise: 0,
          reflected: false,
        },
        displayName: gesture.tool.displayName,
        snapModifier: disableSnap ? "disableSnap" : "none",
      });
      if (committed && !gesture.tool.pinned) {
        this.activeTool = Object.freeze({ kind: "select" });
        this.activeToolKey = '{"kind":"select"}';
        void this.dotnetSink?.invokeMethodAsync("SceneToolConsumedAsync").catch(() => {});
      }
      return;
    }
    if (gesture.tool.kind === "probe") {
      const net = netFromHit(hit);
      if (net) {
        this.emitIntent("toggleProbe", gesture, {
          net: { authoredNet: net, hierarchyPath: gesture.tool.hierarchyPath },
        });
      }
      return;
    }
    if (gesture.tool.kind === "wire") {
      this.commitWireGesture(gesture, disableSnap);
    }
  }

  commitWireGesture(gesture, disableSnap) {
    const snapshot = this.published;
    const hit = gesture.hit;
    if (!snapshot || !hit) return;
    const endHit = this.hitTest(gesture.currentWorld);
    const startTerminal = terminalFromSource(hit.source);
    const endTerminal = endHit ? terminalFromSource(endHit.source) : null;
    if (startTerminal || endTerminal) {
      if (startTerminal && endTerminal && sourceKey(hit.source) === sourceKey(endHit.source)) {
        return;
      }
      const terminals = [startTerminal, endTerminal].filter(Boolean);
      const destinationNet =
        terminals.length === 2 ? null : netFromHit(startTerminal ? endHit : hit);
      if (terminals.length === 1 && !destinationNet) return;

      const routeAdditions = terminalWireRoutes(
        snapshot,
        hit,
        endHit,
        gesture.startWorld,
        gesture.currentWorld,
        disableSnap,
      );
      if (routeAdditions === null) return;
      this.emitIntent("commitWire", gesture, {
        terminals,
        destinationNet,
        newJunctionPositions: [],
        routeAdditions,
        routeReplacements: [],
        snapModifier: disableSnap ? "disableSnap" : "none",
      });
      return;
    }
    const startNet = netFromHit(hit);
    const interaction = hit.item.interaction;
    const start = gridPoint(gesture.startWorld, snapshot, disableSnap);
    const end = gridPoint(gesture.currentWorld, snapshot, disableSnap);
    if (!start || !end) {
      return;
    }
    const moved = start.x !== end.x || start.y !== end.y;
    const dragRoute = orthogonalDragRoute(start, end);
    if (interaction?.interactionKind === "wire" && moved) {
      this.emitIntent("setWireRoute", gesture, {
        wireGeometry: hit.item.source,
        route: dragRoute,
        snapModifier: disableSnap ? "disableSnap" : "none",
      });
    } else if (moved) {
      if (startNet) {
        this.emitIntent("addJunction", gesture, {
          net: startNet,
          position: end,
          routeAdditions: [dragRoute],
          routeReplacements: [],
          routeRemovals: [],
          snapModifier: disableSnap ? "disableSnap" : "none",
        });
      }
    }
  }

  emitIntent(kind, gesture, payload) {
    const snapshot = this.published;
    if (
      !snapshot ||
      !this.connected ||
      this.pendingIntent ||
      gesture.sceneVersion !== snapshot.sceneVersion ||
      gesture.projectionVersion !== snapshot.projectionVersion
    ) {
      return false;
    }
    const intent = {
      kind,
      buildFingerprint: this.buildFingerprint,
      sceneVersion: gesture.sceneVersion,
      projectionVersion: gesture.projectionVersion,
      circuitDefinitionId: snapshot.circuitDefinitionId,
      ...payload,
    };
    const intentBytes = encodedJsonBytes(intent);
    if (intentBytes > BigInt(this.policy.semanticIntentBytes)) {
      void this.reportPolicyFailure(new BrowserPolicyError("semanticIntentBytes", intentBytes));
      return false;
    }
    const pendingIntent = Object.freeze({
      kind,
      sceneVersion: gesture.sceneVersion,
      projectionVersion: gesture.projectionVersion,
    });
    this.pendingIntent = pendingIntent;
    void this.dotnetSink
      ?.invokeMethodAsync("ReceiveSceneIntentAsync", intent)
      .then(() => {
        if (kind === "selectSources" && this.pendingIntent === pendingIntent) {
          this.pendingIntent = null;
        }
      })
      .catch(() => {
        if (this.pendingIntent === pendingIntent) {
          this.pendingIntent = null;
        }
        this.failClosed();
        return this.notifyFailure("web_interop_failure");
      });
    return true;
  }

  updateSelection(sourceKeys, selectionMode) {
    if (selectionMode === "replace") {
      this.selectedSources = new Set(sourceKeys);
      this.primarySelectionSource = sourceKeys[0] ?? null;
    } else if (selectionMode === "add") {
      sourceKeys.forEach((sourceKey) => this.selectedSources.add(sourceKey));
      this.primarySelectionSource ??= sourceKeys[0] ?? null;
    } else {
      for (const sourceKey of sourceKeys) {
        if (this.selectedSources.has(sourceKey)) {
          this.selectedSources.delete(sourceKey);
        } else {
          this.selectedSources.add(sourceKey);
        }
      }
      if (!this.selectedSources.has(this.primarySelectionSource)) {
        this.primarySelectionSource = this.selectedSources.values().next().value ?? null;
      }
    }
  }

  hitTest(world) {
    if (!this.published || !validPoint(world)) {
      return null;
    }

    const candidates = [];
    const cell = this.spatialIndex.get(spatialCellKey(world.x, world.y)) ?? [];
    for (const { item, region } of cell) {
      const local = { x: world.x - item.origin.x, y: world.y - item.origin.y };
      if (contains(region, local)) {
        candidates.push({
          source: region.targetSource ?? item.source,
          item,
          region,
          priority: hitPriority(item, region),
          order: item.order,
        });
      }
    }

    candidates.sort((left, right) => right.priority - left.priority || right.order - left.order);
    return candidates[0] ?? null;
  }

  sourcesInRect(rect) {
    const sources = [];
    const seen = new Set();
    for (const item of this.published?.items ?? []) {
      const key = sourceKey(item.source);
      if (
        item.hasDrawableTarget &&
        !seen.has(key) &&
        intersects(translateRect(item.bounds, item.origin), rect)
      ) {
        seen.add(key);
        sources.push(item.source);
      }
    }
    return sources;
  }

  fitViewport() {
    if (!this.published || !this.cssWidth || !this.cssHeight) {
      return;
    }

    const bounds = this.published.bounds;
    const padding = 32;
    const maximum = Math.min(
      maximumAutomaticGridStepCssPixels / this.published.gridStepPlanUnits,
      Number(this.policy.zoomMillionthsMaximum) / 1_000_000,
    );
    const minimum = Number(this.policy.zoomMillionthsMinimum) / 1_000_000;
    const boundsWidth = bounds.right - bounds.left;
    const boundsHeight = bounds.bottom - bounds.top;
    const fitZoom =
      boundsWidth > 0 && boundsHeight > 0
        ? Math.min(
            (this.cssWidth - padding * 2) / boundsWidth,
            (this.cssHeight - padding * 2) / boundsHeight,
          )
        : 1;
    // Automatic grid spacing is a preference; the policy minimum is a hard limit.
    const zoom = Math.max(minimum, Math.min(maximum, fitZoom));
    this.viewportIsUserControlled = false;
    this.viewport.zoom = zoom;
    this.viewport.x = this.cssWidth / 2 - ((bounds.left + bounds.right) / 2) * zoom;
    this.viewport.y = this.cssHeight / 2 - ((bounds.top + bounds.bottom) / 2) * zoom;
  }

  pointerScreen(event) {
    const rect = this.canvas.getBoundingClientRect();
    return { x: event.clientX - rect.left, y: event.clientY - rect.top };
  }

  screenToWorld(point) {
    return {
      x: (point.x - this.viewport.x) / this.viewport.zoom,
      y: (point.y - this.viewport.y) / this.viewport.zoom,
    };
  }

  visibleWorldRect() {
    const topLeft = this.screenToWorld({ x: 0, y: 0 });
    const bottomRight = this.screenToWorld({ x: this.cssWidth, y: this.cssHeight });
    return {
      left: Math.min(topLeft.x, bottomRight.x),
      top: Math.min(topLeft.y, bottomRight.y),
      right: Math.max(topLeft.x, bottomRight.x),
      bottom: Math.max(topLeft.y, bottomRight.y),
    };
  }

  sourceKeys() {
    return [...this.sourcesByKey.keys()];
  }

  sourceByKey(key) {
    return this.sourcesByKey.get(key) ?? null;
  }

  targetBySource(source) {
    return this.targetsBySource.get(sourceKey(source)) ?? null;
  }

  selectionResizeAnchor() {
    const source = this.primarySelectionSource
      ? this.sourceByKey(this.primarySelectionSource)
      : null;
    const target = source ? this.targetBySource(source) : null;
    if (!target) {
      return null;
    }

    const world = {
      x: target.bounds.left * 0.5 + target.bounds.right * 0.5,
      y: target.bounds.top * 0.5 + target.bounds.bottom * 0.5,
    };
    const screen = {
      x: this.viewport.x + world.x * this.viewport.zoom,
      y: this.viewport.y + world.y * this.viewport.zoom,
    };
    return validPoint(world) && validPoint(screen) ? { world, screen } : null;
  }

  rememberPublishedViewport() {
    const definitionId = this.published?.circuitDefinitionId;
    if (!this.viewportIsUserControlled || !definitionId) {
      return;
    }

    this.savedViewports.delete(definitionId);
    this.savedViewports.set(definitionId, { ...this.viewport });
  }

  forgetPublishedViewport() {
    const definitionId = this.published?.circuitDefinitionId;
    if (definitionId) {
      this.savedViewports.delete(definitionId);
    }
    this.viewportIsUserControlled = false;
  }

  clearCanvas() {
    if (!this.context || this.contextIsLost) {
      return;
    }
    this.context.setTransform(1, 0, 0, 1, 0, 0);
    this.context.clearRect(0, 0, this.canvas.width, this.canvas.height);
  }

  failClosed() {
    this.rememberPublishedViewport();
    this.cancelGesture();
    this.hoveredSource = null;
    this.pendingIntent = null;
    this.primarySelectionSource = null;
    this.selectedSources.clear();
    this.transfers.clear();
    this.published = null;
    this.spatialIndex.clear();
    this.sourcesByKey.clear();
    this.targetsBySource.clear();
    this.dirty = false;
    if (this.pendingFrame) {
      cancelAnimationFrame(this.pendingFrame);
      this.pendingFrame = 0;
    }
    this.clearCanvas();
    if (this.canvas) {
      this.canvas.dataset.sceneLocalUnavailable = "";
    }
  }

  async reportPolicyFailure(error) {
    if (!(error instanceof BrowserPolicyError)) {
      throw new Error("invalid Browser Policy failure");
    }
    if (this.canvas?.hasAttribute("data-scene-local-unavailable")) {
      return;
    }
    this.failClosed();
    const dimension = browserPolicyDimensionTokens[error.dimension];
    if (!dimension) {
      await this.notifyFailure("web_interop_failure");
      return;
    }
    await this.dotnetSink
      ?.invokeMethodAsync(
        "SceneBrowserPolicyExhaustedAsync",
        this.policy.policyId,
        this.policy.policyRevision,
        dimension,
        error.observed.toString(),
      )
      .catch(() => {});
  }

  async rejectCandidate(code) {
    this.cancelGesture();
    await this.notifyFailure(code);
    await this.dotnetSink?.invokeMethodAsync("SceneSnapshotRequiredAsync").catch(() => {});
  }

  async notifyFailure(code) {
    await this.dotnetSink?.invokeMethodAsync("SceneRendererFailedAsync", code).catch(() => {});
  }

  ensureLive() {
    if (this.destroyed) {
      throw new Error("scene handle is destroyed");
    }
  }
}

function logicVectorText(value) {
  const symbols = ["0", "1", "X", "Z"];
  const bytes = decodeBase64(value.data);
  const bits = [];
  for (let index = 0; index < value.width; index++) {
    bits.push(symbols[(bytes[Math.floor(index / 4)] >> ((index % 4) * 2)) & 3]);
  }
  const text = bits.reverse().join("");
  return text.length <= 16 ? text : `${text.slice(0, 7)}…${text.slice(-7)}`;
}

function drawLiveNetValue(context, styles, fontFamily, point, value, zoom) {
  const scale = 1 / zoom;
  const text = logicVectorText(value);
  const height = 24 * scale;
  const padding = 7 * scale;
  const x = point.x + 10 * scale;
  const y = point.y - height - 10 * scale;

  context.save();
  context.font = `600 ${16 * scale}px ${fontFamily}`;
  const width = Math.max(height, context.measureText(text).width + padding * 2);
  context.beginPath();
  context.roundRect(x, y, width, height, 6 * scale);
  context.fillStyle = cssColor(styles, "--ll-canvas", "#ffffff");
  context.fill();
  context.strokeStyle = cssColor(styles, "--ll-signal", "#08788c");
  context.lineWidth = 1.5 * scale;
  context.stroke();
  context.fillStyle = cssColor(styles, "--ll-ink", "#172124");
  context.textAlign = "center";
  context.textBaseline = "middle";
  context.fillText(text, x + width / 2, y + height / 2);
  context.restore();
}

function checkedInteger(value) {
  if (!Number.isSafeInteger(value) || value < -2147483648 || value > 2147483647)
    throw new Error("integer overflow");
  return value;
}
async function sha256(bytes) {
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map((value) => value.toString(16).padStart(2, "0")).join("");
}
