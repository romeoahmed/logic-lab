const mountedHandles = new WeakMap();
const encoder = new TextEncoder();
const decoder = new TextDecoder("utf-8", { fatal: true });
const decodedVectors = new WeakMap();
const logicalTimeMaximum = 18_446_744_073_709_551_615n;
const timeBoundaryMaximum = 18_446_744_073_709_551_616n;
const contextRestoreTimeoutMilliseconds = 2_000;
const interopEnvelopeBytes = 512;
const minimumBase64QuantumBytes = 4;
const recordShapes = Object.freeze({
  policy: shape(
    "policyId",
    "policyRevision",
    "semanticIntentBytes",
    "interopBatchBytes",
    "candidateTransferBytes",
    "canvasBitmapPixels",
    "effectiveDensityMillionths",
  ),
  snapshot: shape(
    "buildFingerprint",
    "waveformVersion",
    "projectionVersion",
    "sessionId",
    "sessionVersion",
    "compilationArtifactKey",
    "rows",
    "viewState",
    "trace",
  ),
  viewState: shape("viewport", "primaryCursor", "secondaryCursor"),
  row: shape(
    "probeId",
    "width",
    "displayOrdinal",
    "appearanceOrdinal",
    "pattern",
    "binding",
  ),
  vector: shape("width", "encoding", "data"),
  cursor: shape("kind", "logicalTime"),
  range: shape("startInclusive", "endExclusive"),
  gap: shape("range"),
  transitions: shape("kind", "segments"),
  summary: shape("kind", "aggregation", "segments"),
  unavailable: shape("kind", "gap"),
  transitionSegment: shape("probeId", "range", "value", "transitionAtStart"),
  summarySegment: shape(
    "probeId",
    "range",
    "firstValue",
    "lastValue",
    "hadTransition",
    "hadMixedValues",
  ),
});

export function mount(host, buildFingerprint, policy, dotnetSink) {
  const existing = mountedHandles.get(host);
  if (
    existing &&
    existing.buildFingerprint === buildFingerprint &&
    !existing.destroyed &&
    !existing.failed
  ) {
    return existing;
  }

  existing?.destroy();
  const handle = new WaveformHandle(
    host,
    buildFingerprint,
    validatePolicy(policy),
    dotnetSink,
  );
  mountedHandles.set(host, handle);
  return handle;
}

class WaveformHandle {
  constructor(host, buildFingerprint, policy, dotnetSink) {
    this.host = host;
    this.buildFingerprint = buildFingerprint;
    this.policy = policy;
    this.dotnetSink = dotnetSink;
    this.canvas = host.querySelector("[data-waveform-canvas]");
    this.probeSpine = host.querySelector("[data-probe-spine]");
    this.context = this.canvas?.getContext("2d", { alpha: false }) ?? null;
    this.published = null;
    this.transientViewport = null;
    this.transientCursor = null;
    this.interactionMode = "commitEnabled";
    this.gesture = null;
    this.pendingIntent = null;
    this.viewportCommitTimer = 0;
    this.contextRestoreTimer = 0;
    this.pendingFrame = 0;
    this.dirty = false;
    this.cssWidth = 0;
    this.cssHeight = 0;
    this.density = 1;
    this.traceByProbe = new Map();
    this.rowLayoutCache = null;
    this.transfers = new Map();
    this.failed = false;
    this.destroyed = false;
    this.abortController = new AbortController();
    this.resizeObserver = null;
    this.removalObserver = null;
    this.densityMedia = null;
    this.onDensityChange = null;
    this.installRemovalObserver();

    if (!this.canvas || !this.context) {
      this.failClosed();
      return;
    }

    this.installListeners();
    this.installObservers();
    this.resize();
  }

  beginTransfer(transferId, kind, byteLength, digest) {
    this.ensureLive();
    if (
      !isToken(transferId) ||
      kind !== "snapshot" ||
      !Number.isSafeInteger(byteLength) ||
      byteLength <= 0 ||
      BigInt(byteLength) > BigInt(this.policy.candidateTransferBytes) ||
      !isDigest(digest) ||
      this.transfers.has(transferId)
    ) {
      throw new Error("invalid Waveform transfer envelope");
    }

    this.transfers.set(transferId, {
      byteLength,
      digest,
      chunks: [],
      receivedBytes: 0,
      nextOrdinal: 0,
    });
  }

  appendTransfer(transferId, ordinal, chunk) {
    this.ensureLive();
    const transfer = this.transfers.get(transferId);
    if (
      !transfer ||
      ordinal !== transfer.nextOrdinal ||
      typeof chunk !== "string" ||
      chunk.length === 0 ||
      encoder.encode(chunk).byteLength + interopEnvelopeBytes > this.policy.interopBatchBytes
    ) {
      this.transfers.delete(transferId);
      throw new Error("invalid Waveform transfer chunk");
    }

    const bytes = decodeBase64(chunk);
    if (transfer.receivedBytes + bytes.byteLength > transfer.byteLength) {
      this.transfers.delete(transferId);
      throw new Error("Waveform transfer exceeds its declared length");
    }
    transfer.chunks.push(bytes);
    transfer.receivedBytes += bytes.byteLength;
    transfer.nextOrdinal += 1;
  }

