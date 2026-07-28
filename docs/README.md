# Documentation Map

Logic Lab is at the design baseline. Read each document according to its authority and subject.

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
| hard-to-reverse decision rationale | [ADRs](./adr/README.md) |
| evidence, derivation, alternatives, and source qualification | [research](./research/) |

There is no generic “narrower file wins” rule. A specification defines current behavior, Architecture defines ownership and seams, Workbench defines experience, a contract defines values at one real seam, a glossary defines terminology, an ADR explains a choice, and research supplies evidence. A conflict is a documentation defect and must be repaired in the owning artifact.

## Specifications

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

## Implementation plan

- [Logic Lab V1 Implementation Plan](./implementation-plan.md) translates the design baseline into context-sized delivery slices and blocking edges. It is a non-normative execution plan; the authorities above continue to own behavior and interfaces.

## Development readiness

The baseline is ready for implementation slices, not qualified for production release.

| Area | Specification state | Remaining evidence |
|---|---|---|
| authoring, Compilation, Simulation, analysis, format, diagnostics, symbols | closed V1 behavior and Module interfaces | executable projects and conformance suites |
| Workspace, Durable Project catalog, persistence, concurrency, browser messages | closed commands, keyset paging, queries, preconditions, outcomes, and transfer values | Application/Infrastructure implementations and integration tests |
| Blazor host and Canvas/waveform runtime | closed ownership, lifecycle, rendering, input, localization, security, and failure rules | browser matrix, corpus traces, load/security tests |
| .NET engineering and dependency supply chain | SDK feature band, central build/analyzer/package/audit policy, execution/DI/JSON/telemetry/publication rules | project files, application-root lock files, CI and architecture gates |
| policies and capacity | dimensions and failure semantics are closed | calibrated values tied to a versioned corpus and deployment profile |
| production operations | required readiness, shutdown, key, migration, proxy, and observability behavior is closed | provider-specific TLS, secrets, backup/restore, telemetry, alert, and runbook evidence |

Do not block the first vertical slice on policy calibration or provider selection. Do not describe the application as release-ready until those evidence rows are complete.

## Research evidence

- [Least-Fixed-Point Semantics](./research/least-fixed-point-semantics.md)
- [.NET Memory and Unsafe Code](./research/dotnet-memory-and-unsafe.md)
- [.NET 10 Engineering Baseline](./research/dotnet-10-engineering-baseline.md)
- [Compiler Representations](./research/compiler-representations.md)
- [Boolean Analysis](./research/boolean-analysis.md)
- [Blazor Web Platform](./research/blazor-web-platform.md)
- [Diagram Presentation](./research/diagram-presentation.md)

Research is not a second implementation specification. It preserves primary-source claims, mathematical reasoning, rejected alternatives, and qualification gaps. Project choices link to the owning specification or ADR.

The repository-root `00027895.pdf` and `1800-2023.pdf` are local standards references. Use `pdftotext -layout` for searchable prose and inspect original pages for figures and typography.

## Maintenance

- Define a fact once and link to it elsewhere.
- Use exact ubiquitous language from the glossaries.
- Keep framework, EF, JSON, SVG, and Fluent types out of Domain language.
- Keep runtime ordinals, storage chunks, algorithm nodes, and renderer details out of browser contracts.
- Add an ADR only for a genuine hard-to-reverse trade-off.
- Classify every number as semantic, format, provisional policy, or measured threshold.
- Cite primary sources at the claim; qualify secondary tutorials and Wikipedia as navigation only.
- Update links and supersession notes in the same change that moves authority.
