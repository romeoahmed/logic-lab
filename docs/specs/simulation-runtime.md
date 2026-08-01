# Simulation Runtime

> Status: normative V1 contract

This specification defines Logic Lab's observable digital semantics. IEEE 1800-2023 informs selected four-state tables, but Logic Lab is not a SystemVerilog simulator.

## Module interface

Simulation Runtime owns opaque mutable Session storage behind this synchronous CPU-bound interface:

```text
Open(OpenSimulationRequest, CancellationToken)
  -> SimulationOpened | InitialProbeBindingsInvalid | SimulationOpenRejected

Execute(SimulationSessionHandle, SimulationCommand, CancellationToken)
  -> SimulationCommandOutcome

Read(SimulationSessionHandle, SimulationQuery, CancellationToken)
  -> SimulationReadOutcome

Close(SimulationSessionHandle)
  -> SessionClosed | SessionAlreadyClosed

OpenSimulationRequest
  sealed CompilationArtifact
  SimulationSessionConfiguration
  resolved SimulationPolicy
  resolved TracePolicy

SimulationCommand =
  ScheduleStimulusBatch(validated future StimulusBatch)
  | AdvanceToNextQuiescentBoundary
  | ReplaceProbeBindings(ordered RetainProbe | CreateProbe bindings)
  | HotSwapTo(sealed CompilationArtifact)

SimulationQuery =
  ReadSessionSnapshot
  | ReadTraceWindow(SimulationTraceWindowRequest)

SimulationTraceWindowRequest
  nonempty unique ordered ProbeIds[]
  nonempty half-open Logical-time range
  representation: Transitions | VisualSummary(maxPoints, "logic-envelope-v1")
  afterSequence?
```

The outcome families are closed:

```text
SimulationOpenOutcome =
    SimulationOpened { ... }
  | InitialProbeBindingsInvalid {
      rule: unresolvedSource | duplicateResolvedNet,
      bindingIndex,
      conflictingBindingIndex?, Diagnostics[], SimulationWorkEvidence
    }
  | SimulationOpenRejected {
      reason, Diagnostics[], SimulationWorkEvidence
    }

SimulationCommandOutcome =
    StimulusBatchScheduled {
      SessionVersion, scheduledLogicalTime, stableSequence
    }
  | StimulusBatchInvalid {
      unchanged SessionVersion, unchanged LogicalTime,
      rule: atOrBeforeCommittedTime | conflictingDriverAssignment
    }
  | AdvanceCommitted {
      SessionVersion, LogicalTime, observed Probe patch,
      Diagnostics[], Trace cursor
    }
  | NoScheduledStimulus { SessionVersion, LogicalTime }
  | AdvanceFailed {
      unchanged SessionVersion, unchanged LogicalTime,
      reason, Diagnostics[], policyEvidence?
    }
  | ProbeBindingsReplaced {
      SessionVersion, ordered ProbeIds[], Trace cursor
    }
  | ProbeBindingsInvalid {
      unchanged SessionVersion,
      rule: duplicateBinding | unresolvedSource | artifactMismatch,
      canonical safe source locations[]
    }
  | HotSwapCommitted {
      SessionVersion, CompilationArtifactKey, migrationEvidence,
      ordered ProbeIds[], Diagnostics[], Trace cursor
    }
  | HotSwapIncompatible {
      unchanged SessionVersion, unchanged CompilationArtifactKey,
      incompatible state source locations[], unresolved ProbeIds[]
    }
  | SimulationCommandFailed {
      unchanged SessionVersion, unchanged LogicalTime,
      reason, Diagnostics[], policyEvidence?
    }

SimulationReadOutcome =
    SessionSnapshotRead {
      SessionId, SessionVersion, CompilationArtifactKey, LogicalTime,
      ordered Probe bindings[], Trace cursor, Diagnostics[]
    }
  | TraceTransitionsAvailable { transitions[], coveredRange, earliestAvailable, latestSequence }
  | TraceSummaryAvailable { buckets[], aggregation, coveredRange, earliestAvailable, latestSequence }
  | TraceRangeUnavailable { Evicted | ArtifactChanged, earliestAvailable, latestSequence }
  | SimulationReadFailed { reason, Diagnostics[] }
```

