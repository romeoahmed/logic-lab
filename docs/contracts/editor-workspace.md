# Editor Workspace Contract

> Status: normative V1 Application seam contract
> Deployment: direct typed calls from Web to Application

This contract owns the interface from Web to the Application-owned Editor Workspace. Compiler, Simulation Runtime, and Boolean Analysis remain direct typed Module calls behind that interface, not serialized command buses.

Stable diagnostic and outcome reason structure is defined by [Diagnostics V1](../specs/diagnostics-v1.md); this contract defines when those values cross the Workspace seam.

[Browser Adapter Contract](./browser-adapters.md) owns Scene and Waveform records. [HTTP Transfer Contract](./http-transfer.md) owns file transport and Problem Details. This file owns Workspace values, validation, ordering, idempotency, and failure semantics only.

## 1. Contract principles

1. Commands express one user intention and have closed request and outcome variants.
2. Stable codes and structured fields drive behavior; localized message text does not.
3. IDs locate resources and never grant access.
4. Attachment, concurrency, authorization, idempotency, and publication are distinct checks.
5. Project Revision, Quiescent Boundary, save, and proposal application publish atomically.
6. Workspace values contain stable source identity, never Compilation-local ordinals or Runtime storage chunks.
7. Attachment validates the application build fingerprint; a mismatch requires a hard reload.

## 2. Identity

| Value | Meaning | Not |
|---|---|---|
| `ProjectId` | authored identity carried by Project Revisions and native packages | Durable Project locator, authorization |
| `DurableProjectId` | one stored Durable Project locator | authored identity, authorization, revision |
| `ProjectRevisionId` | immutable Project Document revision | content digest, Circuit Definition revision |
| `WorkspaceId` | private Editor Workspace locator | Blazor circuit, authorization |
| `WorkspaceAttachmentId` and generation | current controller fence | user token, Workspace lifetime |
| `CompilationArtifactKey` | complete executable provenance | runtime ordinal, Project Revision alone |
| `SimulationSessionId` | one Workspace's active Session locator | Durable Project state |
| `ProbeId` | one Session observation binding | source identity, display row |
| `OperationId` | background Analysis Operation locator | capability, `Task` identity |
| `ProposalId` | Simplification Proposal locator | permission to apply |
| `ExportTicket` | one prepared short-lived download locator | URL, durable authorization |
| `ClientIntentId` | idempotency key in one attachment generation | global request identity |
| `ProjectionVersion` | one Workspace Projection cursor | Project Revision or Session Version |
| `CompilationGeneration` | monotonic Workspace compilation request fence | artifact identity, task identity |

Opaque IDs use canonical strings. JavaScript treats them as strings and derives no ordering or time from them.

Browser records never use a locally scoped entity ID by itself:

```text
AuthoredSourceRefV1
  circuitDefinitionId
  entityKind: definitionPort | componentInstance | instancePort | net | junction | wireGeometry | annotation
  entityId
  portId only when entityKind = instancePort

AuthoredTerminalRefV1 =
  DefinitionTerminal { circuitDefinitionId, portId }
  | InstanceTerminal { circuitDefinitionId, componentInstanceId, portId }

RegionSelectionV1
  circuitDefinitionId
  componentInstanceIds: ComponentInstanceId[]

ElaboratedNetRefV1
  authoredNet: AuthoredSourceRefV1(kind = net)
  hierarchyPath: HierarchyPathV1

HierarchyPathV1
  entryCircuitDefinitionId
  steps: HierarchyStepV1[]

HierarchyStepV1
  containingCircuitDefinitionId
  componentInstanceId
```

For `instancePort`, `entityId` is the owning Component Instance ID and `portId` is required; every other kind uses the entity's own ID and forbids `portId`. The containing Circuit Definition makes authored references unambiguous. Every Hierarchy step is independently scoped and must resolve from the preceding target, while an empty step list denotes the entry definition itself. The final resolved definition must equal `authoredNet.circuitDefinitionId`. A Hierarchy Path additionally distinguishes an elaborated observation; it is never inferred from the current tab, display name, or coordinates.