  async commitTransfer(transferId) {
    this.ensureLive();
    const transfer = this.transfers.get(transferId);
    this.transfers.delete(transferId);
    if (!transfer) return false;

    try {
      const candidateBytes = concatenate(transfer.chunks);
      if (
        candidateBytes.byteLength !== transfer.byteLength ||
        (await sha256(candidateBytes)) !== transfer.digest
      ) {
        return false;
      }

      const candidate = JSON.parse(decoder.decode(candidateBytes));
      const next = validateSnapshot(candidate, this.buildFingerprint);
      const traceByProbe = indexTrace(next.trace);

      this.cancelGesture();
      this.published = deepFreeze(next);
      this.traceByProbe = traceByProbe;
      this.rowLayoutCache = null;
      this.transientViewport = null;
      this.transientCursor = null;
      this.cancelViewportCommit();
      this.pendingIntent = null;
      this.invalidate();
      return true;
    } catch {
      return false;
    }
  }

  abortTransfer(transferId) {
    this.transfers.delete(transferId);
  }

  setInteractionMode(mode) {
    this.ensureLive();
    if (mode !== "commitEnabled" && mode !== "localOnly") {
      throw new Error("invalid Waveform interaction mode");
    }
    this.interactionMode = mode;
    if (mode === "localOnly") {
      this.cancelGesture();
      this.cancelViewportCommit();
    }
  }

  reconnectStateChanged(event) {
    const state = event.detail?.state;
    if (["show", "paused", "retrying", "failed", "rejected"].includes(state)) {
      this.setInteractionMode("localOnly");
    } else if (state === "hide") {
      this.setInteractionMode("commitEnabled");
    }
  }

  installListeners() {
    const signal = this.abortController.signal;
    this.canvas.addEventListener("pointerdown", (event) => this.pointerDown(event), { signal });
    this.canvas.addEventListener("pointermove", (event) => this.pointerMove(event), { signal });
    this.canvas.addEventListener("pointerup", (event) => this.pointerUp(event), { signal });
    this.canvas.addEventListener(
      "pointercancel",
      (event) => this.cancelGesture(event.pointerId),
      { signal },
    );
    this.canvas.addEventListener(
      "lostpointercapture",
      (event) => this.cancelGesture(event.pointerId),
      { signal },
    );
    this.probeSpine?.addEventListener("scroll", () => {
      this.rowLayoutCache = null;
      this.invalidate();
    }, { signal, passive: true });
    this.canvas.addEventListener("wheel", (event) => this.wheel(event), {
      signal,
      passive: false,
    });
    this.canvas.addEventListener("keydown", (event) => this.keyDown(event), { signal });
    this.canvas.addEventListener("contextlost", (event) => {
      event.preventDefault();
      this.cancelGesture();
      this.cancelContextRestore();
      this.contextRestoreTimer = window.setTimeout(
        () => this.failClosed(),
        contextRestoreTimeoutMilliseconds,
      );
    }, { signal });
    this.canvas.addEventListener("contextrestored", () => {
      if (this.failed) return;
      this.cancelContextRestore();
      this.context = this.canvas.getContext("2d", { alpha: false });
      if (!this.context) {
        this.failClosed();
        return;
      }
      this.resize();
      this.invalidate();
    }, { signal });
    // ASP.NET Core publishes Interactive Server reconnect state on this element:
    // https://learn.microsoft.com/aspnet/core/blazor/fundamentals/signalr?view=aspnetcore-10.0#reflect-the-server-side-connection-state-in-the-ui
    this.reconnectModal = document.getElementById("components-reconnect-modal");
    this.reconnectModal?.addEventListener(
      "components-reconnect-state-changed",
      (event) => this.reconnectStateChanged(event),
      { signal },
    );
  }

  installObservers() {
    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(this.canvas);
    if (this.probeSpine) this.resizeObserver.observe(this.probeSpine);
    this.onDensityChange = () => {
      if (this.destroyed) return;
      this.armDensityListener();
      this.resize();
    };
    this.armDensityListener();
  }

  armDensityListener() {
    this.densityMedia?.removeEventListener("change", this.onDensityChange);
    this.densityMedia = window.matchMedia(`(resolution: ${window.devicePixelRatio}dppx)`);
    this.densityMedia.addEventListener("change", this.onDensityChange, { once: true });
  }

  installRemovalObserver() {
    const root = this.host.parentElement ?? document.body;
    this.removalObserver = new MutationObserver(() => {
      if (!this.host.isConnected) this.destroy();
    });
    this.removalObserver.observe(root, { childList: true, subtree: true });
  }

  resize() {
    if (!this.canvas || !this.context || this.destroyed || this.failed) return;
    this.rowLayoutCache = null;
    const bounds = this.canvas.getBoundingClientRect();
    if (!Number.isFinite(bounds.width) || !Number.isFinite(bounds.height)) return;
    if (bounds.width <= 0 || bounds.height <= 0) {
      this.cssWidth = 0;
      this.cssHeight = 0;
      return;
    }

    const maximumDensity = this.policy.effectiveDensityMillionths / 1_000_000;
    const density = Math.min(Math.max(1, window.devicePixelRatio || 1), maximumDensity);
    const width = Math.ceil(bounds.width * density);
    const height = Math.ceil(bounds.height * density);
    if (!Number.isSafeInteger(width) || !Number.isSafeInteger(height)) {
      this.failClosed();
      return;
    }
    const pixels = BigInt(width) * BigInt(height);
    if (pixels > BigInt(this.policy.canvasBitmapPixels)) {
      this.failClosed({
        dimension: "canvas_bitmap_pixels",
        observed: pixels.toString(),
      });
      return;
    }

    this.cssWidth = bounds.width;
    this.cssHeight = bounds.height;
    this.density = density;
    if (this.canvas.width !== width || this.canvas.height !== height) {
      try {
        this.canvas.width = width;
        this.canvas.height = height;
        this.context = this.canvas.getContext("2d", { alpha: false });
      } catch {
        this.failClosed();
        return;
      }
      if (!this.context) {
        this.failClosed();
        return;
      }
    }
    this.invalidate();
  }