`SimulationOpened` contains the new opaque handle, Session ID/version, Compilation Artifact Key, Logical Time, ordered allocated Probe IDs, Trace cursor, ordered Diagnostics, and `SimulationWorkEvidence`. `InitialProbeBindingsInvalid` reports a caller precondition failure without a handle: `bindingIndex` identifies the unresolved or duplicate binding, and `conflictingBindingIndex` identifies the earlier binding only for `duplicateResolvedNet`. `SimulationOpenRejected` is reserved for one exact Runtime failure reason and also contains no handle. Both failure outcomes contain ordered Diagnostics and safe `SimulationWorkEvidence`. Evidence has this exact shape:

```text
SimulationWorkEvidence
  CompilationArtifactKey
  simulationPolicy: { policyId, policyRevision }
  tracePolicy: { policyId, policyRevision }
  observedDimensions: SimulationWorkObservationV1[]
  policyLimitBreach: SimulationWorkObservationV1 | null

SimulationWorkObservationV1
  policy: Simulation | Trace
  dimension: StableToken
  observed: UnsignedDecimal
```

Observations contain at most one row per `(policy, dimension)`, record the maximum reached before termination, and order all Simulation rows by their policy dimension order before all Trace rows by their policy dimension order. `AdvanceFailed.reason` and `SimulationCommandFailed.reason` are exactly `zero_time_oscillation | simulation_resource_limit | simulation_cancelled | simulation_infrastructure_failure | simulation_internal_defect`; only operations that can settle a candidate can return `zero_time_oscillation`. Their `policyEvidence` is present only for `simulation_resource_limit` and contains the matching policy ID/revision, dimension, and observed work. `SimulationWorkEvidence.policyLimitBreach` follows the same rule for an open rejection and is otherwise null. `SimulationReadFailed.reason` is exactly `simulation_cancelled | simulation_infrastructure_failure | simulation_internal_defect`. Open rejection uses those failure reasons when initial settlement encounters them. Every array is present and non-null, even when empty.

