# Browser Adapter Contract

> Status: normative V1 browser seam contract
> Deployment: collocated ECMAScript Scene and Waveform adapters

This contract owns the records exchanged between Web and the two browser adapters. [Browser Runtime](../specs/browser-runtime.md) owns their implementation behavior; [Editor Workspace Contract](./editor-workspace.md) owns Workspace commands, queries, identity, and Trace reads. Browser records never become Domain values.

| Value | Meaning | Not |
|---|---|---|
| `buildFingerprint` | one atomically deployed server/browser build | authorization, protocol negotiation |
| `SceneVersion` | one Scene snapshot/patch cursor | Projection Version |
| `WaveformVersion` | one Waveform snapshot/patch cursor | Trace sequence, Session Version |
| `SceneOverlayId` | one projection-local overlay locator | authored identity, authorization |

## 1. Scene interface

The collocated scene ES Module and Web exchange batched messages tagged with the same application build fingerprint. Browser DTOs are closed records for one deployed build, not a public versioned protocol. A complete scene state is exactly `SceneSnapshotV1` or `SceneUnavailableV1`:

```text
SceneSnapshotV1
  buildFingerprint
  SceneVersion
  ProjectionVersion
  circuitDefinitionId
  uiCulture: en-US | zh-CN
  baseDirection: LeftToRight
  schematicProjectionKey: SchematicProjectionKeyV1
  bounds: RectV1
  gridStepPlanUnits: positive integer
  snapStepGridUnits: positive integer
  ordered SchematicItemV1 values from SchematicProjectionV1
  ordered SceneOverlayV1 values

SceneUnavailableV1
  buildFingerprint
  SceneVersion
  ProjectionVersion
  circuitDefinitionId
  uiCulture: en-US | zh-CN
  baseDirection: LeftToRight
  Presentation diagnostics

ScenePatchV1
  buildFingerprint
  base SceneVersion
  next SceneVersion
  ProjectionVersion
  circuitDefinitionId
  uiCulture: en-US | zh-CN
  baseDirection: LeftToRight
  schematicProjectionKey: SchematicProjectionKeyV1
  bounds: RectV1
  gridStepPlanUnits: positive integer
  snapStepGridUnits: positive integer
  SchematicItemV1 upserts and AuthoredSourceRefV1 removals
  SceneOverlayV1 upserts and SceneOverlayId removals
```

Diagram Presentation rejection produces `SceneUnavailableV1`, never a partial snapshot or a patch over stale geometry. The browser discards the prior static scene, retains only safe local viewport preferences, and exposes Diagnostics plus Razor-owned recovery commands in place of the Canvas. A later successful projection resumes with a complete snapshot.