`RegionSelectionV1.componentInstanceIds` is present, non-null, nonempty, unique, and contains only IDs scoped by `circuitDefinitionId`; no other fields are permitted. Array order carries no semantic meaning. Shape violations fail before dispatch; missing or artifact-mismatched authored sources produce the Compiler-owned `selection_invalid` ineligibility outcome.

Application translates a valid `RegionSelectionV1` into Compiler-owned `BooleanRegionSelection`, a valid `SessionConfigurationV1` into Runtime-owned `SimulationSessionConfiguration`, and a valid `TraceWindowRequest` into Runtime-owned `SimulationTraceWindowRequest`. These pairs deliberately have corresponding facts but are distinct seam types, preserving `LogicLab.Engine`'s dependency direction; no browser/Application DTO enters Engine as a trusted value.

## 3. Editor Workspace interface

The Application exposes one deep Workspace Module. Every operation can perform authorization or persistence I/O, so the interface is asynchronous even when a particular path completes in memory:

```text
OpenAsync(OpenWorkspaceRequest, CancellationToken)
  -> Task<Opened | OpenRejected>
AttachAsync(AttachRequest, CancellationToken)
  -> Task<Attached | Expired | AttachRejected>
DispatchAsync(WorkspaceCommand, CancellationToken)
  -> Task<WorkspaceOutcome>
ReadAsync(WorkspaceQuery, CancellationToken)
  -> Task<WorkspaceReadOutcome>
```

These are typed C# calls. `WorkspaceCommand` and `WorkspaceOutcome` are closed abstract-record hierarchies; there is no string `kind` plus untyped payload dictionary.

`OpenWorkspaceRequest` is exactly one of:

```text
CreateSandbox { NewProjectSeed without persistent IDs }
OpenDurable { DurableProjectId }
ImportProject { validated ImportCandidate }
CopyWorkspace {
  sourceWorkspaceId, sourceAttachmentId, sourceAttachmentGeneration,
  expectedProjectionVersion,
  saveTarget: Preserve | DetachedSandbox
}
```

The deep Workspace implementation asks Project Editor for Project Genesis when required, resolves a durable current Project Revision when requested, compiles before publication, and returns `Opened { WorkspaceId, ProjectRevisionId, ProjectionVersion }` or `OpenRejected { reason, diagnostics, RetryDisposition }`. A failure allocates no visible Workspace or Durable Project. Browser-supplied Project Revision, owner, or persistent entity IDs are not open inputs.

`CopyWorkspace` reauthorizes and fences the source attachment, then starts separate history at its exact current Project Revision, including authorized unsaved edits. Earlier history is not copied and Undo cannot cross the new base. `Preserve` retains Sandbox status or the Durable Project ID and observed Durable Version; `DetachedSandbox` removes the durable association and implements “Keep as copy.” Both preserve authored Project identity and the fork revision, but copy no attachment, Session, Run, Analysis Operation, Proposal, idempotency record, or browser preference. A stale Projection Version returns `projection_version_precondition_failed`; the source remains unchanged.

`AttachRequest` is `InitialAttach { WorkspaceId, buildFingerprint }` or `Reattach { WorkspaceId, priorAttachmentId, priorGeneration, lastProjectionVersion, buildFingerprint }`. Success returns `Attached { newAttachmentId, newGeneration, WorkspaceProjectionV1 }`; an expired Workspace returns `Expired { reason = workspace_expired }`, and every other failure is `AttachRejected { reason, diagnostics, RetryDisposition }`. Reattach reauthorizes and fences the prior generation before returning. Copying a tab calls `OpenAsync(CopyWorkspace)` and creates another Workspace; it does not reuse an attachment.

Every command context contains:

```text
WorkspaceId
WorkspaceAttachmentId
AttachmentGeneration
ClientIntentId
one command-specific Precondition
typed command payload
```

### 3.1 Closed command and outcome catalog

`WorkspaceCommand` contains exactly these V1 variants:

| Command | Typed payload | Precondition |
|---|---|---|
| `ApplyEdit` | one [Edit Intent](../specs/circuit-authoring.md) | Authoring |
| `Undo`, `Redo` | none | Authoring |
| `SaveDurable` | none | Durable Save |
| `ClaimSandbox` | requested Durable Display Name | Claim |
| `CloseWorkspace` | none | current attachment |
| `RequestCompilation` | entry Circuit Definition ID | Compilation |
| `CreateSession` | `SessionConfigurationV1` and target Compilation Artifact Key | Session Creation |
| `RestartSession` | `SessionConfigurationV1` | Session Mutation |
| `ScheduleStimulusBatch` | one complete future Stimulus Batch | Session Mutation |
| `StepSession` | none | Session Mutation |
| `StartRun` | none | Session Mutation |
| `PauseRun` | none | Run Control |
| `ReplaceProbes` | complete ordered Probe binding requests | Session Mutation |
| `HotSwapSession` | target Compilation Artifact Key | Session Mutation |
| `CloseSession` | none | Session Mutation |
| `StartExplanation` | `RegionSelectionV1`, `TruthTable \| KarnaughMap`, Teaching Profile ID, Analysis Policy ID | Authoring |
| `StartSimplification` | `RegionSelectionV1`, Cost Profile ID, Analysis Policy ID | Authoring |
| `CancelAnalysis` | Operation ID | Operation |
| `AcceptProposal` | Proposal ID | Proposal |
| `PrepareExport` | Project Revision ID | Authoring |

Unknown variants fail before dispatch. Import is deliberately absent: it validates an external carrier and opens a separate Workspace. Selection, viewport, panels, waveform cursor, and Transient Preview are browser/Web state and are not Workspace commands.

`DurableDisplayName` is nonempty NFC Unicode without NUL, isolated surrogate code points, or C0 controls and must satisfy both Workspace Policy scalar-count and UTF-8-byte dimensions. Input is rejected rather than silently trimmed or normalized. It is the immutable V1 catalog label, not the authored Project display name or a filename.

One Probe binding request is `RetainProbe { ProbeId, ElaboratedNetRefV1 }` or `CreateProbe { ElaboratedNetRefV1 }`. The complete list has no duplicate Probe ID or elaborated Net binding. The active Session validates every retained binding and allocates a new Probe ID for each create request; callers never invent a Probe ID. Removing a binding retires its Probe ID. Reordering retains identity.

```text
SessionConfigurationV1
  simulationPolicy: { policyId: StableToken, policyRevision: StableToken }
  tracePolicy: { policyId: StableToken, policyRevision: StableToken }
  initialProbes: ElaboratedNetRefV1[]
```

`initialProbes` is an ordered source-only list and contains no Probe ID. The selected policy snapshots must resolve exactly at admission; Application rejects an unknown or changed revision rather than silently substituting current policy. Creating or restarting a Session allocates one fresh Probe ID per source in the same order. Restart retires every Probe ID from the previous Session; a caller that wants the same sources supplies them again in the new configuration.

`WorkspaceOutcome` is a closed union of the outcome families defined in Sections 4–7 plus these immediate dispatch results:

```text
CompilationAccepted { CompilationGeneration, requested ProjectRevisionId }
Claimed { DurableProjectId, DurableVersion, ProjectRevisionId }
SessionCreated { SessionId, SessionVersion, CompilationArtifactKey, LogicalTime, TraceCursor, ordered ProbeIds[] }
SessionRestarted { previousSessionId, SessionId, SessionVersion, CompilationArtifactKey, LogicalTime, TraceCursor, ordered ProbeIds[] }
SessionClosed { SessionId, ProjectionVersion }
WorkspaceClosed { WorkspaceId }
RunStarted { RunGeneration, SessionVersion }
StimulusScheduled { SessionVersion, LogicalTime, stableSequence }
ProbesReplaced { SessionVersion, ordered ProbeIds[] }
HotSwapCommitted { SessionVersion, CompilationArtifactKey, migrationEvidence }
AnalysisCancellationRequested { OperationId }
AnalysisAlreadyTerminal { OperationId, terminalState }
ProposalApplied { ProposalId, ProjectRevisionId, ProjectionVersion }
ExportPrepared { ProjectRevisionId, ExportTicket, expiresAfterSeconds }
Rejected { reason, diagnostics[], RetryDisposition }
```

