import {
  finiteNumbers,
  terminalFromSource,
  translateRect,
  validComponentPlacement,
  validGridPoint,
  validPoint,
  validRect,
  validRectAllowDegenerate,
} from "./geometry.js";

const textEncoder = new TextEncoder();
const spatialCellSize = 400;
const spatialEntryBaseBytes = 64;
export const interopEnvelopeBytes = 512n;

// Generated from the cmap table of the fingerprinted packaged WOFF2 asset. Keep this
// list in lockstep with --ll-scene-font-asset when replacing that file.
const packagedFontCodePointRanges = Object.freeze([
  [0x0020, 0x007e], [0x00a0, 0x00ac], [0x00ae, 0x00b4], [0x00b6, 0x0107],
  [0x010a, 0x0113], [0x0116, 0x011b], [0x011e, 0x0123], [0x0126, 0x0127],
  [0x012a, 0x012b], [0x012e, 0x0133], [0x0136, 0x0137], [0x0139, 0x013e],
  [0x0141, 0x0148], [0x0150, 0x0155], [0x0158, 0x015b], [0x015e, 0x0165],
  [0x016a, 0x016b], [0x016e, 0x017e], [0x0192, 0x0192], [0x0218, 0x021b],
  [0x0237, 0x0237], [0x02c6, 0x02c7], [0x02c9, 0x02c9], [0x02d8, 0x02dd],
  [0x0300, 0x0304], [0x0306, 0x0308], [0x030a, 0x030c], [0x0312, 0x0312],
  [0x0326, 0x0328], [0x0394, 0x0394], [0x03a9, 0x03a9], [0x03bc, 0x03bc],
  [0x03c0, 0x03c0], [0x1e80, 0x1e85], [0x1e9e, 0x1e9e], [0x1ef2, 0x1ef3],
  [0x2009, 0x2009], [0x2013, 0x2014], [0x2018, 0x201a], [0x201c, 0x201e],
  [0x2020, 0x2022], [0x2026, 0x2026], [0x2030, 0x2030], [0x2039, 0x203a],
  [0x2044, 0x2044], [0x20ac, 0x20ac], [0x20b9, 0x20b9], [0x2113, 0x2113],
  [0x2122, 0x2122], [0x212e, 0x212e], [0x2202, 0x2202], [0x220f, 0x220f],
  [0x2211, 0x2212], [0x2215, 0x2215], [0x2219, 0x221a], [0x221e, 0x221e],
  [0x222b, 0x222b], [0x2248, 0x2248], [0x2260, 0x2260], [0x2264, 0x2265],
  [0x25ca, 0x25ca], [0x266a, 0x266a],
]);

export class BrowserPolicyError extends Error {
  constructor(dimension, observed) {
    super(`${dimension} policy exhausted`);
    this.name = "BrowserPolicyError";
    this.dimension = dimension;
    this.observed = BigInt(observed);
  }
}

export const browserPolicyDimensionTokens = Object.freeze({
  semanticIntentBytes: "semantic_intent_bytes",
  sceneSnapshotRecordCount: "scene_snapshot_record_count",
  scenePatchRecordCount: "scene_patch_record_count",
  interopBatchBytes: "interop_batch_bytes",
  candidateTransferBytes: "candidate_transfer_bytes",
  canvasBitmapPixels: "canvas_bitmap_pixels",
  canvasBitmapBytes: "canvas_bitmap_bytes",
  effectiveDensityMillionths: "effective_density_millionths",
  zoomMillionthsMinimum: "zoom_millionths_minimum",
  zoomMillionthsMaximum: "zoom_millionths_maximum",
  semanticTreePageItems: "semantic_tree_page_items",
  displayListBytes: "display_list_bytes",
  spatialIndexBytes: "spatial_index_bytes",
  sceneCacheBytes: "scene_cache_bytes",
  waveformCacheBytes: "waveform_cache_bytes",
});

