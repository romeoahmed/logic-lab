# Diagram Presentation

> Status: normative V1 Diagram Presentation contract

Diagram Presentation projects one immutable Project Revision into renderer-neutral Geometry Plans and one reproducible Schematic Projection. [Workbench](../../WORKBENCH.md) owns visual and interaction behavior; [Browser Runtime](./browser-runtime.md) consumes the projection without reconstructing static geometry.

V1 ships one project-level, versioned `TeachingMixed` Symbol Profile based on IEEE Std 91-1984 with IEEE 91A-1991. It combines permitted distinctive basic-gate outlines with declarative rectangular symbols for complex, sequential, memory, and user-defined components.

## 1. TeachingMixed profile

| Component family                                    | Default Symbol Variant                                       | Basis                                                       |
| --------------------------------------------------- | ------------------------------------------------------------ | ----------------------------------------------------------- |
| AND/NAND                                            | distinctive AND plus output qualifier                        | 5.1-3 and 5.1-17                                            |
| OR/NOR                                              | distinctive OR plus output qualifier                         | 5.1-1 and 5.1-18                                            |
| two-input XOR/XNOR                                  | distinctive XOR plus output qualifier                        | 5.1-11                                                      |
| XOR/XNOR with more than two inputs                  | rectangular odd/even function                                | 5.1-9 and 5.1-10                                            |
| Buffer/NOT                                          | distinctive triangle plus negation qualifier                 | 5.1-12 to 5.1-14                                            |
| MUX, decoder, encoder, arithmetic                   | parameterized rectangle                                      | applicable function and dependency notation                 |
| latch, flip-flop, register, counter, shift register | parameterized rectangle                                      | 5.9 and 5.13                                                |
| ROM and RAM                                         | parameterized rectangle or array                             | 5.14 and address dependency                                 |
| user Circuit Definition                             | rectangular authored-contract symbol                         | explicit extension claim unless a standard symbol is proven |
| interactive switch, LED, and probe marker           | standardized form where proven; otherwise Teaching Extension | explicit extension mark                                     |

The two-input XOR outline is never stretched into a different multi-input parity meaning. Distinctive shapes are not composed into complex symbols.

V1 does not publish the unqualified `HPRI/BIN` standard mark for either Priority Encoder parameter because structured input weights and priority composition are not yet modeled. `highestIndex` uses the visible TeachingMixed extension `[HPRI/BIN]`; `lowestIndex` uses `[LPRI/BIN]`. Both publish `TeachingExtension` and record `teachingmixed-unmodeled-priority-encoder` for their Ports; `lowestIndex` additionally records `teachingmixed-lowest-priority-encoder`.

The project stores Symbol Profile ID and version. Opening an old project never silently adopts a newer default. A Component Instance may store only a registered `SymbolVariantId` override that preserves the same Component Contract, Port identities, widths, and diagram-wide indication convention. Arbitrary SVG, path data, or per-instance polarity convention is invalid.

## 2. Declarative model

```text
SymbolDefinition
  id, version, supported Component Contracts
  parameter schema
  SymbolVariantDefinition[]

SymbolVariantDefinition
  variant ID
  Conformance Claim
  outline recipe
  Port Group recipes
  qualifier slots and ordering
  dependency rules
  layout constraints
  standard references

Symbol Request
  Component Contract and stable Ports
  normalized parameters and labels
  facing, reflection, width, fan-in, control polarity, clock edge
  profile/version and optional registered override
  Metric Set ID/version, font fingerprint, locale ID, and base direction
  matching Symbol Text Measurer supplied at the planning seam

Geometry Plan
  bounds and drawing operations
  Port anchors keyed by Port ID
  visual and interaction hit regions
  conformance evidence
```

Definitions are data plus closed recipe kinds; they do not execute arbitrary type names or scripts. A Geometry Plan contains no Razor, Fluent UI, DOM, Canvas, or SVG type.

## 3. Layout rules

The layout solver applies constraints in this order:

1. normative IEEE `shall` rules;
2. Component Contract Port identity and anchor invariants;
3. no overlap, text clearance, and readability;
4. IEEE 91A Annex A recommended proportions;
5. compactness.

An unsatisfied higher-priority constraint returns a structured layout diagnostic from [Diagnostics V1](./diagnostics-v1.md). The renderer never clips a label, moves a Port to another semantic group, or silently drops a qualifier.

Port groups declare edge, role, stable order, pitch, clearance, grouping, and label policy. Bit groups use explicit ascending or descending weight order. Coordinate order is never used to reconstruct Port identity.

Qualifier and dependency composition is structured. Component Contract Port IDs remain identity values; a registered function recipe must explicitly authorize any identical visible function text.