  pointerDown(event) {
    if (
      !this.published ||
      event.button !== 0 ||
      event.isPrimary === false ||
      this.gesture ||
      this.interactionMode !== "commitEnabled"
    ) return;
    const kind = event.shiftKey ? "secondary" : "primary";
    this.gesture = {
      pointerId: event.pointerId,
      kind,
      logicalTime: this.timeAt(event.offsetX),
      waveformVersion: this.published.waveformVersion,
    };
    try {
      this.canvas.setPointerCapture(event.pointerId);
    } catch {
      this.gesture = null;
      return;
    }
    this.transientCursor = { kind, logicalTime: this.gesture.logicalTime };
    this.invalidate();
  }

  pointerMove(event) {
    if (!this.gesture || this.gesture.pointerId !== event.pointerId) return;
    this.gesture.logicalTime = this.timeAt(event.offsetX);
    this.transientCursor = {
      kind: this.gesture.kind,
      logicalTime: this.gesture.logicalTime,
    };
    this.invalidate();
  }

  pointerUp(event) {
    if (!this.gesture || this.gesture.pointerId !== event.pointerId) return;
    const gesture = this.gesture;
    this.releasePointerCapture(event.pointerId);
    this.gesture = null;
    this.transientCursor = null;
    this.invalidate();
    if (
      this.published?.waveformVersion === gesture.waveformVersion &&
      this.interactionMode === "commitEnabled"
    ) {
      void this.emit("setCursor", {
        cursorKind: gesture.kind,
        logicalTime: gesture.logicalTime.toString(),
      });
    }
  }

  cancelGesture(pointerId = null) {
    if (!this.gesture || (pointerId !== null && this.gesture.pointerId !== pointerId)) return;
    this.releasePointerCapture(this.gesture.pointerId);
    this.gesture = null;
    this.transientCursor = null;
    this.invalidate();
  }

  releasePointerCapture(pointerId) {
    try {
      if (this.canvas?.hasPointerCapture(pointerId)) {
        this.canvas.releasePointerCapture(pointerId);
      }
    } catch {
      // Capture may already have been released by browser lifecycle changes.
    }
  }

  wheel(event) {
    if (!this.published) return;
    event.preventDefault();
    const viewport = this.activeViewport();
    const start = BigInt(viewport.startInclusive);
    const end = BigInt(viewport.endExclusive);
    const span = end - start;
    let nextStart;
    let nextEnd;
    let targetSpan;
    if (event.shiftKey || Math.abs(event.deltaX) > Math.abs(event.deltaY)) {
      const dominantDelta = Math.abs(event.deltaX) > Math.abs(event.deltaY)
        ? event.deltaX
        : event.deltaY;
      const direction = dominantDelta >= 0 ? 1n : -1n;
      const step = span / 8n || 1n;
      targetSpan = span;
      nextStart = direction > 0n ? start + step : start > step ? start - step : 0n;
      nextEnd = nextStart + span;
    } else {
      const zoomIn = event.deltaY < 0;
      const nextSpan = zoomIn ? span / 2n || 1n : span * 2n;
      targetSpan = nextSpan;
      const ratio = clamp(event.offsetX / Math.max(1, this.cssWidth), 0, 1);
      const anchor = start + proportionalOffset(span, ratio);
      const left = proportionalOffset(nextSpan, ratio);
      nextStart = anchor > left ? anchor - left : 0n;
      nextEnd = nextStart + nextSpan;
    }

    if (nextEnd > timeBoundaryMaximum) {
      nextEnd = timeBoundaryMaximum;
      nextStart = nextEnd > targetSpan ? nextEnd - targetSpan : 0n;
    }
    if (nextEnd <= nextStart) return;
    this.transientViewport = {
      startInclusive: nextStart.toString(),
      endExclusive: nextEnd.toString(),
    };
    this.invalidate();
    if (this.interactionMode === "commitEnabled") this.scheduleViewportCommit();
  }

  scheduleViewportCommit() {
    this.cancelViewportCommit();
    const waveformVersion = this.published.waveformVersion;
    this.viewportCommitTimer = window.setTimeout(() => {
      this.viewportCommitTimer = 0;
      if (
        this.published?.waveformVersion === waveformVersion &&
        this.transientViewport &&
        this.interactionMode === "commitEnabled"
      ) {
        if (this.pendingIntent) {
          this.scheduleViewportCommit();
          return;
        }
        void this.emit("setViewport", { viewport: this.transientViewport });
      }
    }, 120);
  }

  cancelViewportCommit() {
    if (!this.viewportCommitTimer) return;
    clearTimeout(this.viewportCommitTimer);
    this.viewportCommitTimer = 0;
  }

  cancelContextRestore() {
    if (!this.contextRestoreTimer) return;
    clearTimeout(this.contextRestoreTimer);
    this.contextRestoreTimer = 0;
  }

