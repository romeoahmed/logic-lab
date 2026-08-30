const mountedHandles = new WeakMap();
const encoder = new TextEncoder();

export function mount(host, buildFingerprint, policy, dotnetSink) {
  const existing = mountedHandles.get(host);
  if (existing && existing.buildFingerprint === buildFingerprint && !existing.destroyed) {
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
    this.context = this.canvas?.getContext("2d", { alpha: false }) ?? null;
    this.published = null;
    this.transientViewport = null;
    this.transientCursor = null;
    this.interactionMode = "commitEnabled";
    this.gesture = null;
    this.pendingFrame = 0;
    this.dirty = false;
    this.cssWidth = 0;
    this.cssHeight = 0;
    this.density = 1;
    this.transfers = new Map();
    this.destroyed = false;
    this.abortController = new AbortController();
    this.resizeObserver = null;
    this.removalObserver = null;
    this.installRemovalObserver();

    if (!this.canvas || !this.context) {
      this.failClosed("contextUnavailable");
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
      (kind !== "snapshot" && kind !== "patch") ||
      !Number.isSafeInteger(byteLength) ||
      byteLength <= 0 ||
      BigInt(byteLength) > BigInt(this.policy.candidateTransferBytes) ||
      !isDigest(digest) ||
      this.transfers.has(transferId)
    ) {
      throw new Error("invalid Waveform transfer envelope");
    }

    this.transfers.set(transferId, {
      kind,
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
      encoder.encode(chunk).byteLength + 512 > this.policy.interopBatchBytes
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

      const candidate = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(candidateBytes));
      let next;
      if (transfer.kind === "snapshot") {
        next = validateSnapshot(candidate, this.buildFingerprint);
      } else {
        const patch = validatePatch(candidate, this.buildFingerprint, this.published);
        next = applyPatch(this.published, patch);
        validateSnapshot(next, this.buildFingerprint);
      }

      this.cancelGesture();
      this.published = deepFreeze(next);
      this.transientViewport = null;
      this.transientCursor = null;
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
    if (mode === "localOnly") this.cancelGesture();
    this.interactionMode = mode;
  }

  installListeners() {
    const signal = this.abortController.signal;
    this.canvas.addEventListener("pointerdown", (event) => this.pointerDown(event), { signal });
    this.canvas.addEventListener("pointermove", (event) => this.pointerMove(event), { signal });
    this.canvas.addEventListener("pointerup", (event) => this.pointerUp(event), { signal });
    this.canvas.addEventListener("pointercancel", () => this.cancelGesture(), { signal });
    this.canvas.addEventListener("lostpointercapture", () => this.cancelGesture(), { signal });
    this.canvas.addEventListener("wheel", (event) => this.wheel(event), {
      signal,
      passive: false,
    });
    this.canvas.addEventListener("keydown", (event) => this.keyDown(event), { signal });
    this.canvas.addEventListener("contextlost", (event) => {
      event.preventDefault();
      this.cancelGesture();
    }, { signal });
    this.canvas.addEventListener("contextrestored", () => {
      this.context = this.canvas.getContext("2d", { alpha: false });
      if (!this.context) {
        this.failClosed("contextLost");
        return;
      }
      this.resize();
      this.invalidate();
    }, { signal });
  }

  installObservers() {
    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(this.canvas);
  }

  installRemovalObserver() {
    const root = this.host.parentElement ?? document.body;
    this.removalObserver = new MutationObserver(() => {
      if (!this.host.isConnected) this.destroy();
    });
    this.removalObserver.observe(root, { childList: true, subtree: true });
  }

  resize() {
    if (!this.canvas || !this.context || this.destroyed) return;
    const bounds = this.canvas.getBoundingClientRect();
    if (!Number.isFinite(bounds.width) || !Number.isFinite(bounds.height)) return;
    if (bounds.width <= 0 || bounds.height <= 0) {
      this.cssWidth = 0;
      this.cssHeight = 0;
      return;
    }

    const maximumDensity = this.policy.effectiveDensityMillionths / 1_000_000;
    const density = Math.min(window.devicePixelRatio || 1, maximumDensity);
    const width = Math.ceil(bounds.width * density);
    const height = Math.ceil(bounds.height * density);
    const pixels = BigInt(width) * BigInt(height);
    if (pixels > BigInt(this.policy.canvasBitmapPixels)) {
      this.failClosed("canvasBitmapPixels");
      return;
    }

    this.cssWidth = bounds.width;
    this.cssHeight = bounds.height;
    this.density = density;
    if (this.canvas.width !== width || this.canvas.height !== height) {
      this.canvas.width = width;
      this.canvas.height = height;
      this.context = this.canvas.getContext("2d", { alpha: false });
    }
    this.invalidate();
  }

  pointerDown(event) {
    if (!this.published || event.button !== 0 || this.interactionMode !== "commitEnabled") return;
    const kind = event.shiftKey ? "secondary" : "primary";
    this.gesture = {
      pointerId: event.pointerId,
      kind,
      logicalTime: this.timeAt(event.offsetX),
      waveformVersion: this.published.waveformVersion,
    };
    this.canvas.setPointerCapture(event.pointerId);
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
    if (this.canvas.hasPointerCapture(event.pointerId)) {
      this.canvas.releasePointerCapture(event.pointerId);
    }
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

  cancelGesture() {
    if (this.gesture && this.canvas?.hasPointerCapture(this.gesture.pointerId)) {
      this.canvas.releasePointerCapture(this.gesture.pointerId);
    }
    this.gesture = null;
    this.transientCursor = null;
    this.invalidate();
  }

  wheel(event) {
    if (!this.published || this.interactionMode !== "commitEnabled") return;
    event.preventDefault();
    const viewport = this.activeViewport();
    const start = BigInt(viewport.startInclusive);
    const end = BigInt(viewport.endExclusive);
    const span = end - start;
    let nextStart;
    let nextEnd;
    if (event.shiftKey || Math.abs(event.deltaX) > Math.abs(event.deltaY)) {
      const direction = event.deltaX + event.deltaY >= 0 ? 1n : -1n;
      const step = span / 8n || 1n;
      nextStart = direction > 0n ? start + step : start > step ? start - step : 0n;
      nextEnd = nextStart + span;
    } else {
      const zoomIn = event.deltaY < 0;
      const nextSpan = zoomIn ? span / 2n || 1n : span * 2n;
      const ratio = clamp(event.offsetX / Math.max(1, this.cssWidth), 0, 1);
      const anchor = start + proportionalOffset(span, ratio);
      const left = proportionalOffset(nextSpan, ratio);
      nextStart = anchor > left ? anchor - left : 0n;
      nextEnd = nextStart + nextSpan;
    }

    const max = 18_446_744_073_709_551_615n;
    if (nextEnd > max) {
      nextEnd = max;
      nextStart = nextEnd > span ? nextEnd - span : 0n;
    }
    if (nextEnd <= nextStart) return;
    this.transientViewport = {
      startInclusive: nextStart.toString(),
      endExclusive: nextEnd.toString(),
    };
    this.invalidate();
    void this.emit("setViewport", { viewport: this.transientViewport });
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
    if (!this.published || this.interactionMode !== "commitEnabled") return;
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
    await this.dotnetSink?.invokeMethodAsync("ReceiveWaveformIntent", intent).catch(() => {});
  }

  invalidate() {
    if (this.destroyed || this.dirty) return;
    this.dirty = true;
    if (!this.pendingFrame) {
      this.pendingFrame = requestAnimationFrame(() => this.render());
    }
  }

  render() {
    this.pendingFrame = 0;
    if (!this.dirty || !this.context || !this.canvas || this.destroyed) return;
    this.dirty = false;
    const context = this.context;
    const width = this.cssWidth;
    const height = this.cssHeight;
    context.setTransform(this.density, 0, 0, this.density, 0, 0);
    const styles = getComputedStyle(this.canvas);
    const background = cssColor(styles, "--ll-canvas", "#f8faf9");
    const ink = cssColor(styles, "--ll-ink", "#172124");
    const muted = cssColor(styles, "--ll-muted", "#647176");
    const border = cssColor(styles, "--ll-border", "#d4dcda");
    context.fillStyle = background;
    context.fillRect(0, 0, width, height);
    if (!this.published || width <= 0 || height <= 0) return;

    const viewport = this.activeViewport();
    const rulerHeight = 30;
    const rowHeight = Math.max(36, (height - rulerHeight) / Math.max(1, this.published.rows.length));
    drawRuler(context, width, rulerHeight, viewport, ink, muted, border);
    for (let index = 0; index < this.published.rows.length; index++) {
      const row = this.published.rows[index];
      const top = rulerHeight + index * rowHeight;
      context.strokeStyle = border;
      context.lineWidth = 1;
      context.beginPath();
      context.moveTo(0, top + rowHeight);
      context.lineTo(width, top + rowHeight);
      context.stroke();
      drawTraceRow(
        context,
        this.published.trace,
        row,
        viewport,
        top,
        rowHeight,
        width,
        probeColor(row.appearanceOrdinal),
        muted,
      );
    }
    drawCursor(context, this.published.viewState.primaryCursor, viewport, width, height, "#b85e3d", "A");
    drawCursor(context, this.published.viewState.secondaryCursor, viewport, width, height, "#6d6ab7", "B");
    if (this.transientCursor) {
      drawCursor(
        context,
        { logicalTime: this.transientCursor.logicalTime.toString() },
        viewport,
        width,
        height,
        this.transientCursor.kind === "primary" ? "#b85e3d" : "#6d6ab7",
        this.transientCursor.kind === "primary" ? "A" : "B",
      );
    }
  }

  failClosed(reason) {
    this.cancelGesture();
    this.published = null;
    this.transfers.clear();
    if (this.pendingFrame) cancelAnimationFrame(this.pendingFrame);
    this.pendingFrame = 0;
    this.dirty = false;
    if (this.context && this.canvas) {
      this.context.setTransform(1, 0, 0, 1, 0, 0);
      this.context.clearRect(0, 0, this.canvas.width, this.canvas.height);
    }
    void this.dotnetSink?.invokeMethodAsync("WaveformRendererFailed", reason).catch(() => {});
  }

  destroy() {
    if (this.destroyed) return;
    this.destroyed = true;
    this.cancelGesture();
    this.abortController.abort();
    this.resizeObserver?.disconnect();
    this.removalObserver?.disconnect();
    if (this.pendingFrame) cancelAnimationFrame(this.pendingFrame);
    this.pendingFrame = 0;
    this.transfers.clear();
    this.published = null;
    this.dotnetSink = null;
    if (mountedHandles.get(this.host) === this) mountedHandles.delete(this.host);
  }

  ensureLive() {
    if (this.destroyed) throw new Error("Waveform handle is destroyed");
  }
}

function validatePolicy(policy) {
  const fields = [
    "interopBatchBytes",
    "candidateTransferBytes",
    "canvasBitmapPixels",
    "effectiveDensityMillionths",
    "zoomMillionthsMinimum",
    "zoomMillionthsMaximum",
  ];
  if (
    !policy ||
    typeof policy.policyId !== "string" ||
    typeof policy.policyRevision !== "string" ||
    fields.some((field) => !Number.isSafeInteger(policy[field]) || policy[field] <= 0) ||
    policy.zoomMillionthsMinimum > policy.zoomMillionthsMaximum
  ) {
    throw new Error("invalid Browser Policy");
  }
  return Object.freeze({ ...policy });
}

function validateSnapshot(candidate, buildFingerprint) {
  if (
    !candidate ||
    candidate.buildFingerprint !== buildFingerprint ||
    !positiveInteger(candidate.waveformVersion) ||
    !positiveInteger(candidate.projectionVersion) ||
    !nonEmptyString(candidate.sessionId) ||
    !positiveInteger(candidate.sessionVersion) ||
    !nonEmptyString(candidate.compilationArtifactKey) ||
    (candidate.uiCulture !== "en-US" && candidate.uiCulture !== "zh-CN") ||
    candidate.baseDirection !== "leftToRight" ||
    !Array.isArray(candidate.rows) ||
    !candidate.viewState ||
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
  validRange(candidate.viewState.viewport);
  if (
    typeof candidate.viewState.liveFollow !== "boolean" ||
    !validCursor(candidate.viewState.primaryCursor, "primary") ||
    !validCursor(candidate.viewState.secondaryCursor, "secondary")
  ) {
    throw new Error("invalid Waveform view state");
  }
  validateTrace(candidate.trace, candidate.rows, candidate.viewState.viewport);
  return candidate;
}

function validatePatch(patch, buildFingerprint, published) {
  if (
    !published ||
    !patch ||
    patch.buildFingerprint !== buildFingerprint ||
    patch.buildFingerprint !== published.buildFingerprint ||
    patch.baseWaveformVersion !== published.waveformVersion ||
    !positiveInteger(patch.nextWaveformVersion) ||
    patch.nextWaveformVersion <= patch.baseWaveformVersion ||
    patch.projectionVersion < published.projectionVersion ||
    patch.sessionId !== published.sessionId ||
    patch.sessionVersion < published.sessionVersion ||
    patch.compilationArtifactKey !== published.compilationArtifactKey ||
    patch.uiCulture !== published.uiCulture ||
    patch.baseDirection !== published.baseDirection ||
    !Array.isArray(patch.rowUpserts) ||
    !Array.isArray(patch.probeRemovals) ||
    !Array.isArray(patch.transitionAppends) ||
    !Array.isArray(patch.summaryReplacements) ||
    !Array.isArray(patch.gapReplacements) ||
    (patch.traceKind !== "transitions" && patch.traceKind !== "summary") ||
    !canonicalUnsigned(patch.latestSequence)
  ) {
    throw new Error("invalid Waveform patch");
  }
  if (
    (patch.traceKind !== "transitions" && patch.transitionAppends.length) ||
    (patch.traceKind !== "summary" && patch.summaryReplacements.length)
  ) {
    throw new Error("mixed Waveform Trace patch");
  }
  const upsertIds = new Set();
  patch.rowUpserts.forEach((row) => {
    if (!validRowShape(row) || upsertIds.has(row.probeId)) {
      throw new Error("invalid Waveform row upsert");
    }
    upsertIds.add(row.probeId);
  });
  const removalIds = new Set();
  patch.probeRemovals.forEach((probeId) => {
    if (!nonEmptyString(probeId) || removalIds.has(probeId) || upsertIds.has(probeId)) {
      throw new Error("invalid Waveform Probe removal");
    }
    removalIds.add(probeId);
  });
  return patch;
}

function applyPatch(current, patch) {
  const rows = new Map(current.rows.map((row) => [row.probeId, row]));
  patch.probeRemovals.forEach((probeId) => rows.delete(probeId));
  patch.rowUpserts.forEach((row) => rows.set(row.probeId, row));
  const orderedRows = [...rows.values()].sort((left, right) => left.displayOrdinal - right.displayOrdinal);
  let trace;
  if (patch.traceKind === "transitions") {
    const prior = current.trace.kind === "transitions" ? current.trace.segments : [];
    trace = {
      kind: "transitions",
      segments: [...prior, ...patch.transitionAppends],
      gaps: patch.gapReplacements,
      latestSequence: patch.latestSequence,
    };
  } else {
    trace = {
      kind: "summary",
      aggregation: "logic-envelope-v1",
      segments: patch.summaryReplacements,
      gaps: patch.gapReplacements,
      latestSequence: patch.latestSequence,
    };
  }
  return {
    ...current,
    waveformVersion: patch.nextWaveformVersion,
    projectionVersion: patch.projectionVersion,
    sessionVersion: patch.sessionVersion,
    rows: orderedRows,
    trace,
  };
}

function validateTrace(trace, rows, viewport) {
  const rowsById = new Map(rows.map((row) => [row.probeId, row]));
  if (
    (trace.kind !== "transitions" && trace.kind !== "summary" && trace.kind !== "unavailable") ||
    !canonicalUnsigned(trace.latestSequence)
  ) {
    throw new Error("invalid Waveform Trace");
  }
  if (trace.kind === "unavailable") {
    if (
      !canonicalUnsigned(trace.earliestAvailable) ||
      !validGap(trace.gap) ||
      !sameRange(trace.gap.range, viewport)
    ) {
      throw new Error("invalid unavailable Waveform Trace");
    }
    return;
  }
  if (!Array.isArray(trace.segments) || !Array.isArray(trace.gaps)) {
    throw new Error("invalid Waveform Trace collections");
  }
  if (trace.kind === "summary" && trace.aggregation !== "logic-envelope-v1") {
    throw new Error("invalid Waveform summary aggregation");
  }
  validateOrderedRanges(
    trace.gaps.map((gap) => {
      validGap(gap);
      return gap.range;
    }),
    viewport,
  );
  trace.segments.forEach((segment) => {
    const row = segment && rowsById.get(segment.probeId);
    if (!row) throw new Error("unknown Waveform Probe");
    validRange(segment.range);
    if (!rangeContains(viewport, segment.range)) {
      throw new Error("Waveform segment is outside the viewport");
    }
    if (trace.kind === "transitions") {
      validVector(segment.value);
      if (
        segment.value.width !== row.width ||
        !canonicalUnsigned(segment.sequence) ||
        typeof segment.transitionAtStart !== "boolean"
      ) {
        throw new Error("invalid Waveform transition segment");
      }
    } else {
      validVector(segment.firstValue);
      validVector(segment.lastValue);
      if (
        segment.firstValue.width !== row.width ||
        segment.lastValue.width !== row.width ||
        typeof segment.hadTransition !== "boolean" ||
        typeof segment.hadMixedValues !== "boolean" ||
        typeof segment.hadUnavailableValues !== "boolean"
      ) {
        throw new Error("invalid Waveform summary segment");
      }
    }
  });

  rows.forEach((row) => {
    const rowRanges = trace.segments
      .filter((segment) => segment.probeId === row.probeId)
      .map((segment) => segment.range);
    validateOrderedRanges(rowRanges, viewport);
    if (row.binding === "resolved") {
      validateExactCoverage(rowRanges, trace.gaps.map((gap) => gap.range), viewport);
    }
  });
}

function validRow(row, ordinal) {
  return validRowShape(row) && row.displayOrdinal === ordinal;
}

function validRowShape(row) {
  if (
    !row ||
    !nonEmptyString(row.probeId) ||
    !validNet(row.net) ||
    !Number.isSafeInteger(row.displayOrdinal) ||
    row.displayOrdinal < 0 ||
    !positiveInteger(row.width) ||
    !nonEmptyString(row.shortLabel) ||
    (row.radix !== "binary" && row.radix !== "hex" && row.radix !== "unsigned") ||
    !Number.isSafeInteger(row.appearanceOrdinal) ||
    row.appearanceOrdinal < 0 ||
    (row.pattern !== "solid" && row.pattern !== "dash" &&
      row.pattern !== "dot" && row.pattern !== "dashDot") ||
    (row.binding !== "resolved" && row.binding !== "unresolved") ||
    (row.binding === "resolved" && row.bindingReason !== null) ||
    (row.binding === "unresolved" &&
      row.bindingReason !== "sourceMissing" && row.bindingReason !== "artifactIncompatible") ||
    (row.sceneNavigation !== "available" && row.sceneNavigation !== "unavailable") ||
    (row.sceneNavigation === "available" && row.navigationReason !== null) ||
    (row.sceneNavigation === "unavailable" &&
      row.navigationReason !== "noVisibleGeometry" &&
      row.navigationReason !== "sourceMissing" &&
      row.navigationReason !== "projectionUnavailable")
  ) return false;
  if (row.currentValue !== null) {
    validVector(row.currentValue);
    if (row.currentValue.width !== row.width) return false;
  }
  return row.binding !== "resolved" || row.currentValue !== null;
}

function validNet(net) {
  return !!net &&
    !!net.authoredNet &&
    nonEmptyString(net.authoredNet.circuitDefinitionId) &&
    net.authoredNet.entityKind === "net" &&
    nonEmptyString(net.authoredNet.entityId) &&
    (net.authoredNet.portId === null || net.authoredNet.portId === undefined) &&
    !!net.hierarchyPath &&
    nonEmptyString(net.hierarchyPath.entryCircuitDefinitionId) &&
    Array.isArray(net.hierarchyPath.steps) &&
    net.hierarchyPath.steps.every((step) =>
      !!step &&
      nonEmptyString(step.containingCircuitDefinitionId) &&
      nonEmptyString(step.componentInstanceId));
}

function validVector(vector) {
  if (
    !vector ||
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
}

function validCursor(cursor, kind) {
  return cursor === null || (
    cursor.kind === kind && canonicalUnsigned(cursor.logicalTime)
  );
}

function validGap(gap) {
  if (!gap || (gap.reason !== "evicted" && gap.reason !== "artifactChanged")) {
    throw new Error("invalid Waveform Trace Gap");
  }
  validRange(gap.range);
  return true;
}

function validRange(range) {
  if (
    !range ||
    !canonicalUnsigned(range.startInclusive) ||
    !canonicalUnsigned(range.endExclusive) ||
    BigInt(range.startInclusive) >= BigInt(range.endExclusive)
  ) throw new Error("invalid Waveform time range");
  return true;
}

function sameRange(left, right) {
  return left.startInclusive === right.startInclusive && left.endExclusive === right.endExclusive;
}

function rangeContains(outer, inner) {
  return BigInt(inner.startInclusive) >= BigInt(outer.startInclusive) &&
    BigInt(inner.endExclusive) <= BigInt(outer.endExclusive);
}

function validateOrderedRanges(ranges, viewport) {
  let priorEnd = null;
  ranges.forEach((range) => {
    validRange(range);
    if (!rangeContains(viewport, range) ||
      (priorEnd !== null && BigInt(range.startInclusive) < priorEnd)) {
      throw new Error("Waveform ranges are outside the viewport or overlap");
    }
    priorEnd = BigInt(range.endExclusive);
  });
}

function validateExactCoverage(segmentRanges, gapRanges, viewport) {
  const ranges = [...segmentRanges, ...gapRanges]
    .sort((left, right) => {
      const start = BigInt(left.startInclusive) - BigInt(right.startInclusive);
      return start < 0n ? -1 : start > 0n ? 1 : 0;
    });
  let expectedStart = BigInt(viewport.startInclusive);
  ranges.forEach((range) => {
    if (BigInt(range.startInclusive) !== expectedStart) {
      throw new Error("Waveform ranges do not exactly cover the viewport");
    }
    expectedStart = BigInt(range.endExclusive);
  });
  if (expectedStart !== BigInt(viewport.endExclusive)) {
    throw new Error("Waveform ranges do not exactly cover the viewport");
  }
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

function drawTraceRow(context, trace, row, viewport, top, height, width, color, muted) {
  if (row.binding === "unresolved") {
    hatch(context, 0, top, width, height, muted);
    return;
  }
  const gaps = trace.kind === "unavailable" ? [trace.gap] : trace.gaps;
  gaps.forEach((gap) => {
    const left = timeX(gap.range.startInclusive, viewport, width);
    const right = timeX(gap.range.endExclusive, viewport, width);
    hatch(context, left, top, Math.max(1, right - left), height, muted);
  });
  if (trace.kind === "unavailable") return;
  const segments = trace.segments.filter((segment) => segment.probeId === row.probeId);
  segments.forEach((segment) => {
    const left = timeX(segment.range.startInclusive, viewport, width);
    const right = timeX(segment.range.endExclusive, viewport, width);
    if (trace.kind === "summary") {
      drawSummary(context, segment, left, right, top, height, color);
    } else {
      drawTransition(context, segment, left, right, top, height, color, row.pattern);
    }
  });
}

function drawTransition(context, segment, left, right, top, height, color, pattern) {
  const symbols = decodeVector(segment.value);
  context.save();
  context.strokeStyle = color;
  context.fillStyle = color;
  context.lineWidth = 2;
  context.setLineDash(pattern === "dash" ? [8, 5] : pattern === "dot" ? [2, 4] : pattern === "dashDot" ? [9, 4, 2, 4] : []);
  if (symbols.length === 1 && (symbols[0] === 0 || symbols[0] === 1)) {
    const y = symbols[0] === 1 ? top + height * 0.28 : top + height * 0.72;
    context.beginPath();
    if (segment.transitionAtStart) {
      context.moveTo(left, top + height * 0.28);
      context.lineTo(left, top + height * 0.72);
      context.moveTo(left, y);
    } else context.moveTo(left, y);
    context.lineTo(right, y);
    context.stroke();
  } else if (symbols.some((value) => value === 2)) {
    hatch(context, left, top + height * 0.22, Math.max(1, right - left), height * 0.56, color);
    context.fillText("X", left + 5, top + height / 2);
  } else if (symbols.some((value) => value === 3)) {
    context.setLineDash([3, 4]);
    context.beginPath();
    context.moveTo(left, top + height / 2);
    context.lineTo(right, top + height / 2);
    context.stroke();
    context.fillText("Z", left + 5, top + height / 2);
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

function drawSummary(context, segment, left, right, top, height, color) {
  context.save();
  context.strokeStyle = color;
  context.fillStyle = `${color}26`;
  const y = top + height * 0.24;
  const h = height * 0.52;
  if (segment.hadUnavailableValues) {
    hatch(context, left, y, Math.max(1, right - left), h, color);
  } else {
    context.fillRect(left, y, Math.max(1, right - left), h);
    context.strokeRect(left, y, Math.max(1, right - left), h);
  }
  if (segment.hadTransition || segment.hadMixedValues) {
    context.beginPath();
    context.moveTo(left, y + h);
    context.lineTo(right, y);
    context.stroke();
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

function decodeVector(vector) {
  const bytes = decodeBase64(vector.data);
  const values = [];
  for (let index = 0; index < vector.width; index++) {
    values.push((bytes[Math.floor(index / 4)] >> ((index % 4) * 2)) & 3);
  }
  return values;
}

function vectorText(values) {
  const symbols = ["0", "1", "X", "Z"];
  const text = values.slice().reverse().map((value) => symbols[value]).join("");
  return text.length <= 14 ? text : `${text.slice(0, 6)}…${text.slice(-6)}`;
}

function probeColor(ordinal) {
  return ["#08788c", "#b85e3d", "#6d6ab7", "#2c8475"][ordinal % 4];
}

function cssColor(styles, name, fallback) {
  return styles.getPropertyValue(name).trim() || fallback;
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

function canonicalUnsigned(value) {
  return typeof value === "string" && /^(0|[1-9][0-9]*)$/.test(value);
}

function isDigest(value) {
  return typeof value === "string" && /^[0-9a-f]{64}$/.test(value);
}

function isToken(value) {
  return typeof value === "string" && /^[A-Za-z0-9_-]+$/.test(value);
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
