# Least-Fixed-Point Semantics Research

> Verified 2026-07-29 (Asia/Shanghai)
> Scope: the mathematical contract for one zero-delay Combinational Feedback Region epoch
> Authority: evidence and derivation; normative behavior remains in [Simulation Runtime](../specs/simulation-runtime.md) and [ADR 0001](../adr/0001-own-four-state-zero-delay-semantics.md)

## 1. Conclusion

Logic Lab's accepted result is sound under explicit finite-domain premises, but the theorem must be named precisely. The carrier

```text
D = { X, 0, 1, Z }
X ⊑ 0, X ⊑ 1, X ⊑ Z
```

is a finite pointed directed-complete partial order (dcpo) and a meet-semilattice. It is **not a lattice**: for example, `0` and `1` have no upper bound in `D`, hence no join. Its finite product over internal Driver and Net bits has the same qualification. Therefore the Knaster–Tarski theorem is not the direct justification. Tarski's original theorem assumes a complete lattice and proves that the fixed points of an increasing endofunction form a complete lattice ([Tarski 1955, Theorem 1](https://doi.org/10.2140/pjm.1955.5.285)).

No completion is needed. A direct finite ascending-chain proof, equivalently a finite Kleene-style iteration, is enough. Adding a distinct top element above `0`, `1`, and `Z` would produce a complete lattice, but it would also introduce a new semantic value and require new transfer rules; silently identifying that top with `X` would conflate opposite order roles.

## 2. Exact fixed-point argument

Let `C` be the finite set of internal value coordinates for one SCC epoch, `P = D^C`, `⊥ = (X, ..., X)`, and `F : P → P` the simultaneous vector of all evaluator-output and Net-resolution equations with boundary inputs frozen. Require `F` to be total, deterministic, pure, and monotone.

Synchronous iteration is

```text
p₀ = ⊥
pₙ₊₁ = F(pₙ)
```

Because `⊥ ⊑ F(⊥)` and `F` is monotone, this is an ascending chain. `P` is finite, so it stabilizes at some `p* = F(p*)`. For every other fixed point `q`, induction gives `pₙ ⊑ q`; hence `p* ⊑ q`. Thus `p*` is the unique least fixed point. The longest chain in `D^C` contains `|C| + 1` elements, so there can be at most `|C|` strict coordinate refinements, although there may be more no-op evaluations.

