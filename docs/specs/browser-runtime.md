# Browser Runtime

> Status: normative V1 Canvas, waveform, JavaScript interop, and browser-lifecycle contract
> Runtime: collocated ECMAScript modules inside the Interactive Server editor

Browser Runtime turns complete renderer-neutral Scene and Waveform values into responsive browser interaction. It owns frame-rate state and pixels, not circuit meaning. [Product](../product.md) owns appearance and workflows; [Diagram Presentation](./diagram-presentation.md) owns static geometry; the [Browser Adapter Contract](../contracts/browser-adapters.md) owns exchanged records; [Web Host](./web-host.md) owns render modes, circuits, security middleware, and culture selection.

## 1. Deep browser modules

Web exposes two narrow typed adapter interfaces over collocated modules:

```text
SceneAdapter
  MountAsync(SceneHostElement, buildFingerprint, SceneIntentSink, CancellationToken)
    -> Task<SceneHandle>

SceneHandle : IAsyncDisposable
  ReplaceAsync(SceneSnapshotV1 | SceneUnavailableV1, CancellationToken) -> Task
  ApplyAsync(ScenePatchV1, CancellationToken) -> Task
  SetInteractionModeAsync(CommitEnabled | LocalOnly, CancellationToken) -> Task

WaveformAdapter
  MountAsync(WaveformHostElement, buildFingerprint, WaveformIntentSink, CancellationToken)
    -> Task<WaveformHandle>

WaveformHandle : IAsyncDisposable
  ReplaceAsync(WaveformSnapshotV1, CancellationToken) -> Task
  SetInteractionModeAsync(CommitEnabled | LocalOnly, CancellationToken) -> Task
```

These are semantic interfaces, not requirements to publish TypeScript declarations or one interop call per method. The C# adapters hide JavaScript module import, batching, serialization, object references, retry, and teardown. Callers never drive a paint pass, manipulate a spatial index, or send pointer samples.

`CommitEnabled` permits a new gesture only while Web has a current attachment and exact published versions. Entering `LocalOnly` cancels any commit-capable gesture without an intent and retains pan, zoom, viewport, waveform cursor, and inspection. The module doesn't infer connection or authorization from elapsed time.

