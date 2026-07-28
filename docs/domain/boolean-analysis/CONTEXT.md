# Boolean Analysis

Boolean Analysis accepts only Compiler-proven binary, combinational, acyclic regions and exposes only explanations or independently verified replacements.

## Language

**Boolean Region**:
An immutable Compiler artifact with ordered binary inputs and outputs, an acyclic Boolean network, source bindings, and a Care Contract.
_Avoid_: arbitrary selection, four-state circuit

**Care Contract**:
The complete provenance and per-output Care Domains that state where a replacement must preserve behavior.
_Avoid_: current test vectors, observed inputs, inferred don't-cares

**Care Domain**:
The set of input assignments for which one output is constrained to equal the original function.
_Avoid_: global input domain, simulation state space

**Don't-care**:
An input assignment explicitly outside one output's Care Domain.
_Avoid_: `X`, `Z`, unconnected input, failed analysis

**Cost Profile**:
A versioned lexicographic ordering over materialized teaching-library circuits.
_Avoid_: vague simplicity, weighted score

**Internal Candidate**:
A generated network that has not passed independent equivalence verification and cannot leave the Module as a replacement.
_Avoid_: previewable circuit, Verified Replacement, proposal

**Verified Replacement**:
A teaching-library circuit that preserves the Boolean Region's ordered interface and Care Contract and has complete independent proof evidence.
_Avoid_: optimizer output, equivalent expression, automatic edit

**Inconclusive**:
A closed failure outcome in which policy, cancellation, or an internal fault prevented a proof-quality conclusion.
_Avoid_: not equivalent, not applicable, Logic Value `X`

**Verifier Disagreement**:
A counterexample showing that an Internal Candidate violates its Care Contract.
_Avoid_: ordinary candidate ranking, user circuit error, no improvement

**Analysis Policy**:
A versioned envelope that limits Analysis work without changing equivalence semantics.
_Avoid_: user algorithm settings, scheduling quota, performance promise
