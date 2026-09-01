# Browser Adapter Contract

> Status: normative V1 browser seam contract
> Deployment: collocated ECMAScript Scene and Waveform adapters

This contract owns the records exchanged between Web and the two browser adapters. [Browser Runtime](../specs/browser-runtime.md) owns their implementation behavior; [Editor Workspace Contract](./editor-workspace.md) owns Workspace commands, queries, identity, and Trace reads. Browser records never become Domain values.

| Value              | Meaning                                      | Not                                 |
| ------------------ | -------------------------------------------- | ----------------------------------- |
| `buildFingerprint` | one atomically deployed server/browser build | authorization, protocol negotiation |
| `SceneVersion`     | one Scene snapshot/patch cursor              | Projection Version                  |
| `WaveformVersion`  | one complete Waveform snapshot cursor        | Trace sequence, Session Version     |
| `SceneOverlayId`   | one projection-local overlay locator         | authored identity, authorization    |

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
ProbeAnchor { ProbeId, ElaboratedNetRefV1, point: PointV1, appearanceOrdinal: u32, pattern }
DiagnosticMarker { AuthoredSourceRefV1, DiagnosticCode, severity, diagnosticOrdinal: u32 }
```

The Probe point must equal the current Schematic Projection's available `NetTopology.probeAnchor`. Its appearance ordinal and `solid | dash | dot | dashDot` pattern are derived from Probe identity and must match the tuple used by the Probe Spine and waveform row. When a valid bound Net has no visible geometry, the scene emits no Probe anchor and the row reports only `sceneNavigation = Unavailable(noVisibleGeometry)`; its binding, values, and Trace remain resolved. Transient Preview, hover, route handles, menus, and pointer samples remain browser-local and are not overlay variants. Every collection has stable scoped-source order; an ordinal is present only where it resolves an otherwise equal diagnostic or presentation order. A patch is valid only when its build fingerprint, Circuit Definition ID, UI culture, base direction, and `base SceneVersion` match the browser's current available snapshot and its complete projection values validate. Switching definitions and recovery from unavailable state always use a complete snapshot. Otherwise the browser discards the complete patch and requests a snapshot; it never applies a prefix. Canvas consumption and coordinate conversion follow [Browser Runtime](../specs/browser-runtime.md).

`SceneIntentV1` is this closed JavaScript-to-Web union:

| Kind                  | Required payload                                                                                                            | Web action                                                                     |
| --------------------- | --------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| `SelectSources`       | ordered `AuthoredSourceRefV1` values and selection mode                                                                     | update Web selection only                                                      |
| `PlaceComponent`      | target Circuit Definition ID, exact target, complete parameter authoring values, and final placement                        | one `PlaceComponentInstance` or `PlaceComponentWithNewMemoryImage` Edit Intent |
| `MoveComponents`      | nonempty scoped Component Instance references and final placements                                                          | one `MoveComponentInstances` Edit Intent                                       |
| `MoveDefinitionPorts` | nonempty scoped definition-Port references and final placements                                                             | one `MoveDefinitionPorts` Edit Intent                                          |
| `MoveAnnotations`     | nonempty scoped Annotation references and final positions                                                                   | one `MoveAnnotations` Edit Intent                                              |
| `CommitWire`          | scoped `AuthoredTerminalRefV1` endpoints, scoped destination Net if any, explicit Junction requests, and final route values | one `ConnectTerminals` Edit Intent                                             |
| `AddJunction`         | scoped Net reference, final point, and complete route additions/replacements/removals                                       | one `AddJunction` Edit Intent                                                  |
| `RemoveJunction`      | scoped Junction reference, explicit resulting partitions, and complete route additions/replacements/removals                | one `RemoveJunction` Edit Intent                                               |
| `SetWireRoute`        | scoped Wire Geometry reference and final routed/unrouted value                                                              | one `SetWireGeometry` Edit Intent                                              |
| `ToggleProbe`         | one `ElaboratedNetRefV1`                                                                                                    | one complete `ReplaceProbes` command using retain/create binding requests      |

Every intent carries the build fingerprint, Scene Version, Projection Version, and Circuit Definition ID from which it began. Every scoped authored reference must name that same Circuit Definition. Coordinate-bearing placement, move, route, and Junction intents additionally carry only their final checked 32-bit grid coordinates and the closed modifier `None | DisableSnap`; `SelectSources` and `ToggleProbe` carry neither gesture coordinates nor a snap modifier. `SelectSources` additionally carries selection mode `Replace | Add | Toggle`. Pan/zoom, hover, marquee preview, route preview, pointer samples, and cancelled gestures remain local and emit no intent.

A `PlaceComponent` parameter value is either one complete persisted parameter value or `newMemoryImage { displayName, width, depth, words }`, whose `words` value is complete. The latter is valid only once, for a library memory-image parameter, and translates to the atomic convenience intent above; Web never first publishes an unbound Memory Image.

Web rejects a stale version, missing source, invalid coordinate, unknown modifier, or intent that cannot translate to exactly one typed Workspace command. It discards the Transient Preview and refreshes the Scene Snapshot; it never guesses a replacement source, splits one gesture into independently committed edits, or forwards the browser record as a Domain patch. An unknown kind or build mismatch requires a hard reload.

## 2. Waveform interface

The collocated waveform ES Module uses a separate cursor because browser-local viewport changes and Trace appends do not necessarily change the Workspace Projection:

```text
WaveformSnapshotV1
  buildFingerprint, WaveformVersion, ProjectionVersion
  SessionId, SessionVersion, CompilationArtifactKey
  ordered WaveformRowV1 values
  WaveformViewStateV1
  WaveformTraceV1 for the requested viewport

