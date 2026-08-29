export function drawOperation(context, operation, styles, symbolFontFamily) {
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

export function drawGridLines(context, visible, interval, color, width) {
  context.save();
  context.strokeStyle = color;
  context.lineWidth = width;
  context.beginPath();
  for (let x = Math.ceil(visible.left / interval) * interval;
    x <= visible.right;
    x += interval) {
    context.moveTo(x, visible.top);
    context.lineTo(x, visible.bottom);
  }
  for (let y = Math.ceil(visible.top / interval) * interval;
    y <= visible.bottom;
    y += interval) {
    context.moveTo(visible.left, y);
    context.lineTo(visible.right, y);
  }
  context.stroke();
  context.restore();
}

export function canvasAlignment(value, direction) {
  if (value === "center") return "center";
  if (value === "start") return direction === "ltr" ? "left" : "right";
  return direction === "ltr" ? "right" : "left";
}

export function cssColor(styles, name, fallback) {
  return styles.getPropertyValue(name).trim() || fallback;
}
