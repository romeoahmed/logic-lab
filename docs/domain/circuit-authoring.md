# Circuit Authoring Glossary

Circuit Authoring describes the design a person creates and revises. The
[authoring specification](../specs/circuit-authoring.md) defines valid documents and
atomic edits; compiled state, browser state, and file carriers have separate owners.

## Projects and revisions

**Logic Lab**: A teaching environment for constructing, simulating, and inspecting
gate-level digital circuits under its own explicit semantics.

**Project**: One authored design lineage, identified by a Project ID and represented
by immutable Project Revisions from Project Genesis onward.

**Project ID**: Authored identity preserved across revisions and native export/import.
It is independent of durable storage location and authorization.

**Project Document**: The complete authored design at one point in its edit history,
including Circuit Definitions, initial data, and presentation choices.

**Project Revision**: An immutable Project Document created by Project Genesis or
one committed Edit Transaction.

**Project Genesis**: Atomic creation of the first Project Revision from a new-project
request or validated Import Candidate. Decoding a package, publishing a Workspace,
and creating durable storage are separate operations.

## Circuit structure

**Circuit Definition**: A named design with an ordered Port contract. It can serve as
an entry circuit or be instantiated by another Circuit Definition.

**Component Contract**: The stable semantic kind, Ports, parameters, and behavior
available for instantiation, independent of its graphical symbol.

**Component Contract Key**: The pair of Library identity and Contract identity that
resolves one Component Contract within a Library Snapshot.

**Library Snapshot**: The immutable, versioned set of Component Contracts against
which a Project Revision is authored and compiled.

**Component Instance**: One use of a Component Contract or Circuit Definition inside
a Circuit Definition.

**Port**: A named, directed, fixed-width connection point in a Component Contract or
Circuit Definition contract.

**Terminal**: A concrete Port occurrence inside a Circuit Definition, identified by
a Component Instance and Port or by the Circuit Definition interface.

**Net**: A stable electrical connection that owns Terminal and Junction membership
and carries one fixed-width Logic Vector.

**Junction**: A stable topological point that explicitly joins branches of one Net.
A geometric crossing alone creates no Junction.

**Wire Geometry**: The editable visual route associated with a Net. It presents
connectivity; topology determines the connection.

**Hierarchy Path**: The ordered sequence of scoped steps
`(containing Circuit Definition, Component Instance)` from an entry Circuit Definition
to an entity inside an elaborated instance. These stable identities distinguish
repeated uses of a definition.

## Values and edits

**Logic Vector**: An ordered, fixed positive-width sequence of Logic Values with the
least-significant bit at index zero and no implicit signed interpretation.

**Memory Image**: Immutable authored initial contents for one ROM or RAM shape.
Runtime RAM writes leave it unchanged.

**Edit Intent**: A closed request describing one authoring intention. Successful
validation turns it into an Edit Transaction.

**Edit Transaction**: One atomic authoring intention that produces one Project
Revision or none. It is the smallest Undo and Redo unit.
