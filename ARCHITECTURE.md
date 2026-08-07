# Logic Lab Architecture

> Status: normative V1 architecture
> Target: .NET 10, C# 14, Blazor Web App

Architecture owns system shape, dependency direction, Module seams, fact ownership, and deployment shape. Specifications own exact interfaces and observable behavior; contracts own values exchanged at real seams; [Workbench](./WORKBENCH.md) owns product behavior; [Context Map](./CONTEXT-MAP.md) owns domain language. The [Implementation Plan](./docs/implementation-plan.md) owns delivery order and completion status.

## 1. Baseline and product scope

This section defines the V1 target. The [Implementation Plan](./docs/implementation-plan.md#delivery-status) tracks delivery, and [Development Readiness](./docs/README.md#development-readiness) summarizes executable evidence and gaps.

V1 supports:

- hierarchical Circuit Definitions with explicit Ports, Nets, Junctions, and Wire Geometry;
- deterministic four-state `0/1/X/Z`, zero-delay, discrete Logical Time;
- combinational and sequential Component Contracts, Clock Sources, registers, counters, ROM, and single-port RAM;
- Transaction History, Logic Analyzer Trace, Truth Table, Karnaugh Map, and proof-gated simplification;
- declarative IEEE 91A-based TeachingMixed symbols;
- strict `.logiclab` import/export, anonymous Sandbox Projects, and authenticated Durable Projects.

V1 excludes HDL compatibility, physical delay, analog or transistor simulation, metastability, timing closure, industrial synthesis quality, collaboration, offline editing, public sharing, arbitrary plug-ins, and user-supplied executable logic.

## 2. Architecture vocabulary and rules

A **module** hides behavior behind one **interface**. The interface includes invariants, ordering, outcomes, configuration, and performance obligations—not only methods. A **seam** is where behavior can vary without editing its caller; an **adapter** satisfies an interface at that seam. **Depth** creates leverage for callers and locality for maintainers.

1. **One owner per fact.** Authored topology, generated representation, runtime state, proof evidence, static geometry, browser state, and durable location never share one universal model.
2. **Deep modules at real seams.** Callers express intent; passes, schedulers, codecs, layout solvers, and caches remain implementation.
3. **One adapter is hypothetical; two adapters are real.** Side-effect ports preserve dependency direction. Internal strategy interfaces require actual variation or measured replacement pressure; mocks alone do not justify them.
4. **The interface is the test surface.** Production callers and tests cross the same seam; internal representations are not separate test contracts.
5. **Publish atomically and fail closed.** Invalid, stale, cancelled, exhausted, unauthorized, or unproved work publishes no partial artifact or replacement.
6. **Determinism is observable.** Stable order, explicit tie-breakers, immutable inputs, and complete provenance govern outcomes and evidence.
7. **Policy is not semantics.** Capacity and latency require corpus evidence; correctness, identity, authorization, ordering, and failure behavior do not vary by deployment.
8. **Optimize from an oracle.** Scalar and full-recomputation paths define correctness before packing, pooling, incrementality, parallelism, or browser caching.

The deletion test applies to every proposed module: removing a useful module redistributes its complexity across callers. Pass-through modules, contracts-only projects, generic message buses, service locators, repository-per-entity abstractions, and shared `Common` models fail this test.

## 3. Contexts and fact ownership

Bounded contexts own language and consistency inside one modular monolith; they do not imply processes or network seams. [CONTEXT-MAP.md](./CONTEXT-MAP.md) owns their translations.

| Fact | Owner | Derived consumers |
|---|---|---|
| Project Document, Project Revision, topology, authored presentation | Circuit Authoring | Compiler, Diagram Presentation, Project Format, repository |
| Compilation Artifact and Source Map | Compiler | Simulation Runtime, Boolean Analysis, Web |
| Logical Time, Driver/Net values, state, event calendar, Trace | Simulation Runtime | Editor Workspace, Web |
| Care Contract, Internal Candidate, proof evidence, Verified Replacement | Boolean Analysis | Editor Workspace proposal lifecycle |
| attachment, history cursor, save/Compilation/operation state, Simplification Proposal | Editor Workspace | Web |
| Geometry Plan, Schematic Projection, symbol conformance | Diagram Presentation | browser and export adapters |
| durable current-revision pointer and payload | repository adapter | Editor Workspace |
| selection, focus, viewport, Transient Preview, live overlays | Web/browser adapters | Workbench projection |

Compiler and Project Format are translation modules, not extra domain models. The Compiler translates a Project Revision into purpose-specific artifacts; Project Format translates an untrusted carrier into an Import Candidate. Neither changes Circuit Authoring facts.

## 4. System shape

The diagram below is the target V1 shape. Boolean Analysis, Project Format, Infrastructure, and their adapters enter only when an implementation slice first exercises them.

```text
Browser
  Razor/Fluent chrome <-> Scene and Waveform adapters
            | completed intents, snapshots, patches, Trace windows
            v
LogicLab.Web
  Static SSR site + per-page Interactive Server editor
  ├── Web Host
  ├── Browser Runtime adapters
  ├── Diagram Presentation
  └── direct typed calls
            v
Editor Workspace
  revisions/history/save/attachments/idempotency
  ├── Project Editor
  ├── Project Format
  ├── repository port -> EF Core adapter -> SQLite
  └── Work Coordinator
        ├── Compiler
        ├── Simulation Runtime
        └── Boolean Analysis
```

The initial deployment is one ASP.NET Core process and one SQLite database. Process placement is not a module seam. A worker process, custom SignalR Hub, alternate database, object store, browser Worker, or WebAssembly execution slice becomes an adapter only when measured evidence justifies it.

## 5. Solution seams

Project seams follow dependency or deployment seams, not every namespace.

| Project | Deep responsibility | Target direct dependencies |
|---|---|---|
| `LogicLab.Domain` | immutable authoring model, Project Editor, `logiclab.core` schema | BCL |
| `LogicLab.BooleanAnalysis` | Boolean Region contract, explanation, synthesis, mapping, proof | Domain, BCL |
| `LogicLab.Engine` | Compiler and Simulation Runtime as separate modules | Domain, BooleanAnalysis |
| `LogicLab.Presentation` | TeachingMixed definitions, Geometry Plans, Schematic Projection | Domain |
| `LogicLab.ProjectFormat` | strict `.logiclab` read/write, migration, digest, memory encoding | Domain, BCL compression/JSON |
| `LogicLab.Application` | Editor Workspace, Work Coordinator, authorization-aware use cases | Domain, Engine, BooleanAnalysis, ProjectFormat |
| `LogicLab.Infrastructure` | EF Core repository and persistence adapters | Application, Domain, EF Core |
| `LogicLab.Web` | Blazor routes, Fluent chrome, browser/HTTP adapters, composition root | Application, Presentation, Infrastructure |

Tests and benchmarks remain separate projects and do not create production seams.

Dependency rules:

- Domain references no EF, JSON, SVG, Web, Simulation, or analysis implementation type.
- Domain owns Component Contract Keys, Port/parameter/state schemas, Library Snapshot resolution, and semantic digests.
- BooleanAnalysis never references Engine or Application.
- Engine constructs Compiler-owned artifacts and the Analysis-owned Boolean Region; Analysis never inspects Simulation IR.
- Presentation emits geometry only; it owns neither selection nor live state.
- Application coordinates modules without reproducing their implementation phases.
- Infrastructure implements Application-owned persistence ports.
- Web is the only project that references Fluent UI or browser interop.

## 6. Module catalog

The linked owner defines the exact interface and closed outcomes. Architecture fixes only the seam and responsibility.

| Module | Caller intention | Hidden implementation | Interface owner |
|---|---|---|---|
| Project Editor | begin a Project; apply one Edit Intent | identity allocation, topology normalization, invariants, structural sharing | [Circuit Authoring](./docs/specs/circuit-authoring.md) |
| Compiler | compile; extract one Boolean Region | validation, hierarchy, SCCs, ordinals, IR, Source Map | [Compiler](./docs/specs/compiler.md) |
| Simulation Runtime | open, execute, and read one Simulation Session | propagation, batching, rollback, memory, Trace, Hot Swap | [Simulation Runtime](./docs/specs/simulation-runtime.md) |
| Boolean Analysis | explain; find one verified simplification | QMC/Petrick, AIG, mapping, ROBDD/exhaustive proof | [Boolean Analysis](./docs/specs/boolean-analysis.md) |
| Project Format | read or write one `.logiclab` carrier | bounded spool, ZIP, strict DTOs, migrations, digests | [Project Package V1](./docs/specs/project-package-v1.md) |
| Diagram Presentation | plan one symbol; project one Circuit Definition | constraints, text metrics, drawing operations, conformance | [Diagram Presentation](./docs/specs/diagram-presentation.md) |
| Editor Workspace | open, attach, dispatch, and read | fencing, idempotency, history, save, work coordination, proposals | [Editor Workspace Contract](./docs/contracts/editor-workspace.md) |
| Durable Project Catalog | list one authorized bounded page | authorization filtering, invariant order, keyset cursor, persistence projection | [Durable Project Catalog Contract](./docs/contracts/durable-project-catalog.md) |
| Scene/Waveform adapters | replace/apply state; return one completed intent | interop, transforms, frames, hit index, previews, caches, teardown | [Browser Runtime](./docs/specs/browser-runtime.md), [Browser Adapter Contract](./docs/contracts/browser-adapters.md) |
| Web Host | serve routes and manage host lifetimes | render modes, circuits, culture, middleware, health, shutdown | [Web Host](./docs/specs/web-host.md), [HTTP Transfer Contract](./docs/contracts/http-transfer.md) |

There is no `CircuitEngine` facade forwarding to Compiler and Simulation Runtime, no general codec port while `.logiclab` is the only native carrier, and no public pass, renderer, spatial-index, or algorithm-selection interface.

## 7. Application, persistence, and Web

One editor opening creates one Application-owned Editor Workspace with one controlling Workspace Attachment and zero or one Simulation Session.

```text
Opening -> Attached <-> Detached -> Expired
              |
              +-- Project Revision + Transaction History
              +-- save + Compilation state
              +-- zero-or-one Simulation Session
              +-- Analysis Operations + Simplification Proposals
```

Reattachment reauthorizes and fences the previous generation. A Detached Workspace accepts no new commands; an active Logical-time Advance commits or rolls back, then the Simulation Session pauses. Reattach never resumes Run automatically.

The Work Coordinator owns three typed CPU lanes:

| Lane | Lifecycle |
|---|---|
| Compilation | newest request wins per Workspace |
| Session | commands serialize per Simulation Session |
| Analysis | host-bounded and identity-fair; operations outlive observers |

CPU modules are synchronous. Async appears at I/O, cancellation, admission, and observation seams. Razor handlers neither call `Task.Run` nor create hidden queues; hosted work creates explicit dependency-injection scopes.

Durable storage keeps immutable Project Revision payloads and a current pointer under optimistic concurrency. It is not event sourcing and does not map gates/wires as one mutable EF graph. Infrastructure owns storage encoding; `.logiclab` remains the only native carrier. The SQLite adapter uses `IDbContextFactory<T>` and one short-lived context per operation.

Public/help/account/project pages use Static SSR; `/editor` uses per-page Interactive Server. Application-owned Workspaces can outlive circuits. Razor owns layout, forms, commands, navigation, dialogs, and semantic projections; browser adapters own frame-rate pixels and input inside their hosts. Pointer samples never cross the circuit. V1 has no `.Client` project, Interactive Auto, public REST interface, custom Hub, offline runtime, or browser execution of engine modules.

## 8. Cross-cutting engineering

### 8.1 Security

- Authenticate and authorize every resource action; IDs, routes, attachments, and tokens are locators, never authority.
- Treat browser records, coordinates, URLs, names, MIME, lengths, ZIP metadata, JSON, and memory bytes as untrusted.
- Retain antiforgery for cookie-authenticated mutation and bound ingress separately from semantic work.
- Encode output, enforce a tested Content Security Policy, and never render or execute uploaded markup or code.
- Log stable codes and low-cardinality operation facts—not project payloads, tokens, full messages, memory images, or Trace values.
- Compiler and Simulation Runtime never execute reflection-selected types, native plug-ins, scripts, external solvers, or user code.

[Web Host](./docs/specs/web-host.md) owns concrete HTTP and operational controls. [Project Package V1](./docs/specs/project-package-v1.md) owns untrusted-carrier validation.

### 8.2 .NET and dependencies

C# 14 is the sole production language. The [.NET Engineering Baseline](./docs/specs/dotnet-engineering.md) owns SDK, build, analyzer, dependency, C#, async, DI, configuration, serialization, observability, and publication rules. Module interfaces expose owned immutable values; storage layout and optimization mechanisms remain implementation.

| Need | Selection |
|---|---|
| Web chrome | centrally pinned Fluent UI Blazor package, Web only |
| persistence/auth | EF Core 10 SQLite and ASP.NET Core Identity |
| unit/integration | TUnit on Microsoft Testing Platform |
| properties | TUnit.FsCheck with FsCheck generation, shrinking, and replay |
| host/Razor/browser | TUnit.AspNetCore, bUnit, and TUnit.Playwright |
| comparative kernels | BenchmarkDotNet in a benchmark project |

Do not add mediation, mapping, generic Result, graph, binary-serialization, BDD/SAT, or alternative-DI packages without a measured missing capability.

### 8.3 Policy and observability

The [Policy Catalog](./docs/policies/catalog.md) separates semantic invariants, format limits, provisional deployment policy, and measured acceptance thresholds. No architecture claim invents entity, queue, timeout, memory, bitmap, density, frame, or latency limits.

Core modules return structured work evidence. Application emits activities, metrics, and structured logs at module calls, queue transitions, atomic publication, transfer phases, and failures. High-cardinality identities are not metric dimensions.

## 9. Verification ownership

Evidence follows fact ownership.

| Owner | Primary evidence |
|---|---|
| Circuit Authoring | model-based Edit Intent sequences, topology split/merge, Project Genesis, Transaction History |
| Compiler | deterministic artifacts/diagnostics, hierarchy/SCC/CSR properties, Source Map totality |
| Simulation Runtime | scalar oracle, packed differential properties, fixed-point/Trigger Batch/rollback/Trace cases |
| Boolean Analysis | brute-force oracles, mapping/proof invariants, verifier mutation tests |
| Project Format | golden/migration/adversarial packages, strict JSON, ZIP and memory properties |
| Diagram Presentation | rule/property tests, Geometry Plan/SVG goldens, accessibility projection |
| Editor Workspace/Infrastructure | attachment, idempotency, concurrency, work lanes, authorized catalog paging, repository integration |
| Web | host integration, bUnit projections, browser adapter contract tests, Playwright workflows |
| Performance | comparative BenchmarkDotNet, browser traces, and load tests on a versioned corpus |

A UI test cannot prove Simulation semantics; a screenshot cannot prove interaction or conformance; line coverage is supporting telemetry, not the release gate.

## 10. Constraints and evidence-triggered seams

The [Development Readiness](./docs/README.md#development-readiness) table owns current implementation and qualification gaps. Only measured evidence justifies revisiting these seams:

| Measured evidence | Revisit |
|---|---|
| network latency dominates after the local gesture loop | browser execution slice, not automatic Interactive Auto |
| hard per-operation memory termination is required | worker-process adapter using the same managed modules |
| durable multi-instance hosting is required | database/object-store adapters and distributed Workspace ownership |
| Trace live-follow exceeds circuit backpressure | dedicated authorized stream or custom Hub |
| scene work dominates the browser main thread | browser Worker behind the same Scene interface |

## 11. Primary references

- [ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/overview?view=aspnetcore-10.0)
- [C# language reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/)
- [Dependency injection](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/overview)
- [.NET observability](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel)
- [OWASP Cheat Sheet Series](https://cheatsheetseries.owasp.org/)

Focused research notes cite primary sources at individual claims.