Compilation completion is observed through the Workspace Projection after the acceptance response. `CompilationState` is exactly `NotRequested | Queued | Running | Superseded(newerGeneration) | Published(CompilationArtifactKey, diagnostics) | Rejected(reason, diagnostics, RetryDisposition)`; every noninitial state carries its Compilation Generation. Only the newest non-cancelled generation can publish. A family-specific outcome never changes shape based on success data, and no success variant also carries a failure reason. `ExportTicket` is an opaque short-lived locator that Web maps to its download route; it is neither a URL nor authority. Export expiry is a canonical nonnegative whole-second duration measured from outcome publication.

Hot Swap migration evidence contains canonical ordered source identities of migrated state-bearing Component Instances plus preserved and unresolved Probe IDs. A failed swap may return incompatible state identities as safe rejection evidence; it retains the old Session unchanged and never labels an incompatible state as migrated.

`RetryDisposition` is exactly one of:

| Value | Caller action |
|---|---|
| `DoNotRetry` | change input or choose another action |
| `ReplaySameIntent` | resend the byte-identical command with the same Client Intent ID after an uncertain acknowledgment |
| `RefreshProjection` | read the current projection, obtain a new Client Intent ID, and reconsider the action |
| `Reattach` | establish a new attachment generation before issuing a new intent |
| `RetryAfter(seconds)` | wait for the supplied canonical nonnegative whole-second duration, then issue a new intent |

No adapter infers retry behavior from localized text or HTTP status alone.

### 3.2 Closed query catalog and projection

Queries carry authenticated Workspace and attachment context but no Client Intent ID and cause no state transition:

```text
WorkspaceQuery =
  ReadProjection { afterProjectionVersion? }
  | ReadAnalysisOperation { OperationId }
  | ReadProposal { ProposalId }
  | ReadTraceWindow { TraceWindowRequest }

WorkspaceReadOutcome =
  ProjectionSnapshot { WorkspaceProjectionV1 }
  | ProjectionUnchanged { ProjectionVersion }
  | AnalysisOperationRead { AnalysisOperationState }
  | ProposalRead { Available | Applied | Stale | Expired }
  | TraceWindowRead { TraceWindowOutcome }
  | ReadRejected { reason, diagnostics, RetryDisposition }
```

`WorkspaceProjectionV1` is one atomic owned snapshot containing Projection Version, the current immutable Project Revision and authorized Project Document, Transaction History availability, sandbox/durable save state, Compilation generation/status and Artifact Key when published, Session summary and ordered Probes when present, Analysis Operation and Proposal summaries, and ordered Diagnostics. It contains no Web connection state, Geometry Plan, browser scene patch, runtime ordinal, Trace storage chunk, EF entity, or localized message. Projection Version increments whenever any included observable fact changes. Web derives connection presentation from circuit and attachment outcomes and switches Browser Runtime between commit-enabled and local-only interaction; transient reconnection never creates a Workspace Projection version.

`ReadProjection` returns `ProjectionUnchanged` only when the supplied version is current; otherwise it returns a complete snapshot. V1 does not expose a generic field-mask query or semantic patch format. The Web projection coordinator derives browser-specific scene and waveform messages from this snapshot plus Diagram Presentation and Trace reads.

The snapshot is an in-process composition of owned immutable values, not a serialized browser payload. Projection Version is an atomic publication fence, not a cache-invalidation command.

### 3.3 Preconditions

| Variant | Applies to | Fields |
|---|---|---|
| Authoring | edit, Undo, Redo, analysis start, export preparation | base Project Revision ID |
| Durable Save | save | Project Revision ID plus expected Durable Version |
| Claim | claim Sandbox Project | Project Revision ID and assertion that no Durable Project exists |
| Compilation | compile | Project Revision ID, entry Circuit Definition ID, Library Snapshot |
| Session Creation | create Session | target Compilation Artifact Key and assertion that no Session exists |
| Session Mutation | step, stimulus, Start Run, probes, Hot Swap, restart, close | Session ID, expected Session Version, Compilation Artifact Key |
| Run Control | pause an active run | Session ID and Run Generation |
| Operation | cancel analysis | Operation ID and optional expected terminal/running state |
| Proposal | accept simplification | Proposal ID, source Project Revision ID, region digest |

