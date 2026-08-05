# Logic Lab V1 Implementation Plan

> Status: approved non-normative execution plan
> Delivery status reviewed: 2026-08-05
> Scope: documentation baseline to V1 implementation, conformance, and one qualified production deployment profile

This plan translates the repository's closed V1 design into context-sized implementation slices. It does not own product behavior, Module interfaces, policy semantics, or deployment requirements. [Architecture](../ARCHITECTURE.md), [Workbench](../WORKBENCH.md), the [specifications](./specs/), [seam contracts](./contracts/README.md), [Policy Catalog](./policies/catalog.md), and [ADRs](./adr/README.md) remain authoritative. If a plan item conflicts with an owning document, repair this plan rather than changing behavior to match it.

## Planning rules

- Each numbered item is one narrow, independently demonstrable or executable increment sized for one fresh implementation context.
- Items `01` through `05` are the minimum executable prefactors required by the empty solution. Item `06` is the first complete user tracer through authoring, Compilation, Simulation, Application, Presentation, and Web.
- Every item keeps the solution green. A production or test project is added only when that item gives it executable behavior or evidence; empty placeholder projects are forbidden.
- Blocking edges name only work that must exist before the blocked behavior can be implemented honestly. Items without an edge between them may proceed in parallel even when they touch adjacent Modules.
- Diagnostics, closed outcomes, cancellation, policy evidence, deterministic ordering, authorization, and atomic publication are implemented with the behavior that first needs them, not deferred to a cross-cutting cleanup ticket.
- Provisional policy values may support development and tests, but they are not compatibility promises or measured acceptance thresholds. Calibration remains in the qualification phase.
- Items `01` through `33` complete V1 implementation and conformance. Items `34` through `43` supply the additional evidence required to describe one deployment as production-qualified.

## Completion discipline

Every completed item must:

1. expose the promised behavior through the owning Module or seam rather than an implementation-only shortcut;
2. include evidence at the layer that owns the fact, including negative and atomic-failure cases;
3. publish no partial artifact, Project Revision, Session state, Trace, Workspace, package, proposal, or durable pointer on rejection, cancellation, exhaustion, or defect;
4. preserve exact domain terminology and dependency direction;
5. pass the applicable restore, build, test, format, architecture, and whitespace gates; and
6. update an authoritative document only when implementation uncovers a real specification defect.

## Phase A — Executable semantics and first tracer

| ID | Slice | Blocked by | Independently delivers |
|---:|---|---|---|
| 01 | Start with the scalar four-state oracle | None | The first nonempty production and test projects enter the solution; repository gates run, and `0/1/X/Z`, Conservative Merge, Net resolution, and the minimum `logiclab.core` schema are executable evidence. |
| 02 | Differentially prove packed Logic Vectors | 01 | Packed operations over arbitrary positive widths match the scalar oracle at word tails, slices, `X/Z`, and multiple-Driver boundaries. |
| 03 | Create the first immutable Project lineage | 01 | Project Genesis and a narrow Edit Intent set create, place, connect, and move an input–NOT–output circuit as atomic Project Revisions with stable source identities. |
| 04 | Compile one flat combinational circuit | 03 | A valid Project Revision produces one sealed Compilation Artifact, Simulation IR, and total Source Map; invalid input produces deterministic source-bound Diagnostics and no artifact. |
| 05 | Advance and observe the first Simulation Session | 02, 04 | A Session opens at time zero, accepts a future Stimulus Batch, advances atomically to the next Quiescent Boundary, and exposes the result through a Probe and Trace. |
| 06 | Deliver the first Sandbox Workbench tracer | 05 | `/editor` lets a user create a Sandbox Project, author the narrow circuit, Compile, create a Session, Step, and observe it through Interactive Server, an accessible Scene adapter, and the intended Application seams. |

The first required product tracer is complete at item `06`. Infrastructure breadth or a polished empty shell does not substitute for it.

## Delivery status

