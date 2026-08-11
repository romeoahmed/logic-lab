# LogicLab.Web

| Setting | Value |
|---------|-------|
| **Interactivity Mode** | Server |
| **Interactivity Scope** | Per-page |

## Rendering configuration

This project uses per-page Interactive Server with prerendering.

Pages are Static SSR by default. Only components that explicitly add `@rendermode InteractiveServer` become interactive.

## Adding components

- Create routable pages in `Components/Pages/` and shared components in `Components/`.
- Add `@rendermode InteractiveServer` only to pages or components that need interactive behavior.
- Keep data flowing down through parameters and events flowing up through `EventCallback<T>`.
- Preserve a stable prerender shell; start interactive-only work only when `RendererInfo.IsInteractive` is true.

## Environment constraints

- Interactive components execute on the server through a SignalR circuit.
- Do not inject `HttpContext` into interactive components.
- Keep browser APIs behind narrow asynchronous interop adapters.
- Do not set a render mode on `<Routes>`; this project uses per-page interactivity.

## Ownership

- Razor owns commands, navigation, semantic fallback, and low-rate state.
- Application owns Editor Workspaces and calls Domain/Engine modules.
- Presentation owns renderer-neutral static scene projection.
- Browser adapters own frame-rate pixels and pointer samples.
