export function contains(region, point) {
  if (region.shape === "rect") {
    return (
      point.x >= region.bounds.left &&
      point.x <= region.bounds.right &&
      point.y >= region.bounds.top &&
      point.y <= region.bounds.bottom
    );
  }
  if (region.shape === "circle") {
    const x = point.x - region.center.x;
    const y = point.y - region.center.y;
    return x * x + y * y <= region.radius * region.radius;
  }

  let inside = false;
  for (
    let index = 0, previous = region.points.length - 1;
    index < region.points.length;
    previous = index++
  ) {
    const currentPoint = region.points[index];
    const priorPoint = region.points[previous];
    if (
      currentPoint.y > point.y !== priorPoint.y > point.y &&
      point.x <
        ((priorPoint.x - currentPoint.x) * (point.y - currentPoint.y)) /
          (priorPoint.y - currentPoint.y) +
          currentPoint.x
    ) {
      inside = !inside;
    }
  }
  return inside;
}

export function hitPriority(item, region) {
  if (region.kind === "port") return 5;
  if (item.source.entityKind === "junction") return 4;
  if (item.source.entityKind === "componentInstance") return 3;
  if (item.source.entityKind === "wireGeometry") return 2;
  return 1;
}

export function translateRect(rect, origin) {
  return {
    left: rect.left + origin.x,
    top: rect.top + origin.y,
    right: rect.right + origin.x,
    bottom: rect.bottom + origin.y,
  };
}

export function rectFromPoints(first, second) {
  return {
    left: Math.min(first.x, second.x),
    top: Math.min(first.y, second.y),
    right: Math.max(first.x, second.x),
    bottom: Math.max(first.y, second.y),
  };
}

export function gestureMoved(gesture) {
  return (
    gesture.startWorld.x !== gesture.currentWorld.x ||
    gesture.startWorld.y !== gesture.currentWorld.y
  );
}

export function selectionModeFromModifiers(event) {
  if (event.ctrlKey || event.metaKey) return "toggle";
  if (event.shiftKey) return "add";
  return "replace";
}

export function intersects(left, right) {
  return (
    left.left <= right.right &&
    left.right >= right.left &&
    left.top <= right.bottom &&
    left.bottom >= right.top
  );
}

export function expandRect(rect, margin) {
  return {
    left: rect.left - margin,
    top: rect.top - margin,
    right: rect.right + margin,
    bottom: rect.bottom + margin,
  };
}

export function validComponentPlacement(placement) {
  return (
    placement &&
    validGridPoint(placement.origin) &&
    Number.isSafeInteger(placement.quarterTurnsClockwise) &&
    placement.quarterTurnsClockwise >= 0 &&
    placement.quarterTurnsClockwise <= 3 &&
    typeof placement.reflected === "boolean"
  );
}

export function validGridPoint(point) {
  return (
    point &&
    Number.isSafeInteger(point.x) &&
    Number.isSafeInteger(point.y) &&
    point.x >= -2147483648 &&
    point.x <= 2147483647 &&
    point.y >= -2147483648 &&
    point.y <= 2147483647
  );
}