- negation and direct-polarity indication are diagram-wide alternatives and are not mixed;
- dynamic, active-low, complemented-output, three-state, bit-grouping, common-control, and common-output marks bind to explicit Ports or groups;
- dependency data records type, identifier or consecutive range, affecting Port, affected endpoints, and application order;
- input and output qualifier sequences follow IEEE 91A clauses 4.4.3 and 4.4.4;
- signal-flow glyphs transform with the symbol while readable text remains upright;
- a V1 aggregate multi-bit Port has one semantic anchor, shows its authored display label without HDL slice notation, and publishes `TeachingExtension` with `teachingmixed-aggregate-multibit-port` naming the affected Port IDs.

### 3.1 Sequential and memory notation

| Concern                    | Rule                                                                                                                                                                                                                                                                                                                                                                                                    |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| astable source             | The general symbol displays the standardized `G` generator mark and no function label derived from the Contract Port ID `Q`.                                                                                                                                                                                                                                                                            |
| bistable body and output   | D, J, K, R, S, and T are Port-bound input functions; a basic bistable has no invented body function. A complemented `QN` output uses the diagram-wide negation or direct-polarity output qualifier, cites rule 3.1.1 and the corresponding 3.1 output symbol, and publishes no `QN` function label.                                                                                                     |
| clock and hold controls    | The dynamic mark binds to the authored `CLK` Port. The closed `edge` parameter selects rising or falling presentation; falling presentation composes the dynamic mark with the diagram-wide indication convention. For affected inputs, inactive C and EN dependencies suppress action, while an inactive G dependency imposes the internal 0-state, so a hold-capable control is not represented as G. |
| register, shift, and count | A storage register uses bistable input qualifiers without an invented `REG*` body mark. Shift registers and counters use `SRG{width}` and `CTR{width}` body functions; direction qualifiers `→`, `←`, `+`, and `−` bind to `CLK` as input functions instead of being concatenated to the body text.                                                                                                     |
| data and write functions   | Parallel, serial, load-value, and memory data inputs use D. Write enable uses EN. Mode, control, enable, and terminal-count functions remain explicit Port-bound notation.                                                                                                                                                                                                                              |
| address dependency         | A scalar address Port uses a structured input bit-grouping qualifier with visible weight, brace, and the complete consecutive range `A0/1`; selected data endpoints carry A. An aggregate address Port retains its authored label, omits the unimplemented bit-grouping and address dependency, removes their exact standard references, and publishes `TeachingExtension`.                             |
| memory size                | ROM and RAM body text is derived from structured `(addressWidth, wordWidth)` array information as `2^addressWidth` words of `wordWidth` bits. The renderer never parses capacity or parameters back from display text.                                                                                                                                                                                  |

The Priority Encoder remains the explicit Teaching Extension defined in Section 1 until input weights and priority composition are structured; it cannot publish `Standardized91A` from its bracketed body text alone.

Text measurement uses a versioned font fingerprint and Metric Set. The planning seam receives a Symbol Text Measurer whose font and Metric Set fingerprints must equal the Symbol Request fingerprints; a mismatch returns `presentation_font_fingerprint_mismatch` or `presentation_metric_fingerprint_mismatch` and publishes no plan. Each measurement request carries the matched Metric Set so the result is expressed at the requested `unitsPerH` scale. Symbol geometry and application chrome typography are separate. A font or Metric Set change invalidates derived Geometry Plans but not Project semantics.

