---
version: v1
name: Logic Lab Instrument Enamel
description: A quiet digital-logic laboratory bench that connects authored topology to observable time.
colors:
  primary: "#08788C"
  bench: "#E7EEF1"
  panel: "#F7FAFB"
  canvas: "#FFFFFF"
  ink: "#172A33"
  muted: "#536871"
  transition: "#A85D00"
  unknown: "#7053A5"
  danger: "#B4232C"
typography:
  ui:
    fontFamily: Atkinson Hyperlegible Next
    fontSize: 16px
    fontWeight: 400
    lineHeight: 1.5
  data:
    fontFamily: IBM Plex Mono
    fontSize: 13px
    fontWeight: 400
    lineHeight: 1.4
spacing:
  base: 4px
  major: 8px
omitted:
  - section: rounded
    reason: Fluent UI owns chrome radii; schematic shapes are semantic geometry.
  - section: components
    reason: Component-level token overrides await implementation visual qualification.
---

# Logic Lab Workbench

> Status: normative V1 visual and interaction contract
> Platform: Blazor Web App, per-page Interactive Server editor, Fluent UI Blazor v5 RC

The Phase A `/editor` route implements the first accessible Sandbox tracer. Layout breadth, Canvas interaction, instruments, persistence, and qualification requirements in this document remain target V1 behavior until their implementation-plan slices complete.

This document owns the workbench experience. System ownership lives in [Architecture](./ARCHITECTURE.md), static schematic geometry in [Diagram Presentation](./docs/specs/diagram-presentation.md), browser records in the [Browser Adapter Contract](./docs/contracts/browser-adapters.md), and Canvas/input/resource behavior in [Browser Runtime](./docs/specs/browser-runtime.md).

## Overview

Logic Lab is a **digital logic laboratory bench** for learners and engineers who need to connect circuit topology, four-state behavior, and time. The editor's single job is to make a circuit understandable while it is being authored and run.

The schematic is the specimen, the command strip is the control surface, the Inspector is the lab notebook, and the Instrument Bay is the logic analyzer. The result must not resemble a SaaS dashboard, a code editor with gate icons, a neon oscilloscope, or a skeuomorphic panel.

The signature interaction is the **Probe Spine**: one stable Probe identity appears at its Net, in the Inspector, and beside its waveform row. Color, pattern, label, and navigation reinforce the relation; color never carries identity alone.

### Design direction

The visual direction is **instrument enamel**: cool low-chroma framing surfaces, a bright schematic field, graphite technical ink, and restrained signal accents. The aesthetic risk is the Probe Spine itself—a visible vertical continuity between topology and waveform—while the surrounding chrome remains quiet.

## Colors

The YAML color tokens are normative; implementation maps them to the CSS aliases below.

| Token | CSS alias | Role |
|---|---|---|
| `colors.bench` | `--ll-bench` | application frame and inactive gutters |
| `colors.panel` | `--ll-panel` | panels and Instrument Bay |
| `colors.canvas` | `--ll-canvas` | schematic field |
| `colors.ink` | `--ll-ink` | text, symbols, and inactive wires |
| `colors.muted` | `--ll-muted` | secondary text and disabled ink |
| `colors.primary` | `--ll-signal` | focus, selection, Probe family seed |
| `colors.transition` | `--ll-transition` | committed time transition |
| `colors.unknown` | `--ll-unknown` | `X` and indeterminate emphasis |
| `colors.danger` | `--ll-danger` | error and destructive confirmation only |

Logic Value recipes use redundant cues:

| Value | Recipe |
|---|---|
| `0` | thin ink stroke plus textual value where labeled |
| `1` | stronger signal stroke plus textual value |
| `X` | violet, diagonal hatch or dashed center, and `X` |
| `Z` | muted open/double-dash stroke and `Z` |

Selection uses an outer focus halo and never erases live-value encoding. Diagnostics use markers and underlines instead of repainting the whole symbol.

## Typography

| Role | Family | Size | Use |
|---|---|---:|---|
| UI and prose | Atkinson Hyperlegible Next; Noto Sans SC fallback | 16px / 1.5 | commands, forms, help, diagnostics |
| dense data | IBM Plex Mono; Noto Sans Mono CJK fallback | 13px / 1.4 | logical time, vectors, addresses, codes |
| IEEE symbol text | versioned Noto Sans subset | Geometry Plan metric | Geometry Plan labels and export |

Fonts are self-hosted, licensed, fingerprinted, and subset after localization coverage is known. Time and address columns use tabular numerals. UI density never changes IEEE symbol metrics.

## Layout