`Pause` does not require the rapidly changing Session Version. `StartRun` creates a monotonic Run Generation; Pause targets that generation and becomes effective at the next committed or rolled-back boundary. A delayed Pause cannot stop a later run generation.

`CreateSession` allocates its Session ID and version on success. `ClaimSandbox` allocates its Durable Project ID and initial Durable Version on success. These are closed absence preconditions, not null sentinels.

### 3.4 Validation order

1. authenticate;
2. authorize the Workspace and referenced resource without disclosing unauthorized existence;
3. validate bounded shape and current build fingerprint at the browser seam;
4. validate current attachment and generation;
5. look up `(WorkspaceId, AttachmentGeneration, ClientIntentId)` and compare canonical intent identity;
6. validate the typed payload and its precondition;
7. execute and publish atomically;
8. record the terminal idempotency result before acknowledging.

The Workspace mutation and idempotency result share one in-memory commit section. A Durable Save stores its idempotency record and current-revision pointer in one repository transaction. If a retained idempotency record cannot establish whether an old intent executed, return `IdempotencyWindowExpired`; never execute a possible duplicate.

Reusing a Client Intent ID for a different typed command returns `IdempotencyKeyConflict`. A new attachment generation starts a new scope and does not replay unacknowledged old commands automatically.

## 4. Authoring outcomes

Project Editor behavior is defined by [Circuit Authoring](../specs/circuit-authoring.md). Workspace authoring commands include structure, explicit connectivity, parameters, Wire Geometry, and Transaction History. Import creates a separate Project Genesis rather than masquerading as an edit to the current Project. Pointer movement and other Transient Preview data never enter the Workspace interface.

```text
AuthoringCommitted
  ProjectRevisionId
  ProjectionVersion
  changedSources: AuthoredSourceRefV1[]
  removedSources: AuthoredSourceRefV1[]
  TransactionHistory availability
  save and Compilation status
  diagnostics[]

AuthoringRejected
  reason
  safe current ProjectRevisionId and ProjectionVersion when authorized
  diagnostics[]
  RetryDisposition
```

Workspace returns semantic changed identities, not Canvas/SVG patches. Web requests the resulting complete Schematic Projection from Diagram Presentation, diffs typed items by scoped source reference, and composes selection, diagnostics, probe, and live-value overlays.

Undo and Redo move the history cursor to an existing Project Revision. A new edit after Undo truncates the Redo branch. History retention truncation can accompany a successful commit; it never turns a valid edit into failure.

Save changes only the Durable Project current-revision pointer under optimistic concurrency:

```text
Saved { DurableVersion, ProjectRevisionId }
Conflict { expectedDurableVersion, actualDurableVersion, recovery: Reload | Copy | Export }
Rejected { reason, diagnostics, RetryDisposition }
```

No silent merge or overwrite occurs.

## 5. Compilation and Session outcomes

Compilation is requested through Workspace and executed through the typed Compiler Module. A successful result binds the complete Compilation Artifact Key. Expected circuit errors are deterministic diagnostics; no partially executable artifact is published.

V1 Session commands are:

- Create or Restart Session;
- schedule one future Stimulus Batch;
- Step one Logical-time Advance;
- Start Run and Pause Run;
- replace the Probe set;
- request one atomic Hot Swap;
- close Session.

Callers cannot enqueue Delta Steps, resolve Nets, commit state, drive Hot Swap phases, or choose an SCC algorithm.

```text
AdvanceCommitted
  SessionVersion
  LogicalTime
  observed Probe patch
  diagnostics[]
  Trace cursor

NoScheduledStimulus { SessionVersion, LogicalTime }
Paused { RunGeneration, SessionVersion, LogicalTime, reason }
AdvanceFailed { unchanged SessionVersion, unchanged LogicalTime, reason, policyEvidence }
```

