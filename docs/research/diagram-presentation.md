# Diagram Presentation Research

> Scope: IEEE 91A evidence, declarative symbol generation, reference implementations, and workbench design rationale
> Authority: evidence and rejected alternatives; normative rules are [Diagram Presentation](../specs/diagram-presentation.md) and [Workbench](../../WORKBENCH.md)

## 1. Research method

The optional untracked local file `00027895.pdf` is the combined publication *IEEE Standard Graphic Symbols for Logic Functions (Including and incorporating IEEE Std 91a-1991)*. `pdftotext -layout` supports clause search, while the original pages remain necessary for figures and proportions. PDF page numbers are eleven greater than printed standard pages in the relevant body.

The research distinguishes:

- **standard fact**: wording and figure references in IEEE 91/91A;
- **implementation evidence**: fixed source from existing teaching tools;
- **Logic Lab choice**: the TeachingMixed profile and workbench direction.

No IEEE plate, GPL drawing code, coordinate table, or asset is copied.

## 2. Standard composition and layout

IEEE 91A clause 2.1.1 describes a symbol as one or more outlines, qualifying symbols where applicable, and input/output connection lines. Labels, unrelated glyphs, and outlines require clear separation; Port labels are vertically centered with clearance. Rectangular aspect ratio is explicitly flexible in clause 2.2.

Clause 1.6 distinguishes lettering and glyph orientation. Some marks follow signal flow while text follows reading direction, so rotating one bitmap cannot satisfy every rule. Clause 3.4 establishes the usual left-to-right flow and arrow requirements when direction is unclear.

The implication is structural: outline, Port groups, labels, qualifiers, dependency notation, and orientation are separate model elements that must participate in layout.

## 3. Distinctive and rectangular outlines

Chapter 5 permits several traditional distinctive basic-gate outlines. Symbols 5.1-1 and 5.1-3 show OR and AND alternatives. Their notes say distinctive shapes are not preferred in the related IEC standard but are not contradictory, and discourage composing them into complex symbols. Clause 2.3.1 similarly discourages distinctive shapes in embedded or abutted complex elements.

Symbol 5.1-11 is specifically a two-input exclusive-OR. For three or more inputs, the odd/even rectangular functions in 5.1-9 and 5.1-10 avoid misrepresenting “exactly one input” as arbitrary parity.

These facts support the selected mixed profile:

- familiar distinctive AND, OR, two-input XOR, Buffer, and NOT;
- parameterized rectangles for parity with larger fan-in, steering, arithmetic, sequential, memory, and user-defined logic.

The choice is not a claim that every project symbol is standardized.

## 4. Qualifiers and dependencies

IEEE 91A treats qualifiers as semantics, not decoration:

- clause 3.1.1 prevents mixing negation and direct-polarity conventions across one diagram, except for its stated internal-negation case;
- Symbols 3.1-1 to 3.1-11 define negation, polarity, and dynamic inputs;
- Symbols 3.3-8 and 3.3-12 define three-state output and enable;
- Symbols 3.3-13 to 3.3-22 cover D/J/K/R/S/T, shift, and count qualifiers;
- Symbols 3.3-25 and 3.3-26 require meaningful bit-weight order;
- Table 4.1 defines dependency categories, and clause 4.3.1 makes dependency numbering and application order meaningful;
- clauses 4.4.3 and 4.4.4 constrain input and output label ordering.

Therefore `activeLow`, `clockEdge`, `threeState`, Port grouping, and dependency relationships must exist before drawing. A file name such as `falling-clock-active-low.svg` cannot own those rules safely.

## 5. Sequential and memory symbols

Clause 5.9 describes bistable functions through input/output qualifiers rather than one universal internal glyph. The examples distinguish latches, edge-triggering, pulse behavior, and control dependencies.

Symbols 5.13-1 to 5.13-18 cover registers, shift registers, counters, direction, load, and mode relationships. Memory Symbols 5.14-1 onward require function and size information such as ROM/RAM and address-count by word-width; address dependency is defined in clause 4.3.11.

These families vary with Port groups, width, labels, clock/control marks, and array presentation. A pre-drawn `RAM.svg` cannot represent the contract space while keeping Port anchors authoritative.

## 6. Annex A

Annex A is explicitly informative. It gives recommended proportions in units of text height `H`, including spacing, common-control geometry, common-output double lines, and distinctive-gate proportions.

Logic Lab can encode these as a versioned Metric Set and test tolerances, but a one-pixel browser antialiasing difference is not a normative violation. Visual glyph size and larger interaction hit area remain independent.

## 7. Declarative generation rationale

The parameter space is a product of orientation, fan-in, bit width, Port groups, clock edge, active-low controls, dependencies, localized text, and array form. Pre-drawn assets cause:

1. duplicate Port-coordinate truth;
2. untestable qualifier and dependency ordering;
3. text overflow under localization;
4. divergence among SVG, Canvas, print, and high-DPI assets;
5. scattered fixes when a template or standard interpretation changes.

