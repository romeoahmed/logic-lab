# Documentation Map

Logic Lab has a closed V1 design baseline. Read each document according to its authority and subject; use the [Implementation Plan](./implementation-plan.md#delivery-status) for completion status and the readiness table below for executable evidence instead of inferring delivery from a normative specification.

## Authority by subject

| Subject | Authority |
|---|---|
| system ownership, dependency direction, Module seams, deployment shape | [ARCHITECTURE.md](../ARCHITECTURE.md) |
| workbench layout, interaction, visual language, responsive and accessibility behavior | [WORKBENCH.md](../WORKBENCH.md) |
| domain words and context translations | [CONTEXT-MAP.md](../CONTEXT-MAP.md) and [bounded-context glossaries](./domain/) |
| observable circuit, component, analysis, symbol, package, and policy behavior | [specifications](./specs/) and [policies](./policies/catalog.md) |
| Application, browser, and HTTP seam values | [seam contracts](./contracts/README.md) |
| ASP.NET Core routes, render modes, culture, circuits, security, and host operations | [Web Host](./specs/web-host.md) |
| Canvas/waveform transforms, frames, input, accessibility, and browser resources | [Browser Runtime](./specs/browser-runtime.md) |
| .NET SDK, build, dependency, C#, async, DI, configuration, serialization, observability, and publication rules | [.NET Engineering Baseline](./specs/dotnet-engineering.md) |
| delivery order, completion status, and blocking frontier | [Implementation Plan](./implementation-plan.md) |
| hard-to-reverse decision rationale | [ADRs](./adr/README.md) |
| evidence, derivation, alternatives, and source qualification | [Research Index](./research/README.md) |

There is no generic “narrower file wins” rule. A specification defines current behavior, Architecture defines ownership and seams, Workbench defines experience, a contract defines values at one real seam, a glossary defines terminology, an ADR explains a choice, and research supplies evidence. A conflict is a documentation defect and must be repaired in the owning artifact.

## Live repository facts

Dynamic repository facts belong to executable configuration rather than prose:

| Fact | Source of truth |
|---|---|
| exact SDK and test runner | [`global.json`](../global.json) |
| exact direct package versions | [`Directory.Packages.props`](../Directory.Packages.props) |
| current executable project graph | [`logic-lab.slnx`](../logic-lab.slnx) |
| resolved dependency closures | application-root `packages.lock.json` files |

Normative documents define the rules governing those files. Dated research may preserve an observed version or graph as historical evidence, but maintained prose does not copy a live inventory.

## Behavior and policy

- [Circuit Authoring](./specs/circuit-authoring.md)
- [Component Contract Catalog V1](./specs/component-contract-catalog-v1.md)
- [Compiler](./specs/compiler.md)
- [Simulation Runtime](./specs/simulation-runtime.md)
- [Boolean Analysis](./specs/boolean-analysis.md)
- [`.logiclab` Project Package V1](./specs/project-package-v1.md)
- [Project Document JSON V1](./specs/project-document-json-v1.md)
- [Diagnostics V1](./specs/diagnostics-v1.md)
- [Diagram Presentation](./specs/diagram-presentation.md)
- [Web Host](./specs/web-host.md)
- [Browser Runtime](./specs/browser-runtime.md)
- [.NET Engineering Baseline](./specs/dotnet-engineering.md)
- [Policy Catalog](./policies/catalog.md)

## Seam contracts

- [Editor Workspace](./contracts/editor-workspace.md)
- [Durable Project Catalog](./contracts/durable-project-catalog.md)
- [Browser Adapters](./contracts/browser-adapters.md)
- [HTTP Transfer](./contracts/http-transfer.md)

## Development readiness

The [Implementation Plan](./implementation-plan.md#delivery-status) is the only completion ledger. This section summarizes executable evidence and open gaps without redefining slice completion. The application is neither V1-complete nor production-qualified.

| Area | Executable now | Not yet claimed |
|---|---|---|
| Circuit Authoring, Compiler, and Simulation Runtime | immutable lineage and the closed V1 Edit Intent/Component Contract catalogs; explicit topology and hierarchy; deterministic flat, hierarchical, cyclic, sequential, and memory execution; structured diagnostics, bounded failure, rollback, scalar-oracle, packed differential, and property evidence | Boolean Analysis, Project Format, TeachingMixed Geometry Plans, and the cross-module conformance manifest |
| Editor Workspace | Sandbox create/author/compile/session/stimulus/step/close workflow; attachment generation fencing, reattachment and expiry; bounded history with Undo/Redo and Redo truncation; Copy Workspace; stale preconditions and fail-closed Client Intent idempotency; bounded admission; accepted/running/published Compilation Generations with newest-wins coalescing; serialized Session work; Run Generation-fenced Run/Pause; compatible-state and Probe-aware Hot Swap; authenticated Claim and Save with explicit Durable conflict recovery; authorized Durable Project catalog and compiled `OpenDurable` publication | Analysis lane and package transfer |
| Presentation and Web | accessible explicit-topology projection; selected-definition and Hierarchy Path navigation; generated-Port widths and arithmetic facts; bounded steering/arithmetic galleries; per-page Interactive Server host and security-header integration; ASP.NET Core Identity account entry; authorized Static SSR `/projects`, protected keyset cursors, antiforgery-protected open, and authenticated editor reattachment | TeachingMixed geometry, Canvas/waveform adapters, complete browser workflows, localization/accessibility matrix, and load/security qualification |
| .NET engineering and performance | executable production, test, and BenchmarkDotNet projects; application-root locks; locked restore; analyzers-as-errors; format, build, and whole-solution test gates | later slice projects and locks, CI publication, architecture automation, frozen corpus, calibrated thresholds, and provider qualification |
| Production operations | development-host lifecycle and security-header evidence; SQLite Durable Project and Identity integration with reviewed migrations | concrete TLS/proxy, secrets, migration deployment, backup/restore, Data Protection key-store qualification, telemetry, alerting, and runbook evidence |

The repository gates in the [.NET Engineering Baseline](./specs/dotnet-engineering.md#9-verification-and-completion-gate) reproduce build and test evidence. Routine verification snapshots belong in the change or CI record, not this maintained map. Passing those gates does not qualify the application for release while any remaining-evidence row is open.

## Executable evidence

[Engine Performance Benchmarks](../benchmarks/LogicLab.Engine.Benchmarks/README.md) records production-shaped kernel, Compilation, and initial-settlement comparisons. It is directional performance evidence, not a release threshold.

## Research evidence

[Research](./research/README.md) preserves primary-source claims, mathematical reasoning, rejected alternatives, and qualification gaps. It is evidence, not a second implementation specification; project choices remain in the owning specification or ADR.

Research notes identify optional untracked standards copies by filename. When available, use `pdftotext -layout` for searchable prose and inspect original pages for figures and typography; do not treat those files as project assets.

## Maintenance

- Define a fact once and link to it elsewhere.
- Use exact ubiquitous language from the glossaries.
- Keep framework, EF, JSON, SVG, and Fluent types out of Domain language.
- Keep runtime ordinals, storage chunks, algorithm nodes, and renderer details out of browser contracts.
- Add an ADR only for a genuine hard-to-reverse trade-off.
- Classify every number as semantic, format, provisional policy, or measured threshold.
- Cite primary sources at the claim; qualify secondary tutorials and Wikipedia as navigation only.
- Keep delivery status only in the Implementation Plan, reproducible commands in the .NET Engineering Baseline, and routine verification snapshots in change or CI records.
- Read exact SDK/package versions and the current project graph from root configuration; repeat them only in an explicitly dated research checkpoint.
- Frame time-sensitive implementation observations in research as dated checkpoint evidence, never as maintained current state.
- Update links and supersession notes in the same change that moves authority.