  keyDown(event) {
    if (!this.published || this.interactionMode !== "commitEnabled") return;
    if (event.key === "Escape") {
      this.cancelGesture();
      return;
    }
    if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
      event.preventDefault();
      const cursor = this.published.viewState.primaryCursor;
      const viewport = this.activeViewport();
      const start = BigInt(viewport.startInclusive);
      const end = BigInt(viewport.endExclusive);
      const current = cursor ? BigInt(cursor.logicalTime) : start;
      const next = event.key === "ArrowLeft"
        ? current > start ? current - 1n : start
        : current + 1n < end ? current + 1n : end - 1n;
      void this.emit("setCursor", { cursorKind: "primary", logicalTime: next.toString() });
    }
  }

  timeAt(cssX) {
    const viewport = this.activeViewport();
    const start = BigInt(viewport.startInclusive);
    const end = BigInt(viewport.endExclusive);
    const span = end - start;
    const ratio = clamp(cssX / Math.max(1, this.cssWidth), 0, 0.999999999);
    return start + proportionalOffset(span, ratio);
  }

  activeViewport() {
    return this.transientViewport ?? this.published.viewState.viewport;
  }

  async emit(kind, payload) {
    if (
      !this.published ||
      this.interactionMode !== "commitEnabled" ||
      this.pendingIntent
    ) return false;
    const intent = {
      kind,
      buildFingerprint: this.published.buildFingerprint,
      waveformVersion: this.published.waveformVersion,
      projectionVersion: this.published.projectionVersion,
      sessionId: this.published.sessionId,
      sessionVersion: this.published.sessionVersion,
      compilationArtifactKey: this.published.compilationArtifactKey,
      ...payload,
    };
    const intentBytes = encoder.encode(JSON.stringify(intent)).byteLength;
    if (intentBytes > this.policy.semanticIntentBytes) {
      this.failClosed({
        dimension: "semantic_intent_bytes",
        observed: intentBytes.toString(),
      });
      return false;
    }
    const pendingIntent = Object.freeze({
      kind,
      waveformVersion: this.published.waveformVersion,
    });
    this.pendingIntent = pendingIntent;
    try {
      await this.dotnetSink.invokeMethodAsync("ReceiveWaveformIntentAsync", intent);
      return true;
    } catch {
      this.failClosed();
      return false;
    } finally {
      if (this.pendingIntent === pendingIntent) this.pendingIntent = null;
    }
  }

  invalidate() {
    if (this.destroyed || this.failed || this.dirty) return;
    this.dirty = true;
    if (!this.pendingFrame) {
      this.pendingFrame = requestAnimationFrame(() => this.render());
    }
  }

  render() {
    this.pendingFrame = 0;
    if (!this.dirty || !this.context || !this.canvas || this.destroyed || this.failed) return;
    this.dirty = false;
    const context = this.context;
    const width = this.cssWidth;
    const height = this.cssHeight;
    context.setTransform(this.density, 0, 0, this.density, 0, 0);
    let palette;
    try {
      palette = waveformPalette(getComputedStyle(this.canvas));
    } catch {
      this.failClosed();
      return;
    }
    context.fillStyle = palette.background;
    context.fillRect(0, 0, width, height);
    if (!this.published || width <= 0 || height <= 0) return;

    const viewport = this.activeViewport();
    const rulerHeight = 30;
    drawRuler(
      context,
      width,
      rulerHeight,
      viewport,
      palette.ink,
      palette.muted,
      palette.border,
    );
    const layouts = this.rowLayouts(rulerHeight);
    if (!layouts) {
      this.failClosed();
      return;
    }
    context.save();
    context.beginPath();
    context.rect(0, rulerHeight, width, Math.max(0, height - rulerHeight));
    context.clip();
    for (const layout of layouts) {
      const row = this.published.rows[layout.index];
      if (!row) continue;
      context.strokeStyle = palette.border;
      context.lineWidth = 1;
      context.beginPath();
      context.moveTo(0, layout.top + layout.height);
      context.lineTo(width, layout.top + layout.height);
      context.stroke();
      drawTraceRow(
        context,
        this.published.trace,
        this.traceByProbe.get(row.probeId) ?? [],
        row,
        viewport,
        layout.top,
        layout.height,
        width,
        palette.probes[row.appearanceOrdinal % palette.probes.length],
        palette.muted,
      );
    }
    context.restore();
    drawCursor(
      context,
      this.published.viewState.primaryCursor,
      viewport,
      width,
      height,
      palette.primaryCursor,
      "A",
    );
    drawCursor(
      context,
      this.published.viewState.secondaryCursor,
      viewport,
      width,
      height,
      palette.secondaryCursor,
      "B",
    );
    if (this.transientCursor) {
      drawCursor(
        context,
        { logicalTime: this.transientCursor.logicalTime.toString() },
        viewport,
        width,
        height,
        this.transientCursor.kind === "primary"
          ? palette.primaryCursor
          : palette.secondaryCursor,
        this.transientCursor.kind === "primary" ? "A" : "B",
      );
    }
  }

  rowLayouts(rulerHeight) {
    if (!this.probeSpine || !this.canvas) return null;
    if (this.rowLayoutCache) return this.rowLayoutCache;
    const rows = [...this.probeSpine.querySelectorAll("[data-waveform-row-track]")];
    if (rows.length !== this.published.rows.length) return null;
    const canvasBounds = this.canvas.getBoundingClientRect();
    const spineBounds = this.probeSpine.getBoundingClientRect();
    if (Math.abs(spineBounds.top - canvasBounds.top) >= 1) {
      const availableHeight = Math.max(1, this.cssHeight - rulerHeight);
      const rowHeight = availableHeight / rows.length;
      this.rowLayoutCache = rows.map((_, index) => ({
        index,
        top: rulerHeight + (index * rowHeight),
        height: rowHeight,
      }));
      return this.rowLayoutCache;
    }

    this.rowLayoutCache = rows
      .map((row, index) => {
        const bounds = row.getBoundingClientRect();
        return {
          index,
          top: bounds.top - canvasBounds.top,
          height: bounds.height,
        };
      })
      .filter((layout) =>
        layout.height > 0 &&
        layout.top + layout.height > rulerHeight &&
        layout.top < this.cssHeight);
    return this.rowLayoutCache;
  }

  failClosed(policyEvidence = null) {
    if (this.destroyed || this.failed) return;
    this.failed = true;
    this.cancelGesture();
    this.published = null;
    this.traceByProbe.clear();
    this.rowLayoutCache = null;
    this.transfers.clear();
    this.cancelViewportCommit();
    this.cancelContextRestore();
    this.abortController.abort();
    this.resizeObserver?.disconnect();
    this.densityMedia?.removeEventListener("change", this.onDensityChange);
    this.pendingIntent = null;
    if (this.pendingFrame) cancelAnimationFrame(this.pendingFrame);
    this.pendingFrame = 0;
    this.dirty = false;
    if (this.context && this.canvas) {
      this.context.setTransform(1, 0, 0, 1, 0, 0);
      this.context.clearRect(0, 0, this.canvas.width, this.canvas.height);
    }
    this.notifyRendererFailure(policyEvidence);
  }

  async notifyRendererFailure(policyEvidence) {
    try {
      if (policyEvidence) {
        await this.dotnetSink?.invokeMethodAsync(
          "WaveformBrowserPolicyExhaustedAsync",
          this.policy.policyId,
          this.policy.policyRevision,
          policyEvidence.dimension,
          policyEvidence.observed,
        );
      } else {
        await this.dotnetSink?.invokeMethodAsync("WaveformRendererFailedAsync");
      }
    } catch {
      // The owning component may already be gone; the renderer is closed either way.
    }
  }

  destroy() {
    if (this.destroyed) return;
    this.destroyed = true;
    this.cancelGesture();
    this.abortController.abort();
    this.resizeObserver?.disconnect();
    this.removalObserver?.disconnect();
    this.densityMedia?.removeEventListener("change", this.onDensityChange);
    if (this.pendingFrame) cancelAnimationFrame(this.pendingFrame);
    this.pendingFrame = 0;
    this.transfers.clear();
    this.cancelViewportCommit();
    this.cancelContextRestore();
    this.pendingIntent = null;
    this.published = null;
    this.traceByProbe.clear();
    this.rowLayoutCache = null;
    this.dotnetSink = null;
    if (mountedHandles.get(this.host) === this) mountedHandles.delete(this.host);
    this.context = null;
    this.canvas = null;
    this.probeSpine = null;
    this.densityMedia = null;
    this.onDensityChange = null;
  }

  ensureLive() {
    if (this.destroyed || this.failed) throw new Error("Waveform handle is unavailable");
  }
}

