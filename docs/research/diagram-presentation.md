# Diagram Presentation Research

> Scope: IEEE 91A evidence, declarative symbol generation, reference implementations, and workbench design rationale
> Authority: evidence and rejected alternatives; normative rules are [Diagram Presentation](../specs/diagram-presentation.md) and [Product](../product.md)

## 1. Evidence boundary

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

Chapter 5 permits several traditional distinctive basic-gate outlines. The same numbered entries 5.1-1 and 5.1-3 each show both the rectangular and distinctive OR or AND alternative, so changing between those outlines does not change the primary symbol reference. Their notes say distinctive shapes are not preferred in the related IEC standard but are not contradictory, and discourage composing them into complex symbols. Clause 2.3.1 similarly discourages distinctive shapes in embedded or abutted complex elements.

Symbol 5.1-11 is specifically a two-input exclusive-OR. For three or more inputs, the odd/even rectangular functions in 5.1-9 and 5.1-10 avoid misrepresenting “exactly one input” as arbitrary parity.

These facts support the selected mixed profile:

- familiar distinctive AND, OR, two-input XOR, Buffer, and NOT;
- parameterized rectangles for parity with larger fan-in, steering, arithmetic, sequential, memory, and user-defined logic.

The choice is not a claim that every project symbol is standardized.

## 4. Qualifiers and dependencies

IEEE 91A treats qualifiers as semantics, not decoration:

- clause 2.1.2 brackets nonstandard input, output, and body information instead of letting it resemble standardized notation;
- clause 3.1.1 prevents mixing negation and direct-polarity conventions across one diagram, except for its stated internal-negation case, while Symbols 3.1-1 to 3.1-11 define the corresponding polarity and dynamic marks;
- clause 3.3.2 distinguishes an input function from dynamic behavior: a D/J/K/R/S/T, shift, or count function receives the separate dynamic-input mark when it is edge-sensitive;
- Symbols 3.3-19 to 3.3-22 place shift direction and count direction at the affected input, not in the register or counter body function;
- Symbols 3.3-25 and 3.3-26 use a brace and meaningful bit-weight order for a bit group; clause 4.4.2 replaces the input-group result with a dependency letter and complete consecutive identifier range;
- clause 4.3.2 makes G an AND dependency whose inactive state imposes internal 0 on affected inputs and outputs; clause 4.3.7 instead identifies sequential action, including an edge clock or transparent-latch data enable, and suppresses an affected input's action while inactive; clause 4.3.9 gives EN the C/M behavior when it affects inputs;
- clause 4.3.1 makes dependency identifiers and application order meaningful, and clauses 4.4.3 and 4.4.4 constrain input and output label ordering.

Therefore polarity, edge behavior, Port grouping, dependency kind, dependency range, and application order must exist before drawing. A file name such as `falling-clock-active-low.svg` cannot own those rules safely.

## 5. Sequential and memory symbols

Clause 5.9 gives a basic bistable no general body qualifier: input and output qualifiers express its function, while dynamic and postponed marks distinguish latch, edge-triggered, pulse-triggered, and data-lock-out behavior. Complementary outputs are therefore output polarity, not a literal `QN` function.

Symbol 5.12-1 places `G` in an astable generator body and does not label its output `Q`. Symbol 5.13-1 registers `SRG*`, `CTR*`, `RCTR*`, `CTRDIVm`, and `RCTRDIVm` as the general shift-register and counter functions; it does not define a `REG*` body function. The examples combine those body functions with Port-bound D, C, EN, mode, shift, count, and terminal-count notation.

Clause 4.3.11 defines A dependency as selection of an addressed array section and refers grouped addresses to clause 4.4.2. Symbol 5.14-1 requires the memory function to carry both address count and bit count; later ROM/RAM examples show the address brace and consecutive A range as a single structured group rather than independent text. A V1 aggregate Port with one anchor cannot reproduce that multi-terminal grouping honestly and must remain an explicit Teaching Extension.

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
  -> SVG | Canvas | print | hit test
```

Caching the complete Geometry Plan key is safe because it is derived. Caching or persisting a hand-edited picture as the contract is not.

## 8. Reference implementation evidence

Logisim Evolution separates gate semantics from appearance selection: its gate painter chooses shaped or rectangular drawing without changing the simulation component, while sequential components recompute bounds and Ports for appearance variants. This proves separation is practical but also shows that a shape change affects more than path artwork.

Digital uses an IEEE-oriented ShapeFactory that maps familiar gates to distinctive shapes while RAM and complex components remain rectangular. Its visual elements persist semantic attributes and position, regenerate transient shapes, take pins from generated geometry, and support more than one graphics backend.

Both projects are GPL-3.0. Logic Lab adopts only observed architecture patterns and independently implements its data, algorithms, code, assets, and package format.

## 9. Product rationale

Dark oscilloscope styling competes with dense authoring, while drafting-paper styling
blurs the difference between editable apparatus and export. The selected instrument
enamel direction keeps chrome quiet and spends visual emphasis on the relation between
one stable Net and its time series. Repeating Probe identity at the Net, Spine,
Inspector, and waveform row makes that relation direct without relying on color alone.

## 10. Browser interaction boundary

[ADR 0008](../adr/0008-use-one-canvas-editor-surface.md) makes Canvas the only dense circuit editor. Consequences are:

- Geometry Plan supplies exact visual paths and enlarged hit regions;
- Canvas is the sole dense editor and has one host focus target for shared shortcuts;
- renderer failure produces an explicit Razor recovery surface rather than a second editor;
- logic states, active-low, selection, and Trace Gaps never rely on color alone.

Route preview and scene indexing remain evidence-gated implementation choices. Orthogonal A* may search `(grid point, arrival direction)` only with nonnegative costs and an admissible heuristic; graph search also needs consistency or a reopen policy ([Hart, Nilsson, Raphael](https://ai.stanford.edu/~nilsson/OnlinePubs-Nils/PublishedPapers/astar.pdf)). A uniform spatial hash is the simple baseline for local edits; an R*-tree earns a seam only if skewed-scene traces justify its update cost ([Beckmann et al.](https://doi.org/10.1145/93597.98741)). Neither structure enters the Diagram Presentation interface.

## 11. Conformance risks

- Live `X/Z` coloring, selection, probes, and diagnostics are editor overlays, not IEEE symbols.
- IEEE 991 details referenced by Annex A were outside the reviewed source set;
  uncovered typography details remain unverified.
- Browser font shaping can alter bounds, so symbol text needs a fixed font fingerprint and export/browser parity tests.
- A generated-symbol tracking matrix supports an auditable claim but is not third-party certification.
- Teaching Extensions must remain visible in UI and export manifests.

## 12. Primary sources

- [IEEE/ANSI 91a-1991 official standard record](https://standards.ieee.org/standard/91a-1991.html).
- [HTML Canvas](https://html.spec.whatwg.org/multipage/canvas.html#the-canvas-element).
- [Logisim Evolution](https://github.com/logisim-evolution/logisim-evolution) and [Digital](https://github.com/hneemann/Digital), GPL-3.0 references only.