export function terminalFromSource(source) {
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

export function netFromHit(hit) {
  if (!hit) return null;
  if (hit.source.entityKind === "net") return hit.source;
  const interaction = hit.item?.interaction;
  return ["wire", "junction", "net"].includes(interaction?.interactionKind)
    ? interaction.net
    : null;
}

export function gridPoint(world, snapshot, disableSnap) {
  const x = gridCoordinate(world.x, snapshot, disableSnap);
  const y = gridCoordinate(world.y, snapshot, disableSnap);
  return x === null || y === null ? null : { x, y };
}

export function gridToWorld(point, snapshot) {
  return {
    x: point.x * snapshot.gridStepPlanUnits,
    y: point.y * snapshot.gridStepPlanUnits,
  };
}

export function terminalWireRoute(snapshot, startHit, endHit, startWorld, endWorld, disableSnap) {
  const start = wireEndpoint(startHit, startWorld, snapshot, disableSnap);
  const end = wireEndpoint(endHit, endWorld, snapshot, disableSnap);
  if (!start || !end || samePoint(start, end)) {
    return null;
  }

  const points = orthogonalWirePoints(
    start,
    end,
    terminalDirection(startHit),
    terminalDirection(endHit),
    disableSnap ? 1 : snapshot.snapStepGridUnits,
  );
  return points.length >= 2 ? { kind: "orthogonal", points } : null;
}

export function orthogonalDragRoute(start, end) {
  return {
    kind: "orthogonal",
    points: compactOrthogonalPoints([start, { x: start.x, y: end.y }, end]),
  };
}

export function translateGridPoint(point, start, end) {
  const x = signedIntegerOrNull(point.x + end.x - start.x);
  const y = signedIntegerOrNull(point.y + end.y - start.y);
  return x === null || y === null ? null : { x, y };
}

export function translateComponentPlacement(placement, start, end) {
  const origin = translateGridPoint(placement.origin, start, end);
  return origin
    ? {
        origin,
        quarterTurnsClockwise: placement.quarterTurnsClockwise,
        reflected: placement.reflected,
      }
    : null;
}

export function validRect(rect) {
  return validRectAllowDegenerate(rect) && rect.right > rect.left && rect.bottom > rect.top;
}

export function validRectAllowDegenerate(rect) {
  return (
    rect &&
    finiteNumbers(rect.left, rect.top, rect.right, rect.bottom) &&
    rect.right >= rect.left &&
    rect.bottom >= rect.top
  );
}

export function validPoint(point) {
  return point && finiteNumbers(point.x, point.y);
}

export function finiteNumbers(...values) {
  return values.every(Number.isFinite);
}

function gridCoordinate(worldCoordinate, snapshot, disableSnap) {
  const integerGridCoordinate = signedIntegerOrNull(
    roundHalfNegativeInfinity(worldCoordinate / snapshot.gridStepPlanUnits),
  );
  if (integerGridCoordinate === null) {
    return null;
  }
  if (disableSnap) {
    return integerGridCoordinate;
  }

  return signedIntegerOrNull(
    roundHalfNegativeInfinity(integerGridCoordinate / snapshot.snapStepGridUnits) *
      snapshot.snapStepGridUnits,
  );
}

function wireEndpoint(hit, fallback, snapshot, disableSnap) {
  if (!terminalFromSource(hit?.source)) {
    return gridPoint(fallback, snapshot, disableSnap);
  }

  const local = hit.region.anchor;
  return gridPoint(
    {
      x: local.x + hit.item.origin.x,
      y: local.y + hit.item.origin.y,
    },
    snapshot,
    disableSnap,
  );
}

function terminalDirection(hit) {
  if (!terminalFromSource(hit?.source)) return null;
  if (hit.region.outwardDirection === "north") return { x: 0, y: -1 };
  if (hit.region.outwardDirection === "east") return { x: 1, y: 0 };
  if (hit.region.outwardDirection === "south") return { x: 0, y: 1 };
  if (hit.region.outwardDirection === "west") return { x: -1, y: 0 };
  return null;
}

function orthogonalWirePoints(start, end, startDirection, endDirection, lead) {
  if (canRouteDirectly(start, end, startDirection, endDirection)) {
    return [start, end];
  }

  const step = Math.max(1, lead);
  const startLead = offsetPoint(start, startDirection, step);
  const endLead = offsetPoint(end, endDirection, step);
  const points = [start, startLead];
  const startIsHorizontal = Boolean(startDirection?.x);
  const endIsHorizontal = Boolean(endDirection?.x);
  const startIsVertical = Boolean(startDirection?.y);
  const endIsVertical = Boolean(endDirection?.y);

  if (startLead.x === endLead.x || startLead.y === endLead.y) {
    points.push(endLead);
  } else if (startIsHorizontal && endIsHorizontal) {
    const delta = end.x - start.x;
    const faceEachOther =
      Math.sign(delta) === startDirection.x &&
      Math.sign(-delta) === endDirection.x &&
      Math.abs(delta) >= step * 2;
    const channelX = faceEachOther
      ? snapMidpoint(startLead.x, endLead.x, step)
      : startDirection.x > 0
        ? Math.max(startLead.x, endLead.x) + step
        : Math.min(startLead.x, endLead.x) - step;
    points.push({ x: channelX, y: startLead.y }, { x: channelX, y: endLead.y }, endLead);
  } else if (startIsVertical && endIsVertical) {
    const delta = end.y - start.y;
    const faceEachOther =
      Math.sign(delta) === startDirection.y &&
      Math.sign(-delta) === endDirection.y &&
      Math.abs(delta) >= step * 2;
    const channelY = faceEachOther
      ? snapMidpoint(startLead.y, endLead.y, step)
      : startDirection.y > 0
        ? Math.max(startLead.y, endLead.y) + step
        : Math.min(startLead.y, endLead.y) - step;
    points.push({ x: startLead.x, y: channelY }, { x: endLead.x, y: channelY }, endLead);
  } else if (endIsHorizontal) {
    points.push({ x: startLead.x, y: endLead.y }, endLead);
  } else {
    points.push({ x: endLead.x, y: startLead.y }, endLead);
  }
  points.push(end);
  return compactOrthogonalPoints(points);
}

function canRouteDirectly(start, end, startDirection, endDirection) {
  if (start.y === end.y) {
    const direction = Math.sign(end.x - start.x);
    return (
      (!startDirection || (startDirection.y === 0 && startDirection.x === direction)) &&
      (!endDirection || (endDirection.y === 0 && endDirection.x === -direction))
    );
  }
  if (start.x === end.x) {
    const direction = Math.sign(end.y - start.y);
    return (
      (!startDirection || (startDirection.x === 0 && startDirection.y === direction)) &&
      (!endDirection || (endDirection.x === 0 && endDirection.y === -direction))
    );
  }
  return false;
}

function offsetPoint(point, direction, distance) {
  return direction
    ? { x: point.x + direction.x * distance, y: point.y + direction.y * distance }
    : point;
}

function snapMidpoint(first, second, step) {
  return roundHalfNegativeInfinity((first + second) / 2 / step) * step;
}

function compactOrthogonalPoints(points) {
  const compacted = [];
  for (const point of points) {
    if (samePoint(compacted.at(-1), point)) {
      continue;
    }
    while (compacted.length >= 2) {
      const previous = compacted.at(-2);
      const current = compacted.at(-1);
      if (
        (previous.x === current.x && current.x === point.x) ||
        (previous.y === current.y && current.y === point.y)
      ) {
        compacted.pop();
      } else {
        break;
      }
    }
    compacted.push(point);
  }
  return compacted;
}

function samePoint(left, right) {
  return left?.x === right?.x && left?.y === right?.y;
}

function roundHalfNegativeInfinity(value) {
  return Math.ceil(value - 0.5);
}

function signedIntegerOrNull(value) {
  return Number.isSafeInteger(value) && value >= -2147483648 && value <= 2147483647 ? value : null;
}
