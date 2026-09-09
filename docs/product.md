# Product and Workbench

> Status: normative V1 product and interaction contract

Logic Lab is a digital-logic workbench for authoring circuits, simulating four-state
behavior, and inspecting signals over Logical Time. Canvas shows the circuit,
Inspector edits the selection and inputs, and Instrument Bay holds waveforms and
diagnostics.

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
│ Undo Redo │ Current simulation actions │ Save │ Project options     │
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
- Instrument Bay has a keyboard-accessible height slider and an expand control.
  Its arrangement is local UI state. Before Simulation, the empty waveform uses a
  compact area.
- With a Project open, Status exposes Logical Time, quiescence, Trace range,
  Compilation, save, and connection independently. The welcome view shows only its
  current message.

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

Canvas pauses input while a changed tool or authored scene is being synchronized
with the browser. It retains focus and resumes when the current tool and revision
are ready; value-overlay refreshes do not interrupt local interaction.

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

During Simulation, the input controls appear first in the Inspector.
Inspector projects the current selection: circuit summary, component contract and
parameters, Net drivers/receivers/value, Junction ownership, definition ports and
references, or common multi-selection properties. It does not duplicate the complete
Project Document.

With no selection, Inspector edits the current Circuit Definition's display name.
With one Component Instance selected, it edits that instance's optional display name;
an empty value restores the component type name. Apply creates one authored revision
and participates in Undo/Redo. A revision or selection change discards the old name
draft; ordinary projection refreshes preserve it.

With no selection, **Circuit definitions** creates a named, empty Circuit Definition
and opens it for editing. The current definition can be deleted only when it is not
the entry and no Component Instance references it. Undo restores the deleted
definition and its identity. The navigator selects existing definitions and changes
the entry through **Set as entry**.

**Public ports** edits the selected Circuit Definition's ordered public contract.
Retaining a Port preserves its identity, direction, and width; its name, position,
and facing remain editable. Replacing a Port creates a new identity, and removing
or replacing one disconnects its old definition-boundary connections. New Ports
have no authored identity until committed. Reordering never identifies Ports by
array position. A selected Canvas Port opens the editor on its page.

**Review port contract** lists every old Port at every call site, including
unconnected Ports. Each must keep or choose a distinct compatible destination,
or explicitly disconnect. Call-site connections can also change while the public
contract remains unchanged. Port edits invalidate the preview. Apply commits the
ordered contract and all call-site migrations together; Undo restores the complete
prior revision. Both lists are paginated, and the complete migration must fit the
Workspace command budget before its call-site rows are expanded. Definition or
revision changes discard the draft.

With no selection, **Add annotation** accepts multiline text, signed grid coordinates,
and text alignment. Creating an Annotation selects it immediately. Selecting an
Annotation exposes the same form for one atomic text/position/alignment replacement;
Canvas dragging and the selection removal action remain available. Invalid text uses
the shared authoring diagnostics. Selection or revision changes discard old drafts,
and each accepted change participates in Undo/Redo.

A single library Component Instance also exposes its contract's complete parameter
set. Numeric fields use decimal digits; Logic Vectors display the most significant
bit first; widths and slices use comma-separated lists. Choice and Memory Image
fields select declared values and existing resources. Apply submits one complete
parameter replacement. Syntax errors stay beside the field, while authored
constraint failures use the shared Diagnostics list and preserve the draft.
Ordinary parameter edits preserve Port shape; changes to Ports require an explicit
contract migration. Parameter drafts follow the same revision and selection
lifetime as name drafts.

**Review port changes** supports replacing the selected Component Instance's target
with a library contract or an existing Circuit Definition, as well as changing its
Port shape. Choosing another library type starts with the Component Palette's
parameter defaults and an existing Memory Image when one is required. The preview
lists every old Port, including unconnected Ports, and proposes same-ID compatible
matches. Each Port must map to a distinct new Port with the same direction and width
or be explicitly disconnected. Destination search uses Port names; the complete new
Port list remains available. Lists are paginated without discarding decisions.
Changing parameters invalidates the preview until reviewed again. Apply publishes
the target, parameters, and connections in one authored revision, preserving the
Component Instance identity. The preview also selects the Symbol Variant: changing
target starts with the profile default, while an unchanged target retains its
compatible override. An incompatible override requires an explicit replacement or
return to the profile default. Undo restores the whole migration.

**Symbol appearance** changes presentation through authored revisions. With no
selection it changes the project-wide indication convention while preserving the
exact Symbol Profile ID/version. With one Component Instance selected it chooses a
registered compatible Symbol Variant or returns to the profile default. Compatibility
comes from the Domain catalog, including the two-input-only distinctive XOR/XNOR
rule. These edits preserve component contracts, parameters, identities, and
connectivity, and participate in Undo/Redo. Drafts expire with selection or revision.

