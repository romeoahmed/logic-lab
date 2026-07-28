# Web Host

> Status: normative V1 ASP.NET Core and Blazor host contract
> Target: ASP.NET Core 10, Blazor Web App, per-page Interactive Server editor

Web Host defines routing, render modes, request culture, circuit integration, security middleware, transfer endpoints, process lifetimes, and operational publication. It does not own authored circuit facts, Workspace behavior, Canvas drawing, or deployment-provider configuration.

[Architecture](../../ARCHITECTURE.md) owns project dependencies and deployment shape. [Editor Workspace Contract](../contracts/editor-workspace.md) owns Application calls, [Browser Adapter Contract](../contracts/browser-adapters.md) owns Scene/Waveform records, and [HTTP Transfer Contract](../contracts/http-transfer.md) owns transfer/error values. [Browser Runtime](./browser-runtime.md) owns Canvas and waveform implementation behavior.

## 1. Host shape

V1 is one ASP.NET Core process containing Static SSR pages, one Interactive Server editor surface, Application Modules, and the initial SQLite adapter. There is no `.Client` project, Interactive Auto mode, WebAssembly execution path, public REST API, custom SignalR Hub, or user-supplied server code.

The route catalog is closed at implementation start:

| Surface | Render/transport | Contract |
|---|---|---|
| `/` and `/help/{**path}` | Static SSR | public product and help content |
| `/account/{**path}` | Static SSR and ordinary HTTP form flows | ASP.NET Core Identity account management |
| `/projects` | Static SSR with authorized HTTP actions | use the bounded [Durable Project Catalog](../contracts/durable-project-catalog.md), open one through `OpenDurable`, or start the new/import flow |
| `/editor` | per-page Interactive Server | create or import a Workspace, then replace the URL with its opaque locator |
| `/editor/{workspaceId}` | per-page Interactive Server | authorize and attach to one Editor Workspace; the ID grants no access |
| `/downloads/{token}` | authorized streaming GET | one prepared `.logiclab` export; token is short-lived and single purpose |
| `/culture` | antiforgery-protected HTTP POST | validate one supported culture, write its cookie, and redirect to a validated local URL |
| `/health/live`, `/health/ready` | noninteractive HTTP | process liveness and dependency readiness without sensitive detail |

Identity may add framework-owned endpoints beneath `/account`; generated routes are captured by an integration snapshot before release. An endpoint outside this table requires an explicit owning contract. Import remains a bounded Blazor file stream into Project Format and opens a new Workspace; it is not a JSON endpoint or an edit to the current Workspace.

ASP.NET Core supports mixing Static SSR and interactive render modes, and Interactive Server communicates through a server circuit. Interactive modes prerender by default. [Microsoft render-mode guidance](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0) is the platform basis for this composition.

## 2. Render and attachment lifecycle

The editor has three distinct lifetimes:

```text
HTTP document
  -> prerendered editor shell
  -> interactive Blazor circuit
       -> authorized Workspace Attachment
            -> Application-owned Editor Workspace
```

