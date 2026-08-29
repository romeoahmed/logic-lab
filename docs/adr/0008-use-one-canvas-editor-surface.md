---
status: accepted
date: 2026-08-29
---

# Use one Canvas editor surface

## Context

The schematic is a dense spatial editor. An earlier design required the Web Host to reproduce every current Canvas entity, navigation path, action, focus target, and page in a second semantic DOM tree. That made accessibility a second editor architecture rather than a presentation property: every Scene change had to remain coherent across Canvas hit testing, semantic paging, fallback actions, focus recovery, resources, browser limits, and parallel tests.

Logic Lab has no mandatory accessibility-compliance target. Its largest product risks are electrical and drawing correctness, a comprehensible authoring flow, Canvas ergonomics, and a maintainable implementation. A parallel circuit editor makes those outcomes harder without being the primary way the product is used.

The Web chrome still benefits from established components. Microsoft documents `Microsoft.FluentUI.AspNetCore.Components` as an extensive Blazor component set that implements or applies the Fluent design system. Reusing an appropriate component avoids creating another interaction contract merely to reproduce standard controls.

## Decision

- Canvas is the only dense schematic editing surface. There is no parallel DOM circuit outline, semantic Scene pager, or alternative keyboard editor.
- Razor and Fluent UI own project commands, menus, forms, Inspector content, status, diagnostics, and explicit renderer-recovery surfaces. They do not reconstruct the circuit from Scene records.
- Web chrome uses the centrally pinned Fluent UI Blazor component when its behavior fits the product. Native HTML and modern CSS are preferred when they express a simpler platform primitive. Custom controls are reserved for domain-specific behavior or a documented gap in both choices.
- A renderer failure replaces the Canvas with a concise unavailable state and useful recovery actions. It never exposes a stale bitmap as current and never activates a second editor.
- Labels, native semantics, predictable focus, and common shortcuts remain when they are inexpensive and share the primary interaction path. They are not allowed to introduce a parallel state machine, duplicate circuit state, distort the visual hierarchy, or make pointer authoring less direct.
- Standard Fluent UI components own their built-in keyboard and ARIA behavior. Do not layer a second roving-tabindex, focus-transfer, expanded-state, or hidden-control model over them; use extra state only when it serves the visible primary workflow.
- Accessibility-only localization catalogs, hidden status mirrors, skip-navigation/focus-routing machinery, and forced-colors or reduced-motion branches are out of scope without a concrete product requirement. Visible labels and one-source native attributes remain preferable to custom substitutes.
- Logic Lab does not claim WCAG conformance, complete screen-reader authoring, full keyboard equivalence, forced-colors parity, or precision touch wiring. These are not release gates unless a later product decision introduces a concrete requirement and funds the corresponding architecture.
- Browser and component tests prove circuit correctness, Canvas geometry, primary user workflows, responsive behavior, and recovery. They do not pin accessibility-specific DOM shape or a second action vocabulary.

## Consequences

The Scene protocol and Geometry Plan no longer carry accessibility recipes, nodes, node identities, semantic paging, or accessibility-specific browser policy. Port anchors and Hit Regions remain because they are primary Canvas interaction data. Localization, text shaping, diagnostics, selection identity, and ordinary HTML semantics remain in their owning modules.

This removes duplicated Razor components, the accessibility-only scene projection, resources, callbacks, focus-navigation rules, policy values, CSS media forks, and tests. It also makes renderer-unavailable behavior intentionally narrower: the user can retry, reload, inspect diagnostics, or preserve their project, but cannot edit through an alternate circuit representation.

This decision does not justify removing useful labels from standard controls or replacing a suitable Fluent UI/native control with an inaccessible custom imitation. The test is architectural cost and product quality, not the mere presence of accessibility metadata.

## Rejected alternatives

- **Maintain a complete semantic fallback editor.** Rejected because it duplicates a spatial editor and its state machine.
- **Select controls primarily for formal accessibility conformance.** Rejected because no such product requirement exists and it can worsen the principal workflow.
- **Remove all semantics and keyboard behavior.** Rejected because native behavior and shared shortcuts are often nearly free and improve the primary product without creating another architecture.

This decision supersedes the accessibility-tree and shared-accessibility-anchor parts of [ADR 0007](./0007-generate-teachingmixed-symbols-declaratively.md). Its declarative geometry and conformance decision remains accepted.

## Sources

- [Microsoft Learn: Use Fluent UI Web Components with Blazor](https://learn.microsoft.com/fluent-ui/web-components/integrations/blazor)
- [ASP.NET Core Blazor components](https://learn.microsoft.com/aspnet/core/blazor/components?view=aspnetcore-10.0)