Pause reason is exactly `UserRequested | NoScheduledStimulus | Detached | SupersededRun`. `policyEvidence` is required only for `simulation_resource_limit` and absent for every other reason; it contains policy ID/revision, dimension, and observed work, never fleet capacity.

Indeterminate Feedback can be committed with diagnostics because the Least Information Fixed Point is a valid Quiescent Boundary. Zero-time Oscillation, Resource Limit, cancellation, and infrastructure failure publish no working state or Trace. They remain distinct reasons.

Each committed Session mutation increments Session Version. Run observations are monotonically sequenced but may be duplicated or skipped across disconnect, reconnect, or bounded backpressure. A consumer detects a gap by sequence and refreshes the authorized projection and Trace cursor.

## 6. Analysis Operation and proposal

```text
StartExplanation | StartSimplification -> Accepted(OperationId) | Rejected

AnalysisOperationState
  Queued | Running | Completed(result) | Cancelled | Expired
  | Failed(reason, diagnostics, RetryDisposition)

AnalysisResult
  Explanation | VerifiedImprovement(ProposalId) | NoImprovement
  | NotApplicable(AnalysisNotApplicableV1) | Inconclusive

AnalysisNotApplicableV1 =
  BooleanRegionIneligible { reason, sourceLocations }
  | TeachingProjectionUnavailable { projection, reason }
```

`BooleanRegionIneligible.reason` is the closed Compiler-owned value in [Compiler](../specs/compiler.md#6-boolean-region-extraction). `TeachingProjectionUnavailable.projection` is `TruthTable | KarnaughMap`; its reason is `dimensionUnsupported | teachingProfileUnsupported`. Localized text is never an eligibility reason.

Web selects a named teaching or Cost Profile, never an algorithm or raw threshold. Disconnecting a circuit stops observation but does not cancel accepted work. Observe, cancel, read result, and accept proposal reauthorize every time.

A proposal can commit only when its source Project Revision and region digest remain current, proof evidence is complete, replacement recompilation succeeds, cost is still a strict improvement, and one Edit Transaction applies atomically. Otherwise the Project Document remains unchanged.

## 7. Trace transfer

Runtime chunks are storage implementation. The Workspace interface exposes stable normalized records, and the Browser Adapter Contract consumes them:

```text
TraceWindowRequest
  SessionId, CompilationArtifactKey, nonempty unique ordered ProbeIds[]
  half-open logical-time range [start, end), with start < end
  representation: Transitions | VisualSummary(maxPoints, "logic-envelope-v1")
  afterSequence?

TraceWindowOutcome
  TransitionsAvailable { transitions[], coveredRange, earliestAvailable, latestSequence }
  SummaryAvailable { buckets[], aggregation, coveredRange, earliestAvailable, latestSequence }
  RangeUnavailable { Evicted | ArtifactChanged, earliestAvailable, latestSequence }
```

Each transition carries Probe identity, unsigned-decimal-string Logical Time and sequence, and `LogicVectorTransferV1`:

```text
width: positive u32
encoding: "logic4-2bit-v1"
data: bytes where each two-bit field is 00=0, 01=1, 10=X, 11=Z
```

`maxPoints` is a positive u32. For requested span `L = end - start`, `n = min(maxPoints, L)`, and bucket `i` covers `[start + floor(iL/n), start + floor((i+1)L/n))`; implementations use checked quotient/remainder arithmetic rather than overflowing multiplication. Fields are packed least-significant bit index first, data length is exactly `ceil(width * 2 / 8)`, and unused final fields are zero. The Workspace returns exactly the requested representation or a structured inability reason; it never silently downsamples transitions.

A visual-summary bucket declares first/last value, whether any transition occurred, and whether values were mixed or unavailable. It cannot invent a flat waveform through a Trace Gap.

## 8. Security and evolution

- Authorize every Workspace, Session, Operation, Proposal, and Durable Project action independently.
- Treat attachment, precondition, idempotency, and authorization failures as distinct closed outcomes.
- Avoid logging project payloads, Trace values, tokens, Session IDs, or full commands.
- Add a new Workspace interface variant only when one user intention cannot be expressed by the closed catalog.
