# HTTP Transfer Contract

> Status: normative V1 authorized HTTP seam contract

This contract owns import/export transport, download authorization, and RFC 9457 error mapping. [Web Host](../specs/web-host.md) owns routes, middleware, security configuration, and process behavior. [Project Package V1](../specs/project-package-v1.md) owns carrier validation.

## 1. Transfer lifecycle

Import uses the Blazor file stream with an explicit maximum and streams into Project Format; it never buffers an untrusted carrier wholly in memory. Application then follows the [Editor Workspace import handoff](./editor-workspace.md#3-editor-workspace-interface), publishing a new Workspace or leaving the origin unchanged. For export, Application returns an opaque Export Ticket and Web maps it to a short-lived authorized URL that streams the package. Cookie-authenticated mutation retains antiforgery protection.

HTTP adapters use RFC 9457 Problem Details:

```json
{
  "type": "https://logiclab.example/problems/stale_workspace_attachment",
  "title": "Workspace connection is no longer current",
  "status": 409,
  "code": "stale_workspace_attachment",
  "traceId": "opaque-correlation"
}
```

`traceId` uses the `CorrelationToken` shape from Diagnostics V1. `detail` is optional and localized. It never exposes unauthorized IDs, project content, stack traces, filesystem paths, tokens, or internal policy capacity.

HTTP status mapping is consistent across transfer and any later measured large-window endpoint:

| Status | Meaning |
|---:|---|
| `400` | malformed HTTP shape before a typed request exists |
| `401` | authentication required |
| `404` | resource absent or deliberately concealed as unauthorized |
| `409` | attachment, idempotency, Durable Version, build, or proposal precondition conflict |
| `413` | request bytes exceed the HTTP ingress limit |
| `422` | authenticated bounded content fails package, Domain, or semantic validation |
| `429` | caller admission or rate policy rejects new work |
| `500` | internal invariant defect with opaque correlation only |
| `503` | temporary infrastructure or host-capacity failure |

`Retry-After` appears only when the server has an honest value for `429` or `503`. Core Module cancellation remains a typed outcome and is not assigned a nonstandard HTTP status. Every non-success body uses the same Problem Details extension fields and an exact closed code from its owning contract. Typed outcome codes come from Diagnostics V1; transport validation before a typed request exists is owned here.

The malformed `/projects/open` form code is exactly:

```text
project_open_request_invalid
```

It maps to `400` when `durableProjectId` is absent, empty, or exceeds the bounded HTTP shape. This validation occurs before constructing `OpenDurable` and never calls the Workspace.

An unauthenticated `/projects/open` request, including a cookie challenge or an
authenticated principal without a stable subject, uses the exact code
`authentication_required` and maps to `401`; this API-style endpoint never
redirects to an account page. Login and registration ingress rejection uses
the exact code `authentication_rate_limit_exceeded`, maps to `429`, and includes
`Retry-After` only when the limiter supplies an honest duration. An endpoint
request body that exceeds its declared byte limit uses the exact code
`request_body_too_large` and maps to `413`.

The Durable Project Web adapter applies this table directly: a malformed open form is `400`, an absent or concealed `OpenDurable` target is `404`, Workspace admission rejection is `429`, catalog request/cursor validation is `422`, infrastructure or cancellation is `503`, and an internal defect is `500`. Static SSR performs the catalog call before rendering `/projects`, while `/projects/open` validates its form before constructing `OpenDurable` and then adapts `WorkspaceOpenRejected` directly; neither failure path redirects to an unconsumed query parameter or renders an HTTP `200` error page.

## 2. Security

- Authenticate and authorize every transfer action; a URL, Workspace ID, Durable Project ID, or download token is only a locator.
- Retain antiforgery for cookie-authenticated mutation and validate browser messages and uploaded bytes under separate size and rate limits.
- Hide unauthorized resource existence and never expose project content, Trace values, tokens, filesystem paths, stack traces, or internal policy capacity.
- Never render or execute uploaded markup, scripts, type names, plug-ins, or solver commands.
