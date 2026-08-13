# Documentation Map

Logic Lab has a closed V1 design baseline. Read each document according to its authority and subject; use the [Implementation Plan](./implementation-plan.md#delivery-status) for completion status instead of inferring delivery from a normative specification.

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

## Delivery and verification

The [Implementation Plan](./implementation-plan.md#delivery-status) is the sole completion ledger and names the current [dependency frontier](./implementation-plan.md#dependency-frontier). Each owning specification or contract defines its required evidence; the [.NET Engineering Baseline](./specs/dotnet-engineering.md#9-verification-and-completion-gate) defines reproducible repository gates. Routine verification snapshots belong in a change or CI record, not in maintained guidance. Passing those gates does not make the application V1-complete or production-qualified; the plan's Phase F slices own that qualification.

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
- Keep delivery status only in the Implementation Plan, required evidence in the owning specification or contract, reproducible commands in the .NET Engineering Baseline, and routine verification snapshots in change or CI records.
- Read exact SDK/package versions and the current project graph from root configuration; repeat them only in an explicitly dated research checkpoint.
- Frame time-sensitive implementation observations in research as dated checkpoint evidence, never as maintained current state.
- Update links and supersession notes in the same change that moves authority.
