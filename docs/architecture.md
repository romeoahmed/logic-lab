# Architecture

> Status: normative V1 architecture

This document owns system shape, dependency direction, module seams, and fact
ownership. Specifications own observable behavior, contracts own exchanged values,
[Product](./product.md) owns the workbench experience, [Domain](./domain/README.md)
owns language, and [Delivery](./delivery.md) alone records completion.

## V1 snapshot

Logic Lab is a modular monolith on .NET 10 and Blazor. V1 supports:

- hierarchical Circuit Definitions with explicit Ports, Nets, Junctions, and Wire
  Geometry;
- deterministic four-state `0/1/X/Z`, zero-delay simulation in discrete Logical
  Time;
- combinational, sequential, clock, register, counter, ROM, and single-port RAM
  Component Contracts;
- Transaction History, durable projects, strict `.logiclab` import/export, and
  Logic Analyzer Trace; and
- declarative TeachingMixed symbols rendered through one Canvas editor.

V1 excludes HDL compatibility, physical delay, analog simulation, timing closure,
collaboration, offline editing, public sharing, plug-ins, user-supplied executable
logic, and Boolean explanation or simplification. The last capability has one
non-normative [future proposal](./future/boolean-analysis.md).

## Design rules

A **module** hides behavior behind one **interface**. The interface includes every
invariant, ordering rule, outcome, configuration requirement, and performance
obligation a caller must know. A **seam** is where behavior can vary without editing
the caller; an **adapter** satisfies an interface at that seam.

1. **One owner per fact.** Authored topology, compiled representation, runtime state,
   static geometry, browser state, and durable location remain distinct.
2. **Prefer deep modules.** Callers express intent; passes, schedulers, codecs,
   solvers, caches, and mutable builders remain implementation.
3. **Create real seams.** Side-effect ports preserve dependency direction. Internal
   strategy interfaces require actual variation, not only a mock.
4. **Test through the interface.** Production callers and tests cross the same seam.
5. **Publish atomically and fail closed.** Invalid, stale, cancelled, exhausted, or
   unauthorized work publishes no partial state.
6. **Make determinism observable.** Stable order, explicit tie-breakers, immutable
   inputs, and complete provenance govern outcomes.
7. **Keep policy separate from semantics.** Capacity and latency require measured
   evidence; correctness and identity do not vary by deployment.
8. **Optimize from an oracle.** Scalar and full-recomputation paths define behavior
   before packing, pooling, incrementality, parallelism, or caching.

Apply the deletion test: removing a useful module redistributes complexity across
callers. Pass-through facades, contracts-only projects, generic message buses,
repository-per-entity wrappers, and shared `Common` models fail this test.

## Fact ownership

Bounded contexts own language and consistency inside one process; they do not imply
network boundaries.

| Fact                                                                | Owner                | Derived consumers                                          |
| ------------------------------------------------------------------- | -------------------- | ---------------------------------------------------------- |
| Project Document, Project Revision, topology, authored presentation | Circuit Authoring    | Compiler, Diagram Presentation, Project Format, repository |
| Compilation Artifact and Source Map                                 | Compiler             | Simulation, Web                                            |
| Logical Time, values, state, event calendar, Trace                  | Simulation           | Editor Workspace, Web                                      |
| attachment, history, save, Compilation, Session, and Run state      | Editor Workspace     | Web                                                        |
| Geometry Plan and Schematic Projection                              | Diagram Presentation | browser and export adapters                                |
| durable current-revision pointer and payload                        | repository adapter   | Editor Workspace                                           |
| selection, focus, viewport, previews, live overlays                 | Web/browser adapters | workbench projection                                       |

Compiler and Project Format are translation modules. Compiler translates one Project
Revision into purpose-specific executable artifacts; Project Format translates an
untrusted carrier into an Import Candidate. Neither changes authored facts.

## System shape

```text
Browser
  Razor/Fluent chrome <-> Scene and Waveform adapters
            | completed intents, snapshots, patches, Trace windows
            v
LogicLab.Web
  Static SSR site + per-page Interactive Server editor
  ├── Web Host and browser adapters
  ├── Diagram Presentation
  └── direct typed calls
            v
Editor Workspace
  revisions, history, save, attachments, idempotency
  ├── Project Editor
  ├── Project Format
  ├── repository port -> EF Core adapter -> PostgreSQL
  └── Work Coordinator
        ├── Compiler
        └── Simulation Runtime
```

The selected production profile runs one ASP.NET Core process in one Azure Container
Apps revision and one Azure Database for PostgreSQL Flexible Server database. One Web
replica is an explicit qualification boundary, not a high-availability claim. Provider
detail belongs to the [production profile](./deployment/production-profile.md).

## Project graph

Project seams follow dependency or deployment seams, not namespaces.

| Project                     | Responsibility                                                     | Direct production dependencies            |
| --------------------------- | ------------------------------------------------------------------ | ----------------------------------------- |
| `LogicLab.Domain`           | immutable authoring model and Project Editor                       | BCL                                       |
| `LogicLab.Engine`           | Compiler and Simulation Runtime                                    | Domain                                    |
| `LogicLab.Presentation`     | TeachingMixed geometry and Schematic Projection                    | Domain                                    |
| `LogicLab.ProjectFormat`    | strict `.logiclab` read/write and canonical encoding               | Domain                                    |
| `LogicLab.Application`      | Editor Workspace, authorization-aware use cases, work coordination | Domain, Engine, ProjectFormat             |
| `LogicLab.Infrastructure`   | EF Core persistence and Identity adapters                          | Application, Domain                       |
| `LogicLab.DatabaseMigrator` | principal bootstrap and deterministic migrations                   | Infrastructure                            |
| `LogicLab.Web`              | Blazor UI, HTTP/browser adapters, composition root                 | Application, Presentation, Infrastructure |

