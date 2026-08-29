# Diagram Presentation

Diagram Presentation projects authored semantics into reproducible, renderable, hittable, and exportable schematic geometry without changing circuit behavior.

## Language

**Symbol Profile**:
A project-level, versioned mapping from Component Contracts to default Symbol Variants and diagram-wide indication conventions.
_Avoid_: icon theme, Symbol Variant, component kind

**Symbol Variant**:
One template-constrained graphical representation of the same Component Contract and Port ordering.
_Avoid_: Component Contract, Symbol Profile

**Geometry Plan**:
An immutable, renderer-neutral result containing drawing operations, Port anchors, bounds, Hit Regions, and conformance evidence.
_Avoid_: rendered image, pre-drawn asset

**Schematic Projection**:
The reproducible static geometry for one Circuit Definition in a Project Revision under one Symbol Profile and presentation fingerprint.
_Avoid_: Workspace Projection, selection state

**Transient Preview**:
A local visual result of an in-progress gesture that has not become an Edit Transaction.
_Avoid_: Project Revision, Edit Transaction
