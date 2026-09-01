---
status: accepted
---

# Compile authored topology into purpose-specific IR

Project Document keeps stable hierarchical identity, explicit Net membership, Junctions, Wire Geometry, and explicit width conversion. Compiler translates one immutable Project Revision through a private Elaborated Graph into Simulation IR and a total Source Map. Dense execution ordinals never become authored, browser, or package identity.

A universal graph would reduce translation code but make edit locality, diagnostics, runtime locality, proof eligibility, and geometry compete in one shallow model. Purpose-specific representations concentrate those concerns behind the Compiler interface and allow each to change without corrupting the others.

[Architecture](../architecture.md#module-catalog) fixes the module seam; [Compiler](../specs/compiler.md) owns the interface, translation, evidence, and publication behavior. Supporting evidence is in [Compiler Representation Research](../research/compiler-representations.md).