Dependency invariants:

- Domain references no persistence, serialization, Web, Simulation, or rendering
  implementation.
- Engine owns compiled and runtime representations without exposing its passes.
- Presentation emits geometry only; it owns neither selection nor live state.
- Application coordinates modules without duplicating their phases.
- Infrastructure implements Application-owned persistence ports.
- DatabaseMigrator alone applies production schema migrations.
- Web alone references Fluent UI and browser interop.

Tests and benchmarks remain separate and create no production seam.

## Module catalog

| Module                  | Caller intention                             | Hidden implementation                                          | Interface owner                                                                                       |
| ----------------------- | -------------------------------------------- | -------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| Project Editor          | begin a Project; apply one Edit Intent       | identity, normalization, invariants, structural sharing        | [Circuit Authoring](./specs/circuit-authoring.md)                                                     |
| Compiler                | compile one Project Revision                 | validation, hierarchy, SCCs, ordinals, IR, Source Map          | [Compiler](./specs/compiler.md)                                                                       |
| Simulation Runtime      | open, execute, and read one Session          | propagation, batching, rollback, memory, Trace, Hot Swap       | [Simulation Runtime](./specs/simulation-runtime.md)                                                   |
| Project Format          | read or write one `.logiclab` carrier        | bounded spool, ZIP, DTOs, digests, memory encoding             | [Project Package V1](./specs/project-package-v1.md)                                                   |
| Diagram Presentation    | plan one symbol; project one circuit         | constraints, text metrics, drawing operations, conformance     | [Diagram Presentation](./specs/diagram-presentation.md)                                               |
| Editor Workspace        | open, attach, dispatch, and read             | fencing, idempotency, history, save, work coordination         | [Editor Workspace](./contracts/editor-workspace.md)                                                   |
| Durable Project Catalog | list one authorized bounded page             | filtering, stable order, keyset cursor, persistence projection | [Catalog Contract](./contracts/durable-project-catalog.md)                                            |
| Scene/Waveform adapters | replace/apply state; return completed intent | interop, transforms, frames, hit index, previews, caches       | [Browser Runtime](./specs/browser-runtime.md) and [Browser Contract](./contracts/browser-adapters.md) |
| Web Host                | serve routes and manage host lifetimes       | render modes, circuits, culture, middleware, health, shutdown  | [Web Host](./specs/web-host.md) and [HTTP Contract](./contracts/http-boundary.md)                     |

There is no general engine facade, codec port, message bus, public compiler pass,
renderer interface, or algorithm-selection interface.

## Application, persistence, and Web

One editor opening creates one Application-owned Editor Workspace with one current
attachment and zero or one Simulation Session.

```text
Opening -> Attached <-> Detached -> Expired
              |
              +-- Project Revision + Transaction History
              +-- save + Compilation state
              +-- optional Simulation Session
```

Reattachment reauthorizes and fences the previous generation. A detached Workspace
accepts no command; active advance commits or rolls back before pausing. Reattach
never resumes Run automatically.

Compilation uses newest-request-wins per Workspace; Session commands serialize per
Session. CPU modules are synchronous. Async appears at I/O, cancellation, admission,
and observation seams. Razor creates no hidden queues.

Durable storage keeps immutable Project Revision payloads and moves one current
pointer under optimistic concurrency. It is not event sourcing or a mutable EF graph.
Infrastructure owns storage encoding and uses short-lived contexts.

Public, help, account, and project pages use Static SSR. `/editor` uses per-page
Interactive Server. Razor owns chrome and recovery; browser adapters own frame-rate
pixels and input. Canvas is the only dense circuit editor. V1 has no `.Client`
project, Interactive Auto, public REST interface, custom Hub, or browser engine.

## Cross-cutting rules

- Authenticate and authorize every resource action. IDs and tokens locate resources;
  they never grant authority.
- Treat browser records, files, JSON, coordinates, names, lengths, and package
  metadata as untrusted.
- Emit stable codes and low-cardinality telemetry; never log project payloads,
  tokens, Trace values, or unbounded identifiers.
- Core modules return structured evidence and do not depend on exporters.
- [Engineering](./engineering.md) owns build, dependency, language, configuration,
  serialization, observability, and test rules.
- [Policies](./policies.md) owns every capacity envelope and measured threshold.

## Verification and evolution

Evidence follows ownership: Domain tests authoring invariants; Engine tests compiler
and runtime semantics; Project Format tests strict carriers; Presentation tests
geometry; Application and Infrastructure test workspace and persistence behavior;
Web tests host, projection, and browser workflows. A UI test cannot prove simulation
semantics, and coverage is supporting telemetry rather than a release gate.

Revisit a seam only when measured evidence identifies the pressure. Network latency
may justify a process boundary; CPU contention may justify workers; frame pressure may
justify browser workers or WebAssembly; storage or recovery evidence may justify a
new persistence adapter. Until then, keep the simpler local module.

The accepted rationale is indexed in [ADRs](./adr/README.md); the current dependency
frontier and production qualification remain in [Delivery](./delivery.md).