Only the outcome variants valid for the supplied command or query can return. `AdvanceToNextQuiescentBoundary` reports its failures only through `AdvanceFailed`; the other commands report infrastructure/cancellation/defect failure only through `SimulationCommandFailed`. Application maps `InitialProbeBindingsInvalid`, `StimulusBatchInvalid`, and `ProbeBindingsInvalid` to `session_precondition_failed`, `HotSwapIncompatible` to `hot_swap_incompatible`, and the remaining reasons one-to-one through the [Diagnostics V1 reason registry](./diagnostics-v1.md#11-outcome-reason-registry). A family never changes shape according to nullable success data. Application validates and translates the Workspace-owned `SessionConfigurationV1` and `TraceWindowRequest` into the Engine-owned request types above, and translates the result records back to the [Editor Workspace Contract](../contracts/editor-workspace.md#7-trace-transfer); Engine never references Application contract types. Read returns one immutable Session snapshot or normalized Runtime Trace outcome, never Runtime storage. Close is idempotent and releases all handle-owned storage.

A handle is an in-process Runtime value, not a serialized ID, authorization capability, or public seam. Application owns it and must serialize `Open`/`Execute`/`Read`/`Close` per handle through the Session lane; concurrent use of one handle or use after Close violates the interface. Calls on different handles may run concurrently. Runtime creates no tasks, timers, background work queues, or dependency-injection scopes and knows no Run Generation or wall-clock pacing. Application implements Run by scheduling repeated `AdvanceToNextQuiescentBoundary` calls and implements Pause between atomic attempts.

Every command either publishes one complete new Session Version or preserves the prior Quiescent Boundary and version. Cancellation is cooperative and is observed only at safe points: cancellation before publication returns the typed cancelled failure and rolls back; cancellation after atomic publication cannot revoke the committed result. Policies are immutable snapshots whose IDs/revisions must match `SimulationSessionConfiguration` and remain fixed for the Session. Runtime revalidates every artifact and binding fact it consumes but exposes no pass, scheduler, SCC, storage, or packed-representation control.

## 1. Value domain

`LogicValue = { 0, 1, X, Z }`.

- `X` means insufficient information or conflicting effective drives.
- `Z` means no effective drive contribution.
- ordinary logic inputs normalize `Z` to `X`;
- a disabled tri-state output produces `Z`;
- Sequential Component state stores only `0`, `1`, or `X`; sampling `Z` stores `X`.

The storage rule is a deliberate Logic Lab deviation: SystemVerilog four-state variables can store `Z`.

### 1.1 Conservative Merge

For a nonempty finite set of possible bit results, Conservative Merge returns their greatest common information in the order defined in Section 2:

```text
merge(v, v, ...) = v             for v in { 0, 1, X, Z }
merge(any differing values) = X
```

Equivalently, it is the nonempty meet in `D`. Possible-case evaluation must produce at least one case; an empty case set is an invalid Component Contract. Merge is applied bitwise to vectors. In particular, equal `Z` results merge to `Z`; state-storage normalization from `Z` to `X` happens only after merge.

### 1.2 Scalar oracle

After input normalization, the scalar oracle is:

| `a` | `NOT a` |
|---|---|
| `0` | `1` |
| `1` | `0` |
| `X` | `X` |

| `a` | `b` | `AND` | `OR` | `XOR` |
|---|---|---|---|---|
| `0` | `0` | `0` | `0` | `0` |
| `0` | `1` | `0` | `1` | `1` |
| `1` | `0` | `0` | `1` | `1` |
| `1` | `1` | `1` | `1` | `0` |
| `0` | `X` | `0` | `X` | `X` |
| `1` | `X` | `X` | `1` | `X` |
| `X` | `0` | `0` | `X` | `X` |
| `X` | `1` | `X` | `1` | `X` |
| `X` | `X` | `X` | `X` | `X` |

NAND, NOR, and XNOR negate the corresponding result. Multi-input gates fold the associative scalar operation; zero-input gates are not valid Component Contracts. Vector gates apply the scalar oracle bitwise after explicit width validation.

Unknown control or selection is evaluated by possible cases and combined with Conservative Merge. A multiplexer with unknown select therefore returns a known bit only when every reachable input agrees on that bit.

### 1.3 Net resolution

For each bit, ignore `Z` and inspect the remaining Driver contributions:

1. no effective contribution produces `Z` with cause set `{ Undriven }`;
2. any effective `X`, or both an effective `0` and effective `1`, produces `X`;
3. otherwise one or more equal effective drives produce that value.

The diagnostic cause is a set computed independently from the value: include `UnknownDriver` when any effective contribution is `X`, and include `Contention` when both `0` and `1` are effective. Both causes can therefore coexist. The observable Logic Value and cause set are separate; a cause-set change does not trigger downstream logic when the Logic Value remains unchanged.

## 2. Information order and combinational feedback

For one combinational solver epoch, use the finite Information Order:

```text
        0   1   Z
         \  |  /
            X
```

Formally, `X` is below `0`, `1`, and `Z`; the three maximal values are incomparable. `D` is a finite pointed directed-complete partial order and has every nonempty finite meet. It is **not a lattice** because, for example, `0` and `1` have no upper bound or join. Every generated combinational evaluator and Net resolver must be total, deterministic, pure, and monotone in this order. Stateful, time-dependent, last-writer, or user-code behavior cannot be a combinational evaluator.

The Compiler builds a bipartite evaluator/Net dependency graph, cuts every Sequential Component output, computes strongly connected components (SCCs), and topologically orders the condensation graph.

For one affected cyclic SCC epoch, let `C` contain every internal Driver and Net bit, let `P = D^C`, and let `F : P -> P` be the simultaneous vector of evaluator-output and Net-resolution equations while boundary inputs and state are frozen. The semantic result is obtained by synchronous iteration from `p0 = (X, ..., X)`: `p(n+1) = F(pn)`. The sequence ascends because `F` is monotone, and it stabilizes because `P` is finite. Its stable value is a fixed point below every other fixed point: the unique Least Information Fixed Point. This direct finite-chain proof is the contract; Knaster-Tarski is not directly applicable because `P` is not a complete lattice.

The Runtime may compute that result with a coordinate worklist only under all of these premises:

1. hold its boundary inputs fixed for the solver epoch;
2. reset every internal Driver and Net slot to `X` in the working layer;
3. initially mark every coordinate equation dirty, or use an equivalent dependency-complete seed;
4. evaluate against current working values and accept only preservation or refinement in the Information Order;
5. after every strict refinement, eventually reevaluate every dependent equation;
6. prevent permanent starvation and unbounded no-op requeueing; and
7. declare quiescence only when every coordinate equation is satisfied.

These fairness and propagation conditions make the settled Logic Values independent of worklist order. Stable source ordinals still define the V1 queue order so diagnostics and work evidence are reproducible; ordering is not the convergence proof. Because every slot can refine from `X` to one maximal value at most once, there are at most `|C|` strict refinements, though there may be more no-op evaluations. An incomparable or regressive evaluator result is an internal Component Contract or Runtime defect: fail the Logical-time Advance and publish no partial state. Operational combinational cycle detection is neither required nor allowed.

Diagnostic cause sets are recomputed as Drivers refine but are not coordinates of `P`; a cause-only change does not enqueue logic propagation. This preserves the value proof even when, for example, an `UnknownDriver` cause is replaced by `Contention` while the Net remains `X`.

An inverter ring and an underconstrained cross-coupled network both settle to least-fixed-point `X` and are reported as Indeterminate Feedback, not as a guessed oscillation. That result alone does not distinguish a network with no total Boolean fixed point from one with several incomparable Boolean fixed points. If the product needs that classification, it requires a separate bounded existence/witness analysis and must not seed Runtime state with a guessed solution.

When a boundary input changes later in the same Logical Time, the affected cyclic SCC begins a new epoch from `X`. A previous known fixed point is never reused as the seed. Acyclic regions may use incremental propagation because they have no feedback state.

Work limits still protect the host from oversized circuits or implementation defects. Exhausting a limit proves nothing about convergence and rolls back the Logical-time Advance.

## 3. Session creation and event calendar

`SimulationSessionConfiguration` contains the exact Simulation Policy ID/revision, Trace Policy ID/revision, and ordered initial Probe source bindings. Each binding is stable source identity plus a fully scoped Hierarchy Path; it contains no Probe ID. Opening a Session allocates one fresh Session-scoped Probe ID per source and returns the IDs in source order. Restart opens a new Session, retires every old Probe ID, and allocates a fresh ordered set even when the requested sources are identical. The configuration contains no scheduler algorithm, packed representation, wall-clock pacing, or omitted-state option.

Every initial Probe source must resolve to exactly one Net in the supplied Compilation Artifact, and no two initial bindings may resolve to the same Net. Violations return `InitialProbeBindingsInvalid`; they are caller precondition failures, never Runtime internal defects.

Session creation begins at Logical Time zero:

1. Sequential Component state comes from its complete explicit authored initial state.
2. ROM and RAM content comes from complete explicit Memory Images. An authoring convenience may create an all-`X` value or image before commit, but Runtime observes no omitted state or cell.
3. input sources and Clock Sources publish their initial Driver values.
4. the combinational network settles.
5. the first Quiescent Boundary and one initial Trace sample for every configured Probe are committed at time zero.

A Clock Source has an initial value, first-transition time, positive high duration, and positive low duration. It stores only generator state and its next transition; it does not pre-materialize an infinite event sequence.

Future Stimulus Batches are held in a min-heap ordered by `(LogicalTime, stableSequence)`. `Step` pops the earliest non-empty time bucket; it never scans empty ticks. Scheduling in the committed past or at the current Logical Time is rejected. Multiple same-time changes to one Driver must normalize to one identical value or the batch is rejected.

Wall-clock pacing controls only how quickly `Run` requests advances. It cannot change event order or results.

## 4. Logical-time Advance

One advance uses a discardable working layer:

```text
previous Quiescent Boundary
  -> apply the next Stimulus Batch at delta zero
  -> settle combinational regions
  -> form one causal Trigger Batch
  -> sample every triggered Sequential Component from one pre-commit snapshot
  -> commit the Trigger Batch together
  -> settle resulting combinational changes
  -> repeat while new definite triggers exist
  -> atomically publish the next Quiescent Boundary and Trace batch
```

The working layer includes Driver and Net values, component state, memory writes, event-frontier state, diagnostics, and staged Trace transitions. Later Delta Steps can see earlier working commits; callers cannot.

Stable source ordinals break all otherwise equal worklist and event ties. Dictionary or hash iteration order is never observable. V1 uses one deterministic scheduler; parallel execution requires differential proof and measurement before adoption.

### 4.1 Sequential triggering

- Only `0 -> 1` is a definite rising edge and only `1 -> 0` is a definite falling edge.
- A transition involving `X` or `Z` does not trigger an edge-sensitive component and emits a clock diagnostic.
- All components triggered by the same settled change form one Trigger Batch and sample one pre-commit snapshot.
- Derived clocks can create several Trigger Batches at one Logical Time; components are not limited to one trigger per time.
- A latch is triggered after any settled change to one of its data or enable inputs. It samples that settled pre-commit snapshot; while enabled, every later settled data change can trigger another batch at the same Logical Time. Edge-sensitive components act only after a configured Definite Edge.
- Simultaneously active `S` and `R` on the V1 SR latch store `X` and produce a control-conflict diagnostic.
- An `X` or `Z` control evaluates active and inactive possibilities and applies Conservative Merge.

Latches follow the same causal batching rule. Their state changes can initiate more Delta Steps. V1 has no asynchronous set/reset variant; a future Component Contract must specify its complete priority and batching behavior.

### 4.2 Zero-time oscillation

Sequential feedback or generated clocks can toggle indefinitely without advancing Logical Time. After each Trigger Batch commit, the Runtime may hash the complete canonical working state and pending frontier as a fast filter. The canonical form contains every value that can affect the next semantic transition, including Driver and Net planes, component and memory state, generator state, and normalized frontier ordering; it excludes hashes, allocation identities, diagnostics, and monotonic work counters. Only exact equality after a full comparison proves Zero-time Oscillation.

A proven repetition fails the entire advance with `ZeroTimeOscillation`; the previous Quiescent Boundary remains committed. If a work or cancellation limit occurs before proof, the distinct result is `ResourceLimit` or `Cancelled`. No partial state, memory, time, or Trace is published.

## 5. Memory

ROM has asynchronous read. V1 RAM is single-port with asynchronous read and definite rising-edge write.

- A known read address returns the selected word.
- An unknown read address returns the Conservative Merge of every possibly addressed word.
- A definite disabled write changes nothing.
- A definite enabled write to a known address updates that word in the Trigger Batch commit.
- Unknown enable merges the no-write and write possibilities.
- A partially unknown write address conservatively merges a possible write into every reachable cell.
- After commit, asynchronous read observes the new memory value in the next Delta Step.

All address ranges and allocation sizes use checked arithmetic and the active policy. Multi-port memory, byte enables, synchronous read, and alternative read-during-write rules require different Component Contracts.

## 6. Trace and probes

Probes bind through stable source identity and Hierarchy Path in the active Compilation Artifact. Trace records only committed transitions by default; optional delta-debug capture has a separate policy and never becomes simulation truth.

Runtime storage is an internal bounded circular sequence of immutable chunks. External callers receive normalized transition segments or an explicitly requested visual summary, never internal chunk bytes. Eviction creates a Trace Gap with the earliest available cursor. A gap is never filled, flattened, or represented as `X`.

Trace capacity cannot block, fail, or roll back simulation. The oldest sealed storage may be evicted after an atomic Trace batch is published.

## 7. Hot swap

Hot Swap is one Runtime intent. Compilation, compatibility checks, State Migration, initial settlement, and atomic publication are hidden inside the Module.

State migrates only when stable instance identity, Component Contract kind, width, and state schema are compatible. Event queues, combinational caches, and runtime ordinals never migrate. At most the current artifact and one newest-wins candidate coexist. Failure discards the candidate and leaves the old Session paused; the user can retry or explicitly Restart.

## 8. Required evidence

- exhaustive scalar gate, control, edge, resolution, Conservative Merge, and memory tables;
- property and differential tests for any packed Logic Vector representation, including tail bits and non-word-aligned slices;
- exhaustive ordered-input-pair monotonicity tests for every combinational evaluator and resolver;
- SCC least-fixed-point comparison with a slow synchronous bottom-iteration whole-region oracle;
- fair-worklist schedule, stable-ordinal, and hash/enumeration-order permutations that produce identical settled values;
- negative mutants for unknown-as-absent resolution, arbitrary unknown-select choice, missed dirty propagation, and regressive evaluators;
- known-resolving feedback, odd inverter rings, bistables with multiple Boolean fixed points, and contention cases with final cause evidence;
- causal Trigger Batch, derived-clock, latch, and unknown-control scenarios;
- exact-state Zero-time Oscillation witnesses and resource-limit non-witnesses;
- atomic rollback tests covering state, RAM, logical time, diagnostics, and Trace;
- hot-swap compatibility and rejection matrices; and
- initial, replacement, Hot Swap preservation, and Restart retirement/allocation cases for Session-scoped Probe IDs.

## 9. Sources and qualification

- IEEE Std 1800-2023, especially clauses 4.4, 6.3, 6.5-6.6, 9.4.2, 11.4.8, and 28-29: [local reference](../../1800-2023.pdf).
- Robert Tarjan, [Depth-First Search and Linear Graph Algorithms](https://doi.org/10.1137/0201010).
- Bernard Zeigler et al., *Theory of Modeling and Simulation*, for discrete-event modeling and DEVS terminology.
- [Least-Fixed-Point Semantics Research](../research/least-fixed-point-semantics.md), for the finite proof, monotonicity audit, counterexamples, and source access record.
- [Diagnostics V1](./diagnostics-v1.md), for Simulation codes, cause evidence, outcome reasons, and ordering.

The least-fixed-point solver, definite-edge rule, storage normalization, and atomic failure behavior are Logic Lab semantics, not claims about IEEE 1800 compatibility.