`PresentationLocaleIdV1` is the shared closed value used by Symbol Requests, measurement requests, text operations, and plan keys. Its V1 registry is exactly the supported UI cultures `en-US | zh-CN` from [Web Host §5](./web-host.md#5-culture-localization-and-direction); arbitrary BCP 47 tags and free-form strings do not cross the planning boundary. The registered values use the full culture-name form documented by [.NET `CultureInfo.Name`](https://learn.microsoft.com/en-us/dotnet/api/system.globalization.cultureinfo.name?view=net-10.0).

## 4. Projection value contracts

### 4.1 Schematic Projection

Diagram Presentation projects exactly one Circuit Definition at a time:

```text
Plan(SymbolRequest, SymbolTextMeasurer, CancellationToken)
  -> PlanSucceeded(GeometryPlanV1)
  | PlanRejected(reason: layout_invalid | layout_cancelled | layout_internal_defect, LayoutDiagnostics)

Project(ProjectRevision, CircuitDefinitionId, SymbolProfile, PresentationFingerprint, CancellationToken)
  -> ProjectionSucceeded(SchematicProjectionV1)
  | ProjectionRejected(reason: layout_invalid | layout_cancelled | layout_internal_defect, LayoutDiagnostics)
```

Failure publishes no partial plan or projection. Cancellation observed before atomic publication returns `layout_cancelled`; a signal after publication does not revoke the result. `presentation_constraint_unsatisfied.constraint` is exactly `request | portBudget | basicPortContract | coordinateRange | outlineRecipe | indicationConvention | parameterKind`. `SchematicProjectionV1` is one complete immutable static scene:

```text
SchematicProjectionV1
  key: SchematicProjectionKeyV1
  bounds: RectV1
  gridStepPlanUnits: positive integer
  snapStepGridUnits: positive integer
  items: SchematicItemV1[]

SchematicProjectionKeyV1
  Project Revision ID
  Circuit Definition ID
  Symbol Profile ID and version
  Presentation Fingerprint digest

SchematicItemV1 =
  ComponentSymbol { componentInstanceId, origin, GeometryPlanV1 }
  | DefinitionPort { portId, operations, PortAnchorV1, hitRegions }
  | NetTopology { netId, terminalAnchors, junctionIds, wireGeometryIds, probeAnchor }
  | WireGeometry { wireGeometryId, netId, route, operations, hitRegions }
  | Junction { junctionId, netId, point, operations, hitRegions }
  | Annotation { annotationId, operations, hitRegions }

ProjectedTerminalAnchorV1 =
  DefinitionTerminal { portId, point }
  | InstanceTerminal { componentInstanceId, portId, point }

probeAnchor = Available(PointV1) | Unavailable(NoVisibleGeometry)
```

`PresentationFingerprint` is a canonical digest over the Diagram Presentation semantic version, Metric Set ID/version, font fingerprint, localization-bundle ID/version, locale, and base-direction policy. The projection key therefore changes whenever geometry, localized text, shaping, grid conversion, snapping, or ordering can change. It scopes every item ID, so a local ID is never a cross-definition identity. `gridStepPlanUnits` converts one authored grid-coordinate unit into the common projection coordinate system; every projected coordinate and bound uses that system. `snapStepGridUnits` is the default positive snapping interval expressed in authored grid units. `origin` translates plan-local coordinates into projection plan units; facing and reflection are already part of the Geometry Plan key. Wire routes are exactly `Unrouted` or `Orthogonal(PointV1[])`; each routed segment owns one narrow rectangular hit region, with adjacent regions overlapping only around their shared joint. `NetTopology` repeats the authoritative ordered membership with projected Terminal anchors and provides one deterministic Probe anchor: first Terminal membership, then first Junction membership, then the first point of the lowest canonical routed Wire Geometry. A Net with none of those has `Unavailable`; it is not given an invented coordinate. Annotation LF boundaries are projected as explicit baseline-positioned nonempty DisplayText lines, while empty logical lines advance layout without creating a text measurement or drawing operation; an Annotation with no visible lines remains selectable with no visible geometry. Definition Ports, Junctions, Wire Geometry, and Annotations carry complete renderer-neutral drawing and hit values rather than requiring an adapter to reconstruct them from the Project Document.

Items use the back-to-front layer order in [Workbench](../../WORKBENCH.md), canonical source order within a layer, and authored Annotation z-order where it is semantic. All coordinate translation uses checked arithmetic. Validation rejects a missing or duplicate authored item, a dangling Net reference, an omitted placement, an out-of-scope ID, a failed Geometry Plan, or an overflow. Selection, focus, probes, live values, diagnostics, and Transient Preview are not Schematic items.

### 4.2 Geometry Plan

`GeometryPlanV1` is the complete Diagram Presentation output interface. It is an owned immutable value, not a renderer interface or a bag of SVG attributes:

```text
GeometryPlanV1
  key: GeometryPlanKeyV1
  bounds: RectV1
  operations: DrawOperationV1[]
  portAnchors: PortAnchorV1[]
  hitRegions: HitRegionV1[]
  conformance: ConformanceEvidenceV1

GeometryPlanKeyV1
  Symbol Definition ID and version
  semantic contract digest
  Symbol Variant ID
  normalized parameter and label digest
  facing, reflection, and indication convention
  locale ID and base direction
  Metric Set ID, version, and fingerprint
  font fingerprint
```

The key contains no Component Instance ID, selection, live value, or Workspace state, so a plan can be reused safely. `semantic contract digest` covers either one Component Contract or one Circuit Definition public Port contract.

All coordinates and widths are checked signed 32-bit integers in **plan units**. The versioned Metric Set declares stable-token ID and version values plus a positive integer `unitsPerH`; a stable token is 1 to 96 ASCII characters, starts with an ASCII letter or digit, and otherwise contains only ASCII letters, digits, `.`, `_`, or `-`. Adapters scale plan units to device or print coordinates only at the final projection. Metric identity comparison covers ID, version, `unitsPerH`, and the canonical fingerprint rather than treating a digest match as complete identity. `PointV1` is `(x,y)`, and `RectV1` is `(left,top,right,bottom)` with nonnegative extent. No `double`, device pixel, CSS length, DOM measurement, or renderer transform is stored in the plan.

`DrawOperationV1` is this closed, back-to-front union:

```text
StrokePath { path, role, width, dashPattern, lineCap, lineJoin }
FillPath   { path, role, fillRule }
DrawText   { text, fontRole, origin, bounds, alignment, orientation, baseDirection, localeId }

PathCommandV1 =
  MoveTo(point) | LineTo(point) | CubicTo(control1, control2, end) | Close
```

A path is nonempty. Each contour starts with `MoveTo`, contains at least one segment, and may end with one `Close`; after `Close`, only a new `MoveTo` may follow. Every Fill Path contour is closed. Stroke roles are `outline`, `qualifier`, `dependency`, or `extensionMark`. Fill roles are `foreground`, `background`, or `extensionMark`. Stroke width is a positive plan-unit integer. Dash patterns are empty for solid or contain an even count of positive plan-unit lengths. `lineCap` is `Butt | Round | Square`; `lineJoin` is `Miter(positive integer limit ratio) | Round | Bevel`; and `fillRule` is `NonZero | EvenOdd`. No adapter default supplies these values.

Text is authorized NFC display text. `fontRole` is `Symbol | PortLabel | Dependency | ExtensionMark`, alignment is `Start | Center | End`, orientation is `FollowFacing | UprightReading`, base direction is `LeftToRight | RightToLeft`, and `localeId` is a registered stable localization token. The Symbol Text Measurer is bound to one font fingerprint and one Metric Set fingerprint; the measurement request carries that Metric Set, and the result returns nonnegative advance width and ink bounds in its plan units relative to the requested alignment point and baseline. Advance width and either ink extent may be zero; a point ink bound at the alignment point and baseline represents no glyph ink. The generator unions advance and ink into the published bounds and solves body dimensions before drawing. A fingerprint mismatch is rejected instead of accepting substitute shaping or scale. `UprightReading` text keeps its measured orientation and envelope after symbol facing/reflection transforms. Color, antialiasing, and device-pixel snapping belong to the adapter and visual design tokens, not Geometry Plan.

Port anchors are sorted by Component Contract or Circuit Definition Port order:

```text
PortAnchorV1
  portId
  point
  outwardDirection: North | East | South | West
  hitRegionId
```

Each semantic Port appears exactly once. A `HitRegionV1` has a stable local ID, kind `Port | Body | Label`, source Port ID when applicable, and one closed shape: `Rect(rect)`, `Circle(center, positiveRadius)`, or `Polygon(points[])`. Polygons are simple and contain at least three points. Hit regions may be larger than visible geometry but may not move the Port anchor or overlap another Port region ambiguously.

Conformance evidence is closed:

```text
ConformanceEvidenceV1
  claim: one value from Section 7
  standardReferences: StandardReferenceV1[]
  deviations: ConformanceDeviationV1[]
  annexA: Pass | Adjusted | NotEvaluated

StandardReferenceV1 { publicationId, edition, nonempty ordered clauseIds[] }
ConformanceDeviationV1 { registered deviationCode, ordered affectedPortIds[] }
```

Publication, edition, clause, and deviation values come from the versioned Symbol Definition registry, not free-form prose. A standardized claim requires a nonempty reference list; `UnverifiedFallback` requires an empty list and `NotEvaluated`. Validation rejects duplicate IDs, unresolved cross-references, out-of-range arithmetic, missing Ports, renderer-specific data, unknown union variants, and operation order that violates qualifier/dependency composition. An adapter consumes the value as given; it does not reconstruct anchors, reorder operations, or remeasure text.

## 5. Generation pipeline

```text
resolve profile and registered override
  -> validate parameters and conformance policy
  -> normalize logical leading/trailing edges
  -> allocate stable Port rows and groups
  -> compose qualifiers and dependency labels
  -> measure text and solve dimensions in H units
  -> generate outline and embedded/common elements
  -> apply facing transform and restore text/glyph orientation rules
  -> validate and emit immutable GeometryPlanV1
```

Caching uses the complete `GeometryPlanKeyV1`. Cached geometry is derived and disposable. Pre-drawn SVG or PNG is never the source of truth.

## 6. Rendering and interaction

SVG, Canvas, print, and export consume the same Schematic Projection and Geometry Plans. Scene hit testing composes their static hit regions with browser-local handles and overlays; no adapter infers static geometry from pixels.

- exact visual paths and enlarged interaction regions are distinct;
- the single Port anchor is shared by rendering, routing, hit testing, and export;
- hit priority is Port, handle, component body, Wire Geometry, then background;
- hit results carry stable source identity, never pixel color or runtime ordinal;
- SVG groups expose useful title, description, and semantic identity;
- Canvas is the sole dense schematic editor and consumes the same Port anchors and Hit Regions;
- shared shortcuts invoke the same tool controller as pointer actions rather than a parallel topology navigator.

Selection, live Logic Values, probes, diagnostics, and the Probe Spine are editor overlays. They are not IEEE symbol content and are omitted from plain schematic export. An annotated teaching export marks them as extensions.

## 7. Conformance

Each generated symbol carries one claim:

| Claim                             | Meaning                                                                          |
| --------------------------------- | -------------------------------------------------------------------------------- |
| `Standardized91A`                 | generated from the cited standardized outline, qualifiers, and composition rules |
| `PermittedDistinctive91A`         | uses a distinctive basic-gate outline explicitly permitted by chapter 5          |
| `StandardBaseWithNonstandardInfo` | uses a standard base with clearly bracketed nonstandard information              |
| `TeachingExtension`               | useful teaching or interaction notation not claimed as IEEE 91A                  |
| `UnverifiedFallback`              | safe rectangular fallback with no conformance claim                              |

Annex A proportions are informative and recorded separately as `Pass`, `Adjusted`, or `NotEvaluated`. `Pass` requires every registered rational proportion to be exactly representable in the Metric Set's integer plan units and the final layout to retain the registered body dimensions; quantization or constraint-driven enlargement reports `Adjusted`. No product-wide “IEEE compliant” claim is made from a mixture of individual claims.

Strict export rejects a Teaching Extension or requires an explicit user-visible standardized fallback. TeachingMixed export includes a manifest of Component Instance, variant, claim, deviations, and exact standard references.

The export preflight is one closed, atomic Presentation interface:

```text
Export(SchematicProjectionV1, TeachingMixed)
  -> ExportSucceeded(TeachingMixedConformanceManifestV1)

Export(SchematicProjectionV1, Strict)
  -> ExportSucceeded(TeachingMixedConformanceManifestV1)
  | ExportRejected(strict_conformance, StrictConformanceViolationV1[])

TeachingMixedConformanceManifestV1
  projectionKey: SchematicProjectionKeyV1
  entries: TeachingMixedConformanceManifestEntryV1[]

TeachingMixedConformanceManifestEntryV1
  componentInstanceId
  symbolVariantId
  claim
  deviations
  exact standard references
```

Manifest entries use ordinal Component Instance ID order and own their nested arrays. `TeachingMixed` mode records every Component Symbol, including extensions and fallbacks. `Strict` mode accepts only `Standardized91A` and `PermittedDistinctive91A`; any other claim rejects the whole preflight, publishes no partial manifest, and returns deterministic violations containing the Component Instance, variant, claim, and deviation codes. The exporter never silently substitutes or replans geometry. A standardized fallback is explicit and user-visible: the caller commits or selects one registered compatible Symbol Variant, creates a new Schematic Projection, and retries strict preflight against that projection.

## 8. Required evidence

- unique versioned definition, variant, metric, and standard-reference schemas;
- rule tests for indication convention, qualifier order, dependency order, and bit weights;
- property matrices across facing, fan-in, width, Port groups, clock edges, polarity, and localized labels;
- canonical Geometry Plan and SVG goldens keyed by Component Contract and standard reference;
- atomic Schematic Projection cases covering positioned components, definition Ports, Net topology/navigation anchor points, Junctions, routed and unrouted Wire Geometry, Annotations, and invalid/cancelled rejection without a partial scene;
- closed-operation, nonpositive-stroke, cross-reference, overflow, and unknown-variant rejection tests;
- SVG/Canvas/print parity for bounds, ordered drawing operations, text metrics, Port anchors, and Hit Regions;
- profile mapping tests, especially two-input XOR versus multi-input odd/even;
- negative conformance tests that reject illegal combinations or missing extension marks;
- text expansion, bidirectional text, and high-zoom scenarios;
- export manifests for strict, TeachingMixed, and extension-containing projects.

Standard clauses, optional local-reference details, page mapping, and reference-implementation observations are documented in [symbol research](../research/diagram-presentation.md).
