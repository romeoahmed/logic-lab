# Workspace and Persistence Glossary

Workspace and Persistence describes editor continuity, background work, and movement
between temporary, durable, imported, and exported project state. The
[Workspace contract](../contracts/editor-workspace.md) owns operations and outcomes.

## Editor continuity

**Editor Workspace**: One private opening of the editor, owning the current Project
Revision, Transaction History cursor, save state, and optional Simulation Session.

**Workspace Attachment**: The currently authorized controlling connection to an
Editor Workspace. A new attachment fences every older attachment.

**Workspace Projection**: The versioned semantic view of an Editor Workspace for
presentation. Schematic Projection supplies its static geometry separately.

**Transaction History**: The private linear sequence of Project Revisions and its
current cursor. A new Edit Transaction after Undo discards the abandoned Redo branch.

**Detached Workspace**: An Editor Workspace with no current attachment, paused and
retained under a bounded recovery policy. Retention does not provide offline editing
or durable storage.

## Projects and storage

**Sandbox Project**: A temporary project supporting full editing and import/export
without a durable-storage promise. Anonymous and authenticated users can open one.

**Durable Project**: An authenticated user's long-lived project whose current
Project Revision is stored under optimistic concurrency.

**Durable Project ID**: The opaque locator for a Durable Project, distinct from its
authored Project ID. Knowing the locator grants no authorization.

**Durable Display Name**: The immutable V1 catalog label for a Durable Project,
independent of the authored Project display name.

**Durable Version**: An opaque concurrency value identifying an observed state of a
Durable Project's current-revision pointer. It changes whenever that pointer changes,
including a return to an earlier Project Revision.

**Claim**: Authorized conversion of a Sandbox Project into a new Durable Project.
It allocates a Durable Project ID and preserves authored Project identity.

**Import Candidate**: A complete Project Document decoded and validated from an
untrusted carrier for Project Genesis. It becomes a Project Revision only through
Project Editor; Workspace publication is a further operation.

## Background work and Session changes

**Scheduling Policy**: Versioned admission, fairness, queue-capacity, and concurrency
rules for background work. It governs work admission independently of circuit semantics.

**Hot Swap**: An atomic switch at a Quiescent Boundary to a newly compiled Project
Revision using State Migration.

**Restart**: Explicit abandonment of runtime state followed by creation of a
Simulation Session from authored initial state. It is separate from a circuit's
Reset input and from Hot Swap.
