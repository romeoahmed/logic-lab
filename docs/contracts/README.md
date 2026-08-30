# Seam Contracts

Each contract owns one real seam. Shared diagnostic codes and outcome reasons remain in [Diagnostics V1](../specs/diagnostics-v1.md); contracts do not create a generic message model.

| Seam                | Contract                                                | Owns                                                                                     |
| ------------------- | ------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| Web → Application   | [Editor Workspace](./editor-workspace.md)               | typed commands, queries, identities, preconditions, idempotency, outcomes, Trace reads   |
| Web → Application   | [Durable Project Catalog](./durable-project-catalog.md) | authorized bounded listing, invariant order, keyset cursor, closed V1 catalog capability |
| Web ↔ browser       | [Browser Adapters](./browser-adapters.md)               | Scene and Waveform snapshots, patches, intents, cursors, validation                      |
| browser ↔ HTTP host | [HTTP Boundary](./http-boundary.md)                     | form ingress, import/export transport, download authorization, RFC 9457 mapping          |

Compiler, Simulation Runtime, Boolean Analysis, Project Format, and Diagram Presentation are direct in-process Module calls. Their interfaces belong to their specifications, not this directory.
