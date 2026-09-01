# Logic Lab V1 Implementation Plan

> **Status:** approved execution plan
>
> **Current frontier:** item `33`
>
> **Qualification gate:** item `43`
>
> **Deferred capability plan:** items `F01`–`F05`

This is the sole delivery ledger. Specifications and contracts define behavior;
this file records ordering and completion only. If the two disagree, repair this
plan rather than treating it as a second specification.

## Delivery status

Items `01`–`27` are complete. Item `33` closes V1 Component evidence.
Items `34`–`43` qualify one concrete production deployment. Boolean explanation
and proof-gated simplification are outside V1 and follow the separate future plan
at items `F01`–`F05`.

### Completed

|   ID | Delivered slice                                       |
| ---: | ----------------------------------------------------- |
| `01` | scalar four-state oracle                              |
| `02` | packed Logic Vector differential proof                |
| `03` | immutable Project lineage and first Edit Intents      |
| `04` | flat combinational Compilation                        |
| `05` | first observable Simulation Session                   |
| `06` | Sandbox Workbench tracer                              |
| `07` | explicit Net, Junction, and Wire Geometry editing     |
| `08` | hierarchical Circuit Definitions                      |
| `09` | topology and width-conversion contracts               |
| `10` | steering and multi-driver combinational contracts     |
| `11` | arithmetic and vector-decision contracts              |
| `12` | complete V1 Edit Intent catalog                       |
| `13` | cyclic combinational feedback settlement              |
| `14` | first clocked state circuit                           |
| `15` | remaining sequential contracts                        |
| `16` | ROM and single-port RAM                               |
| `17` | recoverable, idempotent Workspace control             |
| `18` | typed Compilation, Session, Run, and Hot Swap lanes   |
| `19` | Durable Project claim and save                        |
| `20` | authorized Durable Project catalog and reopen         |
| `21` | canonical `.logiclab` export                          |
| `22` | strict `.logiclab` import                             |
| `23` | basic TeachingMixed Geometry Plans                    |
| `24` | complex and hierarchical symbol projection            |
| `25` | sequential and memory symbols with conformance export |
| `26` | responsive Scene interaction and recovery             |
| `27` | complete Logic Analyzer                               |

Completed-item detail belongs to the owning specification, executable tests, and
Git history. This table intentionally keeps only the delivery record.

## Active implementation frontier

|   ID | Slice                          | Requires                         | Completion signal                                                                                                                           |
| ---: | ------------------------------ | -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| `33` | V1 Component evidence manifest | `15`, `16`, `18`, `22`, `25`, `27` | every `logiclab.core` Contract ID has the required schema, oracle, lowering, serialization, symbol, property, Hot Swap, and browser evidence |

V1 behavior is implementation-complete at item `27` and conformance-complete at
item `33`; neither statement makes a deployment production-qualified.

## Production qualification

|   ID | Slice                                                      | Requires                                 | Completion signal                                                                                                                    |
| ---: | ---------------------------------------------------------- | ---------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| `34` | freeze the representative corpus and observability catalog | `20`, `33`                               | versioned circuit, browser, and load corpora plus a stable redacted telemetry catalog                                                |
| `35` | calibrate core Module policies                             | `34`                                     | Package, Project Scale, Simulation, and Trace limits have repeatable corpus and environment evidence                                 |
| `36` | calibrate Scheduling and Workspace policies                | `20`, `34`                               | queue, fairness, retention, history, idempotency, catalog, and Hot Swap envelopes have load and storage evidence                     |
| `37` | qualify Workbench interaction and visual integrity         | `26`, `27`                               | primary authoring, inspection, simulation, recovery, responsive, zoom, and curated visual workflows pass                             |
| `38` | qualify localization and browser support                   | `26`, `27`                               | `en-US`/`zh-CN`, long-label, bidi, font, zoom, density, reconnect, and supported-device fixtures pass                                |
| `39` | calibrate Browser Policy and frame evidence                | `34`, `37`, `38`                         | intent, snapshot, bitmap, cache, frame, long-task, and idle limits are measured rather than predicted                                |
| `40` | qualify Web and transfer security                          | `20`, `22`, `26`, `27`                   | authentication, authorization concealment, antiforgery, CSP, transport limits, cookies, and redaction fail closed                    |
| `41` | qualify host lifecycle and operations                      | `19`, `20`, `40`                         | migration, readiness, shutdown, restart, auth expiry, and abandoned-lock recovery pass integration evidence                          |
| `42` | define one production deployment profile                   | `35`–`41`                                | origin, TLS/proxy trust, secrets, Data Protection, storage, runtime image, resources, telemetry, and ownership are fixed             |
| `43` | prove the production deployment profile                    | `33`, `42`                               | published artifacts pass migration, backup/restore, key continuity, upgrade, rollback, telemetry, load, security, and runbook drills |

Only item `43` authorizes describing the selected deployment as
production-qualified.

Executable templates and workflows do not change this ledger by themselves. The
[qualification record](./deployment/qualification.md) identifies the decision and
drill evidence that must close through items `34`–`43`.

## Future capability plan

These items preserve the prior Boolean Analysis design without making it V1
scope, a production-qualification dependency, or a delivery commitment.

|    ID | Slice                                                  | Requires                  | Reactivation signal                                                                                                                      |
| ----: | ------------------------------------------------------ | ------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| `F01` | Truth Table explanation                                | `08`, `10`, `11`, `17`    | V1 production qualification is complete and the future capability draft is accepted as a current specification                          |
| `F02` | Karnaugh Map explanation                               | `F01`                     | Gray-code axes, legal wrapping groups, per-output Care Domains, and unsupported-dimension behavior have approved product and test owners |
| `F03` | exact small simplification                             | `23`, `F01`               | bounded QMC/Petrick, independent verification, proposal freshness, recompilation, and atomic application are respecified                 |
| `F04` | deterministic AIG cleanup and teaching-library mapping | `25`, `F03`               | materialized verified improvements and mapping evidence have approved contracts and policy dimensions                                   |
| `F05` | ROBDD proof path                                       | `F03`                     | fixed-order ROBDD proof coverage, exhaustion, and counterexample evidence have approved limits                                           |

Reactivation updates Architecture, Context Map, Workbench, Compiler, Editor
Workspace, Diagnostics, Policy Catalog, executable project graph, and tests in
the same accepted scope change. Future items never block items `33`–`43`.

## Planning and completion rules

- One item is one independently demonstrable increment and leaves the solution green.
- A dependency names only behavior that must exist first; unrelated items may proceed in parallel.
- Diagnostics, cancellation, deterministic ordering, authorization, policy evidence,
  and atomic publication ship with the first behavior that needs them.
- Provisional policy values support development but are neither compatibility promises
  nor measured acceptance thresholds.
- Evidence belongs to the layer that owns the fact, including negative and
  atomic-failure paths.
- Rejection, cancellation, exhaustion, and defects publish no partial artifact,
  revision, Session state, Trace, Workspace, package, proposal, or durable pointer.
- A completed item passes the applicable restore, build, test, format, architecture,
  and whitespace gates.
- Deferred items never become dependencies of current V1 or production
  qualification without an explicit accepted scope change.

## Dependency frontier

Item `33` is next and closes current V1 Component evidence. Item `34` then starts
deployment qualification. Items `F01`–`F05` have no dependency edge into this
frontier.
