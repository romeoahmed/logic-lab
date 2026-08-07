# Blazor Web Platform Research

> Verified 2026-07-30 (Asia/Shanghai)
> Scope: Blazor hosting, browser/server ownership, files, persistence, packages, security, performance, and testing
> Authority: official platform evidence; project choices are in [Architecture](../../ARCHITECTURE.md) and [Workbench](../../WORKBENCH.md)

## 1. Hosting conclusion

Blazor Web App is the best fit because Logic Lab combines conventional server-rendered pages with one rich authenticated editor. The selected composition is:

- Static Server Rendering for public, help, account, and project-list pages;
- per-page Interactive Server for the editor;
- collocated JavaScript for dense frame-rate scene and waveform work;
- direct typed server Module calls for authoring, Compilation, Simulation, analysis, and persistence.

This preserves fast initial HTML and full ASP.NET Core access without forcing the engine, Project Format, and repository behind a browser API.

## 2. Render-mode evidence

Microsoft documents Static SSR, Interactive Server, Interactive WebAssembly, and Interactive Auto as per-component render modes. Interactive modes prerender by default.

| Mode | Relevant consequence for Logic Lab |
|---|---|
| Static SSR | no interactive circuit; ideal for site shell and account/content pages |
| Interactive Server | component code and state remain on server; UI events and DOM updates use the Blazor circuit |
| Interactive WebAssembly | downloads runtime/app, requires browser-compatible dependencies and API-mediated server access |
| Auto | runs Server first and WebAssembly on later visits, requiring `.Client` placement and two valid execution environments |

Auto is not “Server with a free optimization.” It changes execution location and dependency rules. Logic Lab's authoritative Workspace and CPU-heavy managed engine make those constraints pure cost in V1.

The .NET Web Worker guidance for .NET 10 describes manual bridging and limitations; the integrated template documented with the newer moniker is not a reason to introduce a worker into a server-first editor. A browser worker becomes relevant only if a future WebAssembly execution model is independently justified.

## 3. Prerender and lifecycle

Interactive prerender runs component initialization once for static output and again for the interactive instance. JavaScript is unavailable until interactive rendering and `OnAfterRenderAsync`.

A robust editor therefore:

- prerenders stable chrome and a scene placeholder;
- starts Workspace attachment only when `RendererInfo.IsInteractive` is true;
- imports the collocated scene module from `OnAfterRenderAsync`;
- uses persistent component state only for safe display data that prevents duplicate I/O;
- never persists authorization, a live Workspace object, or JS references through prerender;
- treats full reload and internal interactive navigation as different lifecycle paths.

Disabling prerender is a targeted fallback for a browser-only surface, not the default response to duplicate initialization.

## 4. Browser adapter evidence