function validatePolicy(policy) {
  const fields = [
    "semanticIntentBytes",
    "interopBatchBytes",
    "candidateTransferBytes",
    "canvasBitmapPixels",
    "effectiveDensityMillionths",
  ];
  if (
    !hasExactShape(policy, recordShapes.policy) ||
    !isToken(policy.policyId) ||
    !isToken(policy.policyRevision) ||
    fields.some((field) => !Number.isSafeInteger(policy[field]) || policy[field] <= 0) ||
    policy.interopBatchBytes < interopEnvelopeBytes + minimumBase64QuantumBytes
  ) {
    throw new Error("invalid Browser Policy");
  }
  return Object.freeze({ ...policy });
}

function validateSnapshot(candidate, buildFingerprint) {
  if (
    !hasExactShape(candidate, recordShapes.snapshot) ||
    candidate.buildFingerprint !== buildFingerprint ||
    !positiveInteger(candidate.waveformVersion) ||
    !positiveInteger(candidate.projectionVersion) ||
    !nonEmptyString(candidate.sessionId) ||
    !positiveInteger(candidate.sessionVersion) ||
    !nonEmptyString(candidate.compilationArtifactKey) ||
    !Array.isArray(candidate.rows) ||
    !hasExactShape(candidate.viewState, recordShapes.viewState) ||
    !candidate.trace
  ) {
    throw new Error("invalid Waveform snapshot envelope");
  }
  const ids = new Set();
  candidate.rows.forEach((row, ordinal) => {
    if (!validRow(row, ordinal) || ids.has(row.probeId)) {
      throw new Error("invalid Waveform row");
    }
    ids.add(row.probeId);
  });
  validateRange(candidate.viewState.viewport);
  if (
    !validCursor(candidate.viewState.primaryCursor, "primary") ||
    !validCursor(candidate.viewState.secondaryCursor, "secondary")
  ) {
    throw new Error("invalid Waveform view state");
  }
  validateTrace(candidate.trace, candidate.rows, candidate.viewState.viewport);
  return candidate;
}