export function validatePolicy(policy) {
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

export function validateRecoveryState(candidate, policy) {
  if (candidate === null || candidate === undefined) {
    return new Map();
  }

  const shape = Object.keys(candidate);
  const viewports = candidate.viewports;
  const minimumZoom = Number(policy.zoomMillionthsMinimum) / 1_000_000;
  const maximumZoom = Number(policy.zoomMillionthsMaximum) / 1_000_000;
  if (shape.length !== 1 || shape[0] !== "viewports" || !Array.isArray(viewports)
      || BigInt(viewports.length) > BigInt(policy.sceneSnapshotRecordCount)
      || encodedJsonBytes(candidate) + interopEnvelopeBytes
        > BigInt(policy.interopBatchBytes)) {
    throw new Error("invalid Scene recovery state");
  }

  const recovered = new Map();
  for (const viewport of viewports) {
    const viewportShape = viewport && Object.keys(viewport);
    if (!viewport || viewportShape.length !== 4
        || !["circuitDefinitionId", "translateX", "translateY", "zoom"]
          .every((field) => viewportShape.includes(field))
        || !isToken(viewport.circuitDefinitionId)
        || !finiteNumbers(viewport.translateX, viewport.translateY, viewport.zoom)
        || viewport.zoom < minimumZoom || viewport.zoom > maximumZoom
        || recovered.has(viewport.circuitDefinitionId)) {
      throw new Error("invalid Scene recovery viewport");
    }
    recovered.set(viewport.circuitDefinitionId, {
      x: viewport.translateX,
      y: viewport.translateY,
      zoom: viewport.zoom,
    });
  }
  return recovered;
}

export function validateReplacement(candidate, buildFingerprint, fontFingerprint, policy) {
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
  assertPolicyLimit("sceneCacheBytes", encodedJsonBytes(candidate), policy.sceneCacheBytes);
  const displayList = candidate.items.map((item) => ({
    order: item?.order,
    bounds: item?.bounds,
    origin: item?.origin,
    hasDrawableTarget: item?.hasDrawableTarget,
    operations: item?.operations,
  }));
  assertPolicyLimit("displayListBytes", encodedJsonBytes(displayList), policy.displayListBytes);

  const sourceKeys = new Set();
  const orders = new Set();
  let previousOrder = -1;
  let records = 1;
  for (const item of candidate.items) {
    if (!validSource(item?.source, candidate.circuitDefinitionId)
        || sourceKeys.has(sourceKey(item.source)) || !Number.isSafeInteger(item.order)
        || item.order < 0 || item.order <= previousOrder
        || orders.has(item.order) || !validRect(item.bounds) || !validPoint(item.origin)
        || typeof item.hasDrawableTarget !== "boolean"
        || !Array.isArray(item.operations) || !Array.isArray(item.hitRegions)
        || (!item.hasDrawableTarget && (item.operations.length > 0 || item.hitRegions.length > 0))
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
  assertPolicyLimit("sceneSnapshotRecordCount", records, policy.sceneSnapshotRecordCount);
}

export function validatePatch(patch, published, buildFingerprint, fontFingerprint, policy) {
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
    assertPolicyLimit("scenePatchRecordCount", patchRecords, policy.scenePatchRecordCount);
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
  } catch (error) {
    if (error instanceof BrowserPolicyError) {
      throw error;
    }
    return null;
  }
}

function freezeSnapshot(candidate) {
  return deepFreeze(candidate);
}

export function deepFreeze(value) {
  if (value && typeof value === "object" && !Object.isFrozen(value)) {
    Object.values(value).forEach(deepFreeze);
    Object.freeze(value);
  }
  return value;
}

export function buildSourceIndex(snapshot, policy) {
  const sourcesByKey = new Map();
  const targetsBySource = new Map();
  const maximumBytes = BigInt(policy.spatialIndexBytes);
  let observedBytes = 0n;
  const chargeEntry = (key) => {
    observedBytes += BigInt(spatialEntryBaseBytes + textEncoder.encode(key).byteLength);
    if (observedBytes > maximumBytes) {
      throw new BrowserPolicyError("spatialIndexBytes", observedBytes);
    }
  };
  for (const item of snapshot.items) {
    const itemKey = sourceKey(item.source);
    if (!sourcesByKey.has(itemKey)) {
      chargeEntry(itemKey);
      sourcesByKey.set(itemKey, item.source);
    }
    if (item.hasDrawableTarget && !targetsBySource.has(itemKey)) {
      chargeEntry(itemKey);
      targetsBySource.set(itemKey, {
        bounds: translateRect(item.bounds, item.origin),
        item,
      });
    }

    for (const region of item.hitRegions) {
      if (!region.targetSource) {
        continue;
      }
      const targetKey = sourceKey(region.targetSource);
      if (!sourcesByKey.has(targetKey)) {
        chargeEntry(targetKey);
        sourcesByKey.set(targetKey, region.targetSource);
      }
      if (!targetsBySource.has(targetKey)) {
        chargeEntry(targetKey);
        targetsBySource.set(targetKey, {
          bounds: translateRect(region.bounds, item.origin),
          item,
        });
      }
    }
  }
  return { sourcesByKey, targetsBySource, observedBytes };
}

export function buildSpatialIndex(snapshot, policy, sourceIndexBytes) {
  const index = new Map();
  const maximumBytes = BigInt(policy.spatialIndexBytes);
  let observedBytes = sourceIndexBytes;
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
        throw new BrowserPolicyError("spatialIndexBytes", candidateBytes);
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

export function spatialCellKey(x, y) {
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
  const targetsTerminal = Boolean(terminalFromSource(region?.targetSource));
  if (!region || typeof region.localId !== "string" || !["port", "body", "label"].includes(region.kind)
      || !["rect", "circle", "polygon"].includes(region.shape)
      || !validRectAllowDegenerate(region.bounds)
      || (region.targetSource && !validSource(region.targetSource, definitionId))
      || targetsTerminal !== Boolean(validPoint(region.anchor))
      || targetsTerminal !== isPlanDirection(region.outwardDirection)) {
    throw new Error("invalid hit region");
  }
  if (region.shape === "circle" && (!validPoint(region.center) || !Number.isFinite(region.radius)
      || region.radius <= 0)) throw new Error("invalid circle hit region");
  if (region.shape === "polygon" && (!Array.isArray(region.points) || region.points.length < 3
      || region.points.some((point) => !validPoint(point)))) throw new Error("invalid polygon hit region");
}

export function validSource(source, definitionId) {
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

export function validTool(tool) {
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

function sameSource(left, right) {
  return left && right && sourceKey(left) === sourceKey(right);
}

export function sourceKey(source) {
  return [source.circuitDefinitionId, source.entityKind, source.entityId, source.portId ?? ""]
    .map((part) => `${part.length}:${part}`)
    .join("");
}

function positiveSafeInteger(value) { return Number.isSafeInteger(value) && value > 0; }
export function isLocale(value) { return value === "en-US" || value === "zh-CN"; }
export function isDirection(value) { return value === "ltr" || value === "rtl"; }
function isPlanDirection(value) {
  return value === "north" || value === "east" || value === "south" || value === "west";
}
export function isAlignment(value) { return value === "start" || value === "center" || value === "end"; }
export function isTextRole(value) {
  return value === "symbol" || value === "portlabel"
    || value === "dependency" || value === "extensionmark";
}
export function isToken(value) { return typeof value === "string" && /^[A-Za-z0-9._-]+$/.test(value); }
export function isDigest(value) { return typeof value === "string" && /^[0-9a-f]{64}$/.test(value); }
export function compareOrdinal(left, right) { return left < right ? -1 : left > right ? 1 : 0; }
export function encodedJsonBytes(value) { return BigInt(textEncoder.encode(JSON.stringify(value)).byteLength); }
function assertPolicyLimit(dimension, observed, limit) {
  if (BigInt(observed) > BigInt(limit)) {
    throw new BrowserPolicyError(dimension, observed);
  }
}
export function packagedFontSupports(text) {
  for (const character of text) {
    const codePoint = character.codePointAt(0);
    let lower = 0;
    let upper = packagedFontCodePointRanges.length - 1;
    let supported = false;
    while (lower <= upper) {
      const middle = Math.floor((lower + upper) / 2);
      const [minimum, maximum] = packagedFontCodePointRanges[middle];
      if (codePoint < minimum) {
        upper = middle - 1;
      } else if (codePoint > maximum) {
        lower = middle + 1;
      } else {
        supported = true;
        break;
      }
    }
    if (!supported) {
      return false;
    }
  }
  return true;
}
export function decodeBase64(value) { const binary = atob(value); return Uint8Array.from(binary, (character) => character.charCodeAt(0)); }