`uiCulture` and `baseDirection` are the resolved Web Host values and are present even when Diagram Presentation is unavailable. `SchematicProjectionKeyV1`, `bounds`, `gridStepPlanUnits`, `snapStepGridUnits`, and every `SchematicItemV1` placement, static drawing, and hit value come from the closed [Schematic Projection contract](../specs/diagram-presentation.md#41-schematic-projection). A patch repeats the complete new culture, direction, projection key, bounds, and grid/snap conversion; they take effect atomically with its item changes. The browser keys each item with `AuthoredSourceRefV1`, preserving the projection's Circuit Definition scope; it never reconstructs omitted placement transforms, scene bounds, grid scale, snap interval, culture, direction, or static annotations from pixels or a Geometry Plan.

`SceneOverlayV1` is this closed union, and every variant has one `SceneOverlayId`:

```text
LiveNetValue { ElaboratedNetRefV1, SessionId, SessionVersion, LogicVectorTransferV1 }
Selection { AuthoredSourceRefV1, role: Primary | Member }
ProbeAnchor { ProbeId, ElaboratedNetRefV1, point: PointV1, appearanceOrdinal: u32 }
DiagnosticMarker { AuthoredSourceRefV1, DiagnosticCode, severity, diagnosticOrdinal: u32 }
```

The Probe point must equal the current Schematic Projection's available `NetTopology.probeAnchor`. When a valid bound Net has no visible geometry, the scene emits no Probe anchor and the row reports only `sceneNavigation = Unavailable(noVisibleGeometry)`; its binding, values, and Trace remain resolved. Transient Preview, hover, route handles, menus, and pointer samples remain browser-local and are not overlay variants. Every collection has stable scoped-source order with its declared ordinal as the final tie-break. A patch is valid only when its build fingerprint, Circuit Definition ID, UI culture, base direction, and `base SceneVersion` match the browser's current available snapshot and its complete projection values validate. Switching definitions and recovery from unavailable state always use a complete snapshot. Otherwise the browser discards the complete patch and requests a snapshot; it never applies a prefix. Canvas consumption and coordinate conversion follow [Browser Runtime](../specs/browser-runtime.md).

`SceneIntentV1` is this closed JavaScript-to-Web union:

| Kind | Required payload | Web action |
|---|---|---|
| `SelectSources` | ordered `AuthoredSourceRefV1` values and selection mode | update Web selection only |
| `PlaceComponent` | target Circuit Definition ID, exact target, complete parameter authoring values, and final placement | one `PlaceComponentInstance` or `PlaceComponentWithNewMemoryImage` Edit Intent |
| `MoveComponents` | nonempty scoped Component Instance references and final placements | one `MoveComponentInstances` Edit Intent |
| `MoveDefinitionPorts` | nonempty scoped definition-Port references and final placements | one `MoveDefinitionPorts` Edit Intent |
| `MoveAnnotations` | nonempty scoped Annotation references and final positions | one `MoveAnnotations` Edit Intent |
| `CommitWire` | scoped `AuthoredTerminalRefV1` endpoints, scoped destination Net if any, explicit Junction requests, and final route values | one `ConnectTerminals` Edit Intent |
| `AddJunction` | scoped Net reference, final point, and complete route additions/replacements/removals | one `AddJunction` Edit Intent |
| `RemoveJunction` | scoped Junction reference, explicit resulting partitions, and complete route additions/replacements/removals | one `RemoveJunction` Edit Intent |
| `SetWireRoute` | scoped Wire Geometry reference and final routed/unrouted value | one `SetWireGeometry` Edit Intent |
| `ToggleProbe` | one `ElaboratedNetRefV1` | one complete `ReplaceProbes` command using retain/create binding requests |

Every intent carries the build fingerprint, Scene Version, Projection Version, and Circuit Definition ID from which it began. Every scoped authored reference must name that same Circuit Definition. Coordinate-bearing placement, move, route, and Junction intents additionally carry only their final checked 32-bit grid coordinates and the closed modifier `None | DisableSnap`; `SelectSources` and `ToggleProbe` carry neither gesture coordinates nor a snap modifier. `SelectSources` additionally carries selection mode `Replace | Add | Toggle`. Pan/zoom, hover, marquee preview, route preview, pointer samples, and cancelled gestures remain local and emit no intent.

A `PlaceComponent` parameter value is either one complete persisted parameter value or `newMemoryImage { displayName, width, depth, words }`, whose `words` value is complete. The latter is valid only once, for a library memory-image parameter, and translates to the atomic convenience intent above; Web never first publishes an unbound Memory Image.

Web rejects a stale version, missing source, invalid coordinate, unknown modifier, or intent that cannot translate to exactly one typed Workspace command. It discards the Transient Preview and refreshes the Scene Snapshot; it never guesses a replacement source, splits one gesture into independently committed edits, or forwards the browser record as a Domain patch. An unknown kind or build mismatch requires a hard reload.

## 2. Waveform interface

The collocated waveform ES Module uses a separate cursor because browser-local viewport changes and Trace appends do not necessarily change the Workspace Projection:

```text
WaveformSnapshotV1
  buildFingerprint, WaveformVersion, ProjectionVersion
  SessionId, SessionVersion, CompilationArtifactKey
  uiCulture: en-US | zh-CN, baseDirection: LeftToRight
  ordered WaveformRowV1 values
  WaveformViewStateV1
  WaveformTraceV1 for the requested viewport

WaveformPatchV1
  buildFingerprint, base WaveformVersion, next WaveformVersion
  ProjectionVersion, SessionId, SessionVersion, CompilationArtifactKey
  uiCulture: en-US | zh-CN, baseDirection: LeftToRight
  latest sequence
  WaveformRowV1 upserts and ProbeId removals
  typed transition appends, summary replacements, and TraceGapV1 replacements
```

The browser values are closed:

```text
WaveformRowV1
  ProbeId, ElaboratedNetRefV1, positive width, displayOrdinal
  radix: binary | hex | unsigned
  appearanceOrdinal: u32
  binding: Resolved | Unresolved(sourceMissing | artifactIncompatible)
  sceneNavigation: Available | Unavailable(noVisibleGeometry | sourceMissing | projectionUnavailable)

WaveformViewStateV1
  nonempty half-open viewport
  Primary cursor?, Secondary cursor?
  liveFollow: Boolean

WaveformTraceV1 =
  TransitionsView { ordered TransitionSegmentV1[], ordered TraceGapV1[], latestSequence }
  | SummaryView { aggregation = logic-envelope-v1, ordered SummarySegmentV1[], ordered TraceGapV1[], latestSequence }
  | UnavailableView { TraceGapV1, earliestAvailable, latestSequence }

TraceGapV1 { nonempty half-open range, reason: Evicted | ArtifactChanged }
```

Segments are nonempty, nonoverlapping, ordered, use the [Editor Workspace Contract §7](./editor-workspace.md#7-trace-transfer) transition or bucket records, and together with gaps cover exactly the displayed portion of the requested viewport. A patch cannot append transitions to `SummaryView`, silently replace transitions with buckets, or bridge a gap.

`WaveformIntentV1` is closed:

| Kind | Payload |
|---|---|
| `SetViewport` | nonempty logical-time range |
| `SetCursor` | cursor `Primary \| Secondary` and Logical Time, or explicit removal |
| `SetLiveFollow` | Boolean enabled value |
| `SetProbeOrder` | complete ordered Probe IDs |
| `SetProbeRadix` | Probe ID and `binary \| hex \| unsigned` |
| `RequestTraceWindow` | one `TraceWindowRequest` |
| `RevealNet` | Probe ID |
| `CloseWaveform` | none |

Viewport, cursor, live-follow, radix, and close state remain Web/browser preferences. Probe order translates to one complete `ReplaceProbes` command. A trace request translates to `ReadTraceWindow`. Reveal Net changes Web selection and requests the matching Scene snapshot/patch only when `sceneNavigation` is `Available`; otherwise it preserves the waveform and presents the exact unavailable reason plus the applicable remove/rebind/retry action. An unresolved runtime binding may still be navigable when its authored Net remains present.

Every Waveform intent carries the build fingerprint, Waveform Version, Projection Version, Session ID, Session Version, and Compilation Artifact Key from the snapshot or patch on which it acts. `RequestTraceWindow` must repeat the same Session ID and Artifact Key inside its request. Web rejects a mismatched envelope, refreshes the snapshot/window, and never applies a delayed complete Probe order to a newer Session version.

A patch applies only to its exact build, base Waveform Version, Session ID, Compilation Artifact Key, UI culture, and base direction; its Projection and Session Versions cannot regress. A Trace sequence gap, artifact, culture, direction, or version mismatch discards the patch and requests an authorized snapshot/window. Transitions are never silently summarized; summary data appears only after an explicit `VisualSummary` request. A Trace Gap is a first-class replacement range and cannot be bridged by an appended flat value. Runtime chunk layout and browser rendering buffers never cross this seam.

## 3. Security and evolution

- Validate every browser record under separate shape, size, build, and version checks.
- Never log full messages, project payloads, Trace values, tokens, or Session IDs.
- Deploy server and browser assets atomically and force reload on build mismatch.
- Introduce a versioned public or Worker protocol only when a second independently deployed adapter is real, then prove parity through contract tests.
