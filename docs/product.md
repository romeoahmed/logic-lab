# Product and Workbench

> Status: normative V1 product and interaction contract

Logic Lab is a digital-logic workbench that connects authored topology, four-state
behavior, and Logical Time. The schematic is the specimen, the command strip is the
control surface, the Inspector is the notebook, and the Instrument Bay is the logic
analyzer.

[Architecture](./architecture.md) owns system structure, [Diagram Presentation](./specs/diagram-presentation.md)
owns static geometry, [Browser Runtime](./specs/browser-runtime.md) owns Canvas and
input behavior, and [Delivery](./delivery.md) records completion.

## Product priorities

When goals conflict, prefer:

1. electrical and drawing correctness;
2. direct Canvas-first authoring with progressive disclosure;
3. coherent, maintainable implementation;
4. responsive and localized presentation; then
5. inexpensive native semantics that do not create a parallel editor.

Canvas is the only dense circuit editor. Labels, native controls, predictable focus,
and common shortcuts are useful when they share the primary path; they do not justify
a second circuit tree, action model, or state machine. V1 makes no claim of complete
screen-reader, keyboard-only, precision-touch, forced-colors, or WCAG parity.

Boolean explanation, Truth Tables, Karnaugh Maps, and automated simplification are
outside V1 and live only in the [future proposal](./future/boolean-analysis.md).

## Visual language

The direction is **instrument enamel**: quiet cool framing surfaces, a bright
schematic field, graphite technical ink, and restrained signal accents. Exact colors,
spacing, type sizes, and responsive breakpoints are executable CSS facts. This
document owns their roles:

- Bench frames the application; Panel separates controls; Canvas remains the clearest
  surface.
- Signal emphasizes active values and focus; Transition marks committed time changes;
  Unknown distinguishes `X`; Danger is reserved for errors and destructive actions.
- `0/1/X/Z`, selection, diagnostics, and Trace Gaps use text, pattern, weight, or
  shape in addition to color.
- Permanent regions use seams and tonal contrast. Shadows are limited to transient
  overlays such as menus, dialogs, and drag ghosts.
- UI text favors a highly legible sans face; vectors, addresses, codes, and Logical
  Time use tabular monospace. Symbol text follows the Geometry Plan fingerprint.

The signature interaction is the **Probe Spine**: one Probe identity appears at its
Net, in the Inspector, and beside its waveform. Color, pattern, short label, and
two-way navigation reinforce the relation.

## Workbench regions

```text
┌──────────────────────────────────────────────────────────────────────┐
│ Project · history │ Compile │ Step Run Pause │ Save Import Export   │
├──────────────────────────────────────────────────────────────────────┤
│ Definition navigation and hierarchy breadcrumb                      │
├────────────┬─────────────────────────────┬──────┬───────────────────┤
│ Library    │                             │Probe │ Inspector         │
│ hierarchy  │       Circuit Canvas        │Spine │ facts and actions │
│ search     │                             │      │                   │
├────────────┴─────────────────────────────┴──────┴───────────────────┤
│ Instrument Bay: Waveform | Diagnostics                              │
├──────────────────────────────────────────────────────────────────────┤
│ time · quiescence · trace · compile/save · connection               │
└──────────────────────────────────────────────────────────────────────┘
```

- Canvas owns the largest flexible region.
- Library, definition navigation, and Inspector support discovery and editing without
  deriving domain identity from display order.
- Instrument Bay resizes vertically; its arrangement is browser preference, not
  Project Document or Workspace state.
- Status always exposes Logical Time, quiescence, Trace range, Compilation, save, and
  connection independently. A generic global spinner is not enough.

## Tools and Canvas

Exactly one primary tool is active:

| Tool   | Gesture                            | Semantic result                  |
| ------ | ---------------------------------- | -------------------------------- |
| Select | click, marquee, move               | selection or one geometry edit   |
| Place  | choose and position a catalog item | one placement intent             |
| Wire   | route from a Port or Junction      | one explicit connectivity intent |
| Probe  | choose an eligible Net             | add or remove one Probe          |
| Pan    | drag the viewport                  | local browser state only         |

Space temporarily pans. Escape cancels the current preview before clearing selection.
Pointer capture ends on commit, cancel, lost capture, disconnect, or tool change. A
cancelled gesture emits no Workspace command.

Snapping is visible and deterministic. Routes are orthogonal; crossings never create
Junctions. Pan, zoom, hover, snapping, and preview react locally. Only a completed
semantic intention crosses the circuit.