function validateTrace(trace, rows, viewport) {
  if (
    trace.kind !== "transitions" &&
    trace.kind !== "summary" &&
    trace.kind !== "unavailable"
  ) {
    throw new Error("invalid Waveform Trace");
  }
  if (trace.kind === "unavailable") {
    if (!hasExactShape(trace, recordShapes.unavailable)) {
      throw new Error("invalid unavailable Waveform Trace");
    }
    validateGap(trace.gap);
    if (!sameRange(trace.gap.range, viewport)) {
      throw new Error("invalid unavailable Waveform Trace");
    }
    return;
  }
  const traceShape = trace.kind === "summary"
    ? recordShapes.summary
    : recordShapes.transitions;
  if (
    !hasExactShape(trace, traceShape) ||
    !Array.isArray(trace.segments)
  ) {
    throw new Error("invalid Waveform Trace collections");
  }
  if (trace.kind === "summary" && trace.aggregation !== "logic-envelope-v1") {
    throw new Error("invalid Waveform summary aggregation");
  }
  const resolvedRows = rows.filter((row) => row.binding === "resolved");
  let rowIndex = 0;
  let expectedStart = viewport.startInclusive;
  trace.segments.forEach((segment) => {
    const segmentShape = trace.kind === "transitions"
      ? recordShapes.transitionSegment
      : recordShapes.summarySegment;
    if (!hasExactShape(segment, segmentShape)) {
      throw new Error(`invalid Waveform ${trace.kind} segment`);
    }
    const row = resolvedRows[rowIndex];
    if (!row) throw new Error("Waveform Trace exceeds its resolved rows");
    validateRange(segment.range);
    if (
      segment.probeId !== row.probeId ||
      segment.range.startInclusive !== expectedStart ||
      BigInt(segment.range.endExclusive) > BigInt(viewport.endExclusive)
    ) {
      throw new Error("Waveform segments are not in canonical row and range order");
    }
    if (trace.kind === "transitions") {
      validVector(segment.value);
      if (
        segment.value.width !== row.width ||
        typeof segment.transitionAtStart !== "boolean"
      ) {
        throw new Error("invalid Waveform transition segment");
      }
    } else {
      validVector(segment.firstValue);
      validVector(segment.lastValue);
      const firstSymbols = decodedVectors.get(segment.firstValue);
      const lastSymbols = decodedVectors.get(segment.lastValue);
      const endpointsDiffer = firstSymbols.some(
        (value, index) => value !== lastSymbols[index],
      );
      if (
        segment.firstValue.width !== row.width ||
        segment.lastValue.width !== row.width ||
        typeof segment.hadTransition !== "boolean" ||
        typeof segment.hadMixedValues !== "boolean" ||
        (segment.hadMixedValues && !segment.hadTransition) ||
        (endpointsDiffer && !segment.hadMixedValues)
      ) {
        throw new Error("invalid Waveform summary segment");
      }
    }
    expectedStart = segment.range.endExclusive;
    if (expectedStart === viewport.endExclusive) {
      rowIndex += 1;
      expectedStart = viewport.startInclusive;
    }
  });

  if (rowIndex !== resolvedRows.length) {
    throw new Error("Waveform Trace does not cover every resolved row");
  }
}

function indexTrace(trace) {
  const traceByProbe = new Map();
  if (trace.kind === "unavailable") return traceByProbe;
  trace.segments.forEach((segment) => {
    let entries = traceByProbe.get(segment.probeId);
    if (!entries) {
      entries = [];
      traceByProbe.set(segment.probeId, entries);
    }
    entries.push({
      segment,
      symbols: vectorSymbols(
        trace.kind === "transitions" ? segment.value : segment.firstValue,
      ),
      lastSymbols: trace.kind === "summary"
        ? vectorSymbols(segment.lastValue)
        : null,
    });
  });
  return traceByProbe;
}

function validRow(row, ordinal) {
  return validRowShape(row) && row.displayOrdinal === ordinal;
}

function validRowShape(row) {
  if (
    !hasExactShape(row, recordShapes.row) ||
    !nonEmptyString(row.probeId) ||
    !Number.isSafeInteger(row.displayOrdinal) ||
    row.displayOrdinal < 0 ||
    !positiveInteger(row.width) ||
    !Number.isSafeInteger(row.appearanceOrdinal) ||
    row.appearanceOrdinal < 0 ||
    row.appearanceOrdinal >= 16 ||
    row.pattern !== ["solid", "dash", "dot", "dashDot"][
      Math.floor(row.appearanceOrdinal / 4)
    ] ||
    (row.binding !== "resolved" && row.binding !== "unresolved")
  ) return false;
  return true;
}

function validVector(vector) {
  if (
    !hasExactShape(vector, recordShapes.vector) ||
    !positiveInteger(vector.width) ||
    vector.encoding !== "logic4-2bit-v1" ||
    typeof vector.data !== "string"
  ) throw new Error("invalid Waveform Logic Vector");
  const bytes = decodeBase64(vector.data);
  if (bytes.length !== Math.ceil(vector.width / 4)) {
    throw new Error("invalid Waveform Logic Vector length");
  }
  const usedFields = vector.width % 4;
  if (usedFields !== 0) {
    const usedMask = (1 << (usedFields * 2)) - 1;
    if ((bytes.at(-1) & ~usedMask) !== 0) {
      throw new Error("invalid Waveform Logic Vector padding");
    }
  }
  const symbols = new Uint8Array(vector.width);
  for (let index = 0; index < vector.width; index++) {
    symbols[index] = (bytes[Math.floor(index / 4)] >> ((index % 4) * 2)) & 3;
  }
  decodedVectors.set(vector, symbols);
}

function validCursor(cursor, kind) {
  return cursor === null || (
    hasExactShape(cursor, recordShapes.cursor) &&
    cursor.kind === kind &&
    canonicalLogicalTime(cursor.logicalTime)
  );
}

function validateGap(gap) {
  if (!hasExactShape(gap, recordShapes.gap)) {
    throw new Error("invalid Waveform Trace Gap");
  }
  validateRange(gap.range);
}

function validateRange(range) {
  if (
    !hasExactShape(range, recordShapes.range) ||
    !canonicalTimeBoundary(range.startInclusive) ||
    !canonicalTimeBoundary(range.endExclusive) ||
    BigInt(range.startInclusive) >= BigInt(range.endExclusive)
  ) throw new Error("invalid Waveform time range");
}