```

The browser values are closed:

```text
WaveformRowV1
  ProbeId, positive width, displayOrdinal
  appearanceOrdinal: u32
  pattern: solid | dash | dot | dashDot
  binding: Resolved | Unresolved

WaveformViewStateV1
  nonempty half-open viewport
  Primary cursor?, Secondary cursor?

WaveformTraceV1 =
  TransitionsView { ordered TransitionSegmentV1[] }
  | SummaryView { aggregation = logic-envelope-v1, ordered SummarySegmentV1[] }
  | UnavailableView { TraceGapV1 }

TraceGapV1 { nonempty half-open range }
```

Waveform range boundaries are canonical unsigned decimal strings in `0..2^64`;
`2^64` is valid only as an exclusive end. Cursor values remain Logical Times in
`0..2^64 - 1`.

Available segments are nonempty, nonoverlapping, grouped in Probe display order, use the [Editor Workspace Contract §6](./editor-workspace.md#6-trace-transfer) transition or bucket records, and exactly cover the requested viewport for every resolved Probe. An unavailable view carries one explicit Trace Gap covering the viewport.

The browser row is intentionally minimal: Canvas needs only identity, width, order, appearance, and binding. Razor owns the label, radix, current value, authored Net reference, binding reason, and Scene-navigation state. Those values never cross the JavaScript seam.

`WaveformIntentV1` is closed:

| Kind          | Payload                                                             |
| ------------- | ------------------------------------------------------------------- |
| `SetViewport` | nonempty logical-time range                                         |
| `SetCursor`   | cursor `Primary \| Secondary` and Logical Time, or explicit removal |

Only Canvas-owned pointer, wheel, and keyboard interactions cross this intent seam. Fluent/Razor controls call their typed component handlers directly; they do not serialize their actions through JavaScript. Viewport, cursor, live-follow, radix, and close state remain Web/browser preferences. Probe order translates to one complete `ReplaceProbes` command. Web owns Trace reads: a viewport or summary-mode change causes Web to issue the bounded `ReadTraceWindow` request represented by the controls, rather than accepting an arbitrary read request from JavaScript. Reveal Net changes Web selection and requests the matching Scene snapshot/patch only when `sceneNavigation` is `Available`; otherwise it preserves the waveform and presents the exact unavailable reason plus the applicable remove/rebind/retry action. An unresolved runtime binding may still be navigable when its authored Net remains present.

Every Waveform intent carries the build fingerprint, Waveform Version, Projection Version, Session ID, Session Version, and Compilation Artifact Key from the snapshot on which it acts. Web rejects a mismatched envelope, refreshes the snapshot/window, and never applies a delayed complete Probe order to a newer Session version.

V1 publishes complete Waveform snapshots only. There is no incremental Waveform patch producer, so the adapter does not expose a speculative patch protocol. A Trace sequence gap, artifact, or version mismatch requests a fresh authorized snapshot/window. Transitions are never silently summarized; summary data appears only after the user selects visual summary. A Trace Gap is a first-class range and cannot be bridged by an invented flat value. Runtime chunk layout and browser rendering buffers never cross this seam.

## 3. Security and evolution

- Validate every browser record under separate shape, size, build, and version checks.
- Never log full messages, project payloads, Trace values, tokens, or Session IDs.
- Deploy server and browser assets atomically and force reload on build mismatch.
- Introduce a versioned public or Worker protocol only when a second independently deployed adapter is real, then prove parity through contract tests.
