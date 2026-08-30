# Boolean Analysis Research

> Scope: K-map, exact bounded multi-output two-level minimization, AIG, teaching-library mapping, and independent proof
> Authority: derivation and evidence; normative capability is [Boolean Analysis](../specs/boolean-analysis.md)

## 1. Why this pipeline is sufficient

The selected V1 pipeline is internally coherent:

1. Karnaugh Maps explain adjacency and implicants at human scale.
2. Multi-output Quine-McCluskey (QMC) plus Petrick solves a bounded fixed-polarity two-level programmable-logic-array (PLA) cover exactly for its declared objective.
3. An AND-inverter graph (AIG) gives a compact multilevel DAG with cheap complementation and structural hashing.
4. Feasible-cut matching maps AIG functions into a small declarative teaching gate library.
5. Exhaustive evaluation or a fixed-order reduced ordered binary decision diagram (ROBDD) independently proves the final materialized circuit.

The pipeline does not compete with industrial synthesis. It prefers a small implementation surface and auditable write-back. Exponential work reduces coverage by returning Inconclusive; it never lowers proof quality.

## 2. Mathematical contract

For ordered input assignment `x`, output `j`, original `Fj`, candidate `Gj`, and care predicate `Cj`:

```text
equivalent iff for all x,j: Cj(x) => Fj(x) = Gj(x)
miter(x) = OR_j(Cj(x) AND (Fj(x) XOR Gj(x)))
```

The candidate is valid exactly when the miter is false for all assignments. Different outputs may have different Care Domains. `X` and `Z` are simulation values, while a Don't-care is an explicit contract freedom; conflating them would permit incorrect rewrites.