| Platform fact | Architecture consequence |
|---|---|
| Microsoft recommends collocated `.razor.js` modules; Interactive Server interop is asynchronous and fine-grained calls add serialization/dispatch cost ([location](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/location-of-javascript?view=aspnetcore-10.0), [performance](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/javascript-interoperability?view=aspnetcore-10.0#avoid-excessively-fine-grained-calls)). | Mount one Scene and one Waveform adapter; exchange bounded batches and completed intents. |
| JavaScript mutation of Blazor-owned DOM can invalidate the renderer's representation ([DOM interaction](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0#interaction-with-the-dom)). | JavaScript owns pixels and listeners inside its hosts; Razor owns surrounding DOM and semantic fallback. |
| Interop references require disposal, but circuit loss can prevent .NET cleanup; Microsoft recommends browser-side removal observation ([disposal](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/component-disposal?view=aspnetcore-10.0), [DOM cleanup](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0#dom-cleanup-tasks-during-component-disposal)). | Adapter teardown is idempotent and browser-owned resources do not depend on a successful .NET call. |
| Canvas CSS dimensions and bitmap dimensions differ; assigning `width` or `height` clears the bitmap and resets context state ([HTML Canvas](https://html.spec.whatwg.org/multipage/canvas.html#concept-canvas-set-bitmap-dimensions)). | Resize recomputes a policy-bounded bitmap, restores context state, invalidates caches, and repaints once. |
| `devicePixelRatio` changes with page zoom or display movement, while `ResizeObserver` reports element size ([DPR](https://developer.mozilla.org/en-US/docs/Web/API/Window/devicePixelRatio), [ResizeObserver](https://developer.mozilla.org/en-US/docs/Web/API/ResizeObserver)). | CSS size and effective density enter one coalesced resize path; authored coordinates stay unchanged. |
| `requestAnimationFrame` is one-shot and commonly pauses in background tabs ([MDN](https://developer.mozilla.org/en-US/docs/Web/API/Window/requestAnimationFrame)). | It schedules invalidated paint only and never advances Logical Time. |
| Pointer capture can end through up, cancel, or lost capture; move events may be coalesced ([Pointer Events](https://www.w3.org/TR/pointerevents3/)). | Every gesture has one terminal path and emits either one semantic intent or none. |
| Interactive Canvas requires equivalent fallback purpose and focusable actions ([HTML Canvas](https://html.spec.whatwg.org/multipage/canvas.html#the-canvas-element)). | The semantic fallback is part of the Scene interface, not an optional accessibility label. |
| Interactive Server already uses SignalR; inbound messages default to 32 KB and Blazor requires one parallel invocation per client ([SignalR guidance](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/signalr?view=aspnetcore-10.0)). | Pointer samples and dense windows stay local or use dedicated transfer; the global hub limit is not a tuning escape hatch. |
| `OffscreenCanvas`, layered canvases, and Workers are optional techniques with workload-dependent benefit ([Canvas optimization](https://developer.mozilla.org/en-US/docs/Web/API/Canvas_API/Tutorial/Optimizing_canvas)). | They remain implementation candidates until corpus traces justify a real seam. |

These facts support one ownership rule: browser adapters own dense pixels, pointer sampling, transforms, hit testing, previews, paint scheduling, and transient view state; Razor owns forms, commands, navigation, and semantic projections.

## 5. Workspace and background work

Blazor scoped lifetime is per circuit, and circuits can disconnect or be replaced. A long-running analysis or durable edit history should therefore be Application-owned rather than component-owned.

The target Work Coordinator uses three typed execution lanes because their lifecycle differs:

- Compilation coalesces per Workspace and keeps the newest request;
- Session commands serialize per Session and expose Run/Pause control;
- analysis queues under bounded global and per-identity fairness and outlives observers.

At the research checkpoint, the implementation exercised only the bounded Compilation and Session lanes. [Development Readiness](../README.md#development-readiness) owns the maintained implementation boundary; the Analysis lane belongs to the Boolean Analysis slices.

ASP.NET Core hosted-service guidance requires creating an explicit service scope when background work consumes scoped dependencies. Razor event handlers should not create unbounded `Task.Run` work or secondary queues.

A custom SignalR Hub is unnecessary while the Blazor circuit already carries low-rate commands and observations. It becomes justified only if Trace/live-follow requires a distinct connection lifetime, backpressure, or independent non-Blazor client.

## 6. Files and Project Format

Blazor file upload guidance requires an explicit maximum on `OpenReadStream` and warns against reading an untrusted upload wholly into memory. Logic Lab streams into a bounded spool because ZIP validation needs seekable inspection.

ASP.NET Core and OWASP guidance align on:

- allowlisted format and structure, not extension alone;
- untrusted client filename, MIME, and claimed length;
- application-generated storage names;
- independent request, expanded-content, and logical limits;
- authorization and antiforgery for cookie-authenticated mutation;
- defense in depth against decompression bombs and traversal.

`System.IO.Compression.ZipArchive` is an archive parser, not a safe extraction policy. Project Format must enumerate all entries, reject duplicates and unsafe names, count bytes actually read, and never call `ExtractToDirectory`.

`.NET 10 System.Text.Json` supports source generation, explicit polymorphism, unmapped-member rejection, `AllowDuplicateProperties = false`, and low-allocation token reading. Project Format still uses a bounded reader-level validation pass so low-level reader and custom-converter paths cannot bypass the complete strict-input policy. Transport DTOs remain separate from Domain entities.

Download guidance favors a normal authorized URL for server-generated files, avoiding a large file round-trip through JavaScript memory. The URL remains short-lived and action-authorized.

## 7. EF Core and identity

Microsoft's Blazor/EF guidance warns that a scoped `DbContext` can be inappropriate for a long-lived circuit and recommends one context per operation or `IDbContextFactory<T>`.

The planned Infrastructure slice uses EF Core 10 with SQLite because the first deployment is one ASP.NET Core process. At the research checkpoint, that slice was not present. Its repository stores ownership, immutable Project Revision payloads, current pointers, and idempotency records—not a mutable row per gate and wire.

Read paths project only needed columns; entity-returning read-only queries use no tracking, with identity resolution chosen deliberately when duplicate materialization matters. Lazy-loading proxies are absent. Save and idempotency use one transaction. A future multi-instance database is a repository adapter and deployment decision, not a Domain change.

The target Web Host uses ASP.NET Core Identity for cookie authentication and account management; at the research checkpoint, the Sandbox tracer had no Identity or durable account surface. When implemented, Static SSR account pages retain normal HTTP and antiforgery behavior. Every Workspace, Project, Session, Operation, Proposal, upload, and download action still authorizes independently; a locator ID or existing circuit is not authority.

## 8. Security model

The OWASP cheat sheets reinforce several design choices:

- deny by default and validate permission on every request or message;
- avoid object-level authorization based only on guessed IDs;
- validate WebSocket Origin/host on any custom endpoint;
- use structured allowlists, size limits, rate limits, and bounded real queues;
- do not log tokens, full payloads, project contents, or sensitive Trace values;
- return non-disclosing errors and RFC 9457 Problem Details;
- maintain a Content Security Policy and contextual output encoding;
- never execute uploaded code, scripts, plug-ins, or solver commands.

Interactive Server compression can create side-channel risk when secrets and attacker-controlled content share a compressed response. Avoid rendering secrets into the editor stream and follow current Microsoft mitigation guidance before enabling response compression around sensitive interactive content.

## 9. Fluent UI Blazor v5

The [official NuGet version index](https://api.nuget.org/v3-flatcontainer/microsoft.fluentui.aspnetcore.components/index.json), inspected on 2026-07-29 and rechecked on 2026-08-05, contained only RC builds for v5; its latest listed v5 build was `5.0.0-rc.4-26180.1`, while v4 remained the stable line. The Web project consumed that exact centrally pinned build at the checkpoint. [Architecture](../../ARCHITECTURE.md#82-net-and-dependencies) owns package containment; changing the pin requires fresh package and browser qualification.

## 10. Project handoff

[Architecture](../../ARCHITECTURE.md#82-net-and-dependencies) owns package placement, [Architecture §10](../../ARCHITECTURE.md#10-constraints-and-evidence-triggered-seams) owns evidence-triggered alternatives, and [.NET Engineering Baseline](../specs/dotnet-engineering.md) owns language and execution rules. This note supplies only the Web-platform evidence behind those decisions.

## 11. Primary official sources

- [ASP.NET Core 10 overview](https://learn.microsoft.com/en-us/aspnet/core/overview?view=aspnetcore-10.0)
- [Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/?view=aspnetcore-10.0) and [render modes](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0)
- [Blazor prerendered state](https://learn.microsoft.com/en-us/aspnet/core/blazor/state-management/prerendered-state-persistence?view=aspnetcore-10.0)
- [Blazor JavaScript interop](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0), including [DOM cleanup during disposal](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0#dom-cleanup-tasks-during-component-disposal)
- [Blazor rendering performance](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/rendering?view=aspnetcore-10.0)
- [Blazor file uploads](https://learn.microsoft.com/en-us/aspnet/core/blazor/file-uploads?view=aspnetcore-10.0) and [downloads](https://learn.microsoft.com/en-us/aspnet/core/blazor/file-downloads?view=aspnetcore-10.0)
- [Blazor with EF Core](https://learn.microsoft.com/en-us/aspnet/core/blazor/blazor-ef-core?view=aspnetcore-10.0)
- [Blazor security](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/?view=aspnetcore-10.0)
- [Hosted services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0)
- [SignalR overview](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction?view=aspnetcore-10.0)
- [EF Core](https://learn.microsoft.com/en-us/ef/core/) and [efficient querying](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying)
- [Fluent UI Blazor v5](https://v5.fluentui-blazor.net/)
- [TUnit](https://tunit.dev/docs/intro/), [TUnit ASP.NET Core integration](https://tunit.dev/docs/examples/aspnet), [bUnit](https://github.com/bUnit-dev/bUnit), and [TUnit Playwright integration](https://tunit.dev/docs/examples/playwright)
- [OWASP Cheat Sheet Series](https://cheatsheetseries.owasp.org/)