Phase A items `01` through `06` and Phase B items `07` through `16` are complete at the Domain, Compiler, Runtime, and applicable Application seams promised below. The [Development Readiness](./README.md#development-readiness) table owns the current Web surface, executable evidence, verification snapshot, and remaining qualification gaps.

## Phase B — Authoring, Component Contracts, and Runtime breadth

| ID | Slice | Blocked by | Independently delivers |
|---:|---|---|---|
| 07 | Edit explicit Net, Junction, and Wire Geometry topology | 06 | Users can connect, merge, split, route, unroute, and edit Junction-backed topology without inferring electrical membership from pixels or crossings; cancelled gestures commit nothing. |
| 08 | Author and observe hierarchical Circuit Definitions | 07 | Users can create Circuit Definitions, instantiate them, select an entry definition, navigate Hierarchy Paths, and compile deterministic recursion or source-provenance evidence. |
| 09 | Complete topology and width-conversion contracts | 07 | Split, concatenate, zero/sign extend, input/constant sources, and the output sink can be authored, compiled, executed, projected, and verified; Clock Source execution remains with `14`. |
| 10 | Complete steering and multi-driver combinational contracts | 06 | Gate families, tri-state, MUX/DEMUX, decoder, and priority encoder have exact parameters, generated Ports, four-state behavior, Diagnostics, and editor paths. |
| 11 | Complete arithmetic and vector-decision contracts | 06 | Unsigned compare, adder, subtractor, and logical shift work through every seam, including unknown inputs, checked widths, and policy exhaustion. |
| 12 | Close the Project Editor V1 Edit Intent catalog | 08, 09, 10, 11 | Rename, public-Port migration, parameter/contract change, deletion, Memory Image, Symbol Profile/Variant, Annotation, and remaining presentation intentions commit atomically or leave no revision. |
| 13 | Settle cyclic combinational feedback | 07, 10 | A Combinational Feedback Region computes its Least Information Fixed Point from all `X`, with fair-worklist differential evidence and correct Indeterminate Feedback, contention, defect, and exhaustion behavior. |
| 14 | Run the first clocked state circuit | 13 | Clock Source, D latch, DFF/register, event calendar, Definite Edge, and causal Trigger Batch behavior can be stepped with atomic rollback. |
| 15 | Complete the remaining sequential contract family | 12, 14 | SR latch, JK/T flip-flops, shift register, counter, unknown controls, derived clocks, and exact-state Zero-time Oscillation are authorable and execute through Compiler, Runtime, and Application evidence. |
| 16 | Run ROM and single-port RAM end to end | 12, 14 | Explicit Memory Images, asynchronous reads, conservative unknown addressing, Trigger Batch writes, and rollback are authorable and bounded through Domain, Compiler, Runtime, and Application evidence. |

Items `15` and `16` do not claim sequential or memory rendering and browser workflows. Their TeachingMixed projection remains item `25`, and complete Scene/browser interaction remains item `26`.

## Phase C — Workspace continuity, persistence, and transfer

| ID | Slice | Blocked by | Independently delivers |
|---:|---|---|---|
| 17 | Make Editor Workspace control recoverable and idempotent | 06 | Attachment fencing, detach/reattach, expiry, Undo/Redo, Redo truncation, Copy Workspace, stale preconditions, and Client Intent idempotency follow the closed Workspace outcomes. |
| 18 | Run, pause, coalesce Compilation, and Hot Swap through typed lanes | 08, 15, 16, 17 | Compilation is newest-wins, Session work is single-consumer, Run Generation fences delayed Pause, and Hot Swap migrates only compatible state and Probe bindings. |
| 19 | Claim and save a Durable Project | 12, 17 | An authenticated Sandbox becomes a Durable Project; immutable revisions and the current pointer persist through SQLite with application-managed Durable Version and explicit save-conflict recovery. |
| 20 | List and reopen Durable Projects | 19 | `/projects` provides authorized projection-only keyset paging in invariant order, protected cursors, and reauthorization through `OpenDurable`. |
| 21 | Export a canonical `.logiclab` package | 12, 17 | A user can prepare and download a bounded package with canonical Project Document JSON, canonical Memory Image bytes, part digests, package digest, and an unpublished staging write. |
| 22 | Strictly import an untrusted `.logiclab` package | 15, 16, 21 | Bounded spool, ZIP, strict JSON, memory, integrity, migration, and Domain validation produce one new Workspace only after Project Genesis and Compilation succeed; the originating Workspace remains unchanged on every failure. |

## Phase D — TeachingMixed presentation and browser instruments

| ID | Slice | Blocked by | Independently delivers |
|---:|---|---|---|
| 23 | Generate basic TeachingMixed Geometry Plans | 10, 12 | Familiar basic gates are generated declaratively with shared renderer-neutral operations, Port anchors, hit regions, accessibility trees, metric fingerprints, and per-symbol IEEE 91A evidence. |
| 24 | Project complex combinational and hierarchical symbols | 08, 09, 11, 23 | Steering, arithmetic, topology, and user Circuit Definition symbols receive parameterized rectangular Geometry Plans and complete Schematic Projection behavior. |
| 25 | Project sequential and memory symbols with conformance exports | 15, 16, 24 | Sequential and memory qualifiers, dependencies, array information, Teaching Extensions, strict fallback behavior, and TeachingMixed export manifests are complete. |
| 26 | Complete responsive Scene interaction and recovery | 07, 17, 25 | Atomic snapshots/patches, Canvas sizing and density, transforms, culling, hit priority, pointer capture, keyboard actions, semantic fallback, focus recovery, local-only disconnect, teardown, and renderer failures satisfy the Scene contract. |
| 27 | Deliver the complete Logic Analyzer | 18, 26 | Probe Spine identity, waveform rows, radix/order, cursors, live follow, transition and summary windows, Trace Gaps, Reveal Net, retention, and Hot Swap recovery work without exposing Runtime chunks. |

## Phase E — Boolean explanation and proof-gated simplification

| ID | Slice | Blocked by | Independently delivers |
|---:|---|---|---|
| 28 | Explain an eligible Boolean Region with a Truth Table | 08, 10, 11, 17 | Compiler extraction and the identity-fair Analysis lane produce a complete Truth Table or an exact Not Applicable/Inconclusive result tied to the Care Contract. |
| 29 | Explain an eligible Boolean Region with a Karnaugh Map | 28 | Gray-code axes, legal wrapping groups, per-output Care Domains, shared implicant markers, and unsupported-dimension behavior are reviewable in the Instrument Bay. |
| 30 | Produce and accept an exact small simplification | 23, 28 | Bounded multi-output QMC/Petrick, teaching-gate materialization, independent exhaustive verification, proposal freshness, recompilation, and one Undoable replacement Edit Transaction form a complete small-region path. |
| 31 | Add deterministic AIG cleanup and teaching-library mapping | 25, 30 | AIG candidate families, feasible cuts, legal cell matching, materialized sharing, and Cost Profile recomputation can produce a strict mapped improvement without exposing internal algorithm identities. |
| 32 | Add the ROBDD proof path | 30 | Fixed-order ROBDD verification, bounded caches/depth, stable counterexamples, scalar replay, and overlap tests extend proof coverage while exhaustion remains Inconclusive. |
| 33 | Enforce the V1 Component evidence manifest | 15, 16, 18, 22, 25, 27, 29, 31, 32 | Every `logiclab.core` Contract ID has schema, oracle, lowering, serialization, symbol, property, Hot Swap, and browser evidence; missing, duplicate, or unknown evidence fails the conformance gate. |

V1 is implementation- and conformance-complete at item `33`, but it is not yet production-qualified.

## Phase F — Measurement and production qualification

| ID | Slice | Blocked by | Independently delivers |
|---:|---|---|---|
| 34 | Freeze the representative corpus and observability catalog | 20, 33 | A versioned circuit/browser/load corpus and a stable, low-cardinality, redacted Activity, metric, log, and benchmark catalog make later measurements reproducible. |
| 35 | Calibrate core Module policies | 34 | Package, Project Scale, Simulation, Trace, and Analysis limits receive corpus-, environment-, and method-linked evidence without changing successful semantics. |
| 36 | Calibrate Scheduling and Workspace policies | 20, 34 | Queue, fairness, worker, retention, history, idempotency, Workspace, catalog, and Hot Swap envelopes are tied to repeatable load and storage evidence. |
| 37 | Qualify Workbench accessibility | 26, 27, 29, 31, 32 | WCAG 2.2 AA automation plus keyboard, screen-reader, forced-colors, reduced-motion, focus recovery, and 200% text-zoom task scripts pass. |
| 38 | Qualify localization and the supported browser matrix | 26, 27, 29, 31, 32 | `en-US`/`zh-CN`, resource parity, long labels, bidi isolation, font fingerprints, zoom, density, reconnect, and supported browser/device scenarios pass. |
| 39 | Calibrate Browser Policy and frame thresholds | 34, 37, 38 | Intent and snapshot sizes, bitmap and cache allocations, semantic-tree paging, frame/long-task distributions, and idle behavior establish versioned browser limits and observation thresholds. |
| 40 | Qualify Web and transfer security | 20, 22, 26, 27, 29, 31, 32 | Authentication, authorization concealment, antiforgery, CSP, Problem Details, upload/download limits, build mismatch, transport bounds, cookies, and redaction fail closed. |
| 41 | Qualify host lifecycle and operational behavior | 19, 20, 40 | Readiness/liveness, migration-before-readiness, short-lived contexts, graceful shutdown, attachment loss, process restart, auth expiry, and abandoned migration-lock recovery pass integration evidence. |
| 42 | Define one concrete production deployment profile | 35, 36, 37, 38, 39, 40, 41 | One named environment fixes public origin, TLS/proxy trust, secret provider, Data Protection store, database volume and policy, runtime image, resource limits, telemetry backend, and operational ownership. |
| 43 | Prove the production deployment profile | 33, 42 | Published artifacts pass migration, backup/restore, key continuity, patch upgrade, shutdown, rollback, telemetry/alert, load, security, and runbook drills in the declared environment. |

Only item `43` authorizes describing the selected deployment profile as production-qualified.

## Dependency frontier

The initial frontier contained only item `01`; implementation has now completed through item `16`. The current frontier begins with `17` and `23`, opening these primary streams as their named prerequisites land:

- Workspace continuity (`17`);
- TeachingMixed generation once the relevant contracts exist (`23`–`25`); and
- Boolean explanation once hierarchy, combinational contracts, and Workspace operations exist (`28`–`32`).

Persistence and export do not wait for Runtime breadth that they do not consume. Strict import waits for the complete V1 catalog it must validate. Qualification work begins only after its required behavior and evidence exist; policy calibration and provider selection never block the first product tracer.

This plan is intentionally not published as tracker issues. If issue publication is requested later, create one issue per numbered item in dependency order and preserve the blocking edges above.
