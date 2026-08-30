# Documentation Map

Logic Lab separates target behavior, delivery status, decisions, and supporting evidence. Start with the owning document for the fact you need; do not infer implementation status from a normative target.

## Reading path

1. Read [Architecture](../ARCHITECTURE.md), [Workbench](../WORKBENCH.md), or the [Context Map](../CONTEXT-MAP.md) for the relevant ownership, experience, or language.
2. Load one owning specification, contract, or policy—not the entire documentation tree.
3. Use the [Implementation Plan](./implementation-plan.md#delivery-status) only for delivery status and the current frontier.
4. Consult an ADR for rationale or a research note for source evidence after the governing rule is clear.

## Authority by subject

| Subject | Authority |
|---|---|
| system ownership, dependency direction, Module seams, deployment shape | [Architecture](../ARCHITECTURE.md) |
| workbench layout, interaction, visual language, responsive behavior, and product priorities | [Workbench](../WORKBENCH.md) |
| domain words and context translations | [CONTEXT-MAP.md](../CONTEXT-MAP.md) and [bounded-context glossaries](./domain/) |
| observable circuit, component, analysis, symbol, package, and policy behavior | [specifications](./specs/) and [policies](./policies/catalog.md) |
| Application, browser, and HTTP seam values | [seam contracts](./contracts/README.md) |
| ASP.NET Core routes, render modes, culture, circuits, security, and host operations | [Web Host](./specs/web-host.md) |
| Canvas/waveform transforms, frames, input, focus, and browser resources | [Browser Runtime](./specs/browser-runtime.md) |
| .NET SDK, build, dependency, C#, async, DI, configuration, serialization, observability, and publication rules | [.NET Engineering Baseline](./specs/dotnet-engineering.md) |
| delivery order, completion status, and blocking frontier | [Implementation Plan](./implementation-plan.md#delivery-status) |
| hard-to-reverse decision rationale | [ADRs](./adr/README.md) |
| evidence, derivation, alternatives, and source qualification | [Research Index](./research/README.md) |

There is no generic “narrower file wins” rule. A conflict between owners is a documentation defect; repair it where the fact belongs.

## Live repository facts

Dynamic repository facts belong to executable configuration rather than prose:

| Fact | Source of truth |
|---|---|
| exact SDK and test runner | [`global.json`](../global.json) |
| exact direct package versions | [`Directory.Packages.props`](../Directory.Packages.props) |
| current executable project graph | [`logic-lab.slnx`](../logic-lab.slnx) |
| resolved dependency closures | application-root `packages.lock.json` files |

Normative documents govern these files; maintained prose does not copy their live inventory.

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
- [HTTP Boundary](./contracts/http-boundary.md)

## Delivery and evidence

- [Implementation Plan](./implementation-plan.md#delivery-status) is the only completion ledger.
- [.NET Engineering Baseline](./specs/dotnet-engineering.md#9-verification-and-completion-gate) owns reproducible repository gates.
- [Research](./research/README.md) preserves primary-source evidence and qualification boundaries.
- [Engine Performance Benchmarks](../benchmarks/LogicLab.Engine.Benchmarks/README.md) documents comparative measurements, not release thresholds.

## Maintenance

- Define a fact once and link to it elsewhere.
- Use exact ubiquitous language from the glossaries.
- Keep framework, EF, JSON, SVG, and Fluent types out of Domain language.
- Keep runtime ordinals, storage chunks, algorithm nodes, and renderer details out of browser contracts.
- Add an ADR only for a genuine hard-to-reverse trade-off.
- Classify every number as semantic, format, provisional policy, or measured threshold.
- Cite primary sources at the claim; qualify secondary tutorials and Wikipedia as navigation only.
- Keep delivery status in the Implementation Plan, required evidence in the owner, reproducible commands in the .NET Engineering Baseline, and routine results in change or CI records.
- Read exact versions and the project graph from executable configuration; repeat them only in a dated measurement record.
- Remove superseded implementation snapshots instead of surrounding them with ever longer disclaimers.
- Update links and supersession notes in the same change that moves authority.
