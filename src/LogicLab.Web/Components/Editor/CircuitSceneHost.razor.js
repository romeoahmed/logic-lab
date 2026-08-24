const mountedHandles = new WeakMap();
const textEncoder = new TextEncoder();
const spatialCellSize = 400;
const spatialEntryBaseBytes = 64;
const contextRestoreTimeoutMilliseconds = 2_000;

export function mount(host, buildFingerprint, policy, dotnetSink) {
  const existing = mountedHandles.get(host);
  if (existing && existing.buildFingerprint === buildFingerprint && !existing.destroyed) {
    return existing;
  }

  existing?.destroy();
  const handle = new CircuitSceneHandle(host, buildFingerprint, validatePolicy(policy), dotnetSink);
  mountedHandles.set(host, handle);
  return handle;
}

class CircuitSceneHandle {
  constructor(host, buildFingerprint, policy, dotnetSink) {
    this.host = host;
    this.buildFingerprint = buildFingerprint;
    this.policy = policy;
    this.dotnetSink = dotnetSink;
    this.canvas = host.querySelector("[data-scene-canvas]");
    this.context = this.canvas?.getContext("2d", { alpha: false }) ?? null;
    this.published = null;
    this.spatialIndex = new Map();
    this.viewport = { x: 0, y: 0, zoom: 1 };
    this.savedViewports = new Map();
    this.focusedSource = null;
    this.selectedSources = new Set();
    this.gesture = null;
    this.activeTool = Object.freeze({ kind: "select" });
    this.activeToolKey = '{"kind":"select"}';
    this.spacePan = false;
    this.connected = true;
    this.sceneOwnsDocumentFocus = false;
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

    if (!this.canvas || !this.context) {
      void this.notifyFailure("contextUnavailable");
      return;
    }

    this.installListeners();
    this.installObservers();
    this.resize();
  }

  async measureText(requests) {
    this.ensureLive();
    if (!Array.isArray(requests)) {
      throw new Error("invalid text measurement request batch");
    }

    const styles = getComputedStyle(this.canvas);
    const family = styles.getPropertyValue("--ll-scene-font-family").trim();
    const assetFingerprint = styles.getPropertyValue("--ll-scene-font-asset").trim();
    if (family !== "Atkinson Hyperlegible Next" || !isDigest(assetFingerprint)) {
      await this.notifyFailure("assetFingerprintMismatch");
      throw new Error("symbol font asset fingerprint is invalid");
    }

    const font = `400 100px "${family}"`;
    const glyphs = new Set(requests.flatMap((request) => Array.from(request?.text ?? "")));
    if (glyphs.size === 0) glyphs.add(" ");
    for (const glyph of glyphs) {
      let faces;
      try {
        faces = await document.fonts.load(font, glyph);
      } catch {
        faces = [];
      }
      const exactFaceLoaded = faces.some((face) => face.status === "loaded"
        && face.family.replaceAll('"', "") === family);
      if (!exactFaceLoaded || !document.fonts.check(font, glyph)) {
        await this.notifyFailure("fontUnavailable");
        throw new Error("symbol font is unavailable");
      }
    }
    this.symbolFontFamily = `"${family}"`;

    const measurements = [];
    const seen = new Set();
    for (const request of requests) {
      if (!request || typeof request.key !== "string" || seen.has(request.key)
          || typeof request.text !== "string" || !isTextRole(request.fontRole)
          || !isAlignment(request.alignment) || !isLocale(request.locale)
          || !isDirection(request.direction)) {
        throw new Error("invalid text measurement request");
      }

      seen.add(request.key);
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
      if (measurement.advanceWidth < 0 || measurement.inkRight < measurement.inkLeft
          || measurement.inkBottom < measurement.inkTop) {
        throw new Error("invalid text metrics");
      }

      measurements.push(measurement);
    }

    const canonical = measurements
      .slice()
      .sort((left, right) => compareOrdinal(left.key, right.key))
      .map((value) => `${value.key}:${value.advanceWidth}:${value.inkLeft}:${value.inkTop}:${value.inkRight}:${value.inkBottom}`)
      .join("\n");
    this.fontFingerprint = await sha256(textEncoder.encode(
      `logiclab-browser-font-v1\n${family}\n${assetFingerprint}\n${canonical}`,
    ));
    return { fontFingerprint: this.fontFingerprint, measurements };
  }