Inspector's **Memory Images** view creates, replaces, and removes authored initial
memory data. Words start at address zero, one word per line, with the most significant
bit first; initial values allow `0`, `1`, and `X`. Each referenced Component Instance
shows its image binding and word/address widths. **Adopt image dimensions** explicitly
updates bindings that still use the edited image when its depth is a power of two.
Apply submits the image and every affected instance's complete parameters as one
revision. Connected Port shape changes are rejected without partial changes. Removal
requires an unreferenced image; rebind its instances first. Memory Image diagnostics
open the corresponding resource. Revision changes discard drafts; switching Inspector
views preserves them. Images larger than the command budget are not expanded into
text, and an unloaded image cannot be accidentally replaced by an empty draft.

The Diagnostics tab owns the complete ordered list and navigation. Inspector shows
only diagnostics attached to the current selection. Both views reveal the same stable
source identity. Project-level diagnostics clear the Canvas selection and open
Inspector's general authoring controls; resource diagnostics open their resource.
Earlier-revision or unavailable sources remain visible without a navigation action.

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
The command bar shows the Simulation actions available in the current state: Compile,
Start simulation, Step, Run, Pause, Restart simulation, Apply changes (Hot Swap), or
Close simulation. Project
options groups import, export, and saving a Sandbox to an account. Save remains visible
for an already saved Project.

Undo and Redo remain visible while a Project is open, with unavailable directions
disabled. Each action restores one retained Project Revision and updates the Canvas
and selection. History navigation requires Compilation before further Simulation
advances; an existing Session keeps its original Artifact and observations. A new
edit after Undo replaces the Redo branch. History controls are disabled during Run
and while another command is being processed.

| Situation          | Allowed behavior                                           |
| ------------------ | ---------------------------------------------------------- |
| clean and compiled | edit, compile, run, save, transfer                         |
| changed and stale  | edit, compile, save; run only after Restart or Hot Swap    |
| compiling          | edit and replace the pending request; no run or import     |
| running            | Pause is the only authoring-state transition               |
| detached           | local pan/zoom only; no command pretends to commit offline |
| save conflict      | edit and observe; recovery actions replace ordinary save   |

The initial workbench offers an empty Sandbox or a complete built-in example. Opening
an example publishes an already compiled Sandbox; the user can start Simulation
immediately, edit it normally, and export it as a `.logiclab` project. Examples have
ordinary topology and authored state, with no example-specific Runtime behavior. The
welcome view keeps these choices visible and introduces navigation and Simulation
status after a Project is open.

During a paused, current Simulation, the Inspector offers independent binary input
drafts for the viewed Circuit Definition occurrence. Values use the exact input width,
most significant bit first, and accept `0/1/X/Z`. “Apply inputs” schedules one atomic
batch at the next Logical Time; it does not imply that Step has already consumed the
batch. Restart restores authored input drafts. A reusable definition without a selected
occurrence cannot schedule inputs. At the maximum Logical Time there is no future tick
to schedule.

Import exposes upload, package validation, Genesis, Compilation, and publication as
named phases. Failure leaves the current Workspace unchanged. Export separates
preparation from download availability.

An unfinished saved or imported circuit opens for editing with its Compilation
diagnostics selected. The user can repair and compile it before starting Simulation;
ordinary circuit errors do not lock the Project out of the editor.

A save conflict offers Reload remote, Keep as copy, and Export local; overwrite is not
the default. Stale Compilation leaves Canvas editable but disables Step and Run. The
user can Compile, discard edits through history, or observe the old paused Session
under an explicit old-revision label.

Empty and failure states say what happened and offer one real next action. Large work
shows a named phase and Cancel, never a fabricated percentage. Disconnect freezes the
last acknowledged semantic overlay while preserving local navigation.

## Responsive and localization behavior

Wide desktop shows the full three-column workbench and Instrument Bay. Laptop keeps
the Inspector pinned and the Component Palette in a toggleable overlay. Narrow layouts
use exclusive, toggleable overlays for both panels; Escape closes the active panel and
returns focus to its toggle. Selecting a Component closes the Palette overlay.
The active workbench allocates the remaining viewport height after the site header to
Canvas, Instrument Bay, and Status Strip. Instruments can expand into the Canvas area;
returning to the circuit restores the Canvas. Revealing a diagnostic or Probe source
also closes panel overlays so the source is visible and the Canvas is usable.
When the available height cannot fit usable Canvas and Instrument Bay regions, the
workbench scrolls vertically without overlapping controls or clipping the Status Strip. Narrow layouts
keep signal labels beside the waveform and let the waveform toolbar scroll horizontally.
They prioritize Canvas review, Probe, Step, Run, and expanded waveform; dense authoring may
remain unsupported. The smallest layout uses the expand button instead of the height slider.
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
