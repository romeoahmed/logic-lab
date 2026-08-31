---
status: amended
amended-by: 0008-use-one-canvas-editor-surface
---

# Generate TeachingMixed symbols declaratively

> [ADR 0008](./0008-use-one-canvas-editor-surface.md) supersedes this decision's accessibility-tree and shared-accessibility-anchor requirements. Declarative renderer-neutral geometry, hit testing, and conformance remain accepted.

The default project-level Symbol Profile uses IEEE 91A-permitted distinctive outlines for familiar basic gates and parameterized rectangular templates for complex, sequential, memory, and user-defined components. Qualifiers, dependencies, Port groups, text, metrics, interaction geometry, and conformance are structured data that generate one renderer-neutral Geometry Plan; pre-drawn images are not source truth.

This preserves teaching familiarity while avoiding an asset explosion across width, fan-in, controls, orientation, labels, and localization. SVG, Canvas, print, and hit testing share Port anchors and geometry. Teaching extensions remain explicit rather than being marketed as standardized symbols.
