# Architecture Decisions

ADRs record hard-to-reverse, non-obvious trade-offs. Specifications define current observable behavior; ADRs explain why that behavior was chosen. An ADR never overrides a newer specification by accident—supersession must be explicit.

## Accepted

### Core model and execution

- [0001 — Own four-state zero-delay semantics](./0001-own-four-state-zero-delay-semantics.md)
- [0002 — Compile authored topology into purpose-specific IR](./0002-compile-authored-topology-to-purpose-specific-ir.md)
- [0003 — Persist immutable Project Revisions](./0003-persist-immutable-project-revisions.md)

### Application and carriers

- [0004 — Host Application-owned Workspaces on Interactive Server](./0004-host-application-owned-workspaces-on-interactive-server.md)
- [0005 — Use one strict `.logiclab` carrier](./0005-use-one-strict-logiclab-carrier.md)

### Deployment

- [0009 — Select the Azure production deployment profile](./0009-select-azure-production-profile.md)

### Presentation

- [0007 — Generate TeachingMixed symbols declaratively](./0007-generate-teachingmixed-symbols-declaratively.md) — amended by 0008
- [0008 — Use one Canvas editor surface](./0008-use-one-canvas-editor-surface.md)

## Deferred

- [0006 — Keep simplification managed and proof-gated](./0006-keep-simplification-managed-and-proof-gated.md) — retained for the future Boolean Analysis plan; not V1 architecture

## Rule for new records

Create an ADR only when a choice is expensive to reverse, surprising without its context, and selected among real alternatives. Component truth tables, package fields, package versions, policy values, UI tokens, and benchmark results belong in their owning specification, package catalog, policy, or evidence record.
