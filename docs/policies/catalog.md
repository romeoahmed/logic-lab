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
| Browser Policy | Web | semantic-intent bytes, snapshot/patch records, candidate-transfer bytes, bitmap pixels/bytes, effective density, zoom, semantic-tree paging, scene/waveform caches |

Each policy has a stable ID and revision. A policy failure reports the policy revision, dimension, observed work, and stable reason. It does not expose sensitive fleet capacity.

Unless a shape below says otherwise, fields and arrays are present and non-null, dimensions appear exactly once in shown order, maximums are positive, and tokens/unsigned decimals use [Diagnostics V1](../specs/diagnostics-v1.md#2-diagnostic-record) lexical forms.

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

`SimulationPolicy` dimensions are:

```text
scheduled_batch_count
scheduled_assignment_count
advance_work_item_count
advance_frontier_item_count
working_layer_slot_count
trigger_batch_count
zero_time_state_count
```

The first two bound retained future input; the next four bound one discardable Logical-time Advance; `zero_time_state_count` bounds exact repetition evidence, not a heuristic proof. Work counters increment before the corresponding item is admitted, use checked arithmetic, and roll the advance back when the maximum would be exceeded.

`TracePolicy` dimensions are:

```text
probe_count
retained_transition_count
sealed_chunk_count
retained_bytes
delta_debug_record_count
```

`probe_count` bounds Session admission and replacement. The storage dimensions control retention and eviction and never roll back a successful Logical-time Advance. `retained_bytes` counts owned Trace payload and index storage by the Runtime's declared accounting method; it is not a CLR heap measurement. Delta-debug capture is zero when that explicit mode is absent and is never required to reconstruct committed semantics.

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

`SchedulingPolicy` dimensions are:

```text
admission_requests_per_subject
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

The admission pair defines one fixed per-subject window. Queues reject rather than drop when full. Compilation remains newest-wins per Workspace, Session work remains FIFO and single-consumer per Session, and Analysis is FIFO within one subject plus round-robin across nonempty subject queues. Worker counts bound concurrent executing calls, not the number of hidden ThreadPool threads. Retention expiry uses `TimeProvider` and never extends authorization.

`WorkspacePolicy` dimensions are:

```text
global_workspace_count
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

`global_workspace_count` is the process-wide hard bound on retained Workspace state; the
per-subject dimension is an additional fairness bound and never replaces it. Admission and
expiry reclamation share one atomic directory decision, so concurrent opens cannot overshoot
either limit. `authoring_command_item_count` bounds the complete nested shape of one Edit
Intent before Project Editor execution. The definition and entity dimensions validate the
candidate Project Document before publication, using the same authored entity accounting as
Compiler Project Scale Policy; they reject atomically and never substitute for the Compiler's
hierarchy and elaboration limits. History/idempotency limits apply after an atomic successful
publication and produce the contract's explicit truncation or expired-idempotency behavior;
they never make a valid edit partially commit. Retention uses `TimeProvider`.
`hot_swap_peak_bytes` is declared owned-buffer accounting, not a promise about total process
RSS. Both Durable Display Name dimensions must pass, and catalog requests cannot exceed the
page/cursor maxima.

`BrowserPolicy` needs lower as well as upper bounds, so it owns this separate record:

```text
BrowserPolicy
  policyId: StableToken
  policyRevision: StableToken
  limits: BrowserLimitV1[]
  observationThresholds: BrowserObservationThresholdV1[]

BrowserLimitV1
  dimension: one Browser limit token below
  comparison: AtMost | AtLeast
  value: UnsignedDecimal

BrowserObservationThresholdV1
  dimension: frame_work_microseconds | long_task_microseconds
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
canvas_bitmap_bytes               AtMost
effective_density_millionths      AtMost
zoom_millionths_minimum           AtLeast
zoom_millionths_maximum           AtMost
semantic_tree_page_items          AtMost
display_list_bytes                AtMost
spatial_index_bytes               AtMost
scene_cache_bytes                 AtMost
waveform_cache_bytes              AtMost
```

Both arrays contain every row exactly once in the shown order; values are positive and `zoom_millionths_minimum <= zoom_millionths_maximum`. Millionths encode a positive finite scale without making binary floating-point spelling part of configuration. Observation thresholds emit measured diagnostics/telemetry only; crossing them never authorizes a partial Scene, omitted semantic item, or lower-fidelity Trace representation.

## Measurement discipline

- Keep semantic correctness suites separate from performance suites.
- Use BenchmarkDotNet as a comparative instrument for synchronous core kernels, not as a source of universal latency promises.
- Every permanent microbenchmark names its production scenario, comparison axis, baseline, parameter case count, and corpus revision; each case must justify its run cost.
- Validate benchmark compilation and execution with a dry job, use short jobs only for iteration, and record final Release-mode artifacts from the declared measurement job.
- Keep input construction out of measured methods, return observable results, avoid manual timing/loop amplification, and use deterministic nonconstant inputs that prevent folding.
- Use browser traces and Playwright scenarios for scene and interaction performance.
- Measure browser intent/snapshot sizes, bitmap allocations, frame/long-task distributions, idle activity, and semantic-tree paging before calibrating Browser Policy.
- Use load tests for Blazor circuit, queue, database, and memory capacity.
- Record runtime, build mode, hardware, operating system, corpus revision, cold/warm state, and outcome distribution.
- Prefer work counters and allocation profiles over wall-clock time for deterministic algorithm envelopes.
- A CLR allocation counter is telemetry, not a hard memory boundary. A hard termination requirement implies a worker-process adapter.

No policy value is normative until its evidence record is linked from this catalog.
