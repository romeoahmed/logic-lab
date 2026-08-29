# Policy Catalog

The catalog owns policy identity, classification, and calibration rules across Project Format, Compiler, Simulation Runtime, Boolean Analysis, Application, and Web. Policies protect a shared deployment without redefining circuit semantics; concrete values require a representative corpus and measurement method.

## Classification

Every number belongs to exactly one class:

1. **Semantic invariant** — observable correctness, such as positive width or atomic Logical-time Advance.
2. **Format limit** — a representational or safe-decoding bound in a versioned carrier.
3. **Provisional policy** — a replaceable deployment default awaiting calibration.
4. **Measured acceptance threshold** — a gate tied to a corpus, environment, and recorded benchmark.

Do not copy a provisional value into an ADR, Project Document, or public compatibility promise.

## Policy catalogs

| Policy | Owner | Dimensions |
|---|---|---|
| Package Policy | Project Format | input/output carrier bytes, entries, expanded bytes, JSON depth/tokens, strings, entities, memory parts |
| Project Scale Policy | Compiler | `definition_count`, `entity_count`, `hierarchy_depth`, `elaborated_slot_count`, `memory_cell_count` |
| Simulation Policy | Simulation Runtime | event work, frontier work, working-layer size, zero-time state evidence |
| Trace Policy | Simulation Runtime | probes, retained transitions, sealed chunks, bytes, debug capture |
| Analysis Policy | Boolean Analysis | rows, cubes, primes, chart edges, Petrick terms, AIG/cut/mapping work, BDD work |
| Scheduling Policy | Application | admission rate, per-identity fairness, queue capacity, worker concurrency, result retention |
| Workspace Policy | Application | Workspace and authoring admission, history retention, detached recovery, hot-swap peak, sandbox lifetime, Durable Display Name scalar/UTF-8 bytes, catalog page size/cursor bytes |
| Browser Policy | Web | Scene-intent bytes, snapshot/patch records, candidate-transfer bytes, bitmap pixels, effective density, zoom, and the implemented Scene caches |

Each policy has a stable ID and revision. A policy failure reports the policy revision, dimension, observed work, and stable reason. It does not expose sensitive fleet capacity.

