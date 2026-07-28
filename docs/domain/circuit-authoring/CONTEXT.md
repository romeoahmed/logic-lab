# Circuit Authoring

Circuit Authoring is the language of the gate-level design that a person creates and revises. It excludes compiled representations, simulation state, browser state, and file carriers.

## Language

**Logic Lab**:
A teaching-oriented environment for constructing, running, and explaining gate-level digital circuits under its own explicit semantics.
_Avoid_: SystemVerilog simulator, industrial EDA suite, HDL compatibility layer

**Project**:
One authored circuit-design lineage identified by a Project ID and represented by immutable Project Revisions from Project Genesis onward.
_Avoid_: Durable Project, Editor Workspace, file package

**Project Document**:
The complete authored design at one point in its edit history, including Circuit Definitions, initial data, and presentation choices.
_Avoid_: file package, simulation snapshot

**Project ID**:
Stable authored identity preserved across a Project's revisions and native export/import. It is not a durable resource locator or authorization fact.
_Avoid_: Durable Project ID, Project Revision ID, content digest

**Project Genesis**:
The atomic creation of a Project's first Project Revision from a new-project request or a validated Import Candidate.
_Avoid_: Edit Transaction, Workspace publication, Durable Project creation, package decoding

**Project Revision**:
An immutable Project Document created by Project Genesis or one committed Edit Transaction.
_Avoid_: saved file, Circuit Definition revision, content digest

**Circuit Definition**:
A named design with an ordered Port contract that can be opened as an entry circuit or instantiated by another Circuit Definition.
_Avoid_: page, canvas, compiled module

**Component Contract**:
The stable semantic kind, Ports, parameters, and behavior available for instantiation.
_Avoid_: symbol template, component instance

**Component Contract Key**:
The stable pair of Library identity and Contract identity that resolves one Component Contract within a Library Snapshot.
_Avoid_: display name, Symbol Variant

**Library Snapshot**:
The immutable, versioned set of Component Contracts against which a Project Revision is authored and compiled.
_Avoid_: mutable catalog, palette

**Component Instance**:
One use of a Component Contract or Circuit Definition inside a Circuit Definition.
_Avoid_: component kind, palette item, symbol image

**Port**:
A named, directed, fixed-width connection point in a Component Contract or Circuit Definition contract.
_Avoid_: parameter, screen coordinate, implicit pin

**Terminal**:
A concrete occurrence of a Port inside a Circuit Definition, identified either by a Component Instance and Port or by the Circuit Definition interface.
_Avoid_: wire endpoint inferred from pixels

**Net**:
A stable electrical connection that owns the membership of Terminals and Junctions and carries one fixed-width Logic Vector.
_Avoid_: line segment, last-written value, screen path

**Junction**:
A stable topological point that explicitly joins branches of one Net. A geometric crossing is not a Junction.
_Avoid_: decorative dot, pixel intersection, implicit connection

**Wire Geometry**:
The editable visual route associated with a Net. It presents connectivity but never defines it.
_Avoid_: Net, simulation edge, electrical identity

**Logic Vector**:
An ordered, fixed positive-width sequence of Logic Values whose index zero is the least-significant bit. It has no implicit signed interpretation.
_Avoid_: integer, dynamically sized array, signed Net

**Memory Image**:
Immutable authored initial contents for one ROM or RAM shape. Runtime RAM changes never modify the Memory Image.
_Avoid_: runtime memory, binary package part, Trace

**Hierarchy Path**:
The stable ordered sequence of scoped steps `(containing Circuit Definition, Component Instance)` from an entry Circuit Definition to an entity inside an elaborated instance.
_Avoid_: display name, coordinate path

**Edit Transaction**:
One atomic authoring intention that either produces one Project Revision or produces none. It is the smallest Undo and Redo unit.
_Avoid_: Edit Intent, simulation event

**Edit Intent**:
A closed request describing one authoring intention whose successful validation becomes one Edit Transaction.
_Avoid_: arbitrary patch, pointer gesture, Edit Transaction