function sameRange(left, right) {
  return left.startInclusive === right.startInclusive && left.endExclusive === right.endExclusive;
}

function drawRuler(context, width, height, viewport, ink, muted, border) {
  context.fillStyle = muted;
  context.font = '600 10px "IBM Plex Mono", ui-monospace, monospace';
  context.textBaseline = "middle";
  context.strokeStyle = border;
  context.beginPath();
  context.moveTo(0, height - 0.5);
  context.lineTo(width, height - 0.5);
  context.stroke();
  const start = BigInt(viewport.startInclusive);
  const end = BigInt(viewport.endExclusive);
  const span = end - start;
  for (let index = 0; index <= 5; index++) {
    const x = (width * index) / 5;
    const time = start + (span * BigInt(index)) / 5n;
    context.strokeStyle = border;
    context.beginPath();
    context.moveTo(x + 0.5, height - 6);
    context.lineTo(x + 0.5, height);
    context.stroke();
    context.fillStyle = index === 0 || index === 5 ? ink : muted;
    context.textAlign = index === 0 ? "left" : index === 5 ? "right" : "center";
    context.fillText(time.toString(), x, height / 2);
  }
}

function drawTraceRow(context, trace, entries, row, viewport, top, height, width, color, muted) {
  if (row.binding === "unresolved") {
    hatch(context, 0, top, width, height, muted);
    return;
  }
  if (trace.kind === "unavailable") {
    hatch(context, 0, top, width, height, muted);
    return;
  }
  entries.forEach(({ segment, symbols, lastSymbols }) => {
    const left = timeX(segment.range.startInclusive, viewport, width);
    const right = timeX(segment.range.endExclusive, viewport, width);
    if (trace.kind === "summary") {
      drawSummary(
        context,
        segment,
        symbols,
        lastSymbols,
        left,
        right,
        top,
        height,
        color,
      );
    } else {
      drawTransition(context, segment, symbols, left, right, top, height, color, row.pattern);
    }
  });
}

function drawTransition(context, segment, symbols, left, right, top, height, color, pattern) {
  context.save();
  context.strokeStyle = color;
  context.fillStyle = color;
  context.lineWidth = 2;
  context.setLineDash(pattern === "dash" ? [8, 5] : pattern === "dot" ? [2, 4] : pattern === "dashDot" ? [9, 4, 2, 4] : []);
  if (symbols.length === 1) {
    const symbol = symbols[0];
    if (symbol === 0 || symbol === 1) {
      const y = symbol === 1 ? top + height * 0.28 : top + height * 0.72;
      context.beginPath();
      if (segment.transitionAtStart) {
        context.moveTo(left, top + height * 0.28);
        context.lineTo(left, top + height * 0.72);
        context.moveTo(left, y);
      } else context.moveTo(left, y);
      context.lineTo(right, y);
      context.stroke();
    } else if (symbol === 2) {
      hatch(context, left, top + height * 0.22, Math.max(1, right - left), height * 0.56, color);
      context.fillText("X", left + 5, top + height / 2);
    } else {
      context.setLineDash([3, 4]);
      context.beginPath();
      context.moveTo(left, top + height / 2);
      context.lineTo(right, top + height / 2);
      context.stroke();
      context.fillText("Z", left + 5, top + height / 2);
    }
  } else {
    const yTop = top + height * 0.25;
    const yBottom = top + height * 0.75;
    context.beginPath();
    context.moveTo(left, yTop);
    context.lineTo(right, yTop);
    context.lineTo(right, yBottom);
    context.lineTo(left, yBottom);
    context.closePath();
    context.stroke();
    context.font = '600 11px "IBM Plex Mono", ui-monospace, monospace';
    context.textBaseline = "middle";
    context.fillText(vectorText(symbols), left + 5, top + height / 2);
  }
  context.restore();
}

function drawSummary(
  context,
  segment,
  firstSymbols,
  lastSymbols,
  left,
  right,
  top,
  height,
  color,
) {
  context.save();
  context.strokeStyle = color;
  context.fillStyle = color;
  context.lineWidth = 2;
  const segmentWidth = Math.max(1, right - left);
  if (firstSymbols.length === 1 && lastSymbols.length === 1) {
    const first = firstSymbols[0];
    const last = lastSymbols[0];
    if (first === 2 || last === 2) {
      hatch(context, left, top + height * 0.22, segmentWidth, height * 0.56, color);
      if (segmentWidth >= 12) context.fillText("X", left + 4, top + height / 2);
      context.restore();
      return;
    }
    if (first === 3 || last === 3) {
      context.setLineDash([3, 4]);
      context.beginPath();
      context.moveTo(left, top + height / 2);
      context.lineTo(right, top + height / 2);
      context.stroke();
      if (segmentWidth >= 12) context.fillText("Z", left + 4, top + height / 2);
      context.restore();
      return;
    }

    const high = top + height * 0.28;
    const low = top + height * 0.72;
    if (segment.hadMixedValues) {
      context.globalAlpha = 0.14;
      context.fillRect(left, high, segmentWidth, low - high);
      context.globalAlpha = 1;
      context.beginPath();
      context.moveTo(left, high);
      context.lineTo(right, high);
      context.moveTo(left, low);
      context.lineTo(right, low);
      context.stroke();
    }

    const firstY = first === 1 ? high : low;
    const lastY = last === 1 ? high : low;
    context.beginPath();
    if (segment.hadTransition) {
      context.moveTo(left, high);
      context.lineTo(left, low);
    }
    context.moveTo(left, firstY);
    context.lineTo(right, lastY);
    context.stroke();
    context.restore();
    return;
  }

  const y = top + height * 0.25;
  const h = height * 0.5;
  context.globalAlpha = segment.hadMixedValues ? 0.18 : 0.08;
  context.fillRect(left, y, segmentWidth, h);
  context.globalAlpha = 1;
  context.strokeRect(left, y, segmentWidth, h);
  if (segment.hadTransition || segment.hadMixedValues) {
    context.beginPath();
    context.moveTo(left, y + h);
    context.lineTo(right, y);
    context.stroke();
  }
  if (segmentWidth >= 48) {
    const first = vectorText(firstSymbols);
    const last = vectorText(lastSymbols);
    context.font = '600 11px "IBM Plex Mono", ui-monospace, monospace';
    context.textBaseline = "middle";
    context.fillText(first === last ? first : `${first}→${last}`, left + 5, top + height / 2);
  }
  context.restore();
}