This is the finite case of bottom-up constructive fixed-point computation. Cousot and Cousot describe program-analysis algorithms as limits of finite Kleene sequences in their original abstract-interpretation work ([author-hosted record and paper](https://www.di.ens.fr/~cousot/COUSOTpapers/POPL77.shtml), [DOI](https://doi.org/10.1145/512950.512973)). The proof above is self-contained and does not depend on the unavailable general theorem being applicable by name.

The general Kleene theorem is usually stated for a pointed dcpo and a Scott-continuous endofunction. Here every directed subset of the finite `P` has a greatest element, so every monotone `F` preserves its supremum and is Scott-continuous. Finiteness additionally makes the chain stabilize after finitely many refinements. These stronger facts are useful context, but the elementary proof above is the actual Logic Lab proof obligation.

## 3. Fair chaotic worklist

A coordinate worklist produces the same result as synchronous iteration if all of these obligations hold:

1. the SCC and its coordinate set are finite;
2. every boundary input remains fixed for the epoch;
3. every internal value coordinate starts at `X`;
4. every coordinate equation is initially dirty, or an equivalent dependency-complete seed is used;
5. an evaluation reads the current working values and can only preserve or refine its coordinate;
6. every strict refinement eventually reevaluates every dependent equation;
7. no pending equation is permanently starved, and no-op evaluation does not create unbounded work; and
8. quiescence means that every coordinate equation is satisfied, not merely that one queue happened to be empty.

Every chaotic update is then ascending and remains below every fixed point. Fair quiescence is itself a fixed point, so it must equal the least one. The final Logic Values are consequently independent of queue order. Cousot's original asynchronous-iteration report states the corresponding fairness intuition as no component being abandoned forever and distinguishes it from finite termination ([R.R. 88 record](https://www.di.ens.fr/~cousot/COUSOTpapers/IMAG-RR88.shtml), [paper](https://www.di.ens.fr/~cousot/publications.www/Cousot-IMAG-RR88-Sep-1977.pdf)). That report works over complete lattices and also studies asynchronous memory; Logic Lab should rely on the simpler finite proof above, not use the report to authorize parallel or stale-read execution.

A stable deterministic queue order is still valuable for reproducible diagnostics, work evidence, and debugging. It is not what makes the settled value schedule-independent; monotonicity, complete dirty propagation, and fairness do that.

## 4. Current rule audit

IEEE 1800-2023 defines `0/1/x/z`, normally treats gate-input `z` like `x`, gives scalar gate tables, makes an undriven Net `z`, and resolves equal-strength `0`/`1` conflict to `x` (§§6.3.1, 6.6.1, 28.4–28.6). It does not define Logic Lab's Information Order or feedback fixed point.

The scalar tables were checked against every comparable input pair. NOT, AND, OR, XOR, conservative MUX and tri-state rules, and resolver arities zero through four all passed. The complete V1 catalog was then audited by rule family.

For a normalized input vector `v`, let `gamma(v)` be every binary assignment obtained by replacing its `X` coordinates with `0` or `1`. If `v1 ⊑ v2`, then `gamma(v2) ⊆ gamma(v1)`. Therefore, for any total deterministic binary function `g`, the lifted result `meet { g(a) | a in gamma(v) }` is monotone: refining the input removes cases, so the meet can only retain or gain information. This argument covers a family only where its normative rule explicitly uses all consistent cases rather than a heuristic sample.

| Rule                                                                         | Monotonicity finding                                                                                                                                                                                                                |
| ---------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Z → X` ordinary-input normalization                                         | Monotone: the only nontrivial comparison is `X ⊑ Z`, which maps to `X ⊑ X`.                                                                                                                                                         |
| Buffer, NOT, AND, OR, XOR                                                    | Exhaustive scalar tables are monotone; NAND, NOR, and XNOR are monotone compositions. Fixed-arity folds preserve monotonicity.                                                                                                      |
| Split, concatenate, zero/sign extend                                         | Coordinate projection, permutation, duplication, and insertion of constants are monotone; composing them with input normalization preserves monotonicity.                                                                           |
| Conservative Merge                                                           | For a nonempty set, return the common identical value, including `Z`, or `X` otherwise. This is the meet in `D`, hence monotone.                                                                                                    |
| MUX, demultiplexer, decoder, priority encoder, comparison, and logical shift | Each specified unknown data, selection, or control result is the meet over every consistent binary assignment. The `gamma` subset argument applies per output bit.                                                                  |
| Tri-state control                                                            | Enabled data and disabled `Z` are one total binary-control function lifted over all consistent cases; equal `Z` cases remain `Z`.                                                                                                   |
| Adder and subtractor                                                         | The declared carry or borrow chain is a finite composition of monotone scalar gates, coordinate projections, and constants.                                                                                                         |
| ROM and asynchronous RAM read                                                | Memory state is frozen at an epoch boundary. A partially unknown address meets every reachable word, so address refinement only removes cases. A RAM write starts a later epoch rather than refining memory inside the current one. |
| Net resolver                                                                 | If the old result is `0`, `1`, or `Z`, no Driver is still `X`, so no coordinate can change further. If the old result is `X`, every later result is above it. Thus the specified resolver is monotone for any finite Driver count.  |
| Sources, sinks, and state elements                                           | Constants are constant functions; input/clock sources and stored outputs are frozen SCC boundaries; sinks have no output equation. They add no non-monotone coordinate.                                                             |

A nonrecursive Circuit Definition made only from these contracts composes the same monotone equations. Compiler cuts every state output and rejects recursive definitions, so hierarchy does not add a new transfer rule. This closes every combinational, topology, source/sink, and memory-read rule in [Component Contract Catalog V1](../specs/component-contract-catalog-v1.md). Sequential transitions remain outside the fixed-point product.

The optimistic alternative "ignore `X` like `Z`" fails immediately: `[0, X] ⊑ [0, 1]`, but its resolved output would regress from `0` to `X`. The standard's ambiguous tri-state values `L/H` are not Logic Lab values; the project deliberately collapses them conservatively to `X`.

Diagnostic causes are not coordinates of this monotone product. For example, `[0, X]` has `UnknownDriver`, while refinement to `[0, 1]` replaces it with `Contention` although the resolved Logic Value remains `X`. The resolver must update or recompute final cause evidence whenever a Driver refines, but a cause-only change must not trigger logic propagation. The current separation of Logic Value and cause set is therefore necessary.

The normative specification makes Conservative Merge explicit: equal `Z` cases merge to `Z`; differing maximal values merge to `X`; state-storage normalization is applied separately. Every future combinational Component Contract remains subject to the same contract-specific proof plus exhaustive ordered-pair tests over bounded shapes.

## 5. What an `X` fixed point does not prove

The least fixed point is a conservative value result, not a classification of why information is absent:

| Network                                     | Fixed-point facts                                                        | Required interpretation                                               |
| ------------------------------------------- | ------------------------------------------------------------------------ | --------------------------------------------------------------------- |
| `y = y AND 0`                               | least fixed point is `0`                                                 | feedback can resolve to a known value                                 |
| odd inverter ring                           | only four-state fixed point is `X`; there is no `{0,1}` fixed assignment | Indeterminate Feedback, not a runtime oscillation                     |
| two cross-coupled inverters                 | fixed points include `(X,X)`, `(0,1)`, and `(1,0)`                       | least result is `(X,X)` although two stable Boolean assignments exist |
| Net driven by constant `0` and constant `1` | resolved value is fixed at `X` with `Contention` evidence                | definite conflict, not underconstraint                                |

A maximal fixed point of `F` is not automatically a Boolean assignment: a fixed point containing an unavoidable `X` can be maximal within `Fix(F)`. If the product needs to distinguish no Boolean solution from several Boolean solutions, that is a separate bounded existence/witness analysis over `{0,1}` assignments. It must not seed the Runtime with a guessed stable state.

Combinational evaluation from bottom cannot oscillate because it only ascends. Sequential commits and generated-clock transitions are a different, generally non-monotone transition system: stored `0 → 1 → 0` is possible. Only exact repetition of the complete deterministic working state and pending frontier proves Zero-time Oscillation. A work limit proves neither oscillation nor failure of the combinational theorem.

## 6. Specification and verification implications

The Simulation Runtime remains a deep Module: callers should request settlement and receive committed values plus evidence, never manipulate SCC seeds or worklist steps. Internally, the specification and tests should make these obligations executable:

- name `D` a finite pointed dcpo/meet-semilattice, not a complete lattice;
- define the combined coordinate function and the nonempty Conservative Merge table;
- require dependency-complete initial seeding, strict-refinement checks, fixed boundaries, and restart from `X` after a boundary change;
- fail closed if an evaluator returns an incomparable or regressive value, because that is an implementation/Component Contract defect;
- keep conflict causes outside the value fixed-point coordinates and verify their final recomputation;
- exhaustively test every scalar evaluator over all ordered input pairs;
- compare small SCCs with a slow synchronous bottom-iteration oracle;
- permute fair worklist schedules and hash/enumeration orders and require identical settled values;
- retain negative mutants such as unknown-as-absent resolution and arbitrary unknown-select choice;
- include known-resolving feedback, odd rings, bistables, conflict, and sequential-repeat witnesses in the corpus; and
- treat parallel scheduling as a new implementation proof obligation, not a consequence of the single-threaded result.

## 7. Source and access record

Primary material inspected:

- Alfred Tarski, _A Lattice-Theoretical Fixpoint Theorem and Its Applications_, Pacific Journal of Mathematics 5 (1955), 285–309: [DOI](https://doi.org/10.2140/pjm.1955.5.285), [publisher PDF](https://msp.org/pjm/1955/5-2/pjm-v5-n2-p11-s.pdf).
- Patrick and Radhia Cousot, _Abstract Interpretation: A Unified Lattice Model for Static Analysis of Programs by Construction or Approximation of Fixpoints_, POPL 1977: [author record/PDF](https://www.di.ens.fr/~cousot/COUSOTpapers/POPL77.shtml), [DOI](https://doi.org/10.1145/512950.512973).
- Patrick Cousot, _Asynchronous Iterative Methods for Solving a Fixed Point System of Monotone Equations in a Complete Lattice_, R.R. 88 (1977): [author record](https://www.di.ens.fr/~cousot/COUSOTpapers/IMAG-RR88.shtml), [author PDF](https://www.di.ens.fr/~cousot/publications.www/Cousot-IMAG-RR88-Sep-1977.pdf).
- IEEE Std 1800-2023, especially §§6.3.1, 6.5–6.6, and 28.4–28.6. An optional untracked local copy is named `1800-2023.pdf`.

The [Wikipedia least-fixed-point page](https://en.wikipedia.org/wiki/Least_fixed_point) was used only for navigation. No claim depends on it or on an inaccessible abstract. Future Component Contracts remain unverified until their complete scalar rules exist and pass the same audit.
