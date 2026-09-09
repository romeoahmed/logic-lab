# Blazor Web Platform Evidence

> Sources reviewed: 2026-09-09
> Scope: hosting, render modes, browser/server ownership, lifecycle, and Interactive Server constraints
> Authority: this note records external evidence; [Architecture](../architecture.md) and [Product](../product.md) own project decisions

## Hosting model

Logic Lab uses Static Server Rendering for conventional pages and per-page Interactive Server rendering for the editor, including anonymous Sandboxes. The editor keeps its authoritative Workspace and managed engine on the server while collocated JavaScript owns frame-rate Canvas work.

Microsoft documents Static SSR, Interactive Server, Interactive WebAssembly, and Interactive Auto as per-component render modes; interactive modes prerender by default ([render modes](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0)). WebAssembly and Auto require browser-compatible dependencies and, for Auto, valid server and client execution paths. Those constraints add no value to the V1 server-owned Workspace.

## Prerender and component lifecycle

Interactive prerender can initialize a component once for static output and again for its interactive instance ([prerendered state](https://learn.microsoft.com/en-us/aspnet/core/blazor/state-management/prerendered-state-persistence?view=aspnetcore-10.0)). JavaScript is unavailable until interactive rendering and `OnAfterRenderAsync` ([component lifecycle](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/lifecycle?view=aspnetcore-10.0#after-component-render-onafterrenderasync)).

The editor therefore:

- prerenders stable chrome and a scene placeholder;
- attaches the Workspace only when `RendererInfo.IsInteractive` is true;
- imports its collocated scene module from `OnAfterRenderAsync`;
- persists only safe display data needed to avoid duplicate I/O; and
- treats full reload and internal interactive navigation as different lifecycle paths.

Disabling prerender is a targeted fallback for a browser-only surface, not the default response to duplicate initialization.

Enhanced navigation preserves page state while patching the document. The links that
intentionally leave an existing editor for the new-Workspace chooser set
`data-enhance-nav="false"`, giving the chooser a fresh component lifetime
([enhanced navigation](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/navigation?view=aspnetcore-10.0#enhanced-navigation-and-form-handling)).

A component can be reentered after every incomplete `await`, including by disposal.
Late results must verify component lifetime before publishing state, and late-created
interop references still need cleanup ([synchronization context](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/synchronization-context?view=aspnetcore-10.0)).

Adapter diagnostics follow the same lifetime fence. Their callbacks execute on the
renderer context; parent components accept only evidence for their current revision.
Full Diagram Presentation evidence stays in the server-side composition and is
excluded from Canvas JSON. The Diagnostics tab and Inspector share one projection;
unchanged immutable evidence avoids rebuilding the paged list. Microsoft recommends
targeting expensive rendering subtrees rather than suppressing renders indiscriminately
([rendering performance](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/rendering?view=aspnetcore-10.0)).

The package picker uses the framework's `InputFile` and its native change event
([file uploads](https://learn.microsoft.com/en-us/aspnet/core/blazor/file-uploads?view=aspnetcore-10.0)).
The Fluent UI 5 RC5 file-picker wrapper produced a disposed-JavaScript-reference
exception when a late attachment rejection removed the command bar during its
initialization. A labeled native file input needs no extra anchor-binding module;
the existing bounded stream import workflow still owns reading and validation.

## Browser adapter boundary

| Platform fact                                                                                                                                                                                                                                                                                                                                                                                                                                        | Consequence                                                                                                                |
| ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| Microsoft recommends collocated `.razor.js` modules; fine-grained Interactive Server interop adds serialization and dispatch cost ([location](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/location-of-javascript?view=aspnetcore-10.0), [performance](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/javascript-interoperability?view=aspnetcore-10.0#avoid-excessively-fine-grained-calls)). | Mount one Scene and one Waveform adapter; exchange bounded batches and completed intents.                                  |
| JavaScript mutation of Blazor-owned DOM can invalidate the renderer's representation ([DOM interaction](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0#interaction-with-the-dom)).                                                                                                                                                                                                           | JavaScript owns pixels and listeners inside its hosts; Razor owns surrounding DOM, commands, status, and recovery.         |
| Interop references require disposal, but circuit loss can prevent .NET cleanup; browser-side removal observation is recommended ([disposal](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/component-disposal?view=aspnetcore-10.0), [DOM cleanup](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0#dom-cleanup-tasks-during-component-disposal)).                            | Teardown is idempotent and browser-owned resources do not depend on a successful .NET call.                                |
| Canvas CSS and bitmap dimensions differ; assigning `width` or `height` clears the bitmap and context state ([HTML Canvas](https://html.spec.whatwg.org/multipage/canvas.html#concept-canvas-set-bitmap-dimensions)).                                                                                                                                                                                                                                 | Resize recomputes a bounded bitmap, restores context state, invalidates caches, and repaints once.                         |
| `devicePixelRatio` can change independently of element size ([DPR](https://developer.mozilla.org/en-US/docs/Web/API/Window/devicePixelRatio), [ResizeObserver](https://developer.mozilla.org/en-US/docs/Web/API/ResizeObserver)).                                                                                                                                                                                                                    | CSS size and effective density enter one coalesced resize path; authored coordinates remain unchanged.                     |
| `requestAnimationFrame` is one-shot and commonly pauses in background tabs ([MDN](https://developer.mozilla.org/en-US/docs/Web/API/Window/requestAnimationFrame)).                                                                                                                                                                                                                                                                                   | It schedules invalidated paint only; it never advances Logical Time.                                                       |
| Pointer capture can end through up, cancel, or lost capture; move events may be coalesced ([Pointer Events](https://www.w3.org/TR/pointerevents3/)).                                                                                                                                                                                                                                                                                                 | Every gesture has one terminal path and emits either one semantic intent or none.                                          |
| Interactive Server already uses SignalR; inbound messages default to 32 KB and Blazor permits one parallel invocation per client ([SignalR guidance](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/signalr?view=aspnetcore-10.0)).                                                                                                                                                                                               | Pointer samples and dense windows stay local or use dedicated transfer; the global hub limit is not a tuning escape hatch. |

These facts yield one ownership rule: browser adapters own dense pixels, pointer sampling, transforms, hit testing, previews, paint scheduling, and transient view state. Razor owns forms, commands, navigation, status, and recovery. [ADR 0008](../adr/0008-use-one-canvas-editor-surface.md) records the single-Canvas product boundary.

Signal readouts are unsigned fixed-width Logic Vectors. .NET's `BigInteger` `X`
format preserves a sign bit and can prepend a zero even when a precision is
specified ([numeric format strings](https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-numeric-format-strings#hexadecimal-format-specifier-x)).
Hexadecimal readouts therefore compose digits from the signal bits; decimal
readouts use unsigned `BigInteger`. Browser bus labels convert decoded typed-array
symbols into an ordinary string array before joining, preserving `X` and `Z`.

Canvas language affects text shaping. Razor declares the resolved UI culture on
the Canvas element; measurement and drawing also set the matching context language
when the browser exposes `CanvasRenderingContext2D.lang`. This property is not yet
available across all browsers, so the explicit element language remains the
inheritance source ([HTML Canvas language](https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-lang),
[compatibility and inheritance](https://developer.mozilla.org/en-US/docs/Web/API/CanvasRenderingContext2D/lang)).

Fluent's typography uses a platform font stack and exposes a base font token
([Fluent typography](https://fluent2.microsoft.design/typography)). The host assigns
its packaged Latin and Chinese UI faces to the Fluent base font token as well as
ordinary page text. The Chinese UI face is available before any Canvas mounts;
it uses a separate family name from the digest-verified Scene face so CSS loading
cannot substitute for Scene asset verification.

## Circuit and background-work lifetime

Blazor scoped lifetime is per circuit, and circuits can disconnect or be replaced ([state management](https://learn.microsoft.com/en-us/aspnet/core/blazor/state-management/?view=aspnetcore-10.0)). Durable edit state and long-running work must therefore be Application-owned rather than component-owned.

The Work Coordinator has two typed lanes because their lifecycle differs:

- Compilation coalesces per Workspace and keeps the newest request;
- Session commands serialize per Session and expose Run/Pause control.

ASP.NET Core [hosted-service guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0) requires an explicit service scope when background work consumes scoped dependencies. Razor event handlers must not create unbounded `Task.Run` work or secondary queues.

A custom SignalR Hub is unnecessary while the Blazor circuit carries low-rate commands and observations. It becomes justified only when Trace/live-follow needs a distinct connection lifetime, backpressure, or a non-Blazor client.

## Interactive Server security

Every Workspace, Project, Session, Operation, Proposal, upload, and download action authorizes independently; a locator ID or an existing circuit is not authority.

Error responses omit request paths because download routes contain private tickets.
The ASP.NET Core 10 default Problem Details writer overwrites `traceId` before
calling `CustomizeProblemDetails`; the host uses that callback to preserve the
application's logged correlation ([writer implementation](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Http/Http.Extensions/src/DefaultProblemDetailsWriter.cs),
[error-response guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0)).

Interactive Server compression can create a side channel when secrets and attacker-controlled content share a compressed response. Secrets do not enter the editor stream, and response compression around sensitive interactive content follows Microsoft's [threat-mitigation guidance](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/interactive-server-side-rendering?view=aspnetcore-10.0).

Package versions and analyzer policy belong to [Engineering](../engineering.md).
Project package validation belongs to [Project Package V1](../specs/project-package-v1.md)
and the [HTTP Boundary](../contracts/http-boundary.md). Architecture owns persistence
and Identity; Product owns visual and Fluent UI policy.