Under the [V1 Care Contract](../specs/boolean-analysis.md#2-eligible-input), `Cj` is true for every binary assignment. The general formula keeps proof evidence precise without making this research note a second owner of the Care Domain.

“Minimum” also needs an objective. Product count, literal count, mapped gate count, depth, fan-in, and teaching readability are different. V1 uses named lexicographic Cost Profiles and compares only complete materialized gate graphs.

## 3. Karnaugh Map

Gray-code axes make adjacent cells differ in one variable. A group is a power-of-two wrapping rectangle containing no cared zero. The group's implicant retains exactly the variables constant over all cells.

The map still has `2^n` cells and is therefore an explanation, not a scalable optimizer. Multi-output functions should use separate layers or panels; shared implicants can carry a common marker without obscuring per-output Care Domains.

## 4. Multi-output QMC

Represent one cube as:

```text
Cube(fixedMask, fixedValue, outputMask)
matches(a) = (a & fixedMask) == fixedValue
invariant: fixedValue & ~fixedMask == 0
```

For output `j`, define cared on-set `On_j` and off-set `Off_j`. A cube can feed `j` only if it intersects `On_j` and does not intersect `Off_j`. Requirements are `(j, assignment)` pairs, because a shared product can satisfy several outputs while each output has different legal expansion.

Two equal-mask cubes differing in one fixed bit merge only over the intersection of their output masks. A minimal residual-tag example has one input `x`:

| `x` | `A` | `B` |
| --: | --: | --: |
|   0 |   1 |   1 |
|   1 |   1 |   0 |

The `x=0` cube begins with tags `{A,B}` and `x=1` with `{A}`. They merge to `-` for `{A}`. The unmerged residual `{B}` on `x=0` remains a prime candidate. A single `wasCombined=true` flag on the parent would discard the only cover for `B` and is incorrect.

The implementation therefore tracks extension by output tag, deduplicates canonical `(mask,value,outputMask)` tuples, and preserves residual tags. Stable sorting cannot depend on hash iteration.

The prime chart is a bipartite relation:

```text
Prime implicants P --covers--> R = { (j,x) | x in On_j }
```

Bitsets support both prime-to-requirement coverage and requirement covering counts. Essential primes are selected before Petrick expansion. Shared product cost is counted once, with output connections costed separately by the declared model.

## 5. Petrick's method

For each uncovered requirement `ri`, let `Si` be the primes covering it:

```text
P = product_i (sum_{p in Si} p)
```

Each expanded monomial is a set of prime indexes. A practical exact implementation multiplies incrementally and immediately applies canonical deduplication and absorption: if `A` is a strict subset of `B`, `B` cannot win under a monotone set cost. Stronger cost dominance is safe only when proven against the complete lexicographic objective.

The product can grow exponentially. Cube, prime, chart-edge, monomial, work, and storage policies must be checked before append or allocation. Hitting a bound returns Inconclusive; returning the best partial product as “exact” would be false.

QMC/Petrick exactness is limited to the complete fixed-polarity two-level PLA search space and declared cost. It says nothing about global optimality after multilevel decomposition and sharing.

## 6. AIG representation

An AIG contains constant false, primary inputs, two-input AND nodes, complemented literals, and outputs:

```text
literal = nodeOrdinal << 1 | phase
false = (0,0); true = (0,1)
```

`CreateAnd(a,b)` sorts fan-ins, applies `0&a`, `1&a`, `a&a`, and `a&!a` identities, and consults a unique table keyed by the ordered literal pair. Nodes append in topological order.

Structural hashing removes identical structure but is not functional canonicalization. Cleanup reverse-marks output-reachable nodes and rebuilds deterministically. Balancing is safe only on associative AND/OR cones; reconvergence and complemented boundaries must be preserved.

QMC/Petrick SOP candidates lower into AIG, while the original Region also lowers directly. Both then share mapping and verification. Structural digests deduplicate identical candidates; functionally equal but structurally different candidates may remain until cost and proof.

## 7. Teaching gate mapping

The library is semantic data, not an SVG catalog. Each cell declares function, legal fan-in, pin permutation/polarity rules, cost vector, materialization, and Symbol Variant.

For each AIG node and requested phase, the mapper combines fan-in cuts into stable leaf sets within the Analysis Policy. It computes each cut's truth table over ordered leaves and matches declared cell functions under only allowed transformations.

Dynamic programming can minimize depth or a local cost over cut choices. On a reconvergent DAG, local area sums double-count or ignore sharing; global minimum-area DAG covering is not obtained automatically. A sound V1 process is:

1. retain a bounded deterministic set of legal choices per node/phase;
2. materialize output-reachable choices with explicit sharing;
3. recompute actual gate, pin, inverter, fanout, and depth cost;
4. optionally apply bounded reference/dereference refinement;
5. rank complete graphs by Cost Profile and canonical digest.

The verifier sees the materialized teaching graph, not AIG bookkeeping or mapper cost estimates.

## 8. Independent verification

### 8.1 Exhaustive packed evaluation

For a manageable complete input space, machine words hold many assignments. Primary inputs receive repeating bit patterns; original and candidate are evaluated through independent code paths; each output checks `care & (original XOR candidate)`. A set bit identifies a concrete counterexample. Completed exhaustive coverage is a proof, not sampling.

### 8.2 ROBDD

For one fixed variable order, a reduced ordered BDD is canonical. A manager maintains terminals, a unique table `(variable, low, high)`, and a computed Apply cache. `Make` returns `low` when `low == high`; otherwise it interns the node.

Original, candidate, and Care functions must use one manager and one primary-input ordinal order. Each care-aware miter root must be false. A true path yields a counterexample, completed deterministically and replayed through scalar evaluators.

Bad variable order can cause exponential size. Fixed order improves reproducibility but reduces coverage. Node, cache, Apply, depth, and cancellation bounds return Inconclusive. Dynamic reordering, portfolio orders, SAT fallback, and a second verifier after exhaustion are intentionally absent from V1.

### 8.3 Common-mode failure control

- optimizer transforms and verifier construction do not share rewrite rules;
- scalar, packed, mapped-graph, and BDD evaluators are distinct implementations;
- exhaustive and ROBDD cross-check their overlap corpus;
- mutations target phase, fan-in, output ordinal, Care Domain, cut truth table, BDD reduction, and counterexample completion;
- any rejected self-generated candidate becomes Verifier Disagreement rather than a skipped ranking entry.

## 9. Data structures and resource evidence

| Structure     | Representation                                                            |
| ------------- | ------------------------------------------------------------------------- |
| QMC cube      | machine-word or multiword masks plus output bitset                        |
| prime chart   | stable-index bitsets in both directions                                   |
| Petrick term  | immutable/copy-on-write prime-index bitset                                |
| AIG           | append-only node array plus structural unique table                       |
| cut           | sorted leaf ordinals plus packed truth table                              |
| mapping state | bounded choices by node and requested phase                               |
| ROBDD         | node array, unique table, computed Apply cache, explicit traversal frames |

Evidence records input/output count, work counters, retained structures, policy revision, chosen verifier, variable order, and outcome. Wall time is diagnostic, not the sole algorithm limit. Rebuilding a structure does not refund work already consumed.

## 10. Reference boundary

mockturtle demonstrates modern network/view/algorithm separation and AIG invariants. Berkeley ABC demonstrates the complexity of rewriting, mapping, and combinational equivalence checking. Z3 demonstrates a production SMT solver interface. They are useful for invariants, adversarial examples, and corpus design, not as Logic Lab runtime dependencies or release oracles.

## 11. Primary sources

- W. V. Quine, [The Problem of Simplifying Truth Functions](https://doi.org/10.1080/00029890.1952.11988183), 1952.
- E. J. McCluskey, [Minimization of Boolean Functions](https://doi.org/10.1002/j.1538-7305.1956.tb03835.x), 1956.
- S. R. Petrick, _On the Minimization of Boolean Functions_, International Conference on Information Processing, UNESCO, 1959.
- Randal E. Bryant, [Graph-Based Algorithms for Boolean Function Manipulation](https://www.cs.cmu.edu/~bryant/pubdir/ieeetc86.pdf), 1986.
- Alan Mishchenko et al., [DAG-aware AIG rewriting](https://people.eecs.berkeley.edu/~alanmi/publications/2006/dac06_rwr.pdf), 2006.
- [mockturtle](https://github.com/lsils/mockturtle), [Berkeley ABC](https://github.com/berkeley-abc/abc), and [Z3](https://github.com/Z3Prover/z3), source references only.

Wikipedia pages for QMC, Petrick, Karnaugh Maps, AIG, BDD, and SAT are useful navigation but are secondary sources.
