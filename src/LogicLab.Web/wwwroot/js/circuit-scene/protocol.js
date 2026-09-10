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
  effectiveDensityMillionths: "effective_density_millionths",
  zoomMillionthsMinimum: "zoom_millionths_minimum",
  zoomMillionthsMaximum: "zoom_millionths_maximum",
  displayListBytes: "display_list_bytes",
  spatialIndexBytes: "spatial_index_bytes",
  sceneCacheBytes: "scene_cache_bytes",
});

export function validatePolicy(policy) {
  const fields = [
    "semanticIntentBytes",
    "sceneSnapshotRecordCount",
    "scenePatchRecordCount",
    "interopBatchBytes",
    "candidateTransferBytes",
    "canvasBitmapPixels",
    "effectiveDensityMillionths",
    "zoomMillionthsMinimum",
    "zoomMillionthsMaximum",
    "displayListBytes",
    "spatialIndexBytes",
    "sceneCacheBytes",
  ];
  const exactShape = new Set(["policyId", "policyRevision", ...fields]);
  const actualShape = policy ? Object.keys(policy) : [];
  if (
    !policy ||
    !isToken(policy.policyId) ||
    !isToken(policy.policyRevision) ||
    actualShape.length !== exactShape.size ||
    actualShape.some((field) => !exactShape.has(field)) ||
    fields.some((field) => !positiveSafeInteger(policy[field])) ||
    policy.interopBatchBytes <= Number(interopEnvelopeBytes) ||
    BigInt(policy.zoomMillionthsMinimum) > BigInt(policy.zoomMillionthsMaximum)
  ) {
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
  if (
    shape.length !== 1 ||
    shape[0] !== "viewports" ||
    !Array.isArray(viewports) ||
    BigInt(viewports.length) > BigInt(policy.sceneSnapshotRecordCount) ||
    encodedJsonBytes(candidate) + interopEnvelopeBytes > BigInt(policy.interopBatchBytes)
  ) {
    throw new Error("invalid Scene recovery state");
  }

  const recovered = new Map();
  for (const viewport of viewports) {
    const viewportShape = viewport && Object.keys(viewport);
    if (
      !viewport ||
      viewportShape.length !== 4 ||
      !["circuitDefinitionId", "translateX", "translateY", "zoom"].every((field) =>
        viewportShape.includes(field),
      ) ||
      !isToken(viewport.circuitDefinitionId) ||
      !finiteNumbers(viewport.translateX, viewport.translateY, viewport.zoom) ||
      viewport.zoom < minimumZoom ||
      viewport.zoom > maximumZoom ||
      recovered.has(viewport.circuitDefinitionId)
    ) {
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
  if (
    !candidate ||
    candidate.buildFingerprint !== buildFingerprint ||
    !positiveSafeInteger(candidate.sceneVersion) ||
    !positiveSafeInteger(candidate.projectionVersion) ||
    typeof candidate.circuitDefinitionId !== "string" ||
    !candidate.circuitDefinitionId ||
    !isLocale(candidate.uiCulture) ||
    candidate.baseDirection !== "leftToRight"
  ) {
    throw new Error("invalid scene replacement envelope");
  }

  if (Array.isArray(candidate.diagnostics) && !candidate.items) {
    if (candidate.diagnostics.some((diagnostic) => typeof diagnostic !== "string")) {
      throw new Error("invalid unavailable scene");
    }
    return { kind: "unavailable", value: deepFreeze(candidate) };
  }

  validateSnapshot(candidate, fontFingerprint, policy);
  return { kind: "snapshot", value: deepFreeze(candidate) };
}

function validateSnapshot(candidate, fontFingerprint, policy) {
  if (
    !isDigest(candidate.fontFingerprint) ||
    candidate.fontFingerprint !== fontFingerprint ||
    typeof candidate.schematicProjectionKey !== "string" ||
    !candidate.schematicProjectionKey ||
    !validRect(candidate.bounds) ||
    !positiveSafeInteger(candidate.gridStepPlanUnits) ||
    !positiveSafeInteger(candidate.snapStepGridUnits) ||
    !Array.isArray(candidate.items) ||
    !Array.isArray(candidate.overlays)
  ) {
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
  let previousOrder = -1;
  let records = 1;
  for (const item of candidate.items) {
    if (
      !validSource(item?.source, candidate.circuitDefinitionId) ||
      sourceKeys.has(sourceKey(item.source)) ||
      !Number.isSafeInteger(item.order) ||
      item.order < 0 ||
      item.order <= previousOrder ||
      !validRect(item.bounds) ||
      !validPoint(item.origin) ||
      typeof item.hasDrawableTarget !== "boolean" ||
      !Array.isArray(item.operations) ||
      !Array.isArray(item.hitRegions) ||
      (!item.hasDrawableTarget && (item.operations.length > 0 || item.hitRegions.length > 0)) ||
      !validInteraction(item.interaction, item.source, candidate.circuitDefinitionId)
    ) {
      throw new Error("invalid scene item");
    }
    sourceKeys.add(sourceKey(item.source));
    previousOrder = item.order;
    records += 1 + item.operations.length + item.hitRegions.length;
    item.operations.forEach(validateOperation);
    item.hitRegions.forEach((region) => validateHit(region, candidate.circuitDefinitionId));
    records += item.operations.reduce((sum, operation) => sum + operation.commands.length, 0);
  }
  let previousOverlayId = null;
  for (const overlay of candidate.overlays) {
    if (
      !overlay ||
      typeof overlay.id !== "string" ||
      !overlay.id ||
      (previousOverlayId !== null && compareOrdinal(previousOverlayId, overlay.id) >= 0) ||
      !validOverlay(overlay, candidate.circuitDefinitionId)
    ) {
      throw new Error("invalid scene overlay");
    }
    previousOverlayId = overlay.id;
    records++;
  }
  assertPolicyLimit("sceneSnapshotRecordCount", records, policy.sceneSnapshotRecordCount);
}

export function validatePatch(patch, published, buildFingerprint, fontFingerprint, policy) {
  if (
    !published ||
    !patch ||
    patch.buildFingerprint !== buildFingerprint ||
    patch.baseSceneVersion !== published.sceneVersion ||
    !positiveSafeInteger(patch.nextSceneVersion) ||
    patch.nextSceneVersion <= patch.baseSceneVersion ||
    patch.projectionVersion < published.projectionVersion ||
    patch.circuitDefinitionId !== published.circuitDefinitionId ||
    patch.uiCulture !== published.uiCulture ||
    patch.baseDirection !== published.baseDirection ||
    patch.fontFingerprint !== fontFingerprint ||
    !Array.isArray(patch.itemUpserts) ||
    !Array.isArray(patch.itemRemovals) ||
    !Array.isArray(patch.overlayUpserts) ||
    !Array.isArray(patch.overlayRemovals)
  ) {
    return null;
  }

  try {
    const itemUpsertIds = patch.itemUpserts.map((item) => item?.source && sourceKey(item.source));
    const itemRemovalIds = patch.itemRemovals.map((source) => source && sourceKey(source));
    const overlayUpsertIds = patch.overlayUpserts.map((overlay) => overlay?.id);
    if (
      new Set(itemUpsertIds).size !== itemUpsertIds.length ||
      new Set(itemRemovalIds).size !== itemRemovalIds.length ||
      itemUpsertIds.some((id) => itemRemovalIds.includes(id)) ||
      new Set(overlayUpsertIds).size !== overlayUpsertIds.length ||
      new Set(patch.overlayRemovals).size !== patch.overlayRemovals.length ||
      patch.overlayRemovals.some((id) => typeof id !== "string" || !id) ||
      overlayUpsertIds.some((id) => patch.overlayRemovals.includes(id))
    ) {
      throw new Error();
    }
    let patchRecords =
      patch.itemUpserts.length +
      patch.itemRemovals.length +
      patch.overlayUpserts.length +
      patch.overlayRemovals.length;
    patchRecords += patch.itemUpserts.reduce(
      (sum, item) =>
        sum +
        item.operations.length +
        item.hitRegions.length +
        item.operations.reduce(
          (commandSum, operation) => commandSum + operation.commands.length,
          0,
        ),
      0,
    );
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
      if (
        !Number.isSafeInteger(minimumX) ||
        !Number.isSafeInteger(minimumY) ||
        !Number.isSafeInteger(maximumX) ||
        !Number.isSafeInteger(maximumY) ||
        !Number.isSafeInteger(columns) ||
        !Number.isSafeInteger(rows) ||
        columns <= 0 ||
        rows <= 0
      ) {
        throw new Error("spatial index coordinate range is invalid");
      }

      const source = region.targetSource ?? item.source;
      const entryBytes = BigInt(
        spatialEntryBaseBytes +
          textEncoder.encode(sourceKey(source)).byteLength +
          textEncoder.encode(region.localId).byteLength,
      );
      const candidateBytes = observedBytes + BigInt(columns) * BigInt(rows) * entryBytes;
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
  if (
    !operation ||
    !["stroke", "fill", "text"].includes(operation.kind) ||
    typeof operation.role !== "string" ||
    !validRectAllowDegenerate(operation.bounds) ||
    !Array.isArray(operation.commands)
  )
    throw new Error("invalid draw operation");
  for (const command of operation.commands) {
    if (
      !command ||
      !["move", "line", "cubic", "close"].includes(command.kind) ||
      !finiteNumbers(
        command.x,
        command.y,
        command.control1X,
        command.control1Y,
        command.control2X,
        command.control2Y,
      )
    )
      throw new Error("invalid path command");
  }
  if (
    operation.kind === "stroke" &&
    (!Number.isFinite(operation.width) ||
      operation.width <= 0 ||
      !Array.isArray(operation.dashPattern) ||
      operation.dashPattern.length % 2 !== 0 ||
      operation.dashPattern.some((value) => !Number.isFinite(value) || value <= 0) ||
      !["butt", "round", "square"].includes(operation.lineCap) ||
      !["miter", "round", "bevel"].includes(operation.lineJoin) ||
      !Number.isSafeInteger(operation.miterLimitRatio) ||
      (operation.lineJoin === "miter" && operation.miterLimitRatio <= 0) ||
      (operation.lineJoin !== "miter" && operation.miterLimitRatio !== 0))
  ) {
    throw new Error("invalid stroke");
  }
  if (operation.kind === "fill" && !["nonzero", "evenodd"].includes(operation.fillRule)) {
    throw new Error("invalid fill");
  }
  if (
    operation.kind === "text" &&
    (typeof operation.text !== "string" ||
      !validPoint(operation.origin) ||
      !isAlignment(operation.alignment) ||
      !isDirection(operation.direction) ||
      !isLocale(operation.locale))
  )
    throw new Error("invalid text");
}

function validateHit(region, definitionId) {
  const targetsTerminal = Boolean(terminalFromSource(region?.targetSource));
  if (
    !region ||
    typeof region.localId !== "string" ||
    !["port", "body", "label"].includes(region.kind) ||
    !["rect", "circle", "polygon"].includes(region.shape) ||
    !validRectAllowDegenerate(region.bounds) ||
    (region.targetSource && !validSource(region.targetSource, definitionId)) ||
    (region.connectedNet &&
      (!targetsTerminal || !validNetSource(region.connectedNet, definitionId))) ||
    targetsTerminal !== Boolean(validPoint(region.anchor)) ||
    targetsTerminal !== isPlanDirection(region.outwardDirection)
  ) {
    throw new Error("invalid hit region");
  }
  if (
    region.shape === "circle" &&
    (!validPoint(region.center) || !Number.isFinite(region.radius) || region.radius <= 0)
  )
    throw new Error("invalid circle hit region");
  if (
    region.shape === "polygon" &&
    (!Array.isArray(region.points) ||
      region.points.length < 3 ||
      region.points.some((point) => !validPoint(point)))
  )
    throw new Error("invalid polygon hit region");
}

export function validSource(source, definitionId) {
  const shape = source && Object.keys(source);
  return (
    source &&
    shape.length === 4 &&
    ["circuitDefinitionId", "entityKind", "entityId", "portId"].every((field) =>
      shape.includes(field),
    ) &&
    source.circuitDefinitionId === definitionId &&
    [
      "definitionPort",
      "componentInstance",
      "instancePort",
      "net",
      "junction",
      "wireGeometry",
      "annotation",
    ].includes(source.entityKind) &&
    typeof source.entityId === "string" &&
    source.entityId.length > 0 &&
    (source.entityKind === "instancePort"
      ? typeof source.portId === "string" && source.portId.length > 0
      : source.portId === null)
  );
}

function validInteraction(interaction, source, definitionId) {
  if (!interaction || typeof interaction.interactionKind !== "string") return false;
  if (interaction.interactionKind === "component") {
    return (
      source.entityKind === "componentInstance" && validComponentPlacement(interaction.placement)
    );
  }
  if (interaction.interactionKind === "definitionPort") {
    return (
      source.entityKind === "definitionPort" &&
      validGridPoint(interaction.placement?.position) &&
      ["north", "east", "south", "west"].includes(interaction.placement?.facing)
    );
  }
  if (interaction.interactionKind === "annotation") {
    return source.entityKind === "annotation" && validGridPoint(interaction.position);
  }
  if (interaction.interactionKind === "net") {
    return source.entityKind === "net" && sameSource(interaction.net, source);
  }
  if (interaction.interactionKind === "wire") {
    return (
      source.entityKind === "wireGeometry" &&
      validNetSource(interaction.net, definitionId) &&
      validRoute(interaction.route)
    );
  }
  return (
    interaction.interactionKind === "junction" &&
    source.entityKind === "junction" &&
    validNetSource(interaction.net, definitionId)
  );
}

function validOverlay(overlay, definitionId) {
  if (
    !overlay ||
    typeof overlay.id !== "string" ||
    !overlay.id ||
    !validSource(overlay.source, definitionId)
  )
    return false;
  if (overlay.kind === "selection") return ["primary", "member"].includes(overlay.role);
  if (overlay.kind === "diagnosticMarker") {
    return (
      typeof overlay.diagnosticCode === "string" &&
      overlay.diagnosticCode.length > 0 &&
      ["info", "warning", "error"].includes(overlay.severity) &&
      Number.isSafeInteger(overlay.diagnosticOrdinal) &&
      overlay.diagnosticOrdinal >= 0
    );
  }
  if (overlay.kind === "probeAnchor") {
    return (
      typeof overlay.probeId === "string" &&
      overlay.probeId.length > 0 &&
      validElaboratedNet(overlay.net, definitionId) &&
      sameSource(overlay.source, overlay.net.authoredNet) &&
      validPoint(overlay.point) &&
      Number.isSafeInteger(overlay.appearanceOrdinal) &&
      overlay.appearanceOrdinal >= 0 &&
      overlay.appearanceOrdinal < 16 &&
      overlay.pattern ===
        ["solid", "dash", "dot", "dashDot"][Math.floor(overlay.appearanceOrdinal / 4)]
    );
  }
  if (overlay.kind === "liveNetValue") {
    if (
      !validElaboratedNet(overlay.net, definitionId) ||
      !sameSource(overlay.source, overlay.net.authoredNet) ||
      typeof overlay.sessionId !== "string" ||
      !overlay.sessionId ||
      !positiveSafeInteger(overlay.sessionVersion) ||
      !positiveSafeInteger(overlay.value?.width) ||
      overlay.value?.encoding !== "logic4-2bit-v1" ||
      typeof overlay.value?.data !== "string"
    )
      return false;
    try {
      return decodeBase64(overlay.value.data).byteLength === Math.ceil(overlay.value.width / 4);
    } catch {
      return false;
    }
  }
  return false;
}

function validElaboratedNet(net, definitionId) {
  return (
    net && validNetSource(net.authoredNet, definitionId) && validHierarchyPath(net.hierarchyPath)
  );
}

function validNetSource(source, definitionId) {
  return validSource(source, definitionId) && source.entityKind === "net";
}

function validHierarchyPath(path) {
  return (
    path &&
    typeof path.entryCircuitDefinitionId === "string" &&
    path.entryCircuitDefinitionId.length > 0 &&
    Array.isArray(path.steps) &&
    path.steps.every(
      (step) =>
        step &&
        typeof step.containingCircuitDefinitionId === "string" &&
        step.containingCircuitDefinitionId.length > 0 &&
        typeof step.componentInstanceId === "string" &&
        step.componentInstanceId.length > 0,
    )
  );
}

function validRoute(route) {
  return (
    route?.kind === "unrouted" ||
    (route?.kind === "orthogonal" &&
      Array.isArray(route.points) &&
      route.points.every(validGridPoint))
  );
}

export function validTool(tool) {
  if (!tool || typeof tool.kind !== "string") return false;
  if (["select", "wire", "pan"].includes(tool.kind)) {
    return Object.keys(tool).length === 1;
  }
  if (tool.kind === "probe") {
    return validHierarchyPath(tool.hierarchyPath);
  }
  return (
    tool.kind === "placeComponent" &&
    validComponentTarget(tool.target) &&
    Array.isArray(tool.parameters) &&
    tool.parameters.every(
      (parameter) =>
        parameter &&
        typeof parameter.parameterId === "string" &&
        parameter.parameterId.length > 0 &&
        parameter.value &&
        typeof parameter.value.kind === "string",
    ) &&
    (tool.displayName === null || typeof tool.displayName === "string") &&
    typeof tool.pinned === "boolean"
  );
}

function validComponentTarget(target) {
  return target?.kind === "libraryContract"
    ? typeof target.libraryId === "string" &&
        target.libraryId.length > 0 &&
        typeof target.contractId === "string" &&
        target.contractId.length > 0
    : target?.kind === "circuitDefinition" &&
        typeof target.circuitDefinitionId === "string" &&
        target.circuitDefinitionId.length > 0;
}

function sameSource(left, right) {
  return left && right && sourceKey(left) === sourceKey(right);
}

export function sourceKey(source) {
  return [source.circuitDefinitionId, source.entityKind, source.entityId, source.portId ?? ""]
    .map((part) => `${part.length}:${part}`)
    .join("");
}

function positiveSafeInteger(value) {
  return Number.isSafeInteger(value) && value > 0;
}
export function isLocale(value) {
  return value === "en-US" || value === "zh-CN";
}
export function isDirection(value) {
  return value === "ltr" || value === "rtl";
}
function isPlanDirection(value) {
  return value === "north" || value === "east" || value === "south" || value === "west";
}
export function isAlignment(value) {
  return value === "start" || value === "center" || value === "end";
}
export function isTextRole(value) {
  return (
    value === "symbol" ||
    value === "portlabel" ||
    value === "dependency" ||
    value === "extensionmark"
  );
}
export function isToken(value) {
  return typeof value === "string" && /^[A-Za-z0-9._-]+$/.test(value);
}
export function isDigest(value) {
  return typeof value === "string" && /^[0-9a-f]{64}$/.test(value);
}
export function compareOrdinal(left, right) {
  return left < right ? -1 : left > right ? 1 : 0;
}
export function encodedJsonBytes(value) {
  return BigInt(textEncoder.encode(JSON.stringify(value)).byteLength);
}
function assertPolicyLimit(dimension, observed, limit) {
  if (BigInt(observed) > BigInt(limit)) {
    throw new BrowserPolicyError(dimension, observed);
  }
}
export function decodeBase64(value) {
  return Uint8Array.fromBase64(value);
}
