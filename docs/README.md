# Documentation Map

Use the shallowest document that answers the question. Target behavior, current
delivery, rationale, and evidence are different facts and have different owners.

## Fast paths

| Task                                    | Load first                                                      | Then load only                                            |
| --------------------------------------- | --------------------------------------------------------------- | --------------------------------------------------------- |
| understand the product                  | [Workbench](../WORKBENCH.md)                                    | the relevant browser, host, or presentation specification |
| change a project or circuit rule        | [Context Map](../CONTEXT-MAP.md)                                | one bounded-context glossary and its owning specification |
| change a module boundary                | [Architecture](../ARCHITECTURE.md)                              | the interface specification or seam contract              |
| change Web/Application/browser exchange | [Contracts](./contracts/README.md)                              | the one contract for that seam                            |
| change build, dependencies, or tests    | [.NET Engineering](./specs/dotnet-engineering.md)               | one focused research note when rationale matters          |
| change production deployment            | [Production Profile](./deployment/production-profile.md)        | Architecture, Web Host, then executable Bicep              |
| release, rollback, or recover production | [Production Runbook](./deployment/runbook.md)                    | [Qualification Record](./deployment/qualification.md)      |
| find what is implemented next           | [Implementation Plan](./implementation-plan.md#delivery-status) | the owner of the active slice                             |
| understand why a durable choice exists  | [ADRs](./adr/README.md)                                         | the current owner, which remains normative                |

Do not preload the whole tree. Before editing, read the target, its tests, one
analogous implementation, and the directly governing interface or rule.

## Authority

| Fact                                                                                   | Owner                                                                        |
| -------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| system shape, dependency direction, and module ownership                               | [Architecture](../ARCHITECTURE.md)                                           |
| visible product behavior, layout, and interaction priorities                           | [Workbench](../WORKBENCH.md)                                                 |
| domain language and translations                                                       | [Context Map](../CONTEXT-MAP.md) and [bounded-context glossaries](./domain/) |
| observable domain, compiler, runtime, format, presentation, host, and browser behavior | [Specifications](./specs/)                                                   |
| values exchanged across Application, browser, and HTTP seams                           | [Contracts](./contracts/README.md)                                           |
| limits and their semantic/format/provisional/measured classification                   | [Policy Catalog](./policies/catalog.md)                                      |
| selected production provider shape, operations, and qualification boundary             | [Production Profile](./deployment/production-profile.md)                     |
| production release, rollback, recovery, and drill procedure                             | [Production Runbook](./deployment/runbook.md)                                |
| unresolved production decisions and required drill evidence                             | [Qualification Record](./deployment/qualification.md)                        |
| completion, dependency frontier, and deferred capability order                         | [Implementation Plan](./implementation-plan.md)                              |
| hard-to-reverse decision rationale                                                     | [ADRs](./adr/README.md)                                                      |
| derivations, external sources, and dated measurements                                  | [Research](./research/README.md)                                             |
| license grant and redistribution terms                                                 | [LICENSE](../LICENSE) and [third-party notices](../THIRD-PARTY-NOTICES.md)   |

There is no generic “narrower file wins” rule. A conflict between owners is a
documentation defect; fix the fact where it belongs and replace copies with links.

## Specifications

- [Circuit Authoring](./specs/circuit-authoring.md)
- [Component Contract Catalog V1](./specs/component-contract-catalog-v1.md)
- [Compiler](./specs/compiler.md)
- [Simulation Runtime](./specs/simulation-runtime.md)
- [`.logiclab` Project Package V1](./specs/project-package-v1.md)
- [Project Document JSON V1](./specs/project-document-json-v1.md)
- [Diagnostics V1](./specs/diagnostics-v1.md)
- [Diagram Presentation](./specs/diagram-presentation.md)
- [Web Host](./specs/web-host.md)
- [Browser Runtime](./specs/browser-runtime.md)
- [.NET Engineering](./specs/dotnet-engineering.md)

## Future capability drafts

- [Boolean Analysis](./specs/boolean-analysis.md) is non-normative for V1 and is
  ordered only by the [future capability plan](./implementation-plan.md#future-capability-plan).

## Executable facts

| Fact                                  | Source of truth                                           |
| ------------------------------------- | --------------------------------------------------------- |
| SDK and test runner                   | [`global.json`](../global.json)                           |
| direct package versions               | [`Directory.Packages.props`](../Directory.Packages.props) |
| shared build policy                   | [`Directory.Build.props`](../Directory.Build.props)       |
| project graph                         | [`logic-lab.slnx`](../logic-lab.slnx)                     |
| resolved executable dependency graphs | application-root `packages.lock.json` files               |

Prose may explain these files but must not copy their live inventories.

## Maintenance rules

- Define each fact once and link to it elsewhere.
- Keep current delivery out of normative target documents.
- Keep framework, persistence, transport, rendering, and algorithm details out of
  Domain language unless they are the domain fact.
- Classify each number as semantic, format, provisional policy, or measured threshold.
- Cite primary sources at the supported claim; treat tutorials as navigation only.
- Remove superseded snapshots and setup inventories instead of adding disclaimers.
- Update links, indexes, and explicit supersession metadata in the same change that
  moves authority.
