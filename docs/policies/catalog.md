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

| Policy               | Owner              | Dimensions                                                                                                                                                                        |
| -------------------- | ------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Package Policy       | Project Format     | input/output carrier bytes, entries, expanded bytes, JSON depth/tokens, strings, entities, memory parts                                                                           |
| Project Scale Policy | Compiler           | `definition_count`, `entity_count`, `hierarchy_depth`, `elaborated_slot_count`, `memory_cell_count`                                                                               |
| Simulation Policy    | Simulation Runtime | event work, frontier work, working-layer size, zero-time state evidence                                                                                                           |
| Trace Policy         | Simulation Runtime | probes, retained transitions, sealed chunks, bytes, debug capture                                                                                                                 |
| Analysis Policy      | Boolean Analysis   | rows, cubes, primes, chart edges, Petrick terms, AIG/cut/mapping work, BDD work                                                                                                   |
| Scheduling Policy    | Application        | admission rate, per-identity fairness, queue capacity, worker concurrency, result retention                                                                                       |
| Workspace Policy     | Application        | Workspace and authoring admission, history retention, detached recovery, hot-swap peak, sandbox lifetime, Durable Display Name scalar/UTF-8 bytes, catalog page size/cursor bytes |
| Browser Policy       | Web                | Scene-intent bytes, snapshot/patch records, candidate-transfer bytes, bitmap pixels, effective density, zoom, and the implemented Scene caches                                    |

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

`elaborated_slot_count` counts every library evaluator, resolved library Port slot, and Net record in the elaborated entry, plus each reachable hierarchical occurrence and scoped Net before boundary unions. Compiler measures generated Port cardinality and admits the complete count before allocating generated identities or topology storage. `memory_cell_count` is `word width * depth` for every elaborated ROM or RAM instance and is admitted before packed memory is materialized.

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

The global, per-subject, and partition admission capacities share one fixed window: they bound aggregate attempts, per-subject fairness, and retained caller state respectively. Exhausting any capacity rejects before queue admission; expired partitions are reclaimed with bounded amortized work instead of a full identity scan. Full queues reject rather than drop.

Compilation is newest-wins per Workspace. Session work is FIFO and single-consumer per Session. Analysis is FIFO within one subject and round-robin across nonempty subjects. An active Run retains one admitted Session item across repeated Advances; Pause takes effect at an atomic boundary without consuming another queue item. Worker counts bound executing calls, not hidden ThreadPool threads. Admission and retention use monotonic `TimeProvider` timestamps, and retention never extends authorization.

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

Workspace dimensions have these grouped rules:

| Group                   | Rule                                                                                                                                                                                                                                                                   |
| ----------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| process admission       | `global_workspace_count` bounds all retained Workspaces; `anonymous_workspace_count_global` additionally bounds anonymous live, reserved, and pending-transfer ownership; `workspace_count_per_subject` is a fairness bound, not a replacement for either global bound |
| authoring               | definition, entity, and nested command-item counts stop at budget exhaustion and reject before publication; they share Compiler's authored-entity accounting but do not replace hierarchy or elaboration limits                                                        |
| history and idempotency | limits apply only after atomic success; repository receipt pruning shares the command transaction and never makes an edit or save partially commit                                                                                                                     |
| retention               | detached and Sandbox deadlines use `TimeProvider` and never slide because of a rejected operation                                                                                                                                                                      |
| names and catalog       | both Durable Display Name dimensions must pass; page and cursor requests cannot exceed their maxima                                                                                                                                                                    |

Workspace admission and expiry reclamation are one atomic directory decision, so concurrent opens cannot overshoot a limit. `AuthoringLimits` groups the three authoring dimensions at the public composition seam; there is no second internal authoring policy.

The trusted `WorkspaceCaller` defines the per-subject partition. An anonymous Browser ID is a continuity locator, not an unresettable person or network identity. Sandbox Claim reserves the authenticated target before persistence, retains the reservation while commit acknowledgement is uncertain, and transfers ownership only on success. Definitive failure, close, and retention cleanup release the reservations they own.

`hot_swap_peak_bytes` is logical owned-buffer accounting, not process RSS:

| Unit                                  |                      Bytes |
| ------------------------------------- | -------------------------: |
| owned reference or index slot         |                          8 |
| packed Logic Vector word, two planes  |                         16 |
| resolved Net word, three cause planes |                         24 |
| unpacked Application `LogicValue`     |                          1 |
| Trace transition                      | 48 plus packed value words |

Event-frontier accounting charges three slots per queued Stimulus Batch, one per queued assignment, and two per Logical-Time index entry, indexed Driver assignment, Clock bucket, or Clock transition.

Count each unique owned buffer once. Shared immutable Compilation Artifact/source-index records, CLR headers, allocator slack, tree links, and transient preflight metadata are excluded. Session and outcome share Diagnostics; Application and Engine share migration evidence instead of cloning it. Net resolution reads Artifact Driver ordinals without a per-Net reference buffer.

| Lifecycle peak | Included storage                                                                                                                                                                                                                                     |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| candidate      | committed Session working layer and Diagnostics, retained Trace, current Workspace Projection, complete replacement working layer and Clock calendar, replacement Diagnostics, and one extra two-plane buffer per concurrently retained migrated RAM |
| settlement     | candidate plus one reusable cyclic-region work area and the larger of one evaluator's temporary envelope or the widest recomputed Net resolution plane                                                                                               |
| publication    | replacement Session, Engine outcome, retained and replacement Workspace Projections, exact changed-Probe staging, Trace fork/index/chunk, migrated-state sources, Probe ID/value arrays, and observed Probes                                         |

The cyclic work area contains pending ordinals for the largest region, one pending-state slot per evaluator, Driver ordinals for the largest region, and prior-output references for the widest evaluator. A Driver already at bottom is reused; an epoch reset adds a plane only when its prior value differs. Count multi-output storage by unique plane—Demux shares its selected-data and zero planes. Checked arithmetic saturation is over-limit.

Initial admission occurs before replacement working-layer or RAM-clone allocation. After settlement derives exact Diagnostics and changed Probe values without allocating publication buffers, final admission compares the candidate/settlement peak with the publication peak and uses the larger value before allocating either publication shape. The Trace fork index is charged at its final staged capacity, and post-eviction retained size is calculated without materializing a second fork. Rejection reports Workspace Policy identity/revision, `hot_swap_peak_bytes`, and observed bytes while retaining the old Session unchanged.

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

The limits array contains every row exactly once in the shown order; values are positive JavaScript-safe integers, `interop_batch_bytes >= 516` (the 512-byte conservative envelope plus one Base64 quantum), and `zoom_millionths_minimum <= zoom_millionths_maximum`. Millionths encode a positive finite scale without making binary floating-point spelling part of configuration. A second byte limit for the Canvas bitmap would be algebraically identical to `canvas_bitmap_pixels * 4` for the fixed opaque 2D backing store, so the policy defines only the independent pixel bound. Unimplemented waveform storage and uncalibrated frame/long-task thresholds do not appear in the active policy shape; item 39 must first produce measurement evidence and an actual consuming behavior before either can become a versioned field.

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
