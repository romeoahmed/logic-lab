---
status: accepted
---

# Persist immutable Project Revisions

One Edit Transaction publishes one immutable whole-Project Revision. Transaction History stores revision roots and a cursor, while Simulation Session, Trace, viewport, and pointer previews remain separate. Durable storage keeps immutable normalized snapshots and moves a current-revision pointer under optimistic concurrency; it does not persist edit events as the source of truth.

This costs snapshot storage and explicit structural sharing, but gives atomic multi-definition edits, simple Undo/Redo, deterministic Compilation inputs, safe save conflicts, and straightforward import/export. Project Revision identity remains distinct from content digest and Durable Version; provider-specific concurrency mapping remains an Infrastructure concern.
