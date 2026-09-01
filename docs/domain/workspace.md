# Workspace and Persistence Glossary

Workspace and Persistence describes private editor continuity, background operations, and movement between transient, durable, imported, and exported project state.

## Language

**Editor Workspace**:
One private opening of the editor, owning the current Project Revision, Transaction History cursor, save state, and optional Simulation Session.
_Avoid_: browser tab, Durable Project

**Workspace Attachment**:
The currently authorized controlling connection to an Editor Workspace. A new attachment fences every older attachment.
_Avoid_: access token, Editor Workspace, permanent session

**Workspace Projection**:
The versioned semantic view of one Editor Workspace for presentation, excluding static schematic geometry.
_Avoid_: Project Document, Schematic Projection

**Transaction History**:
The private linear sequence of Project Revisions and its current cursor. A new Edit Transaction after Undo discards the abandoned Redo branch.
_Avoid_: audit log, simulation replay, waveform history

**Detached Workspace**:
An Editor Workspace with no current attachment that is paused and retained only under a bounded recovery policy.
_Avoid_: offline editor, Durable Project, disconnected circuit itself

**Sandbox Project**:
An anonymous temporary project that supports full editing and import/export but has no durable-storage promise.
_Avoid_: read-only demo, Durable Project, browser file

**Durable Project**:
An authenticated user's long-lived project whose current Project Revision is stored under optimistic concurrency.
_Avoid_: Editor Workspace, Simulation Session, file carrier

**Durable Project ID**:
The opaque locator for one Durable Project. It is distinct from authored Project identity and never grants authorization.
_Avoid_: Project ID, Project Revision ID, capability token

**Durable Display Name**:
The immutable V1 catalog label for one Durable Project. It is distinct from the authored Project display name.
_Avoid_: Project identity, filename, authorization fact, mutable title

**Durable Version**:
An opaque optimistic-concurrency value naming one observed state of a Durable Project's current-revision pointer. It changes whenever that pointer changes, including when it later points to an earlier Project Revision.
_Avoid_: HTTP ETag, Project Revision ID, content digest

**Claim**:
The authorized conversion of a Sandbox Project into a new Durable Project, allocating a Durable Project ID without changing authored Project identity.
_Avoid_: import, copy, save as

**Import Candidate**:
A complete Project Document decoded and migrated from an untrusted carrier but not yet compiled or published into a Workspace.
_Avoid_: partial upload, Project Revision, trusted package

**Scheduling Policy**:
Versioned admission, fairness, queue-capacity, and concurrency rules for background work.
_Avoid_: Module work policy, semantic invariant

**Hot Swap**:
An atomic switch at a Quiescent Boundary to a newly compiled Project Revision using State Migration.
_Avoid_: in-place IR mutation, Restart, UI hot reload

**Restart**:
The explicit abandonment of runtime state followed by creation of a Simulation Session from authored initial state.
_Avoid_: Reset input, Hot Swap, resume