  beginTransfer(transferId, kind, byteLength, digest) {
    this.ensureLive();
    if (!isToken(transferId) || !["replacement", "patch"].includes(kind)
        || !Number.isSafeInteger(byteLength) || byteLength <= 0
        || BigInt(byteLength) > BigInt(this.policy.candidateTransferBytes)
        || !isDigest(digest) || this.transfers.has(transferId)) {
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
    if (!transfer || ordinal !== transfer.nextOrdinal || typeof base64Chunk !== "string") {
      this.transfers.delete(transferId);
      this.rejectBatch("invalid scene transfer batch");
    }
    if (BigInt(textEncoder.encode(base64Chunk).byteLength + 512)
        > BigInt(this.policy.interopBatchBytes)) {
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
    this.transfers.delete(transferId);
    if (!transfer || transfer.received !== transfer.byteLength) {
      await this.rejectCandidate("invalidBatch");
      return false;
    }

    const bytes = new Uint8Array(transfer.byteLength);
    let offset = 0;
    for (const chunk of transfer.chunks) {
      bytes.set(chunk, offset);
      offset += chunk.byteLength;
    }

    let candidate;
    try {
      if (await sha256(bytes) !== transfer.digest) {
        throw new Error("scene transfer digest mismatch");
      }

      candidate = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes));
    } catch {
      await this.rejectCandidate("invalidBatch");
      return false;
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
    } catch {
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

  focusSource(sourceKey) {
    this.ensureLive();
    if (this.sourceKeys().includes(sourceKey)) {
      this.focusedSource = sourceKey;
      this.invalidate();
    }
  }

  setSelection(sources, selectionMode) {
    this.ensureLive();
    const available = new Set(this.sourceKeys());
    if (!Array.isArray(sources) || sources.length === 0
        || !["replace", "add", "toggle"].includes(selectionMode)
        || !this.published
        || sources.some((source) => !validSource(
          source,
          this.published.circuitDefinitionId,
        ) || !available.has(sourceKey(source)))) {
      throw new Error("invalid semantic Scene selection");
    }

    const sourceKeys = sources.map(sourceKey);
    this.updateSelection(sourceKeys, selectionMode);
    this.focusedSource = sourceKeys[0];
    this.invalidate();
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
    this.published = null;
    this.spatialIndex.clear();
    this.context = null;
    this.dotnetSink = null;
    if (mountedHandles.get(this.host) === this) {
      mountedHandles.delete(this.host);
    }
  }

  replace(candidate) {
    const validated = validateReplacement(candidate, this.buildFingerprint, this.fontFingerprint, this.policy);
    const spatialIndex = validated.kind === "snapshot"
      ? buildSpatialIndex(validated.value, this.policy)
      : new Map();
    this.cancelGesture();
    if (this.published?.circuitDefinitionId) {
      this.savedViewports.set(this.published.circuitDefinitionId, { ...this.viewport });
    }

    if (validated.kind === "unavailable") {
      this.published = null;
      this.spatialIndex = spatialIndex;
      this.focusedSource = null;
      this.selectedSources.clear();
      this.clearCanvas();
      return;
    }

    const priorKeys = this.sourceKeys();
    const priorFocusIndex = Math.max(0, priorKeys.indexOf(this.focusedSource));
    const shouldRecoverFocus = this.sceneOwnsDocumentFocus;
    this.published = validated.value;
    this.spatialIndex = spatialIndex;
    const saved = this.savedViewports.get(validated.value.circuitDefinitionId);
    if (saved) {
      this.viewport = { ...saved };
    } else {
      this.fitViewport();
    }

    const keys = this.sourceKeys();
    if (!keys.includes(this.focusedSource)) {
      this.focusedSource = keys[Math.min(priorFocusIndex, Math.max(0, keys.length - 1))] ?? null;
      this.recoverDocumentFocus(shouldRecoverFocus);
    }

    this.selectedSources = new Set(validated.value.overlays
      .filter((overlay) => overlay.kind === "selection")
      .map((overlay) => sourceKey(overlay.source)));
    this.invalidate();
  }

  apply(patch) {
    const candidate = validatePatch(patch, this.published, this.buildFingerprint, this.fontFingerprint, this.policy);
    if (!candidate) {
      void this.rejectCandidate("invalidPatch");
      return;
    }

    this.replace(candidate);
  }

  installListeners() {
    const signal = this.abortController.signal;
    this.canvas.addEventListener("pointerdown", (event) => this.pointerDown(event), { signal });
    this.canvas.addEventListener("pointermove", (event) => this.pointerMove(event), { signal });
    this.canvas.addEventListener("pointerup", (event) => this.pointerUp(event), { signal });
    this.canvas.addEventListener("pointercancel", () => this.cancelGesture(), { signal });
    this.canvas.addEventListener("lostpointercapture", () => this.cancelGesture(), { signal });
    this.canvas.addEventListener("wheel", (event) => this.wheel(event), { passive: false, signal });
    this.canvas.addEventListener("keydown", (event) => this.keyDown(event), { signal });
    document.addEventListener("keyup", (event) => this.keyUp(event), { signal });
    window.addEventListener("blur", () => {
      this.spacePan = false;
      this.cancelGesture();
    }, { signal });
    this.canvas.addEventListener("contextlost", (event) => this.contextLost(event), { signal });
    this.canvas.addEventListener("contextrestored", () => this.contextRestored(), { signal });
    this.host.addEventListener("focusin", (event) => this.semanticFocus(event), { signal });
    document.addEventListener("focusin", (event) => {
      this.sceneOwnsDocumentFocus = this.host.contains(event.target);
    }, { signal });

    this.reconnectModal = document.getElementById("components-reconnect-modal");
    this.reconnectModal?.addEventListener(
      "components-reconnect-state-changed",
      (event) => this.reconnectStateChanged(event),
      { signal },
    );
  }

  installObservers() {
    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(this.host);
    this.onDensityChange = () => {
      this.armDensityListener();
      this.resize();
    };
    this.armDensityListener();

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
    if (!Number.isFinite(rect.width) || !Number.isFinite(rect.height)
        || rect.width <= 0 || rect.height <= 0) {
      this.cssWidth = 0;
      this.cssHeight = 0;
      return;
    }

    const maximumDensity = Number(this.policy.effectiveDensityMillionths) / 1_000_000;
    const density = Math.min(Math.max(1, window.devicePixelRatio || 1), maximumDensity);
    const width = Math.ceil(rect.width * density);
    const height = Math.ceil(rect.height * density);
    const pixels = BigInt(width) * BigInt(height);
    const bytes = pixels * 4n;
    if (pixels > BigInt(this.policy.canvasBitmapPixels)
        || bytes > BigInt(this.policy.canvasBitmapBytes)) {
      void this.notifyFailure("browserPolicyExhausted");
      return;
    }

    const center = this.cssWidth > 0 && this.cssHeight > 0
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
        void this.notifyFailure("contextUnavailable");
        return;
      }
    }

    if (center) {
      this.viewport.x = (this.cssWidth / 2) - (center.x * this.viewport.zoom);
      this.viewport.y = (this.cssHeight / 2) - (center.y * this.viewport.zoom);
    }

    this.invalidate();
  }

  invalidate() {
    if (this.destroyed) {
      return;
    }

    this.dirty = true;
    if (!this.pendingFrame) {
      this.pendingFrame = requestAnimationFrame(() => this.render());
    }
  }