Render back to front: grid; Wire Geometry; components and annotations; Definition
Ports, Junctions, and anchors; live and Probe overlays; selection and focus; transient
preview; diagnostics and handles; HTML overlays. Diagram Presentation owns static
layers, Web owns semantic overlays, and the browser owns transient preview.

Canvas remains crisp across browser zoom, schematic zoom, resizing, and display
density without changing authored coordinates. A resize preserves the focused world
point or viewport center. Switching definitions fits only when no browser viewport is
known.

If the renderer is unavailable, oversized, or given invalid presentation data, replace
Canvas with a concise unavailable state and Retry, Reload, Diagnostics, or Preserve
Project actions. Never display a blank or stale bitmap as current, and never activate
a fallback editor.

## Inspector, diagnostics, and waveform

Inspector projects the current selection: circuit summary, component contract and
parameters, Net drivers/receivers/value, Junction ownership, definition ports and
references, or common multi-selection properties. It does not duplicate the complete
Project Document.

The Diagnostics tab owns the complete ordered list and navigation. Inspector shows
only diagnostics attached to the current selection. Both views reveal the same stable
source identity.

Each Probe repeats its identity cue at the Net, Probe Spine, Inspector, and waveform
row. Reordering rows never changes identity. Hot Swap preserves a Probe only when the
Source Map remains compatible; otherwise the row is explicitly unresolved. A valid
Probe without a drawable anchor remains observable, but scene navigation reports
unavailable instead of inventing geometry.

Waveform provides reorderable rows, radix/vector controls, Logical Time ruler,
measurement cursors, zoom/pan, explicit live-follow, Trace Gap bands, `0/1/X/Z`
detail, and a summary-resolution indicator. Historical navigation pauses live-follow,
not Simulation; returning live is explicit.

## Commands and recovery

Commands keep one verb across action and result: Save/Saved, Run/Running,
Pause/Paused. Compile, save, Simulation, Trace, and connection state remain separate.

| Situation          | Allowed behavior                                           |
| ------------------ | ---------------------------------------------------------- |
| clean and compiled | edit, compile, run, save, transfer                         |
| changed and stale  | edit, compile, save; run only after Restart or Hot Swap    |
| compiling          | edit and replace the pending request; no run or import     |
| running            | Pause is the only authoring-state transition               |
| detached           | local pan/zoom only; no command pretends to commit offline |
| save conflict      | edit and observe; recovery actions replace ordinary save   |

Import exposes upload, package validation, Genesis, Compilation, and publication as
named phases. Failure leaves the current Workspace unchanged. Export separates
preparation from download availability.

A save conflict offers Reload remote, Keep as copy, and Export local; overwrite is not
the default. Stale Compilation leaves Canvas editable but disables Step and Run. The
user can Compile, discard edits through history, or observe the old paused Session
under an explicit old-revision label.

Empty and failure states say what happened and offer one real next action. Large work
shows a named phase and Cancel, never a fabricated percentage. Disconnect freezes the
last acknowledged semantic overlay while preserving local navigation.

## Responsive and localization behavior

Wide desktop shows the full three-column workbench and Instrument Bay. Laptop keeps
one pinned side panel and one overlay drawer. Narrow layouts prioritize Canvas review,
Probe, Step, Run, and full-screen waveform; dense authoring may remain unsupported.
No layout hides save state, diagnostics, Logical Time, or connection state.

English, Simplified Chinese, long-label, bidi-content, text zoom, browser zoom, and
display-density fixtures qualify layout. Browser text zoom and schematic zoom remain
independent. Pointer preview has no decorative easing or ambient animation; committed
Probe and waveform changes update together.

Use the pinned Fluent UI package for standard chrome when it fits. Prefer native HTML
and CSS for a simpler platform primitive. Custom controls require domain-specific
interaction or a documented gap. Geometry and Scene code never depend on Fluent DOM
or CSS internals.

## Verification

Pure projections prove command availability and state mapping; bUnit proves Razor
forms and recovery; browser-adapter tests prove exchanged records; Playwright proves
primary gestures, zoom, resize, reconnect, transfer, and conflict flows; curated
screenshots and Geometry Plan goldens prove visual integrity. A screenshot cannot
prove electrical semantics, and a Razor test cannot prove Canvas input.

Platform sources are retained in [browser](./research/blazor-web-platform.md) and
[diagram](./research/diagram-presentation.md) research notes.