Unless a shape below says otherwise, fields and arrays are present and non-null, dimensions appear exactly once in shown order, maximums are positive, and tokens/unsigned decimals use [Diagnostics V1](../specs/diagnostics-v1.md#2-diagnostic-record) lexical forms.

### Package Policy

`PackagePolicy` has this closed shape and applies to both read and write:

```text
PackagePolicy
  policyId: StableToken
  policyRevision: StableToken
  limits: PackageLimitV1[]

PackageLimitV1
  dimension: carrier_bytes | entry_count | part_bytes | expanded_bytes
             | json_depth | json_tokens | string_scalar_count
             | string_utf8_bytes | array_items | entity_count
             | memory_part_count | memory_cell_count
  maximum: UnsignedDecimal
```

The reader counts actual bytes/tokens/items rather than trusting ZIP or HTTP declarations. The writer applies the same dimensions before publication, so a carrier emitted under one Package Policy is readable by the same Project Format build and policy. A deployment may accept an older, larger package only by selecting and recording a policy that admits it; it never bypasses checks ad hoc.

### Project Scale Policy

`ProjectScalePolicy` has this closed shape; it defines no uncalibrated default:

```text
ProjectScalePolicy
  policyId: StableToken
  policyRevision: StableToken
  limits: ProjectScaleLimitV1[]

ProjectScaleLimitV1
  dimension: definition_count | entity_count | hierarchy_depth
             | elaborated_slot_count | memory_cell_count
  maximum: UnsignedDecimal
```

`elaborated_slot_count` counts every library evaluator, resolved library Port
slot, and Net record in the elaborated entry. Hierarchical compilation also
counts each reachable occurrence and each scoped Net before boundary unions.
The Compiler measures generated Port cardinality and admits this complete count
before it allocates generated Port identities or topology storage.
`memory_cell_count` counts every referenced Memory Image bit cell as
`word width * depth` for each elaborated ROM or RAM instance. Compiler admits
that total before materializing the private packed two-plane memory storage.

### Shared policy shape

The remaining Module and Application policies use the same local schema notation without creating a shared CLR `Common` type:

```text
<OwnedPolicy>
  policyId: StableToken
  policyRevision: StableToken
  limits: <OwnedLimitV1>[]

<OwnedLimitV1>
  dimension: one owner-specific token below
  maximum: UnsignedDecimal
```

A Module owns its concrete policy type; the notation only makes identity, revision, ordering, and integer encoding consistent.

### Simulation Policy

`SimulationPolicy` dimensions are:

```text
scheduled_batch_count
scheduled_assignment_count
advance_work_item_count
advance_frontier_item_count
working_layer_slot_count
trigger_batch_count
zero_time_state_count
zero_time_state_word_count
```

The first two bound retained future input; the next four bound one discardable Logical-time Advance. `zero_time_state_count` bounds the number of distinct exact repetition witnesses. `zero_time_state_word_count` bounds their cumulative canonical 64-bit-word representation, including shape markers and both packed Logic planes, so circuit size and witness count cannot multiply into unbounded retained evidence. Neither dimension is a heuristic proof. Work counters increment before the corresponding item is admitted, use checked arithmetic, and roll the advance back when the maximum would be exceeded.
Each reachable amount admitted by logical shift's explicit possible-case set consumes one `advance_work_item_count` item before case evaluation begins. The evaluator may stream the Conservative Merge without retaining every reachable result; symbolic logic that does not enumerate a case set is charged for its ordinary evaluator and Net work only.
Before a first effective RAM write clones copy-on-write storage, each copied 64-bit word in both packed Logic planes consumes one `advance_work_item_count` item.

### Trace Policy

`TracePolicy` dimensions are:

```text
probe_count
retained_transition_count
sealed_chunk_count
retained_bytes
delta_debug_record_count
```

`probe_count` bounds Session admission and replacement. The storage dimensions control retention and eviction and never roll back a successful Logical-time Advance. `retained_bytes` counts owned Trace payload and index storage by the Runtime's declared accounting method; it is not a CLR heap measurement. Delta-debug capture is zero when that explicit mode is absent and is never required to reconstruct committed semantics.

### Analysis Policy

`AnalysisPolicy` dimensions are:

```text
truth_table_row_count
qmc_cube_count
prime_implicant_count
prime_chart_edge_count
petrick_term_count
aig_node_count
cut_count
mapping_candidate_count
bdd_node_count
bdd_cache_entry_count
verification_assignment_count
analysis_work_item_count
analysis_depth
```

These dimensions bound all V1 explanation, exact-cover, graph, mapping, and proof paths without exposing an algorithm control to callers. `analysis_depth` is the maximum explicit or prevalidated logical depth and never licenses recursion over untrusted depth. Exhaustion returns Inconclusive and no best-so-far replacement.

### Scheduling Policy

`SchedulingPolicy` dimensions are:

```text
admission_requests_global
admission_requests_per_subject
admission_partition_count
admission_window_milliseconds
compilation_queue_items
session_queue_items
analysis_queue_items
analysis_queue_items_per_subject
compilation_worker_count
session_worker_count
analysis_worker_count
analysis_result_retention_seconds
```

The three admission capacities share one fixed window. The global request limit
bounds aggregate scheduling attempts, the per-subject request limit preserves fairness,
and the partition-count limit bounds retained caller identity state. Exhausting any
one rejects before queue admission. Expired partitions are reclaimed with bounded
amortized work rather than by scanning every identity on a new caller. Queues reject
rather than drop when full. Compilation remains newest-wins per Workspace, Session
work remains FIFO and single-consumer per Session, and Analysis is FIFO within one
subject plus round-robin across nonempty subject queues. An active Run retains one
admitted Session scheduling item across its repeated Advances; continuations reuse
that item and never bypass `session_queue_items`. Pause is bounded Run control and
becomes effective at an atomic boundary without allocating another Session queue
item. Worker counts bound concurrent executing calls, not the number of hidden
ThreadPool threads. Admission windows and retention expiry use monotonic timestamps
from `TimeProvider`; result retention never extends authorization.

### Workspace Policy

`WorkspacePolicy` dimensions are:

```text
global_workspace_count
anonymous_workspace_count_global
workspace_count_per_subject
authoring_definition_count
authoring_entity_count
authoring_command_item_count
history_revision_count
idempotency_record_count
detached_retention_seconds
sandbox_retention_seconds
hot_swap_peak_bytes
durable_display_name_scalar_count
durable_display_name_utf8_bytes
catalog_page_items
catalog_cursor_bytes
```

`global_workspace_count` is the process-wide hard bound on retained Workspace state.
`anonymous_workspace_count_global` is an additional process-wide bound across every
anonymous caller identity; it counts live, reserved, and pending-transfer ownership so
rotating a browser identity cannot consume the capacity reserved for authenticated callers.
The per-subject dimension remains an additional fairness bound and never replaces either
global bound. Admission and expiry reclamation share one atomic directory decision, so
concurrent opens cannot overshoot any limit. `authoring_command_item_count` bounds the complete nested shape of one Edit
Intent before Project Editor execution. Command-shape and candidate-document accounting both
stop as soon as their remaining budget is exhausted. The definition and entity dimensions
validate the candidate Project Document before publication, using the same authored entity
accounting as Compiler Project Scale Policy. All three authoring dimensions are configurable
through the owning Workspace Policy, not a second internal policy. Its `AuthoringLimits` value
keeps those related dimensions together at the public composition seam; they reject atomically
and never substitute for the Compiler's
hierarchy and elaboration limits. History/idempotency limits apply after atomic successful
publication and produce the contract's explicit truncation or expired-idempotency behavior;
the same idempotency count bounds newest-first Durable Project repository receipts, whose
pruning shares the command transaction. These limits never make a valid edit or save partially
commit. Time-based retention uses `TimeProvider`.

The trusted `WorkspaceCaller` on every open request defines the per-subject partition. Anonymous
browser IDs remain useful continuity locators, but are not treated as an unresettable person or
network identity. A Sandbox Claim reserves the authenticated target partition before persistence
begins, retains that reservation while commit acknowledgement is uncertain, and atomically
transfers the live Workspace from its prior partition only when Claim succeeds. A definitive
failure releases the target reservation; close or retention cleanup releases both the live and
any pending transfer.

`hot_swap_peak_bytes` is declared owned-buffer accounting, not a promise about total process
RSS. The accounting uses fixed logical byte units: eight bytes per owned reference or index
slot, sixteen bytes per packed Logic Vector word (the two 64-bit logic planes), twenty-four
bytes per resolved Net word (the three 64-bit cause planes), one byte per unpacked Application
`LogicValue`, and the Trace Policy's 48-byte
transition base plus packed value words. Event-frontier accounting charges three slots per
queued Stimulus Batch (the batch reference and its two-part priority), one slot per queued
Stimulus assignment reference, two slots per Logical-Time index entry, two slots per indexed
Driver assignment, two slots per Clock bucket, and two slots per Clock transition. These are
logical storage units; tree links, collection growth slack, and other CLR storage metadata are
excluded. The peak includes the committed Session working-layer buffers, nested Diagnostic
reference buffers, and retained Trace storage once; the complete replacement working-layer
candidate including its Diagnostics and new Clock event calendar; one additional packed
two-plane buffer only for each compatible migrated RAM while both versions can coexist; the exact
changed-Probe staging array, Trace fork
index, and staged transition chunk; and the Hot Swap terminal publication arrays that coexist with
the candidate, including migrated-state sources, shared evidence/outcome Probe IDs, and observed
Probes. Application reports the retained Workspace Projection buffers that coexist with the
Runtime attempt. Runtime derives the replacement-dependent consumer publication bytes from the
exact rebound Probe count and widths. Application materializes one owned Probe-reference array
and one unpacked value array per Probe, while its Hot Swap outcome shares the Engine outcome's
immutable migration-evidence collections instead of cloning them. The Session and outcome share
the top-level Diagnostic reference array and its immutable Diagnostic records, so those buffers
are counted once. Shared
immutable Compilation Artifact records and their source indexes, CLR object headers, allocator
metadata, and transient preflight metadata are excluded. Net resolution reads the Artifact's
Driver ordinals directly and does not materialize a per-Net reference buffer. When the replacement
contains cyclic regions,
settlement reserves one reusable work area before execution: one pending-ordinal slot for each
member of the largest cyclic region, one pending-state slot per evaluator, one ordinal slot per
Driver in the largest cyclic region, one previous-output reference per output of the widest cyclic
evaluator. A cyclic Driver already at the least-fixed-point bottom is reused; an epoch reset
allocates a replacement plane only when the preceding value differs. Settlement also charges the
maximum temporary envelope of one evaluator invocation: its superseded output planes, fan-in and
multi-output reference buffers, packed intermediate values, and evaluator-specific value/index
work buffers. Recomputing an output Net retains its preceding resolution until the replacement
value and cause planes are complete. Because evaluator materialization and Net replacement are
serial and do not overlap, settlement charges the larger of the evaluator envelope and the widest
recomputed Net resolution plane. Multi-output values are counted by unique owned plane; in
particular, Demux outputs share one selected-data plane and one zero plane. Arithmetic is checked
and saturation is treated as over-limit. Initial candidate admission runs before any replacement
working-layer or RAM clone allocation and covers every buffer materialized for settlement. After
settlement determines the exact Diagnostics and changed Probe values without allocating their
staging array, final admission compares two non-overlapping lifecycle peaks: the pre-commit
Runtime candidate with the retained Application projection, and the post-commit replacement
Session plus Engine terminal outcome and retained/new Application projections. It uses the larger
value before allocating Diagnostic, changed-Probe staging, terminal-publication, Trace, or
Application projection buffers. The Trace fork index is allocated once at the final capacity
required by the staged chunk, and its post-eviction retained size is measured without allocating
the fork. Either rejection
reports Workspace Policy ID/revision,
`hot_swap_peak_bytes`, and the observed value while retaining the old Session unchanged. Both
Durable Display Name dimensions must pass, and catalog requests cannot exceed the page/cursor
maxima.

### Browser Policy

`BrowserPolicy` needs lower as well as upper bounds, so it owns this separate record:

```text
BrowserPolicy
  policyId: StableToken
  policyRevision: StableToken
  limits: BrowserLimitV1[]

BrowserLimitV1
  dimension: one Browser limit token below
  comparison: AtMost | AtLeast
  value: UnsignedDecimal
```

Browser limit tokens and required comparisons are:

```text
semantic_intent_bytes             AtMost
scene_snapshot_record_count       AtMost
scene_patch_record_count          AtMost
interop_batch_bytes               AtMost
candidate_transfer_bytes          AtMost
canvas_bitmap_pixels              AtMost
effective_density_millionths      AtMost
zoom_millionths_minimum           AtLeast
zoom_millionths_maximum           AtMost
display_list_bytes                AtMost
spatial_index_bytes               AtMost
scene_cache_bytes                 AtMost
```

The limits array contains every row exactly once in the shown order; values are positive and `zoom_millionths_minimum <= zoom_millionths_maximum`. Millionths encode a positive finite scale without making binary floating-point spelling part of configuration. A second byte limit for the Canvas bitmap would be algebraically identical to `canvas_bitmap_pixels * 4` for the fixed opaque 2D backing store, so the policy defines only the independent pixel bound. Unimplemented waveform storage and uncalibrated frame/long-task thresholds do not appear in the active policy shape; item 39 must first produce measurement evidence and an actual consuming behavior before either can become a versioned field.

The current Interactive Server adapter additionally requires `semantic_intent_bytes <= 16384`
and `interop_batch_bytes <= 16384`. The former and browser-to-.NET uses of the latter leave
room for the Blazor JS-interop envelope below SignalR's default 32-KB incoming-message limit;
the adapter applies the same conservative batch budget in the opposite direction. These are
transport invariants, not calibrated circuit-size thresholds
([Blazor JS interop size limits](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0#size-limits-on-javascript-interop-calls)). Larger browser-originated values require the contract's bounded private transfer/streaming mechanism rather than a larger global Hub limit.

## Measurement discipline

- Keep semantic correctness suites separate from performance suites.
- Use BenchmarkDotNet as a comparative instrument for synchronous core kernels, not as a source of universal latency promises.
- Every permanent microbenchmark names its production scenario, comparison axis, baseline, parameter case count, and corpus revision; each case must justify its run cost.
- Validate benchmark compilation and execution with a dry job, use short jobs only for iteration, and record final Release-mode artifacts from the declared measurement job.
- Keep input construction out of measured methods, return observable results, avoid manual timing/loop amplification, and use deterministic nonconstant inputs that prevent folding.
- Use browser traces and Playwright scenarios for scene and interaction performance.
- Measure browser intent/snapshot sizes, bitmap allocations, frame/long-task distributions, and idle activity before calibrating Browser Policy.
- Use load tests for Blazor circuit, queue, database, and memory capacity.
- Record runtime, build mode, hardware, operating system, corpus revision, cold/warm state, and outcome distribution.
- Prefer work counters and allocation profiles over wall-clock time for deterministic algorithm envelopes.
- A CLR allocation counter is telemetry, not a hard memory boundary. A hard termination requirement implies a worker-process adapter.

No policy value is normative until its evidence record is linked from this catalog.