function drawCursor(context, cursor, viewport, width, height, color, label) {
  if (!cursor) return;
  const x = timeX(cursor.logicalTime, viewport, width);
  context.save();
  context.strokeStyle = color;
  context.fillStyle = color;
  context.lineWidth = 1.5;
  context.beginPath();
  context.moveTo(x + 0.5, 0);
  context.lineTo(x + 0.5, height);
  context.stroke();
  context.font = '700 10px "IBM Plex Mono", ui-monospace, monospace';
  context.fillText(label, x + 4, 11);
  context.restore();
}

function hatch(context, x, y, width, height, color) {
  context.save();
  context.beginPath();
  context.rect(x, y, width, height);
  context.clip();
  context.strokeStyle = color;
  context.globalAlpha = 0.45;
  context.lineWidth = 1;
  for (let offset = -height; offset < width + height; offset += 8) {
    context.beginPath();
    context.moveTo(x + offset, y + height);
    context.lineTo(x + offset + height, y);
    context.stroke();
  }
  context.restore();
}

function timeX(value, viewport, width) {
  const start = BigInt(viewport.startInclusive);
  const end = BigInt(viewport.endExclusive);
  const time = BigInt(value);
  const scale = 1_000_000n;
  const ratio = ((time - start) * scale) / (end - start);
  return (Number(ratio) / Number(scale)) * width;
}

function proportionalOffset(span, ratio) {
  const scale = 1_000_000n;
  const scaledRatio = BigInt(Math.floor(clamp(ratio, 0, 1) * Number(scale)));
  return (span * scaledRatio) / scale;
}

function vectorSymbols(vector) {
  const symbols = decodedVectors.get(vector);
  if (!symbols) throw new Error("unvalidated Waveform Logic Vector");
  return symbols;
}

function vectorText(values) {
  const symbols = ["0", "1", "X", "Z"];
  const text = values.slice().reverse().map((value) => symbols[value]).join("");
  return text.length <= 14 ? text : `${text.slice(0, 6)}…${text.slice(-6)}`;
}

function waveformPalette(styles) {
  const color = (name) => {
    const value = styles.getPropertyValue(name).trim();
    if (!value) throw new Error(`missing Waveform design token ${name}`);
    return value;
  };
  return {
    background: color("--ll-canvas"),
    ink: color("--ll-ink"),
    muted: color("--ll-muted"),
    border: color("--ll-border"),
    probes: [0, 1, 2, 3].map((ordinal) => color(`--ll-probe-${ordinal}`)),
    primaryCursor: color("--ll-waveform-cursor-primary"),
    secondaryCursor: color("--ll-waveform-cursor-secondary"),
  };
}

function decodeBase64(value) {
  try {
    const binary = atob(value);
    return Uint8Array.from(binary, (character) => character.charCodeAt(0));
  } catch {
    throw new Error("invalid Base64");
  }
}

function concatenate(chunks) {
  const length = chunks.reduce((sum, chunk) => sum + chunk.byteLength, 0);
  const result = new Uint8Array(length);
  let offset = 0;
  chunks.forEach((chunk) => {
    result.set(chunk, offset);
    offset += chunk.byteLength;
  });
  return result;
}

function deepFreeze(value) {
  if (!value || typeof value !== "object" || Object.isFrozen(value)) return value;
  Object.values(value).forEach(deepFreeze);
  return Object.freeze(value);
}

function positiveInteger(value) {
  return Number.isSafeInteger(value) && value > 0;
}

function nonEmptyString(value) {
  return typeof value === "string" && value.trim().length > 0;
}

function canonicalLogicalTime(value) {
  return typeof value === "string" &&
    value.length <= 20 &&
    /^(0|[1-9][0-9]*)$/.test(value) &&
    BigInt(value) <= logicalTimeMaximum;
}

function canonicalTimeBoundary(value) {
  return typeof value === "string" &&
    value.length <= 20 &&
    /^(0|[1-9][0-9]*)$/.test(value) &&
    BigInt(value) <= timeBoundaryMaximum;
}

function isDigest(value) {
  return typeof value === "string" && /^[0-9a-f]{64}$/.test(value);
}

function isToken(value) {
  return typeof value === "string" && /^[A-Za-z0-9._-]+$/.test(value);
}

function shape(...fields) {
  return Object.freeze(fields);
}

function hasExactShape(value, fields) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const keys = Object.keys(value);
  return keys.length === fields.length &&
    fields.every((field) => Object.hasOwn(value, field));
}

function clamp(value, minimum, maximum) {
  return Math.min(maximum, Math.max(minimum, value));
}

async function sha256(bytes) {
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)]
    .map((value) => value.toString(16).padStart(2, "0"))
    .join("");
}
