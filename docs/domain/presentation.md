# Diagram Presentation Glossary

Diagram Presentation derives reproducible schematic geometry for rendering, hit
testing, and export without changing circuit behavior. The
[presentation specification](../specs/diagram-presentation.md) owns geometry rules.

**Symbol Profile**: A project-level, versioned mapping from Component Contracts to
default Symbol Variants and diagram-wide indication conventions.

**Symbol Variant**: One template-constrained graphical representation of a Component
Contract, preserving its semantics and Port ordering.

**Geometry Plan**: An immutable, renderer-neutral result containing drawing
operations, Port anchors, bounds, Hit Regions, and conformance evidence.

**Schematic Projection**: Static geometry for one Circuit Definition in a Project
Revision under one Symbol Profile and presentation fingerprint. Selection and live
values are composed separately.

**Transient Preview**: The local visual result of an in-progress gesture before it
becomes an Edit Transaction.