- Base spacing is 4 CSS pixels; major rhythm is 8.
- Straight resize seams separate Canvas, side panels, and Instrument Bay.
- Visual glyph size and accessible hit size are separate tokens.

### Workbench layout

```text
┌──────────────────────────────────────────────────────────────────────────┐
│ Project · Revision │ Undo Redo │ Compile │ Step Run Pause │ Save Transfer│
├──────────────────────────────────────────────────────────────────────────┤
│ Definition tabs / hierarchy breadcrumb / entry marker                   │
├──────────────┬───────────────────────────────────┬──────┬────────────────┤
│ Library      │                                   │Probe │ Inspector      │
│ search       │          Circuit Canvas           │Spine │ identity       │
│ categories   │      topology and live overlay    │      │ properties     │
│ hierarchy    │                                   │      │ context help   │
├──────────────┴───────────────────────────────────┴──────┴────────────────┤
│ Instrument Bay: Waveform │ Truth Table │ K-map │ Analysis │ Diagnostics │
├──────────────────────────────────────────────────────────────────────────┤
│ logical time · quiescence · trace range · compile/save · connection     │
└──────────────────────────────────────────────────────────────────────────┘
```

The Canvas owns the largest flexible region. Library and Inspector collapse independently. The Instrument Bay resizes vertically and remembers its arrangement as a Web preference or browser recovery value, not Project Document or Editor Workspace state.

Definition tabs show open Circuit Definitions; the hierarchy breadcrumb shows the current instance path; the entry marker distinguishes editing a definition from observing one elaborated instance. Closing a tab never deletes a Circuit Definition.

## Elevation & Depth

Depth comes from tonal separation among Bench, Panel, and Canvas surfaces. Shadows are reserved for popovers, drag ghosts, menus, and dialogs; permanent work regions use seams and contrast rather than stacked cards.

## Shapes