Interactive Server modules also consume the framework's
`components-reconnect-state-changed` event on `#components-reconnect-modal`.
`show`, `paused`, `retrying`, `failed`, and `rejected` enter `LocalOnly`; `hide`
returns to `CommitEnabled`. This browser-local transition remains available while
.NET interop is disconnected and follows the
[ASP.NET Core reconnect-state contract](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/signalr?view=aspnetcore-10.0#reflect-the-server-side-connection-state-in-the-ui).

JavaScript returns exactly one closed Scene or Waveform intent for a committed browser action. The sink validates the build and version envelope before translating it into Web state or one Workspace command. Interop is asynchronous because Interactive Server calls cross the circuit; related work is batched rather than split into fine-grained calls ([Blazor interop performance](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/javascript-interoperability?view=aspnetcore-10.0#avoid-excessively-fine-grained-calls)).

V1 uses modern ECMAScript modules in collocated `.razor.js` files. There is no global function surface, inline event handler, general browser message bus, rendering framework, scene library, or npm runtime dependency.

## 2. Host DOM and ownership

`CircuitSceneHost` renders one Web-owned host containing:

```text
scene host
├── canvas bitmap and focus surface
└── Razor-owned status and recovery surface
```

Razor creates and updates the host, Canvas element, status, recovery actions, dialogs, and HTML editors. JavaScript receives only the host `ElementReference`, obtains the Canvas context, and owns bitmap drawing plus listeners attached to its own host. It never adds, removes, reparents, or edits Blazor-owned DOM. Blazor explicitly warns that external DOM mutation can invalidate its internal representation ([DOM interaction](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0#interaction-with-the-dom)).

Menus, tooltips, property forms, label editors, confirmation, and text input are HTML/Razor surfaces. Canvas contains no editable text field or hidden authority. `WaveformHost` follows the same ownership rule with its own Canvas and Razor-owned controls.

`MountAsync` occurs once after an interactive first render with a valid `ElementReference`. Mounting is idempotent by host and build fingerprint: repeating it returns the existing live handle or destroys an incompatible stale instance before replacement. Prerender never imports or mounts a module.

## 3. Published browser state

Each module maintains one published immutable semantic state and separate transient browser state:

| Published                                                                            | Transient and browser-local                                                                                      |
| ------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------- |
| exact published versions, static display list, overlays, waveform rows/segments/gaps | viewport, hover, focus ring, active tool, captured pointer, preview, marquee, local cursor motion, pending frame |

The [Browser Adapter Contract](../contracts/browser-adapters.md) solely defines valid published values and preconditions. `ReplaceAsync` validates a complete candidate, then swaps published state atomically; `SceneUnavailableV1` is a complete replacement that clears drawable static state. The Scene adapter alone supports exact-base patches because it has a real patch producer; Waveform uses complete replacements.

The C# adapter may divide a large semantic replacement into bounded private interop batches. JavaScript assembles them under an unguessable transfer identity and publishes only after a terminal commit validates counts and digest. Batch mechanics are adapter implementation, never a second Scene contract; interruption discards the candidate and leaves the prior published state.

## 4. Coordinate spaces and transforms

The scene uses four explicit spaces:

```text
authored grid integers
  -> Diagram Presentation plan units
      -> browser world coordinates
          -> CSS viewport pixels
              -> device bitmap pixels
```

- Project Editor receives only final authored signed 32-bit grid coordinates.
- Schematic Projection supplies all static coordinates in checked integer plan units, its complete plan-space bounds, a positive `gridStepPlanUnits`, and a positive default `snapStepGridUnits`.
- JavaScript converts plan units to finite double world coordinates. Its viewport is one invertible affine transform containing translation and uniform positive zoom; rotation and skew are not browser viewport operations.
- Pointer `clientX/clientY` values are converted relative to the Canvas content box, then through the inverse viewport transform. Device pixels never participate in hit identity or snapping.
- Coordinate commit divides by `gridStepPlanUnits` and rounds to the nearest authored integer grid unit, with exact halves toward negative infinity. Normal snapping then rounds to the nearest multiple of `snapStepGridUnits` with the same tie rule; `DisableSnap` retains the first integer result. Every result is checked to signed 32-bit range. A route preview may remain sub-grid until commit.
- Panning and zooming change only the viewport. They never mutate Schematic items, authored coordinates, hit regions, or the Project Document.

Fit and reveal use the supplied projection and Hit Region bounds. A browser never reconstructs bounds from painted pixels. World-to-screen and screen-to-world functions are pure and receive property tests for round trip, negative coordinates, extreme legal values, zoom limits, and content-box offsets.

## 5. Canvas sizing and display density

Canvas CSS size and bitmap size are different. V1 uses the host content box as CSS size and computes a policy-bounded effective density from `window.devicePixelRatio`. Page zoom or moving the window to another display can change the ratio; `ResizeObserver` detects host-size changes, while a resolution `matchMedia` listener is re-armed after each density change. Both feed the same coalesced resize path; no polling or idle animation loop observes density ([device pixel ratio](https://developer.mozilla.org/en-US/docs/Web/API/Window/devicePixelRatio), [ResizeObserver](https://developer.mozilla.org/en-US/docs/Web/API/ResizeObserver)).

For one effective resize:

1. read one coherent content-box width and height;
2. reject nonfinite or nonpositive values and suspend paint while the host has no area;
3. choose the Browser Policy-bounded effective density;
4. compute each checked positive bitmap dimension as `ceil(CSS dimension * effective density)` and enforce bitmap-pixel/byte policy before allocation;
5. assign Canvas `width` and `height` only when either dimension changed;
6. reacquire or reset the 2D context, restore every context option/style/transform, invalidate every raster cache, and request one full frame.

Changing a Canvas bitmap dimension clears the bitmap and resets its rendering context, even when set redundantly; the adapter therefore never treats resize as a CSS-only event ([HTML Canvas bitmap dimensions](https://html.spec.whatwg.org/multipage/canvas.html#concept-canvas-set-bitmap-dimensions)).

The visible schematic and waveform canvases are opaque and request a 2D context with `alpha: false`; their backgrounds are painted from the current resolved CSS design token. V1 doesn't request `desynchronized` or `willReadFrequently`, read pixels for hit testing, or infer state from antialiasing.

## 6. Invalidation and frame scheduling

Each module has a dirty mask such as `Size | Viewport | Static | Overlay | Preview | Focus | Theme`. Any update merges its reasons and calls one scheduler:

```text
invalidate(reason)
  dirty |= reason
  if no frame is pending:
      pending = requestAnimationFrame(render)
```

At frame start, the renderer clears the pending token, snapshots one coherent published/transient state and dirty mask, then paints only the required layers. New invalidation during paint schedules one later frame. When nothing is dirty and no bounded motion is active, no animation frame remains scheduled.

`requestAnimationFrame` is one-shot, normally aligns with repaint, and is commonly paused in background tabs ([MDN](https://developer.mozilla.org/en-US/docs/Web/API/Window/requestAnimationFrame)). It controls paint only. It never advances Logical Time, clocks a circuit, polls Workspace, estimates a server result, or turns elapsed wall time into semantic state.

V1 has no time-based Canvas animation. An acknowledged Session commit invalidates the affected overlay and the next frame paints its final state. Hidden-tab resumption therefore never replays presentation-only transitions.

## 7. Drawing, culling, and caches

The Canvas adapter consumes ordered Draw Operations exactly. It maps semantic roles to resolved CSS color tokens, groups compatible paths to reduce state changes, and preserves back-to-front order. It doesn't change Geometry Plan paths, text, anchors, bounds, dash patterns, or conformance.

The baseline renderer uses:

- a retained static display list keyed by Schematic Projection key and scoped item identity;
- separate dynamic overlay, focus, preview, and diagnostic lists;
- viewport culling by authoritative bounds with a conservative stroke/hit margin;
- a browser-private spatial index over authoritative Hit Regions; and
- bounded caches keyed by complete Geometry Plan key, resolved theme fingerprint, and effective scale where scale affects a cached raster.

Hit testing queries geometric regions and declared hit priority, never pixels or rendered color. A cache miss changes cost only, never ordering, hit result, or text. Snapshot replacement, font/theme fingerprint change, density change, context restoration, and build change invalidate every affected cache.

Offscreen Canvas, layered visible canvases, `ImageBitmap`, a Worker, and partial dirty rectangles are measured optimizations, not V1 premises. MDN documents their potential benefit but not a universal win ([Canvas optimization](https://developer.mozilla.org/en-US/docs/Web/API/Canvas_API/Tutorial/Optimizing_canvas)). The first implementation remains one visible Canvas per dense host; an optional same-thread offscreen cache is allowed only behind identical display-list tests. No Worker protocol exists until a second deployment context and measured main-thread bottleneck make that seam real.

The module waits until the required self-hosted fonts are reported ready before publishing text-bearing Canvas output. A font failure produces an exact presentation-unavailable state; it never silently substitutes metrics. The Geometry Plan font fingerprint, browser asset fingerprint, and loaded font must agree ([FontFaceSet readiness](https://developer.mozilla.org/en-US/docs/Web/API/FontFaceSet/ready)).

## 8. Pointer, wheel, and keyboard input

The scene uses Pointer Events as the common mouse, pen, and touch event model. One active scene gesture has this state:

```text
Idle
  -> Primed(pointerId, tool, starting versions)
  -> Captured(preview)
  -> Committed(one intent) | Cancelled(no intent)
```

- A primary accepted `pointerdown` may call `setPointerCapture`. Events from other pointers cannot mutate that gesture.
- Move samples update only the local preview. An implementation may use coalesced samples, but never processes both the parent sample and the same coalesced sample twice.
- `pointerup` commits only when the tool's final semantic value is valid. `pointercancel`, `lostpointercapture`, Escape, tool change, disconnect, host removal, build/version replacement, or invalid final geometry cancels and emits no Workspace command.
- Capture is released on every terminal path. A delayed event whose pointer or starting versions don't match the active gesture is ignored.
- `touch-action` is set deliberately per supported host interaction so browser scroll/zoom takeover and scene gestures don't compete. V1 narrow layouts support review, pan/zoom, Probe, Step, Run, and waveform navigation; precision touch wiring is not implied.
- Wheel and trackpad zoom are anchored at the pointer, clamped by Browser Policy, and browser-local. Page/browser text zoom remains independent.

Keyboard shortcuts enter through the Canvas host and the same tool controller as pointer actions. Tab reaches the Canvas region; Space temporarily pans, Escape cancels, and the documented tool and command shortcuts follow [Product](../product.md). There is no keyboard-only topology navigator or parallel edit implementation. Text and IME input always use an HTML control.

## 9. Localization and focus

Canvas is the sole dense schematic editor. Razor supplies the host's localizable name, status, and explicit recovery actions, but does not reconstruct Scene items as fallback descendants. Standard HTML and Fluent UI controls retain their native focus behavior; the Canvas has one focus target for shared shortcuts and visible focus state.

Selection identity is shared by Canvas, Inspector, Diagnostics, Probe Spine, and waveform navigation. When a snapshot removes the selected identity, Web clears selection or chooses the owning source defined by the Workspace projection. Browser-local hover and focus never become Project state.

Every Scene replacement carries the resolved BCP-47 UI culture and base direction from [Web Host §5](./web-host.md#5-culture-localization-and-direction). Scene Canvas text sets its explicit language/direction state, uses supplied Geometry Plan text and alignment, and never generates a localized diagnostic. The Waveform Canvas receives no localized text; Razor owns its labels and direction. Stable protocol values remain invariant. Geometry Plan owns measured Canvas shaping and direction for user-authored text.

Browser text zoom, schematic zoom, long English/Chinese labels, bidirectional content, and non-color Logic Value/Probe recipes are layout and visual-correctness scenarios. Logic Lab does not require a parallel screen-reader editor, forced-colors renderer, or full keyboard authoring path.

## 10. Lifecycle and resource ownership

One mounted handle owns exactly:

- event listeners and pointer capture on its host;
- `ResizeObserver`, density-change observation, and host-removal observation;
- pending animation-frame identifier;
- published/transient scene state, display lists, spatial index, and caches;
- optional JavaScript-to-.NET callback reference; and
- Canvas contexts and any private offscreen resources.

Each C# handle's `DisposeAsync` invokes one idempotent JavaScript `Destroy`. Destruction cancels frames and the active gesture, releases capture, removes listeners, disconnects observers, releases callback/object references, clears caches and candidate transfers, and marks the handle unusable. Later JavaScript calls are ignored or return one stable destroyed outcome.

Browser-owned cleanup must not depend on a successful .NET disposal call. Microsoft recommends `MutationObserver` or a custom element `disconnectedCallback` for DOM cleanup because the component or renderer may already be gone; module-reference disposal can fail after circuit loss ([DOM cleanup](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0#dom-cleanup-tasks-during-component-disposal)). V1 uses one observer scoped to the stable editor host ancestor and calls `Destroy` when its scene/waveform host is removed. The observer disconnects itself during teardown.

The C# adapter implements asynchronous disposal, releases every `IJSObjectReference`/callback reference, catches `JSDisconnectedException` only for circuit loss, and retains no handle after disposal. JavaScript module evaluation may be cached by the browser; all per-mount state therefore lives in the handle rather than module globals.

## 11. Failure and recovery

| Failure                                                | Stable evidence                                                                               | Required behavior                                                                                                         |
| ------------------------------------------------------ | --------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| 2D context unavailable                                 | `web_renderer_unavailable(contextUnavailable)`                                                | hide the bitmap; show the renderer-unavailable state and recovery actions                                                 |
| effective size/pixel policy exceeded                   | `web_browser_policy_exhausted` with exact policy evidence                                     | don't allocate or degrade silently; hide any noncurrent bitmap and show recovery actions                                  |
| invalid snapshot, Scene patch, or private batch        | `web_browser_contract_rejected(invalidSnapshot \| invalidPatch \| invalidBatch, correlation)` | apply nothing; request one complete replacement; never log the record                                                     |
| build mismatch                                         | `build_fingerprint_mismatch` attachment/outcome reason                                        | cancel the gesture, destroy handles, and force a hard reload                                                              |
| browser font unavailable or asset fingerprint mismatch | `web_renderer_unavailable(fontUnavailable \| assetFingerprintMismatch)`                       | publish local renderer unavailable; use no substitute geometry                                                            |
| context loss/restoration when supported                | no evidence if restored; otherwise `web_renderer_unavailable(contextLost)`                    | cancel paint, invalidate caches, and perform a full redraw after restoration; fail closed if restoration doesn't complete |
| circuit disconnect                                     | Web-owned connection state, not a Diagnostic                                                  | freeze acknowledged semantic state, cancel the commit-capable gesture, and allow local pan/zoom only                      |
| JavaScript exception                                   | `web_interop_failure(correlation)`                                                            | fail the affected adapter closed, show recovery UI, and expose no payload or exception text                               |

No browser failure mutates the Project Document, Session, Trace, or Workspace. A prior bitmap may remain visible only while it is explicitly identified as the last acknowledged version; it is hidden as soon as it could be mistaken for a rejected newer Project Revision. Reload and snapshot refresh are explicit recovery actions; repeated exceptions are bounded and never create a hot retry/frame loop.

## 12. Browser Policy and measurement

Browser Policy is deployment configuration, not circuit semantics. Its exact shape, dimension tokens, integer scale encoding, and order are owned by [Policies](../policies.md). The browser validates the complete captured policy before mount and does not change it beneath a published adapter handle. Measurement-only frame and long-task evidence remains outside the policy until qualification establishes a consumer and a versioned threshold.

No numeric default becomes normative until the versioned circuit corpus, supported browser/device matrix, measurement method, and evidence record exist. A policy failure is structured and doesn't silently reduce geometry, omit an item, lower required display density, or summarize Trace without an explicit request.

Browser traces record input-to-preview, acknowledged-intent-to-overlay, replacement, Scene patch apply, frame script/render, long tasks, allocations/heap, cache behavior, and idle activity. `requestAnimationFrame` cadence is display-dependent; acceptance uses distributions on declared environments, not a universal 16.7 ms promise.

## 13. Required evidence

- mount, remount, disposal, host removal, circuit loss, module disposal, and leak-soak scenarios;
- snapshot and Scene-patch atomicity, build/version mismatch, duplicate/out-of-order batch, and complete-replacement recovery;
- transform round trips, grid tie rule, negative/extreme coordinates, hit priority, culling, and viewport fit/reveal properties;
- CSS size, fractional size, DPR, page zoom, monitor-density change, zero-size suspension, bitmap policy, and context-reset scenarios;
- dirty-mask/rAF tests proving at most one pending frame, no idle loop, coherent frame state, and no relation to Logical Time;
- Canvas/SVG parity for operations, text metrics, anchors, bounds, hit regions, and ordering;
- pointer capture/up/cancel/lost capture, coalesced samples, disconnect, stale version, Escape, shared shortcuts, wheel, and touch-action browser scenarios;
- Canvas focus, renderer recovery, browser text zoom, `en-US`/`zh-CN`, long labels, and bidi fixtures;
- Trace Gap, summary/transitions separation, Probe reorder/radix/reveal, context loss, font failure, and scene-unavailable recovery; and
- corpus traces comparing the simple baseline with any retained offscreen, layered, dirty-rectangle, Worker, or cache optimization.

## 14. Source qualification

[Blazor Web Platform Research](../research/blazor-web-platform.md) records the official-source findings, exact links, version limitations, and unverified optimization choices. ECMAScript 2026 supplies module language semantics, not an application architecture; Canvas/Pointer/HTML standards define browser behavior, while Logic Lab owns the state machine, policies, messages, and acceptance evidence.
