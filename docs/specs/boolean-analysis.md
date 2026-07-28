# Boolean Analysis

> Status: normative V1 capability and proof contract

Boolean Analysis is a synchronous, deterministic, CPU-bound deep Module implemented in managed .NET. It has no repository, Web, native, solver-process, or algorithm-package dependency.

## 1. Outcomes

[Architecture §6](../../ARCHITECTURE.md#6-module-catalog) fixes the Boolean Analysis seam; this specification owns its exact interface. It exposes no `Optimize`, `Verify`, QMC, AIG, BDD, candidate collection, or per-algorithm threshold. The implementation returns at most one Verified Replacement for the requested Cost Profile.

The synchronous CPU-bound Module exposes exactly these entry points:

```text
Explain(ExplanationRequest, CancellationToken)
  -> ExplanationCompleted
   | TeachingProjectionUnavailable
   | AnalysisInconclusive

FindVerifiedImprovement(SimplificationRequest, CancellationToken)
  -> VerifiedImprovement
   | NoImprovement
   | AnalysisInconclusive

ExplanationRequest
  BooleanRegion
  projection: TruthTable | KarnaughMap
  TeachingProfile
  AnalysisPolicy

SimplificationRequest
  BooleanRegion
  TeachingGateLibrarySnapshot
  CostProfile
  AnalysisPolicy
```

`ExplanationCompleted` contains the complete requested projection, Care Contract provenance, ordered Diagnostics, and `AnalysisEvidence`. `TeachingProjectionUnavailable` contains the requested projection and the exact reason from Section 3. `VerifiedImprovement` contains exactly one Verified Replacement and complete `AnalysisEvidence`. `NoImprovement` contains complete search/proof `AnalysisEvidence` and no candidate. `AnalysisInconclusive` contains one exact reason—`PolicyExhausted | Cancelled | VerifierDisagreement | InternalDefect`—ordered Diagnostics, and `AnalysisEvidence` for work observed before termination; it contains no replacement.

```text
AnalysisEvidence
  policy: { policyId: StableToken, policyRevision: StableToken }
  observedDimensions: AnalysisDimensionObservationV1[]
  policyLimitBreach: AnalysisDimensionObservationV1 | null

AnalysisDimensionObservationV1
  dimension: one AnalysisPolicy dimension
  observed: UnsignedDecimal
```

Collections are present and non-null. Observations contain at most one row per dimension, record the maximum reached before termination, and follow Analysis Policy dimension order. `policyLimitBreach` is present exactly for `PolicyExhausted` and matches one observation; it is null otherwise.

Application maps `PolicyExhausted` to `analysis_inconclusive`, `Cancelled` to `analysis_cancelled`, and `VerifierDisagreement` or `InternalDefect` to `analysis_internal_defect`; Verifier Disagreement also carries its required Diagnostic and opaque correlation.

Profiles, policy, and the Teaching Gate Library are immutable versioned values captured before the call; the Module never reads a live registry or changes policy mid-operation. The caller cannot select algorithms, variable orders, passes, thresholds, or a verifier. Calls over distinct immutable requests are reentrant and own all mutable builders. Cancellation observed before publication returns the typed cancelled Inconclusive outcome; a signal after publication does not revoke the result. Application schedules both methods on the Analysis lane rather than the Module creating tasks or queues.

Application owns authorization, queuing, cancellation requests, result retention, proposal review, source freshness, recompilation, and the final Edit Transaction. Boolean Analysis never writes a Project Document.

## 2. Eligible input

Only the Compiler can construct a Boolean Region. [Compiler](./compiler.md) owns Region Selection, exact ineligibility reasons, extraction, and source bindings. It proves that the selected region:

- contains no state or memory;
- contains no combinational feedback;
- has explicit hierarchy and bit-level boundaries;
- has ordered binary primary inputs and outputs;
- produces definite `0/1` on every cared assignment;
- carries a per-output Care Contract and complete source provenance.

V1 uses the full binary input domain for every output Care Domain. The representation remains per-output so proof formulas and evidence are unambiguous, but no transient user assertion, observed sample, `X`, or `Z` creates a Don't-care.

For output `j`, original function `Fj`, candidate `Gj`, and care predicate `Cj`, replacement correctness is:

```text
for every assignment x and output j:
    Cj(x) implies Fj(x) == Gj(x)
```

The miter is `OR_j(Cj AND (Fj XOR Gj))`. Only proof that the miter is false can create a Verified Replacement. `X`, `Z`, an unprobed output, or an unobserved test vector is never a Don't-care.

## 3. Explanation path

`TeachingProjection` is exactly `TruthTable | KarnaughMap`; `TeachingProfile` controls versioned teaching conventions and never selects an algorithm. Truth Table and Karnaugh Map are projections of the Boolean Region and Care Contract. Karnaugh cells use Gray-code axes; a legal group is a power-of-two wrapping rectangle that covers cared `1` cells and no cared `0` cell. Multiple outputs are shown separately, with shared implicants linked by stable markers.

The explanation path returns `TeachingProjectionUnavailable` only with reason `dimensionUnsupported | teachingProfileUnsupported`. It is not an optimization oracle. Policy exhaustion is Inconclusive, not projection unavailability.

## 4. Candidate pipeline

```text
Boolean Region
  -> baseline AIG
  -> candidate family A: exact bounded multi-output QMC + Petrick SOP -> AIG
  -> candidate family B: AIG cleanup, constant normalization, and balancing
  -> structural deduplication
  -> declarative Teaching Gate Library mapping
  -> materialize legal gate graphs and measure Cost Profile vectors
  -> independent exhaustive or ROBDD verification
  -> first verified strict improvement, or no result
```

All orderings and tie-breakers are stable. AIG structural equality deduplicates identical structures but does not prove functional equivalence.

### 4.1 Multi-output QMC

A cube is represented canonically by `(fixedMask, fixedValue, outputMask)` with `fixedValue & ~fixedMask == 0`. It matches assignment `a` when `(a & fixedMask) == fixedValue`.

For output `j`:

```text
On_j  = cared assignments where F_j = 1
Off_j = cared assignments where F_j = 0
```

A cube may feed `j` only when it intersects `On_j` and does not intersect `Off_j`. Cover requirements are pairs `(output, on-assignment)`, not bare minterms.

Two equal-mask cubes differing in one fixed bit can merge only for the intersection of their output masks. Output tags that did not participate in the merge remain prime candidates on the parent cubes. A single `wasCombined` flag is incorrect for multi-output minimization.

The prime chart is a deterministic bipartite relation between prime implicants and `(output, assignment)` requirements. Essential primes are selected first.

### 4.2 Petrick cover

For each uncovered requirement, Petrick's method forms the sum of covering prime identifiers; multiplying all sums enumerates covers. The implementation stores each product as a prime-index bitset and immediately applies:

- canonical deduplication;
- absorption of strict supersets under monotone cost;
- only dominance rules proven safe for the selected lexicographic objective;
- checked work, storage, cancellation, and policy accounting.

QMC plus complete Petrick search is exact only for the declared fixed-polarity two-level PLA objective and only when no policy limit truncates the prime or product space. It does not claim a globally minimal mapped multilevel circuit.

### 4.3 AIG

An AND-inverter graph (AIG) contains constant false, ordered primary inputs, append-only two-input AND nodes, complemented literals, and ordered outputs.

```text
literal = nodeOrdinal << 1 | complementBit
node 0, phase 0 = false
node 0, phase 1 = true
```

`CreateAnd` canonicalizes fan-in order, applies constant/idempotence/complement identities, consults a structural unique table, and appends only topological nodes. Cleanup rebuilds reachable nodes. Balancing applies only to proven associative cones and preserves reconvergence. AIG node identity never crosses the Module seam.

### 4.4 Teaching Gate Library mapping

Each declarative cell defines its Boolean function, legal fan-in and pin permutations, polarity support, materialization recipe, symbol key, and Cost Profile vector.

The mapper enumerates bounded feasible cuts for each `(AIG node, requested phase)`. A cut has a stable sorted leaf set and a packed truth table. Cell matching may use declared input permutations and polarities; unsupported transforms are not inferred. Dynamic programming computes legal depth and local cost choices. Because the input is a reconvergent DAG, local dynamic programming is not advertised as globally minimum area; materialization, sharing, reference accounting, and a bounded deterministic refinement establish the actual final cost.

Mapping always materializes a complete teaching-library graph before ranking or verification. No AIG node count is shown as a gate count.

## 5. Independent verification

The verifier is chosen once before candidate execution under a versioned Analysis Policy.

### Exhaustive evaluator

For a measured small complete domain, packed words evaluate all assignments for the original Region and mapped replacement. `care & (original XOR candidate)` must be zero for every output. A nonzero bit yields the stable lowest counterexample assignment.

### ROBDD evaluator

A reduced ordered binary decision diagram (ROBDD) manager uses primary-input ordinal order, reduction `low == high`, a unique table `(variable, low, high)`, and a computed Apply cache. Original, candidate, and Care roots are built independently in the same manager and order. Equivalence holds only when every care-aware miter root is the false terminal.

ROBDD size is exponential in the worst case. Work, nodes, caches, depth, and cancellation are bounded. Traversal uses explicit frames or a prevalidated depth bound; untrusted graph depth cannot exhaust the CLR stack. Counterexample extraction completes unspecified variables in stable `0`-first order and replays the assignment through both scalar evaluators.

The verifier never falls back after starting. Random simulation can find a counterexample but cannot prove equivalence. Exhaustive and ROBDD implementations are cross-checked in their overlap corpus, not redundantly run for every production request.

If the independent verifier rejects any self-generated candidate, the whole operation returns `Inconclusive(VerifierDisagreement)` with replay evidence. It does not skip the defect and continue ranking.

## 6. Cost and evidence

A Cost Profile is a versioned lexicographic vector over the materialized gate graph, for example legality, gate count, depth, and input-pin count. Floating weighted sums and unspecified “simplest” labels are not contracts.

A Verified Replacement records:

- source Project Revision, region, network, and Care Contract digests;
- stable ordered input and output bindings;
- Compiler, analysis algorithm, gate library, Cost Profile, and Analysis Policy revisions;
- canonical mapped-graph digest and measured cost vector;
- verifier kind, variable order when applicable, completed coverage, and resource evidence.

Internal cube, prime, AIG, cut, mapping, and BDD identifiers are absent.

## 7. Failure semantics

Diagnostic and outcome reason codes follow [Diagnostics V1](./diagnostics-v1.md). Eligibility and proof outcomes remain typed variants rather than localized strings or generic errors.

| Outcome | Meaning | Can create a proposal? |
|---|---|---|
| Verified Improvement | a strict cost improvement with complete proof | yes, after Application freshness and recompilation checks |
| No Improvement | all completed eligible work found no strict verified improvement | no |
| Teaching Projection Unavailable | the requested teaching projection has an unsupported dimension or profile | no |
| Inconclusive | policy exhaustion, cancellation, verifier disagreement, or internal defect | no |

Policy exhaustion never returns a best-so-far candidate as exact. Authentication, admission, and queue rejection belong to Application and are not Analysis results.

## 8. Required evidence

- cube normalization, merge, residual-output-tag, and prime properties;
- QMC/Petrick comparison with brute-force multi-output set cover on small domains;
- AIG identity, topological, cleanup, balance, and determinism properties;
- cut truth-table, cell-match, phase, fan-in, sharing, and materialization tests;
- mapped Cost Profile recomputation independent of mapper bookkeeping;
- exhaustive/ROBDD overlap across arithmetic, parity, mux, care, and hostile-order functions;
- mutation tests for phase, fan-in, output order, Care Domain, BDD reduction, and counterexample replay;
- cancellation and every policy dimension returning Inconclusive without a replacement;
- stale Proposal and atomic Edit Transaction integration tests.

ABC, mockturtle, and Z3 are research references only. Production, build, tests, and release do not download, invoke, P/Invoke, package, or trust their output.