Schematic shapes, Port anchors, and hit regions come from the closed [Geometry Plan value contract](./docs/specs/diagram-presentation.md#42-geometry-plan). Application chrome follows Fluent UI geometry until visual qualification records a project override. Straight resize seams and restrained outlines keep the instrument-like structure; decorative hardware is absent.

## Do's and Don'ts

- Do keep the Canvas dominant and the surrounding chrome quiet.
- Do repeat Probe identity with color, pattern, label, and navigation.
- Do preserve non-color encodings for Logic Values, selection, diagnostics, and Trace Gaps.
- Don't use nested decorative cards, gradients, glowing wires, fake screws, ambient particles, or moving electrons.
- Don't replace distinct compile, save, Session, analysis, Trace, and connection states with one global spinner.
- Don't animate a causal path without bounded causal evidence from the Simulation Runtime.

## Region ownership

- The command strip owns project actions and concurrent-state visibility.
- Library and hierarchy navigation own discovery, never Project mutation by inspection.
- Canvas owns dense topology, overlays, focus, and local Transient Preview.
- Inspector owns selected-source facts and contextual actions.
- Instrument Bay owns waveform, Truth Table, Karnaugh Map, Analysis Review, and complete Diagnostics views.
- The status strip always exposes Logical Time, quiescence, Trace range, Compilation, save, and connection state.

[Web Host](./docs/specs/web-host.md) owns render lifecycle and dependency lifetimes. [Browser Runtime](./docs/specs/browser-runtime.md) owns frame-rate implementation. This document names only user-visible region behavior.

## Tool state machine

Exactly one primary tool is active:

| Tool | Primary gesture | Commit |
|---|---|---|
| Select | click, marquee, move | one selection or geometry Edit Transaction |
| Place | choose catalog item, position ghost | one place intent; remains active only when pinned |
| Wire | start at Port/Junction, preview orthogonal route | one explicit Net/Junction/connectivity intent |
| Probe | click an eligible Net | add or remove one Probe |
| Pan | drag Canvas | local viewport only |

Space temporarily activates Pan and then returns to the previous tool. Escape cancels the current preview before clearing selection. Pointer capture ends on commit, cancel, lost capture, disconnect, or tool change. A cancelled gesture emits no Workspace command.

Snapping is visible and deterministic. Route previews are orthogonal, and crossings never create Junctions. Holding the documented modifier disables geometry snapping without changing electrical rules.

## Scene layers and ownership

Render back to front:

1. grid and print guides;
2. static Wire Geometry;
3. component Geometry Plans, then authored Annotations;
4. Definition Ports, Junctions, and static Net hit/navigation anchors;
5. live-value and Probe overlays;
6. selection, hover, and keyboard focus;
7. route, move, and placement preview;
8. diagnostics and contextual handles;
9. HTML menus, tooltips, and dialogs.

Diagram Presentation owns layers two through four as a reproducible static projection. Web owns live, Probe, selection, focus, and diagnostics overlays; the browser owns Transient Preview. Workspace returns semantic changed identities, not renderer patches.

Hit priority is Port, handle, component body, Wire Geometry, then Canvas. Every hit returns stable source identity.

### Canvas behavior

The Canvas is an instrument surface, not an image viewer. It remains crisp across browser zoom, schematic zoom, panel resize, and display-density changes without changing authored coordinates. Pan, zoom, hover, snapping, and route preview react locally; a network round trip is visible only when one completed semantic action awaits acknowledgment.

Resize preserves the world point under focus or the viewport center and never performs an unsolicited fit. A complete definition switch may fit only when that definition has no saved browser viewport. HTML owns menus, tooltips, text entry, and confirmations so Canvas pixels never become the only route to an action.

If the bitmap renderer is unavailable, too large for active Browser Policy, or rejected by presentation validation, the semantic circuit outline, Inspector, Diagnostics, and recovery actions remain usable. The UI does not show a blank rectangle or pretend that the last bitmap represents the new Project Revision. Exact sizing, density, frame, input, fallback, and cleanup rules are in [Browser Runtime](./docs/specs/browser-runtime.md).

## Inspector and diagnostics

Inspector content is selected by semantic state:

| Selection | Inspector content |
|---|---|
| none | current Circuit Definition, entry status, profile, compile summary |
| Component Instance | identity, Component Contract, parameters, Ports, state initialization, symbol variant |
| Net | stable identity, width, Drivers, receivers, live value, Probe action |
| Junction | owning Net, connected branches, delete/split consequences |
| Circuit Definition | public Ports, instances, hierarchy references, definition diagnostics |
| multi-selection | common editable properties and explicit mixed values |

The Instrument Bay Diagnostics tab is the complete ordered list and primary navigation surface. Inspector shows only diagnostics attached to the current selection. Activating either view selects and reveals the same source location.

## Probe Spine and waveform

A Probe has stable identity independent of display order. Removing or reordering probes can change visible row numbers; it never changes Probe identity or silently rebinds a Trace.

Each visible Probe repeats a color/pattern/short-label tuple at:

- the Net anchor;
- the Probe Spine;
- Inspector observation details;
- the waveform row label and cursor readout.

Both directions provide `Go to Net` and `Reveal waveform`. Hot Swap preserves a Probe only when Source Map identity remains compatible; otherwise the row becomes unresolved with a recovery action.

A valid Probe can remain electrically resolved when its authored Net has no drawable anchor. In that case waveform and Inspector observation remain available, while scene navigation reports unavailable and no marker is invented.

Waveform anatomy includes:

- reorderable, keyboard-navigable Probe rows;
- sticky labels and radix/vector-format controls;
- logical-time ruler and one or two measurement cursors;
- zoom, pan, fit selection, and explicit live-follow mode;
- visible Trace Gap bands that cannot be crossed by a flat line;
- `0/1/X/Z` vector display and transition detail on focus;
- summary-resolution indicator when viewing aggregated data.

Historical navigation pauses live-follow but not Simulation. Returning to live is an explicit action.

## Commands and concurrent states

Commands use the same verb in action and result: `Save` becomes `Saved`, `Run` becomes `Running`, and `Pause` becomes `Paused`.

| State | Edit | Compile | Step/Run | Save | Analyze | Import |
|---|---:|---:|---:|---:|---:|---:|
| clean and compiled | yes | yes | yes | when durable change exists | eligible region only | yes |
| changed / compile stale | yes | yes | no; Restart/Hot Swap after compile | yes | no | yes |
| compiling | yes; newest revision wins next request | cancel/replace | no | yes | no | no |
| running | no; Pause first | no | Pause only | yes | observe only | no |
| analysis running | yes | yes | yes | yes | cancel/observe | no |
| detached/reconnecting | local pan/zoom only | no | no | no | observe after attach | no |
| save conflict | yes | yes | compiled Session may continue | recovery actions only | yes | export recovery only |

Compile, save, Simulation, analysis, Trace, and connection states retain distinct indicators. A generic global spinner is forbidden.

## Review and recovery flows

### Simplification Proposal

Analysis Review shows source boundary, original and replacement diagrams, Care Contract, Cost Profile comparison, proof method, and any limitations. `Accept replacement` is available only while the source Project Revision is current. Acceptance creates one Edit Transaction; Undo restores the original region.

### Import and export

Import shows upload, package validation, Project Genesis, Compilation, and new-Workspace publication as named phases without invented percentages. Failure identifies the phase and leaves the current Workspace untouched. Success opens the imported Project in a new Workspace. Export shows preparation and download availability separately.

### Save conflict

A conflict states that another Workspace saved first and offers `Reload remote`, `Keep as copy`, and `Export local`. It never presents overwrite as the default action.

### Stale Compilation

The Canvas remains editable, but Step and Run are disabled. The user can Compile, discard edits through Transaction History, or keep observing the old paused Session with an explicit old-revision label.

## Empty and failure states

- Empty Project: “Place a component to begin.” Primary action opens Library search.
- Empty Trace: “Add a probe, then step or run the circuit.”
- Ineligible analysis: name the exact eligibility reason and link to a suitable instrument.
- Prerender: stable shell and project identity; no fake-gate skeleton.
- Large work: named phase and Cancel action; no synthetic completion percentage.
- Disconnect: freeze the last acknowledged semantic overlay; local pan/zoom remains available; authoring never pretends to commit offline.

Errors use active, specific language and one available recovery action. They do not apologize or say only “Something went wrong.”

## Keyboard and accessibility

The target is WCAG 2.2 AA.

- Landmarks and skip links reach Library, Canvas, Inspector, Instrument Bay, and status.
- The command strip follows the WAI-ARIA toolbar pattern; arrow keys move within it and Tab exits.
- The Canvas has one primary focus target plus synchronized focusable fallback descendants and an Inspector path.
- Keyboard users can enumerate components and Ports, connect, move, delete, add Probes, and navigate to waveform rows.
- Focus returns to the nearest logical owner when a selected entity is deleted or a panel closes.
- Active-low, `0/1/X/Z`, selection, diagnostics, and Trace Gaps have non-color encodings.
- The current focusable Canvas regions map one-to-one to semantic fallback actions; every authored entity remains reachable through deterministic paging and topology navigation.
- Reduced motion disables value-change pulses and panel transitions.
- Browser text zoom and schematic zoom are independent.
- English, Simplified Chinese, long-label, and bidirectional-text fixtures are included before localization release.

## Responsive behavior

| Class | Product behavior |
|---|---|
| wide desktop | three-column authoring, visible Probe Spine, resizable Instrument Bay |
| laptop | one pinned side panel, one overlay drawer, full keyboard/mouse authoring |
| narrow/touch | Canvas-first review, Probe, Step, Run, and full-screen waveform; panels become sheets |

V1 does not promise precision touch wiring or dense property editing on narrow screens. Responsive layouts never hide save state, diagnostics, logical time, or connection state.

## Motion

After a server-acknowledged Session commit, changed Probe markers and corresponding waveform rows may pulse once. The UI does not animate a “causal path” unless a future Runtime contract supplies bounded causal evidence; changed values alone do not prove causality.

Pointer previews follow locally without decorative easing. There is no ambient animation.

## Fluent UI Blazor qualification

Use Fluent UI Blazor v5 RC only for Web chrome. Geometry Plans and Scene code never depend on Fluent DOM or CSS internals. [Architecture](./ARCHITECTURE.md#82-net-and-dependencies) owns package containment and qualification; the exact build is implementation state, not a design invariant.

## Verification

| Layer | Evidence |
|---|---|
| pure Web projection | command availability, labels, state mapping, diagnostics, proposal freshness |
| Razor/Fluent | bUnit forms, menus, dialogs, focus, empty/failure states, prerender handoff |
| browser adapters | [Browser Runtime](./docs/specs/browser-runtime.md) and [Browser Adapter](./docs/contracts/browser-adapters.md) conformance |
| browser | Playwright pointer, keyboard, zoom, resize, reconnect, transfer, and conflict workflows |
| accessibility | automated audit plus keyboard and screen-reader task scripts |
| visual | Geometry Plan/SVG goldens and a small curated chrome screenshot set |
| performance | browser traces on a versioned circuit corpus; measured thresholds only |

A screenshot cannot prove electrical semantics, and a Razor test cannot prove Canvas gestures.

## Sources

- [Fluent UI Blazor v5](https://v5.fluentui-blazor.net/)
- [Blazor rendering performance](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/rendering?view=aspnetcore-10.0)
- [Blazor JavaScript interop](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0)
- [HTML Canvas](https://html.spec.whatwg.org/multipage/canvas.html#the-canvas-element)
- [Pointer Events](https://www.w3.org/TR/pointerevents3/)
- [WCAG 2.2](https://www.w3.org/TR/WCAG22/)
- [WAI-ARIA Toolbar Pattern](https://www.w3.org/WAI/ARIA/apg/patterns/toolbar/)
