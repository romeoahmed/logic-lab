---
status: accepted
date: 2026-08-29
---

# Use one Canvas editor surface

## Context

The earlier design mirrored every Canvas entity, action, focus target, and page in
a semantic DOM editor. Each Scene change then required coordinated hit testing,
paging, fallback actions, focus recovery, resources, policies, and tests in two
representations. Logic Lab has no mandatory accessibility-compliance target;
circuit correctness, drawing correctness, authoring ergonomics, and maintenance
are the primary V1 concerns.

## Decision

- Canvas is the only dense schematic editor. Do not add a parallel DOM circuit
  outline, semantic Scene pager, or alternative keyboard editor.
- Razor owns commands, menus, forms, Inspector content, status, diagnostics, and
  renderer recovery. Use a suitable centrally pinned Fluent UI component, or native
  HTML and CSS when simpler. Custom controls require domain-specific behavior or a
  documented gap in those choices.
- Retain useful labels, native semantics, predictable focus, and common shortcuts
  on the primary interaction path. Fluent UI owns its controls' keyboard and ARIA
  behavior; extra state must serve the visible workflow, rather than duplicate
  roving tabindex, focus transfer, expanded state, or hidden controls.
- Renderer failure replaces Canvas with an unavailable state and actions to retry,
  reload, inspect diagnostics, or preserve the Project. A stale bitmap cannot remain
  presented as current, and failure does not activate a second editor.

Accessibility-only localization, hidden status mirrors, skip-navigation or
focus-routing machinery, and forced-colors or reduced-motion branches require a
new concrete product requirement. V1 does not claim WCAG conformance, complete
screen-reader authoring, full keyboard equivalence, forced-colors parity, or
precision touch wiring; these are not release gates.

## Consequences and alternatives

Scene and Geometry Plan retain Port anchors and Hit Regions for Canvas interaction,
but no accessibility recipes, nodes, identities, paging, or related browser policy.
Text shaping, localization, diagnostics, and selection identity stay in their
owning modules. Browser and component tests verify workflows, projected geometry,
responsive layout, and recovery; Domain and Engine tests own electrical semantics.
Tests do not pin a second action vocabulary or accessibility-specific DOM shape.

A complete semantic fallback editor was rejected because it duplicates the spatial
editor's state machine. Selecting controls primarily for formal conformance was
rejected because V1 has no such requirement. Removing all semantics and keyboard
behavior was also rejected: native behavior and shared shortcuts improve the
primary workflow without creating another editor.

This amends the accessibility-tree and shared-accessibility-anchor requirements in
[ADR 0007](./0007-generate-teachingmixed-symbols-declaratively.md). Declarative
geometry and conformance remain accepted.

## Sources

- [Microsoft Learn: Use Fluent UI Web Components with Blazor](https://learn.microsoft.com/fluent-ui/web-components/integrations/blazor)
- [ASP.NET Core Blazor components](https://learn.microsoft.com/aspnet/core/blazor/components?view=aspnetcore-10.0)