  render() {
    this.pendingFrame = 0;
    if (this.destroyed || this.contextIsLost || !this.dirty || !this.context
        || !this.cssWidth || !this.cssHeight) {
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
    for (const item of this.published.items) {
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
  }

  drawOverlays(context, styles) {
    const selected = new Set(this.selectedSources);
    let focused = this.focusedSource;
    for (const overlay of this.published.overlays) {
      if (overlay.kind === "selection") {
        selected.add(sourceKey(overlay.source));
      } else if (overlay.kind === "keyboardFocus") {
        focused = sourceKey(overlay.source);
      }
    }

    for (const overlay of this.published.overlays) {
      if (overlay.kind === "liveNetValue") {
        const target = this.targetBySource(overlay.source);
        if (target) {
          const point = {
            x: target.bounds.right + (8 / this.viewport.zoom),
            y: target.bounds.top - (8 / this.viewport.zoom),
          };
          context.save();
          context.fillStyle = cssColor(styles, "--ll-ink", "#172124");
          context.font = `${36 / this.viewport.zoom}px ${this.symbolFontFamily}`;
          context.textAlign = "left";
          context.textBaseline = "bottom";
          context.fillText(logicVectorText(overlay.value), point.x, point.y);
          context.restore();
        }
      } else if (overlay.kind === "probeAnchor") {
        context.save();
        context.strokeStyle = cssColor(styles, "--ll-signal", "#08788c");
        context.fillStyle = cssColor(styles, "--ll-canvas", "#ffffff");
        context.lineWidth = 3 / this.viewport.zoom;
        context.beginPath();
        context.arc(overlay.point.x, overlay.point.y, 10 / this.viewport.zoom, 0, Math.PI * 2);
        context.fill();
        context.stroke();
        context.fillStyle = cssColor(styles, "--ll-ink", "#172124");
        context.font = `${18 / this.viewport.zoom}px ${this.symbolFontFamily}`;
        context.textAlign = "center";
        context.textBaseline = "middle";
        context.fillText(String(overlay.appearanceOrdinal + 1), overlay.point.x, overlay.point.y);
        context.restore();
      }
    }

    for (const item of this.published.items) {
      const targets = [{ source: item.source, bounds: item.bounds }];
      for (const region of item.hitRegions) {
        if (region.targetSource) {
          targets.push({ source: region.targetSource, bounds: region.bounds });
        }
      }

      for (const target of targets) {
        const key = sourceKey(target.source);
        const isFocused = key === focused;
        if (!isFocused && !selected.has(key)) {
          continue;
        }

        const bounds = translateRect(target.bounds, item.origin);
        context.save();
        context.strokeStyle = cssColor(styles, "--ll-signal", "#08788c");
        context.lineWidth = 3 / this.viewport.zoom;
        context.setLineDash(isFocused ? [8, 5] : []);
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
      context.strokeStyle = overlay.severity === "error"
        ? cssColor(styles, "--ll-danger", "#b42318")
        : cssColor(styles, "--ll-warning", "#a15c00");
      context.lineWidth = 4 / this.viewport.zoom;
      context.setLineDash([6 / this.viewport.zoom, 4 / this.viewport.zoom]);
      context.beginPath();
      context.moveTo(target.bounds.left, target.bounds.bottom + (5 / this.viewport.zoom));
      context.lineTo(target.bounds.right, target.bounds.bottom + (5 / this.viewport.zoom));
      context.stroke();
      context.restore();
    }
  }

  pointerDown(event) {
    if (this.destroyed || !event.isPrimary || event.button !== 0 || this.gesture) {
      return;
    }

    const screen = this.pointerScreen(event);
    const world = this.screenToWorld(screen);
    const tool = this.spacePan || !this.connected
      ? { kind: "pan" }
      : this.activeTool;
    if (tool.kind !== "pan" && !this.published) {
      return;
    }
    const hit = tool.kind !== "pan" ? this.hitTest(world) : null;
    this.gesture = {
      pointerId: event.pointerId,
      tool,
      hit,
      start: screen,
      last: screen,
      startWorld: world,
      currentWorld: world,
      sceneVersion: this.published?.sceneVersion ?? 0,
      projectionVersion: this.published?.projectionVersion ?? 0,
    };
    this.canvas.setPointerCapture(event.pointerId);
    event.preventDefault();
  }

  pointerMove(event) {
    const gesture = this.gesture;
    if (!gesture || gesture.pointerId !== event.pointerId) {
      return;
    }

    const screen = this.pointerScreen(event);
    gesture.currentWorld = this.screenToWorld(screen);
    if (gesture.tool.kind === "pan") {
      this.viewport.x += screen.x - gesture.last.x;
      this.viewport.y += screen.y - gesture.last.y;
      gesture.last = screen;
      this.invalidate();
    }
  }

  pointerUp(event) {
    const gesture = this.gesture;
    if (!gesture || gesture.pointerId !== event.pointerId) {
      return;
    }

    this.releaseCapture(event.pointerId);
    this.gesture = null;
    gesture.currentWorld = this.screenToWorld(this.pointerScreen(event));
    if (this.published && this.connected
        && gesture.sceneVersion === this.published.sceneVersion
        && gesture.projectionVersion === this.published.projectionVersion) {
      this.commitGesture(gesture, event.altKey);
    }
  }

  cancelGesture() {
    const gesture = this.gesture;
    this.gesture = null;
    if (gesture) {
      this.releaseCapture(gesture.pointerId);
    }
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
    const anchor = this.pointerScreen(event);
    const world = this.screenToWorld(anchor);
    const minimum = Number(this.policy.zoomMillionthsMinimum) / 1_000_000;
    const maximum = Number(this.policy.zoomMillionthsMaximum) / 1_000_000;
    const zoom = Math.min(maximum, Math.max(minimum, this.viewport.zoom * Math.exp(-event.deltaY * 0.001)));
    this.viewport.zoom = zoom;
    this.viewport.x = anchor.x - (world.x * zoom);
    this.viewport.y = anchor.y - (world.y * zoom);
    this.invalidate();
  }

  keyDown(event) {
    if (event.key === "Escape") {
      this.cancelGesture();
      this.spacePan = false;
      event.preventDefault();
      return;
    }

    if (event.key === " ") {
      if (!this.spacePan) {
        this.cancelGesture();
      }
      this.spacePan = true;
      event.preventDefault();
      return;
    }

    const keys = this.semanticSourceKeys();
    if ((event.key === "ArrowRight" || event.key === "ArrowDown"
        || event.key === "ArrowLeft" || event.key === "ArrowUp") && keys.length) {
      const direction = event.key === "ArrowRight" || event.key === "ArrowDown" ? 1 : -1;
      const current = Math.max(0, keys.indexOf(this.focusedSource));
      this.focusedSource = keys[(current + direction + keys.length) % keys.length];
      this.invalidate();
      event.preventDefault();
    } else if (event.key === "Enter" && this.focusedSource) {
      const source = this.sourceByKey(this.focusedSource);
      if (source) {
        this.selectSource(source, "replace");
      }
      event.preventDefault();
    }
  }

  keyUp(event) {
    if (event.key === " ") {
      this.spacePan = false;
    }
  }

  semanticFocus(event) {
    this.sceneOwnsDocumentFocus = true;
    const sourceKey = event.target?.closest?.("[data-scene-source]")?.dataset.sceneSource;
    if (sourceKey && this.sourceKeys().includes(sourceKey)) {
      this.focusedSource = sourceKey;
      this.invalidate();
    }
  }

  reconnectStateChanged(event) {
    const state = event.detail?.state;
    if (["show", "failed"].includes(state)) {
      this.setConnected(false);
      void this.dotnetSink?.invokeMethodAsync("SceneConnectionChangedAsync", false).catch(() => {});
    } else if (state === "hide") {
      this.setConnected(true);
      void this.dotnetSink?.invokeMethodAsync("SceneConnectionChangedAsync", true).catch(() => {});
    }
  }

  contextLost(event) {
    event.preventDefault();
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
      void this.notifyFailure("contextLost");
      return;
    }

    this.invalidate();
  }

  selectSource(source, selectionMode) {
    const snapshot = this.published;
    if (!snapshot || !this.connected) {
      return;
    }

    const key = sourceKey(source);
    this.updateSelection([key], selectionMode);
    this.focusedSource = key;
    this.invalidate();

    const intent = {
      kind: "selectSources",
      buildFingerprint: this.buildFingerprint,
      sceneVersion: snapshot.sceneVersion,
      projectionVersion: snapshot.projectionVersion,
      circuitDefinitionId: snapshot.circuitDefinitionId,
      sources: [source],
      selectionMode,
    };
    if (encodedJsonBytes(intent) > BigInt(this.policy.semanticIntentBytes)) {
      void this.notifyFailure("browserPolicyExhausted");
      return;
    }
    void this.dotnetSink?.invokeMethodAsync("ReceiveSceneIntentAsync", intent)
      .catch(() => this.notifyFailure("invalidSnapshot"));
  }

  commitGesture(gesture, disableSnap) {
    const hit = gesture.hit;
    const snapshot = this.published;
    if (!snapshot) {
      return;
    }
    if (gesture.tool.kind === "select") {
      if (!hit) return;
      const start = gridPoint(gesture.startWorld, snapshot, disableSnap);
      const end = gridPoint(gesture.currentWorld, snapshot, disableSnap);
      const moved = start.x !== end.x || start.y !== end.y;
      const interaction = hit.item.interaction;
      if (moved && interaction?.interactionKind === "component") {
        this.emitIntent("moveComponents", gesture, {
          moves: [{
            component: hit.item.source,
            placement: translateComponentPlacement(interaction.placement, start, end),
          }],
          snapModifier: disableSnap ? "disableSnap" : "none",
        });
      } else if (moved && interaction?.interactionKind === "definitionPort") {
        this.emitIntent("moveDefinitionPorts", gesture, {
          moves: [{
            port: hit.item.source,
            placement: {
              position: translateGridPoint(interaction.placement.position, start, end),
              facing: interaction.placement.facing,
            },
          }],
          snapModifier: disableSnap ? "disableSnap" : "none",
        });
      } else if (moved && interaction?.interactionKind === "annotation") {
        this.emitIntent("moveAnnotations", gesture, {
          moves: [{
            annotation: hit.item.source,
            position: translateGridPoint(interaction.position, start, end),
          }],
          snapModifier: disableSnap ? "disableSnap" : "none",
        });
      } else {
        this.selectSource(hit.source, "replace");
      }
      return;
    }
    if (gesture.tool.kind === "placeComponent") {
      const committed = this.emitIntent("placeComponent", gesture, {
        target: gesture.tool.target,
        parameters: gesture.tool.parameters,
        placement: {
          origin: gridPoint(gesture.currentWorld, snapshot, disableSnap),
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
    if (startTerminal && endTerminal
        && sourceKey(hit.source) !== sourceKey(endHit.source)) {
      this.emitIntent("commitWire", gesture, {
        terminals: [startTerminal, endTerminal],
        destinationNet: null,
        newJunctionPositions: [],
        routeAdditions: [],
        routeReplacements: [],
        snapModifier: disableSnap ? "disableSnap" : "none",
      });
      return;
    }
    if (startTerminal) {
      const destinationNet = netFromHit(endHit);
      if (destinationNet) {
        this.emitIntent("commitWire", gesture, {
          terminals: [startTerminal],
          destinationNet,
          newJunctionPositions: [],
          routeAdditions: [],
          routeReplacements: [],
          snapModifier: disableSnap ? "disableSnap" : "none",
        });
      }
      return;
    }
    const interaction = hit.item.interaction;
    const start = gridPoint(gesture.startWorld, snapshot, disableSnap);
    const end = gridPoint(gesture.currentWorld, snapshot, disableSnap);
    const moved = start.x !== end.x || start.y !== end.y;
    if (interaction?.interactionKind === "wire" && moved) {
      const corner = { x: start.x, y: end.y };
      this.emitIntent("setWireRoute", gesture, {
        wireGeometry: hit.item.source,
        route: { kind: "orthogonal", points: [start, corner, end] },
        snapModifier: disableSnap ? "disableSnap" : "none",
      });
    } else if (interaction?.interactionKind === "junction") {
      this.emitIntent("removeJunction", gesture, {
        junction: hit.item.source,
        resultingPartitions: [],
        routeReplacements: [],
        routeRemovals: [],
        snapModifier: disableSnap ? "disableSnap" : "none",
      });
    } else {
      const net = netFromHit(hit);
      if (net) {
        this.emitIntent("addJunction", gesture, {
          net,
          position: end,
          routeAdditions: [],
          routeReplacements: [],
          routeRemovals: [],
          snapModifier: disableSnap ? "disableSnap" : "none",
        });
      }
    }
  }

  emitIntent(kind, gesture, payload) {
    const snapshot = this.published;
    if (!snapshot || !this.connected
        || gesture.sceneVersion !== snapshot.sceneVersion
        || gesture.projectionVersion !== snapshot.projectionVersion) {
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
    if (encodedJsonBytes(intent) > BigInt(this.policy.semanticIntentBytes)) {
      void this.notifyFailure("browserPolicyExhausted");
      return false;
    }
    void this.dotnetSink?.invokeMethodAsync("ReceiveSceneIntentAsync", intent)
      .catch(() => this.notifyFailure("invalidSnapshot"));
    return true;
  }

  updateSelection(sourceKeys, selectionMode) {
    if (selectionMode === "replace") {
      this.selectedSources = new Set(sourceKeys);
    } else if (selectionMode === "add") {
      sourceKeys.forEach((sourceKey) => this.selectedSources.add(sourceKey));
    } else {
      for (const sourceKey of sourceKeys) {
        if (this.selectedSources.has(sourceKey)) {
          this.selectedSources.delete(sourceKey);
        } else {
          this.selectedSources.add(sourceKey);
        }
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

  fitViewport() {
    if (!this.published || !this.cssWidth || !this.cssHeight) {
      return;
    }

    const bounds = this.published.bounds;
    const padding = 32;
    const maximum = Number(this.policy.zoomMillionthsMaximum) / 1_000_000;
    const minimum = Number(this.policy.zoomMillionthsMinimum) / 1_000_000;
    const zoom = Math.min(
      maximum,
      Math.max(minimum, Math.min(
        (this.cssWidth - (padding * 2)) / (bounds.right - bounds.left),
        (this.cssHeight - (padding * 2)) / (bounds.bottom - bounds.top),
      )),
    );
    this.viewport.zoom = zoom;
    this.viewport.x = (this.cssWidth / 2) - (((bounds.left + bounds.right) / 2) * zoom);
    this.viewport.y = (this.cssHeight / 2) - (((bounds.top + bounds.bottom) / 2) * zoom);
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
    const sourceKeys = [];
    const seen = new Set();
    for (const item of this.published?.items ?? []) {
      for (const sourceKey of [
        sourceKey(item.source),
        ...item.hitRegions.map((region) => region.targetSource
          ? sourceKey(region.targetSource)
          : null).filter(Boolean),
      ]) {
        if (!seen.has(sourceKey)) {
          seen.add(sourceKey);
          sourceKeys.push(sourceKey);
        }
      }
    }
    return sourceKeys;
  }

  semanticSourceKeys() {
    const available = new Set(this.sourceKeys());
    const keys = [];
    const seen = new Set();
    for (const action of this.host.querySelectorAll("[data-scene-source]")) {
      const sourceKey = action.dataset.sceneSource;
      if (sourceKey && available.has(sourceKey) && !seen.has(sourceKey)) {
        seen.add(sourceKey);
        keys.push(sourceKey);
      }
    }
    return keys;
  }

  sourceByKey(key) {
    for (const item of this.published?.items ?? []) {
      if (sourceKey(item.source) === key) {
        return item.source;
      }
      const target = item.hitRegions.find((region) => region.targetSource
        && sourceKey(region.targetSource) === key)?.targetSource;
      if (target) {
        return target;
      }
    }
    return null;
  }

  targetBySource(source) {
    const key = sourceKey(source);
    for (const item of this.published?.items ?? []) {
      if (sourceKey(item.source) === key) {
        return { bounds: translateRect(item.bounds, item.origin), item };
      }
      const region = item.hitRegions.find((candidate) => candidate.targetSource
        && sourceKey(candidate.targetSource) === key);
      if (region) {
        return { bounds: translateRect(region.bounds, item.origin), item };
      }
    }
    return null;
  }

  recoverDocumentFocus(shouldRecover) {
    if (!shouldRecover || !this.focusedSource) {
      return;
    }

    const escaped = CSS.escape(this.focusedSource);
    const fallback = this.host.querySelector(`[data-scene-source="${escaped}"]`);
    (fallback ?? this.canvas).focus({ preventScroll: true });
  }

  clearCanvas() {
    if (!this.context) {
      return;
    }
    this.context.setTransform(1, 0, 0, 1, 0, 0);
    this.context.clearRect(0, 0, this.canvas.width, this.canvas.height);
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

function validatePolicy(policy) {
  const fields = [
    "semanticIntentBytes",
    "sceneSnapshotRecordCount",
    "scenePatchRecordCount",
    "interopBatchBytes",
    "candidateTransferBytes",
    "canvasBitmapPixels",
    "canvasBitmapBytes",
    "effectiveDensityMillionths",
    "zoomMillionthsMinimum",
    "zoomMillionthsMaximum",
    "semanticTreePageItems",
    "displayListBytes",
    "spatialIndexBytes",
    "sceneCacheBytes",
    "waveformCacheBytes",
  ];
  const exactShape = new Set(["policyId", "policyRevision", ...fields]);
  const actualShape = policy ? Object.keys(policy) : [];
  if (!policy || !isToken(policy.policyId) || !isToken(policy.policyRevision)
      || actualShape.length !== exactShape.size
      || actualShape.some((field) => !exactShape.has(field))
      || fields.some((field) => !positiveSafeInteger(policy[field]))
      || BigInt(policy.zoomMillionthsMinimum) > BigInt(policy.zoomMillionthsMaximum)) {
    throw new Error("invalid Browser Policy");
  }
  return Object.freeze({ ...policy });
}

function validateReplacement(candidate, buildFingerprint, fontFingerprint, policy) {
  if (!candidate || candidate.buildFingerprint !== buildFingerprint
      || !positiveSafeInteger(candidate.sceneVersion)
      || !positiveSafeInteger(candidate.projectionVersion)
      || typeof candidate.circuitDefinitionId !== "string" || !candidate.circuitDefinitionId
      || !isLocale(candidate.uiCulture) || candidate.baseDirection !== "leftToRight") {
    throw new Error("invalid scene replacement envelope");
  }

  if (Array.isArray(candidate.diagnostics) && !candidate.items) {
    if (candidate.diagnostics.some((diagnostic) => typeof diagnostic !== "string")) {
      throw new Error("invalid unavailable scene");
    }
    return { kind: "unavailable", value: deepFreeze(candidate) };
  }

  validateSnapshot(candidate, fontFingerprint, policy);
  return { kind: "snapshot", value: freezeSnapshot(candidate) };
}

function validateSnapshot(candidate, fontFingerprint, policy) {
  if (!isDigest(candidate.fontFingerprint) || candidate.fontFingerprint !== fontFingerprint
      || typeof candidate.schematicProjectionKey !== "string" || !candidate.schematicProjectionKey
      || !validRect(candidate.bounds) || !positiveSafeInteger(candidate.gridStepPlanUnits)
      || !positiveSafeInteger(candidate.snapStepGridUnits)
      || !Array.isArray(candidate.items) || !Array.isArray(candidate.overlays)) {
    throw new Error("invalid scene snapshot");
  }
  if (encodedJsonBytes(candidate) > BigInt(policy.sceneCacheBytes)) {
    throw new Error("scene cache policy exhausted");
  }
  const displayList = candidate.items.map((item) => ({
    order: item?.order,
    bounds: item?.bounds,
    origin: item?.origin,
    operations: item?.operations,
  }));
  if (encodedJsonBytes(displayList) > BigInt(policy.displayListBytes)) {
    throw new Error("display list policy exhausted");
  }

  const sourceKeys = new Set();
  const orders = new Set();
  let previousOrder = -1;
  let records = 1;
  for (const item of candidate.items) {
    if (!validSource(item?.source, candidate.circuitDefinitionId)
        || sourceKeys.has(sourceKey(item.source)) || !Number.isSafeInteger(item.order)
        || item.order < 0 || item.order <= previousOrder
        || orders.has(item.order) || !validRect(item.bounds) || !validPoint(item.origin)
        || !Array.isArray(item.operations) || !Array.isArray(item.hitRegions)
        || !validInteraction(item.interaction, item.source, candidate.circuitDefinitionId)) {
      throw new Error("invalid scene item");
    }
    sourceKeys.add(sourceKey(item.source));
    orders.add(item.order);
    previousOrder = item.order;
    records += 1 + item.operations.length + item.hitRegions.length;
    item.operations.forEach(validateOperation);
    item.hitRegions.forEach((region) => validateHit(region, candidate.circuitDefinitionId));
    records += item.operations.reduce((sum, operation) => sum + operation.commands.length, 0);
  }
  const overlayIds = new Set();
  let previousOverlayId = null;
  for (const overlay of candidate.overlays) {
    if (!overlay || typeof overlay.id !== "string" || !overlay.id
        || overlayIds.has(overlay.id)
        || (previousOverlayId !== null && compareOrdinal(previousOverlayId, overlay.id) >= 0)
        || !validOverlay(overlay, candidate.circuitDefinitionId)) {
      throw new Error("invalid scene overlay");
    }
    overlayIds.add(overlay.id);
    previousOverlayId = overlay.id;
    records++;
  }
  if (BigInt(records) > BigInt(policy.sceneSnapshotRecordCount)) {
    throw new Error("scene snapshot record policy exhausted");
  }
}

function validatePatch(patch, published, buildFingerprint, fontFingerprint, policy) {
  if (!published || !patch || patch.buildFingerprint !== buildFingerprint
      || patch.baseSceneVersion !== published.sceneVersion
      || !positiveSafeInteger(patch.nextSceneVersion)
      || patch.nextSceneVersion <= patch.baseSceneVersion
      || patch.projectionVersion < published.projectionVersion
      || patch.circuitDefinitionId !== published.circuitDefinitionId
      || patch.uiCulture !== published.uiCulture || patch.baseDirection !== published.baseDirection
      || patch.fontFingerprint !== fontFingerprint || !Array.isArray(patch.itemUpserts)
      || !Array.isArray(patch.itemRemovals) || !Array.isArray(patch.overlayUpserts)
      || !Array.isArray(patch.overlayRemovals)) {
    return null;
  }

  try {
    const itemUpsertIds = patch.itemUpserts.map((item) => item?.source && sourceKey(item.source));
    const itemRemovalIds = patch.itemRemovals.map((source) => source && sourceKey(source));
    const overlayUpsertIds = patch.overlayUpserts.map((overlay) => overlay?.id);
    if (new Set(itemUpsertIds).size !== itemUpsertIds.length
        || new Set(itemRemovalIds).size !== itemRemovalIds.length
        || itemUpsertIds.some((id) => itemRemovalIds.includes(id))
        || new Set(overlayUpsertIds).size !== overlayUpsertIds.length
        || new Set(patch.overlayRemovals).size !== patch.overlayRemovals.length
        || patch.overlayRemovals.some((id) => typeof id !== "string" || !id)
        || overlayUpsertIds.some((id) => patch.overlayRemovals.includes(id))) {
      throw new Error();
    }
    let patchRecords = patch.itemUpserts.length + patch.itemRemovals.length
      + patch.overlayUpserts.length + patch.overlayRemovals.length;
    patchRecords += patch.itemUpserts.reduce((sum, item) => sum + item.operations.length
      + item.hitRegions.length
      + item.operations.reduce((commandSum, operation) => commandSum + operation.commands.length, 0), 0);
    if (BigInt(patchRecords) > BigInt(policy.scenePatchRecordCount)) throw new Error();
    const items = new Map(published.items.map((item) => [sourceKey(item.source), item]));
    for (const removal of patch.itemRemovals) {
      if (!validSource(removal, published.circuitDefinitionId)) throw new Error();
      items.delete(sourceKey(removal));
    }
    for (const upsert of patch.itemUpserts) items.set(sourceKey(upsert.source), upsert);
    const overlays = new Map(published.overlays.map((overlay) => [overlay.id, overlay]));
    patch.overlayRemovals.forEach((id) => overlays.delete(id));
    patch.overlayUpserts.forEach((overlay) => overlays.set(overlay.id, overlay));
    const candidate = {
      buildFingerprint,
      sceneVersion: patch.nextSceneVersion,
      projectionVersion: patch.projectionVersion,
      circuitDefinitionId: patch.circuitDefinitionId,
      uiCulture: patch.uiCulture,
      baseDirection: patch.baseDirection,
      schematicProjectionKey: patch.schematicProjectionKey,
      bounds: patch.bounds,
      gridStepPlanUnits: patch.gridStepPlanUnits,
      snapStepGridUnits: patch.snapStepGridUnits,
      fontFingerprint: patch.fontFingerprint,
      items: [...items.values()].sort((left, right) => left.order - right.order),
      overlays: [...overlays.values()].sort((left, right) => compareOrdinal(left.id, right.id)),
    };
    validateSnapshot(candidate, fontFingerprint, policy);
    return candidate;
  } catch {
    return null;
  }
}

function freezeSnapshot(candidate) {
  return deepFreeze(candidate);
}

function deepFreeze(value) {
  if (value && typeof value === "object" && !Object.isFrozen(value)) {
    Object.values(value).forEach(deepFreeze);
    Object.freeze(value);
  }
  return value;
}

function buildSpatialIndex(snapshot, policy) {
  const index = new Map();
  const maximumBytes = BigInt(policy.spatialIndexBytes);
  let observedBytes = 0n;
  for (const item of snapshot.items) {
    for (const region of item.hitRegions) {
      const bounds = translateRect(region.bounds, item.origin);
      const minimumX = Math.floor(bounds.left / spatialCellSize);
      const minimumY = Math.floor(bounds.top / spatialCellSize);
      const maximumX = Math.floor(bounds.right / spatialCellSize);
      const maximumY = Math.floor(bounds.bottom / spatialCellSize);
      const columns = maximumX - minimumX + 1;
      const rows = maximumY - minimumY + 1;
      if (!Number.isSafeInteger(minimumX) || !Number.isSafeInteger(minimumY)
          || !Number.isSafeInteger(maximumX) || !Number.isSafeInteger(maximumY)
          || !Number.isSafeInteger(columns) || !Number.isSafeInteger(rows)
          || columns <= 0 || rows <= 0) {
        throw new Error("spatial index coordinate range is invalid");
      }

      const source = region.targetSource ?? item.source;
      const entryBytes = BigInt(spatialEntryBaseBytes
        + textEncoder.encode(sourceKey(source)).byteLength
        + textEncoder.encode(region.localId).byteLength);
      const candidateBytes = observedBytes + (BigInt(columns) * BigInt(rows) * entryBytes);
      if (candidateBytes > maximumBytes) {
        throw new Error("spatial index policy exhausted");
      }
      observedBytes = candidateBytes;

      for (let cellX = minimumX; cellX <= maximumX; cellX++) {
        for (let cellY = minimumY; cellY <= maximumY; cellY++) {
          const key = `${cellX}:${cellY}`;
          const cell = index.get(key) ?? [];
          cell.push({ item, region });
          index.set(key, cell);
        }
      }
    }
  }
  return index;
}

function spatialCellKey(x, y) {
  return `${Math.floor(x / spatialCellSize)}:${Math.floor(y / spatialCellSize)}`;
}

function validateOperation(operation) {
  if (!operation || !["stroke", "fill", "text"].includes(operation.kind)
      || typeof operation.role !== "string" || !validRectAllowDegenerate(operation.bounds)
      || !Array.isArray(operation.commands)) throw new Error("invalid draw operation");
  for (const command of operation.commands) {
    if (!command || !["move", "line", "cubic", "close"].includes(command.kind)
        || !finiteNumbers(command.x, command.y, command.control1X, command.control1Y,
          command.control2X, command.control2Y)) throw new Error("invalid path command");
  }
  if (operation.kind === "stroke" && (!Number.isFinite(operation.width) || operation.width <= 0
      || !Array.isArray(operation.dashPattern)
      || operation.dashPattern.length % 2 !== 0
      || operation.dashPattern.some((value) => !Number.isFinite(value) || value <= 0)
      || !["butt", "round", "square"].includes(operation.lineCap)
      || !["miter", "round", "bevel"].includes(operation.lineJoin)
      || !Number.isSafeInteger(operation.miterLimitRatio)
      || (operation.lineJoin === "miter" && operation.miterLimitRatio <= 0)
      || (operation.lineJoin !== "miter" && operation.miterLimitRatio !== 0))) {
    throw new Error("invalid stroke");
  }
  if (operation.kind === "fill" && !["nonzero", "evenodd"].includes(operation.fillRule)) {
    throw new Error("invalid fill");
  }
  if (operation.kind === "text" && (typeof operation.text !== "string"
      || !validPoint(operation.origin) || !isAlignment(operation.alignment)
      || !isDirection(operation.direction) || !isLocale(operation.locale))) throw new Error("invalid text");
}

function validateHit(region, definitionId) {
  if (!region || typeof region.localId !== "string" || !["port", "body", "label"].includes(region.kind)
      || !["rect", "circle", "polygon"].includes(region.shape)
      || !validRectAllowDegenerate(region.bounds)
      || (region.targetSource && !validSource(region.targetSource, definitionId))) {
    throw new Error("invalid hit region");
  }
  if (region.shape === "circle" && (!validPoint(region.center) || !Number.isFinite(region.radius)
      || region.radius <= 0)) throw new Error("invalid circle hit region");
  if (region.shape === "polygon" && (!Array.isArray(region.points) || region.points.length < 3
      || region.points.some((point) => !validPoint(point)))) throw new Error("invalid polygon hit region");
}

function drawOperation(context, operation, styles, symbolFontFamily) {
  if (operation.kind === "text") {
    context.save();
    context.fillStyle = cssColor(styles, "--ll-ink", "#172124");
    context.font = `100px ${symbolFontFamily}`;
    context.textAlign = canvasAlignment(operation.alignment, operation.direction);
    context.textBaseline = "alphabetic";
    context.direction = operation.direction;
    context.fillText(operation.text, operation.origin.x, operation.origin.y);
    context.restore();
    return;
  }

  context.beginPath();
  for (const command of operation.commands) {
    if (command.kind === "move") context.moveTo(command.x, command.y);
    else if (command.kind === "line") context.lineTo(command.x, command.y);
    else if (command.kind === "cubic") context.bezierCurveTo(
      command.control1X, command.control1Y, command.control2X, command.control2Y,
      command.x, command.y,
    );
    else context.closePath();
  }
  if (operation.kind === "stroke") {
    context.strokeStyle = cssColor(styles, "--ll-ink", "#172124");
    context.lineWidth = operation.width;
    context.lineCap = operation.lineCap;
    context.lineJoin = operation.lineJoin;
    if (operation.lineJoin === "miter") {
      context.miterLimit = operation.miterLimitRatio;
    }
    context.setLineDash(operation.dashPattern);
    context.stroke();
  } else {
    context.fillStyle = operation.role === "background"
      ? cssColor(styles, "--ll-canvas", "#ffffff")
      : cssColor(styles, "--ll-ink", "#172124");
    context.fill(operation.fillRule);
  }
}

function contains(region, point) {
  if (region.shape === "rect") return point.x >= region.bounds.left && point.x <= region.bounds.right
    && point.y >= region.bounds.top && point.y <= region.bounds.bottom;
  if (region.shape === "circle") {
    const x = point.x - region.center.x;
    const y = point.y - region.center.y;
    return (x * x) + (y * y) <= region.radius * region.radius;
  }
  let inside = false;
  for (let index = 0, previous = region.points.length - 1; index < region.points.length; previous = index++) {
    const currentPoint = region.points[index];
    const priorPoint = region.points[previous];
    if (((currentPoint.y > point.y) !== (priorPoint.y > point.y))
        && point.x < ((priorPoint.x - currentPoint.x) * (point.y - currentPoint.y)
          / (priorPoint.y - currentPoint.y)) + currentPoint.x) inside = !inside;
  }
  return inside;
}

function hitPriority(item, region) {
  if (region.kind === "port") return 5;
  if (item.source.entityKind === "junction") return 4;
  if (item.source.entityKind === "componentInstance") return 3;
  if (item.source.entityKind === "wireGeometry") return 2;
  return 1;
}

function translateRect(rect, origin) {
  return { left: rect.left + origin.x, top: rect.top + origin.y,
    right: rect.right + origin.x, bottom: rect.bottom + origin.y };
}

function intersects(left, right) {
  return left.left <= right.right && left.right >= right.left
    && left.top <= right.bottom && left.bottom >= right.top;
}

function expandRect(rect, margin) {
  return {
    left: rect.left - margin,
    top: rect.top - margin,
    right: rect.right + margin,
    bottom: rect.bottom + margin,
  };
}

function validSource(source, definitionId) {
  const shape = source && Object.keys(source);
  return source && shape.length === 4
    && ["circuitDefinitionId", "entityKind", "entityId", "portId"]
      .every((field) => shape.includes(field))
    && source.circuitDefinitionId === definitionId
    && ["definitionPort", "componentInstance", "instancePort", "net", "junction", "wireGeometry", "annotation"]
      .includes(source.entityKind)
    && typeof source.entityId === "string" && source.entityId.length > 0
    && (source.entityKind === "instancePort"
      ? typeof source.portId === "string" && source.portId.length > 0
      : source.portId === null);
}

function validInteraction(interaction, source, definitionId) {
  if (!interaction || typeof interaction.interactionKind !== "string") return false;
  if (interaction.interactionKind === "component") {
    return source.entityKind === "componentInstance"
      && validComponentPlacement(interaction.placement);
  }
  if (interaction.interactionKind === "definitionPort") {
    return source.entityKind === "definitionPort"
      && validGridPoint(interaction.placement?.position)
      && ["north", "east", "south", "west"].includes(interaction.placement?.facing);
  }
  if (interaction.interactionKind === "annotation") {
    return source.entityKind === "annotation" && validGridPoint(interaction.position);
  }
  if (interaction.interactionKind === "net") {
    return source.entityKind === "net" && sameSource(interaction.net, source);
  }
  if (interaction.interactionKind === "wire") {
    return source.entityKind === "wireGeometry"
      && validNetSource(interaction.net, definitionId) && validRoute(interaction.route);
  }
  return interaction.interactionKind === "junction"
    && source.entityKind === "junction" && validNetSource(interaction.net, definitionId);
}

function validOverlay(overlay, definitionId) {
  if (!overlay || typeof overlay.id !== "string" || !overlay.id
      || !validSource(overlay.source, definitionId)) return false;
  if (overlay.kind === "selection") return ["primary", "member"].includes(overlay.role);
  if (overlay.kind === "keyboardFocus") return true;
  if (overlay.kind === "diagnosticMarker") {
    return typeof overlay.diagnosticCode === "string" && overlay.diagnosticCode.length > 0
      && ["info", "warning", "error"].includes(overlay.severity)
      && Number.isSafeInteger(overlay.diagnosticOrdinal) && overlay.diagnosticOrdinal >= 0;
  }
  if (overlay.kind === "probeAnchor") {
    return typeof overlay.probeId === "string" && overlay.probeId.length > 0
      && validElaboratedNet(overlay.net, definitionId)
      && sameSource(overlay.source, overlay.net.authoredNet)
      && validPoint(overlay.point)
      && Number.isSafeInteger(overlay.appearanceOrdinal) && overlay.appearanceOrdinal >= 0;
  }
  if (overlay.kind === "liveNetValue") {
    if (!validElaboratedNet(overlay.net, definitionId)
        || !sameSource(overlay.source, overlay.net.authoredNet)
        || typeof overlay.sessionId !== "string" || !overlay.sessionId
        || !positiveSafeInteger(overlay.sessionVersion)
        || !positiveSafeInteger(overlay.value?.width)
        || overlay.value?.encoding !== "logic4-2bit-v1"
        || typeof overlay.value?.data !== "string") return false;
    try {
      return decodeBase64(overlay.value.data).byteLength
        === Math.ceil(overlay.value.width / 4);
    } catch {
      return false;
    }
  }
  return false;
}

function validElaboratedNet(net, definitionId) {
  return net && validNetSource(net.authoredNet, definitionId)
    && validHierarchyPath(net.hierarchyPath);
}

function validNetSource(source, definitionId) {
  return validSource(source, definitionId) && source.entityKind === "net";
}

function validHierarchyPath(path) {
  return path && typeof path.entryCircuitDefinitionId === "string"
    && path.entryCircuitDefinitionId.length > 0 && Array.isArray(path.steps)
    && path.steps.every((step) => step
      && typeof step.containingCircuitDefinitionId === "string"
      && step.containingCircuitDefinitionId.length > 0
      && typeof step.componentInstanceId === "string"
      && step.componentInstanceId.length > 0);
}

function validRoute(route) {
  return route?.kind === "unrouted"
    || (route?.kind === "orthogonal" && Array.isArray(route.points)
      && route.points.every(validGridPoint));
}

function validTool(tool) {
  if (!tool || typeof tool.kind !== "string") return false;
  if (["select", "wire", "pan"].includes(tool.kind)) {
    return Object.keys(tool).length === 1;
  }
  if (tool.kind === "probe") {
    return validHierarchyPath(tool.hierarchyPath);
  }
  return tool.kind === "placeComponent" && validComponentTarget(tool.target)
    && Array.isArray(tool.parameters) && tool.parameters.every((parameter) => parameter
      && typeof parameter.parameterId === "string" && parameter.parameterId.length > 0
      && parameter.value && typeof parameter.value.kind === "string")
    && (tool.displayName === null || typeof tool.displayName === "string")
    && typeof tool.pinned === "boolean";
}

function validComponentTarget(target) {
  return target?.kind === "libraryContract"
    ? typeof target.libraryId === "string" && target.libraryId.length > 0
      && typeof target.contractId === "string" && target.contractId.length > 0
    : target?.kind === "circuitDefinition"
      && typeof target.circuitDefinitionId === "string"
      && target.circuitDefinitionId.length > 0;
}

function validComponentPlacement(placement) {
  return placement && validGridPoint(placement.origin)
    && Number.isSafeInteger(placement.quarterTurnsClockwise)
    && placement.quarterTurnsClockwise >= 0 && placement.quarterTurnsClockwise <= 3
    && typeof placement.reflected === "boolean";
}

function validGridPoint(point) {
  return point && Number.isSafeInteger(point.x) && Number.isSafeInteger(point.y)
    && point.x >= -2147483648 && point.x <= 2147483647
    && point.y >= -2147483648 && point.y <= 2147483647;
}

function sameSource(left, right) {
  return left && right && sourceKey(left) === sourceKey(right);
}

function terminalFromSource(source) {
  if (source?.entityKind === "definitionPort") {
    return {
      kind: "definitionTerminal",
      circuitDefinitionId: source.circuitDefinitionId,
      portId: source.entityId,
    };
  }
  if (source?.entityKind === "instancePort") {
    return {
      kind: "instanceTerminal",
      circuitDefinitionId: source.circuitDefinitionId,
      componentInstanceId: source.entityId,
      portId: source.portId,
    };
  }
  return null;
}

function netFromHit(hit) {
  if (!hit) return null;
  if (hit.source.entityKind === "net") return hit.source;
  const interaction = hit.item?.interaction;
  return ["wire", "junction", "net"].includes(interaction?.interactionKind)
    ? interaction.net
    : null;
}

function gridPoint(world, snapshot, disableSnap) {
  const snap = disableSnap ? 1 : snapshot.snapStepGridUnits;
  return {
    x: checkedInteger(roundHalfNegativeInfinity(
      world.x / snapshot.gridStepPlanUnits / snap) * snap),
    y: checkedInteger(roundHalfNegativeInfinity(
      world.y / snapshot.gridStepPlanUnits / snap) * snap),
  };
}

function roundHalfNegativeInfinity(value) {
  return Math.ceil(value - 0.5);
}

function translateGridPoint(point, start, end) {
  return {
    x: checkedInteger(point.x + end.x - start.x),
    y: checkedInteger(point.y + end.y - start.y),
  };
}

function translateComponentPlacement(placement, start, end) {
  return {
    origin: translateGridPoint(placement.origin, start, end),
    quarterTurnsClockwise: placement.quarterTurnsClockwise,
    reflected: placement.reflected,
  };
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

function sourceKey(source) {
  return [source.circuitDefinitionId, source.entityKind, source.entityId, source.portId ?? ""]
    .map((part) => `${part.length}:${part}`)
    .join("");
}

function validRect(rect) {
  return validRectAllowDegenerate(rect) && rect.right > rect.left && rect.bottom > rect.top;
}

function validRectAllowDegenerate(rect) {
  return rect && finiteNumbers(rect.left, rect.top, rect.right, rect.bottom)
    && rect.right >= rect.left && rect.bottom >= rect.top;
}

function validPoint(point) { return point && finiteNumbers(point.x, point.y); }
function finiteNumbers(...values) { return values.every(Number.isFinite); }
function positiveSafeInteger(value) { return Number.isSafeInteger(value) && value > 0; }
function isLocale(value) { return value === "en-US" || value === "zh-CN"; }
function isDirection(value) { return value === "ltr" || value === "rtl"; }
function isAlignment(value) { return ["start", "center", "end"].includes(value); }
function isTextRole(value) { return ["symbol", "portlabel", "dependency", "extensionmark"].includes(value); }
function isToken(value) { return typeof value === "string" && /^[A-Za-z0-9._-]+$/.test(value); }
function isDigest(value) { return typeof value === "string" && /^[0-9a-f]{64}$/.test(value); }
function checkedInteger(value) { if (!Number.isSafeInteger(value) || value < -2147483648 || value > 2147483647) throw new Error("integer overflow"); return value; }
function canvasAlignment(value, direction) {
  if (value === "center") return "center";
  if (value === "start") return direction === "ltr" ? "left" : "right";
  return direction === "ltr" ? "right" : "left";
}
function cssColor(styles, name, fallback) { return styles.getPropertyValue(name).trim() || fallback; }
function compareOrdinal(left, right) { return left < right ? -1 : left > right ? 1 : 0; }
function encodedJsonBytes(value) { return BigInt(textEncoder.encode(JSON.stringify(value)).byteLength); }
function decodeBase64(value) { const binary = atob(value); return Uint8Array.from(binary, (character) => character.charCodeAt(0)); }
async function sha256(bytes) { const digest = await crypto.subtle.digest("SHA-256", bytes); return [...new Uint8Array(digest)].map((value) => value.toString(16).padStart(2, "0")).join(""); }