The stable flow is:

```text
Component Contract + presentation request
  -> registered Symbol Variant
  -> structured qualifiers and Port groups
  -> constraint solve using actual font metrics
  -> immutable Geometry Plan
  -> SVG | Canvas | print | hit test | accessibility projection
```

Caching the complete Geometry Plan key is safe because it is derived. Caching or persisting a hand-edited picture as the contract is not.

## 8. Reference implementation evidence

Logisim Evolution separates gate semantics from appearance selection: its gate painter chooses shaped or rectangular drawing without changing the simulation component, while sequential components recompute bounds and Ports for appearance variants. This proves separation is practical but also shows that a shape change affects more than path artwork.

Digital uses an IEEE-oriented ShapeFactory that maps familiar gates to distinctive shapes while RAM and complex components remain rectangular. Its visual elements persist semantic attributes and position, regenerate transient shapes, take pins from generated geometry, and support more than one graphics backend.

Both projects are GPL-3.0. Logic Lab adopts only observed architecture patterns and independently implements its data, algorithms, code, assets, and package format.

## 9. Workbench directions considered

### Dark oscilloscope

A near-black interface with bright signal colors makes live values dramatic but competes with dense symbols, encourages glow effects, and fatigues long authoring sessions. It also resembles a generic developer tool more than a teaching bench.

### Paper schematic

A warm drafting-paper surface gives diagrams familiarity but turns every interaction state into markup on a document and drifts toward a common cream/serif template. It weakens the distinction between editable apparatus and export.

### Instrument enamel

Cool framing surfaces, white schematic field, technical ink, and one Probe Spine fit physical lab instruments without copying their decoration. The design spends its expressiveness on the topological-to-temporal Probe relation and keeps panels disciplined. This direction was selected.

## 10. Probe Spine rationale

The main conceptual difficulty is not placing a gate; it is understanding that one stable Net becomes a time series. Repeating Probe identity at the Net, Spine, Inspector, and waveform row provides a direct perceptual bridge.

Probe ID remains stable while display order may change. Redundant color, pattern, text, and bidirectional navigation avoid color-only identity. Plain schematic export omits the editor overlay; annotated teaching export marks it as an extension.

## 11. Browser and accessibility evidence

SVG 2 hit testing is based on rendered geometry and `pointer-events`; `title` and `desc` provide textual description. Graphics ARIA defines graphics roles, but real assistive-technology support still needs product testing.

The HTML standard requires Canvas fallback content that conveys equivalent purpose and maps interactive regions to focusable fallback areas. One `aria-label` on the entire Canvas is insufficient for authoring.

Consequences:

- Geometry Plan supplies exact visual paths and enlarged hit regions;
- SVG groups expose meaningful identity and description;
- Canvas is paired with synchronized focusable fallback descendants and actions;
- keyboard navigation follows circuit topology and stable Port order;
- logic states, active-low, selection, and Trace Gaps never rely on color alone.

Route preview and scene indexing remain evidence-gated implementation choices. Orthogonal A* may search `(grid point, arrival direction)` only with nonnegative costs and an admissible heuristic; graph search also needs consistency or a reopen policy ([Hart, Nilsson, Raphael](https://ai.stanford.edu/~nilsson/OnlinePubs-Nils/PublishedPapers/astar.pdf)). A uniform spatial hash is the simple baseline for local edits; an R*-tree earns a seam only if skewed-scene traces justify its update cost ([Beckmann et al.](https://doi.org/10.1145/93597.98741)). Neither structure enters the Diagram Presentation interface.

## 12. Conformance risks

- Live `X/Z` coloring, selection, probes, and diagnostics are editor overlays, not IEEE symbols.
- IEEE 991 details referenced by Annex A were not part of the supplied local standard; uncovered typography details remain unverified.
- Browser font shaping can alter bounds, so symbol text needs a fixed font fingerprint and export/browser parity tests.
- A generated-symbol tracking matrix supports an auditable claim but is not third-party certification.
- Teaching Extensions must remain visible in UI and export manifests.

## 13. Primary sources

- IEEE Std 91-1984 with IEEE 91A-1991.
- [SVG 2 interaction](https://www.w3.org/TR/SVG2/interact.html#pointer-processing) and [descriptive elements](https://www.w3.org/TR/SVG2/struct.html#DescriptionAndTitleElements).
- [WAI-ARIA Graphics Module](https://www.w3.org/TR/graphics-aria-1.0/).
- [HTML Canvas](https://html.spec.whatwg.org/multipage/canvas.html#the-canvas-element).
- [WCAG 2.2](https://www.w3.org/TR/WCAG22/).
- [Logisim Evolution](https://github.com/logisim-evolution/logisim-evolution) and [Digital](https://github.com/hneemann/Digital), GPL-3.0 references only.