- Prerender emits the stable workbench frame, route-safe project label when authorized, connection state, and an empty scene placeholder. It creates no Workspace Attachment, imports no JavaScript module, and renders no invented gate skeleton.
- Initialization and any safe boot read must tolerate prerender followed by a new interactive component instance. Persistent component state may carry authorization-safe display data only.
- The editor starts attachment only when the renderer is interactive. Scene and waveform modules mount from `OnAfterRenderAsync` after their `ElementReference` values exist. `OnAfterRenderAsync` isn't called during prerender because there is no live browser DOM ([Blazor lifecycle](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/lifecycle?view=aspnetcore-10.0#after-component-render-onafterrenderasync)).
- The circuit-scoped Web projection coordinator observes one Workspace Attachment. Razor children receive immutable projections and typed callbacks; they neither attach independently nor resolve repositories.
- Circuit loss freezes the last acknowledged semantic display and allows browser-local pan/zoom only. Reattachment reauthorizes, fences the prior attachment generation, returns a complete Workspace Projection, and never resumes Run automatically.
- A rejected reattachment because server state was lost offers `Reload workspace` or an authorized recovery flow. A browser reconnection indicator never claims that a Workspace command committed.
- Component disposal releases .NET-held module references on a best-effort basis and tolerates `JSDisconnectedException`; browser-owned teardown follows [Browser Runtime §10](./browser-runtime.md#10-lifecycle-and-resource-ownership).

An Editor Workspace is Application-owned and can outlive a circuit under bounded retention. It is never stored in a Razor component, circuit-scoped dependency, `HttpContext`, or `DbContext`.

## 3. Dependency lifetimes

| Lifetime | Owns | Must not own |
|---|---|---|
| process | bounded Workspace directory, Work Coordinator, policy registry, library/profile registries, observability instruments | request user, `DbContext`, browser module reference |
| hosted operation scope | one queued Compilation, Session, analysis, cleanup, or retention action and its scoped adapters | live Razor component or circuit callback |
| circuit | Web projection coordinator, authorized attachment observation, localization and browser-module adapters | Editor Workspace lifetime, background CPU work, database context |
| operation | short-lived repository context, authorization check, import/export stream | retained EF tracking graph or pooled buffer after completion |

Background services create an explicit dependency-injection scope for scoped adapters. CPU-bound Modules remain synchronous and execute only through the three typed lanes in Architecture. Razor handlers do not call `Task.Run`, build secondary queues, or capture circuit-scoped dependencies in process work.

The SQLite repository uses `IDbContextFactory<T>` and one context per operation. SQLite has no database-generated `rowversion`: the adapter treats Durable Version as an application-managed opaque concurrency token, replaces it only when the current-revision pointer changes, and predicates the same transaction's update on the expected token. A zero-row update or `DbUpdateConcurrencyException` becomes exactly `durable_save_conflict`; it never becomes last-write-wins or a generic 500 ([EF Core concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency#application-managed-concurrency-tokens)).

Production database schema changes run as an explicit deployment step before readiness is published; the Web process does not race multiple startup instances through automatic migrations. The deployment profile chooses and reviews one version-specific script or migration bundle, backs up before mutation, verifies the expected schema, and defines rollback/restore plus abandoned `__EFMigrationsLock` recovery. It does not assume SQLite supports EF idempotent migration scripts or every direct `ALTER`; provider limitations include table rebuilds and a lock that can remain after abnormal termination ([SQLite limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations), [applying migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)). Database migrations and repository storage encoding are Infrastructure concerns, not `.logiclab` compatibility.

## 4. Circuit transport and SignalR posture

Interactive Server already uses SignalR for render batches, UI events, and JavaScript interop. WebSockets are enabled and verified in the production hosting path; framework fallback transport is recovery compatibility, not the performance baseline. A custom Hub would duplicate connection lifetime and authorization without improving the browser-local gesture loop, so V1 has none.

The initial host preserves `HubOptions.MaximumReceiveMessageSize` at its .NET 10 default and preserves `MaximumParallelInvocationsPerClient = 1`. Microsoft documents a default 32 KB inbound maximum, warns that increasing it consumes resources and raises denial-of-service risk, and states that Blazor relies on one parallel client invocation ([Blazor SignalR guidance](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/signalr?view=aspnetcore-10.0#maximum-receive-message-size)).

Consequences:

- browser-to-.NET calls contain one bounded semantic intent, never pointer samples, full Project Documents, Trace windows, or Canvas pixels;
- the Browser Policy limits the encoded intent beneath the configured transport ceiling with room for framework envelope overhead, established by an integration test rather than a guessed constant;
- scene metadata and changes are batched by the typed Web adapter; dense Logic Vectors use optimized byte-array interop instead of base64 strings;
- uploads and downloads stream through their file contracts, and large Trace reads use the authorized Workspace query/HTTP seam when measurement selects it;
- a transport-size failure is a rejected browser interaction and connection diagnostic, never permission to raise the global limit silently.

Interactive Server WebSocket compression is disabled for the editor in V1. Project names, annotations, imported content, and JS-originated values are attacker-controlled, while the circuit is authenticated; Microsoft's guidance warns about this combination when compression is enabled ([Interactive Server threat mitigation](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/interactive-server-side-rendering?view=aspnetcore-10.0), [Blazor SignalR compression](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/signalr?view=aspnetcore-10.0#websocket-compression-for-interactive-server-components)). Re-enabling compression requires a recorded security review and measured benefit; it is not a tuning toggle.

## 5. Culture, localization, and direction

V1 supports exactly these UI cultures:

| Culture | Language | Base direction |
|---|---|---|
| `en-US` | English | left-to-right |
| `zh-CN` | Simplified Chinese | left-to-right |

The first request selects the first supported `Accept-Language` match or `en-US`. An explicit user choice writes the standard localization cookie through a same-origin, antiforgery-protected endpoint with a validated local return URL, then performs a full reload. The reload intentionally creates a culture-consistent HTTP document, circuit, localization scope, Diagram Presentation fingerprint, Canvas text state, and semantic tree.

Request-localization middleware runs before Razor endpoints. Web uses .NET resources through `IStringLocalizer`; stable codes, IDs, enum values, JSON member names, logical-time decimal strings, and digests remain culture-invariant. The host sets `<html lang>` and `dir` explicitly because culture selection doesn't set the HTML language attribute automatically. These rules follow [Blazor globalization and localization](https://learn.microsoft.com/en-us/aspnet/core/blazor/globalization-localization?view=aspnetcore-10.0) and [ASP.NET Core localization](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/localization?view=aspnetcore-10.0).

Localized resources are strongly keyed by owning feature. English and Simplified Chinese catalogs must have identical keys and safe typed placeholders. User-authored text is never a resource key or markup. Bidirectional user text is isolated in HTML and Canvas text layout even though the two V1 UI cultures are left-to-right. Browser text uses the resolved locale and direction carried by the Scene and Waveform contracts; ECMA-402 output is presentation only and never compared with invariant .NET contract text.

## 6. HTTP, files, and errors

All cookie-authenticated mutations retain antiforgery validation. Import, export preparation, download, account, and Durable Project actions authenticate and authorize independently. The culture action is available before authentication but remains same-origin, antiforgery-protected, allowlisted, and unable to name a protected resource. A route, Workspace ID, Durable Project ID, attachment, or download token is a locator, never sufficient authority.

The host applies separate bounded policies to request rate, request body bytes, concurrent transfers, expanded package work, Workspace admission, and background work. ASP.NET Core rate limiting is an ingress control, not a complete DDoS defense or a replacement for Module policy; official guidance requires load testing configured policies ([rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)).

Authorized HTTP failures use the exact RFC 9457 shape and status mapping in the [HTTP Transfer Contract](../contracts/http-transfer.md). `IProblemDetailsService` supplies the common adapter; titles and optional details are localized, while `type`, `status`, `code`, and correlation token remain stable. Unhandled exceptions expose only an opaque correlation. ASP.NET Core provides `IProblemDetailsService` for RFC 9457 responses ([error handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0#problem-details-service)).

Client filenames, MIME, lengths, ZIP metadata, logical paths, JSON, Canvas messages, and return URLs are untrusted. Responses set an application-generated attachment filename, `X-Content-Type-Options: nosniff`, and a precise content type. Uploaded or authored HTML, SVG, script, and URLs are never rendered as markup.

## 7. Host security baseline

- Deny by default and reauthorize every resource action, including observation and cancellation.
- Close or reauthorize work when authentication expires; a long-lived circuit never freezes old authorization into a capability.
- Emit a centrally tested Content Security Policy with `frame-ancestors 'none'`; allow only the scripts, styles, fonts, connections, and images required by the built app. A release CSP contains no wildcard source and no permission to execute uploaded content.
- Trust forwarded headers only from configured proxies and networks. The external scheme and host used for redirects, antiforgery, and secure cookies must be reconstructed safely ([proxy guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)).
- Persist ASP.NET Core Data Protection keys outside an ephemeral process in production, restrict key access, and use one stable application discriminator so restarts don't invalidate every auth cookie ([Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)).
- Mark authentication and antiforgery cookies Secure, HttpOnly where applicable, and with the narrowest workable SameSite policy.
- Redact project payloads, annotations, Trace values, tokens, cookies, session identifiers, and full browser messages from logs and telemetry.
- Validate startup configuration and refuse readiness for missing production secrets, an unavailable required database, an incompatible schema, an unresolved library/profile fingerprint, or an invalid security configuration.

## 8. Health, shutdown, and observability

`/health/live` answers only whether the process event loop can respond. `/health/ready` checks required startup configuration, repository reachability, schema compatibility, and Work Coordinator admission readiness. Neither returns exception messages, connection strings, project counts, queue depths, or dependency addresses. ASP.NET Core health checks support separate readiness and liveness probes ([health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0#separate-readiness-and-liveness-probes)).

On graceful shutdown the host stops admission, marks readiness unhealthy, requests lane cancellation, lets an active Logical-time Advance commit or roll back, pauses Sessions, finishes or aborts transfers safely, disposes scopes, and then exits within deployment policy. Durable data is never inferred from an in-memory Workspace during shutdown.

Activities span authenticated route handling, Workspace calls, repository operations, queued work, and transfer phases. Metrics use low-cardinality outcome, lane, and policy dimensions only. Logs record stable codes and correlations, not user content. Development diagnostics are never enabled in production.

## 9. Required evidence

- route and render-mode integration snapshots proving only the `/editor` route family is Interactive Server;
- prerender/interactive tests proving one attachment, one module mount, no JS during prerender, and no duplicate side effect;
- circuit disconnect, reattach, rejection, process restart, auth expiry, and build-fingerprint reload scenarios;
- transport tests at, below, and above the browser-intent budget while the hub maximum remains unchanged;
- WebSocket hosting verification plus an explicit test that the editor's WebSocket compression is disabled;
- `en-US`/`zh-CN`, cookie, reload, `lang`, direction, resource-key parity, long-label, and bidirectional-content scenarios;
- authentication, authorization concealment, antiforgery, local-return-URL, CSP, upload, download, rate, and Problem Details tests;
- short-lived `DbContext`, application-managed Durable Version conflict mapping, reviewed migration/abandoned-lock recovery, migration-before-readiness, and process-shutdown integration tests;
- liveness/readiness redaction and dependency-failure tests; and
- browser/load traces on the versioned corpus before any circuit, buffer, timeout, or rate value becomes an acceptance threshold.

## 10. Qualification limits

Host provider, public origin, TLS termination, trusted proxy ranges, Data Protection store, database backup/restore, telemetry backend, dashboards, alerts, and calibrated limit values remain deployment evidence. Their absence doesn't change the Module interfaces, but a production release is not qualified until one deployment profile supplies and tests them.

Do not use `ServerComponentsEndpointOptions.ConfigureConnection` in the .NET 10 implementation. The current Microsoft page shows that API only in newer monikers; the V1 host configures only APIs verified against `net10.0`. [Blazor Web Platform Research](../research/blazor-web-platform.md) records the version audit and remaining browser qualification gaps.
