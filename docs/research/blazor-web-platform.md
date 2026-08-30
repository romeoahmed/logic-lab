# Blazor Web Platform Evidence

> Sources reviewed: 2026-08-30
> Scope: hosting, render modes, browser/server ownership, lifecycle, and Interactive Server constraints
> Authority: this note records external evidence; [Architecture](../../ARCHITECTURE.md) and [Workbench](../../WORKBENCH.md) own project decisions

## Hosting model

Logic Lab uses Static Server Rendering for conventional pages and per-page Interactive Server rendering for the authenticated editor. The editor keeps its authoritative Workspace and managed engine on the server while collocated JavaScript owns frame-rate Canvas work.

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

## Browser adapter boundary

| Platform fact | Consequence |
|---|---|
| Microsoft recommends collocated `.razor.js` modules; fine-grained Interactive Server interop adds serialization and dispatch cost ([location](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/location-of-javascript?view=aspnetcore-10.0), [performance](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/javascript-interoperability?view=aspnetcore-10.0#avoid-excessively-fine-grained-calls)). | Mount one Scene and one Waveform adapter; exchange bounded batches and completed intents. |
| JavaScript mutation of Blazor-owned DOM can invalidate the renderer's representation ([DOM interaction](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0#interaction-with-the-dom)). | JavaScript owns pixels and listeners inside its hosts; Razor owns surrounding DOM, commands, status, and recovery. |
| Interop references require disposal, but circuit loss can prevent .NET cleanup; browser-side removal observation is recommended ([disposal](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/component-disposal?view=aspnetcore-10.0), [DOM cleanup](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0#dom-cleanup-tasks-during-component-disposal)). | Teardown is idempotent and browser-owned resources do not depend on a successful .NET call. |
| Canvas CSS and bitmap dimensions differ; assigning `width` or `height` clears the bitmap and context state ([HTML Canvas](https://html.spec.whatwg.org/multipage/canvas.html#concept-canvas-set-bitmap-dimensions)). | Resize recomputes a bounded bitmap, restores context state, invalidates caches, and repaints once. |
| `devicePixelRatio` can change independently of element size ([DPR](https://developer.mozilla.org/en-US/docs/Web/API/Window/devicePixelRatio), [ResizeObserver](https://developer.mozilla.org/en-US/docs/Web/API/ResizeObserver)). | CSS size and effective density enter one coalesced resize path; authored coordinates remain unchanged. |
| `requestAnimationFrame` is one-shot and commonly pauses in background tabs ([MDN](https://developer.mozilla.org/en-US/docs/Web/API/Window/requestAnimationFrame)). | It schedules invalidated paint only; it never advances Logical Time. |
| Pointer capture can end through up, cancel, or lost capture; move events may be coalesced ([Pointer Events](https://www.w3.org/TR/pointerevents3/)). | Every gesture has one terminal path and emits either one semantic intent or none. |
| Interactive Server already uses SignalR; inbound messages default to 32 KB and Blazor permits one parallel invocation per client ([SignalR guidance](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/signalr?view=aspnetcore-10.0)). | Pointer samples and dense windows stay local or use dedicated transfer; the global hub limit is not a tuning escape hatch. |

These facts yield one ownership rule: browser adapters own dense pixels, pointer sampling, transforms, hit testing, previews, paint scheduling, and transient view state. Razor owns forms, commands, navigation, status, and recovery. [ADR 0008](../adr/0008-use-one-canvas-editor-surface.md) records the single-Canvas product boundary.

## Circuit and background-work lifetime

Blazor scoped lifetime is per circuit, and circuits can disconnect or be replaced ([state management](https://learn.microsoft.com/en-us/aspnet/core/blazor/state-management/?view=aspnetcore-10.0)). Durable edit state and long-running work must therefore be Application-owned rather than component-owned.

The Work Coordinator has three typed lanes because their lifecycle differs:

- Compilation coalesces per Workspace and keeps the newest request;
- Session commands serialize per Session and expose Run/Pause control; and
- analysis queues under bounded global and per-identity fairness and outlives observers.

ASP.NET Core [hosted-service guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0) requires an explicit service scope when background work consumes scoped dependencies. Razor event handlers must not create unbounded `Task.Run` work or secondary queues.

A custom SignalR Hub is unnecessary while the Blazor circuit carries low-rate commands and observations. It becomes justified only when Trace/live-follow needs a distinct connection lifetime, backpressure, or a non-Blazor client.

## Interactive Server security

Every Workspace, Project, Session, Operation, Proposal, upload, and download action authorizes independently; a locator ID or an existing circuit is not authority.

Interactive Server compression can create a side channel when secrets and attacker-controlled content share a compressed response. Secrets do not enter the editor stream, and response compression around sensitive interactive content follows Microsoft's [threat-mitigation guidance](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/interactive-server-side-rendering?view=aspnetcore-10.0).

Package versions and analyzer policy belong to [.NET Platform Evidence](./dotnet-platform.md). Project package validation belongs to the [Project Package V1 Specification](../specs/project-package-v1.md) and [HTTP Boundary Contract](../contracts/http-boundary.md). Persistence and Identity ownership belong to [Architecture](../../ARCHITECTURE.md); visual and Fluent UI policy belongs to [Workbench](../../WORKBENCH.md).
