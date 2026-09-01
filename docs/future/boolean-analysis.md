# Boolean Analysis Proposal

> Status: deferred, non-normative, and outside V1

This document preserves one coherent design for future explanation and exact,
proof-gated simplification. It creates no current module, interface, policy,
diagnostic, project, test, or production-qualification obligation. Activation must
first update [Delivery](../delivery.md#future-capability-plan) and every affected
current owner in one accepted scope change.

## Product boundary

The proposed capability would:

- explain an eligible binary combinational region as a Truth Table or Karnaugh Map;
- search for a strictly cheaper circuit built from a versioned teaching library;
- publish at most one independently verified replacement proposal; and
- leave review, freshness checks, recompilation, and the final Edit Transaction to
  Application and Project Editor.

It would not optimize arbitrary four-state, sequential, cyclic, timed, or stateful
circuits; infer don't-cares from observed data; expose internal candidates; select
algorithms through UI flags; or edit a Project automatically.

## Activation gate

Before implementation, the scope change must define:

1. the product owner and user workflow for explanation and proposal review;
2. exact Compiler eligibility and Care Contract provenance;
3. one small module interface and its closed outcomes;
4. authorization, scheduling, cancellation, retention, and idempotency at the
   Application seam;
5. a measured Analysis Policy and representative corpus; and
6. independent proof evidence that does not share the candidate generator's failure
   mode.

No nullable field or inactive variant is reserved in V1 while this gate is open.

## Proposed language

- **Boolean Region** — immutable Compiler output with ordered binary inputs and
  outputs, an acyclic network, source bindings, and a Care Contract.
- **Care Contract** — provenance plus the per-output Care Domains on which behavior
  must be preserved.
- **Don't-care** — an assignment explicitly outside one output's Care Domain; never
  another spelling of `X`, `Z`, unconnected, or untested.
- **Internal Candidate** — generated network that has not passed independent proof and
  cannot leave the module.
- **Verified Replacement** — teaching-library circuit that preserves the ordered
  interface and Care Contract with complete proof evidence.
- **Inconclusive** — policy, cancellation, verifier disagreement, or defect prevented
  a proof-quality conclusion; never “probably equivalent.”

## Proposed interface

One synchronous CPU module is sufficient:

```text
Explain(BooleanRegion, TruthTable | KarnaughMap, TeachingProfile, AnalysisPolicy)
  -> ExplanationCompleted
   | TeachingProjectionUnavailable
   | AnalysisInconclusive

FindImprovement(BooleanRegion, TeachingGateLibrary, CostProfile, AnalysisPolicy)
  -> VerifiedImprovement
   | NoImprovement
   | AnalysisInconclusive
```

The interface exposes no algorithm, pass, variable order, threshold, or solver
selection. Calls capture immutable versioned inputs and own their mutable builders.
Application would provide the typed CPU lane and retain results; the module would
create no tasks, queue, or Project mutation.

## Eligibility and explanation

Only Compiler can create a Boolean Region. It must prove that the selection is:

- combinational and acyclic;
- binary under an explicit Care Contract;
- closed over every internal dependency;
- free of state, clocks, memories, feedback, tri-state ambiguity, and unresolved
  multi-driver behavior; and
- bound to ordered source identities and exact Compiler semantics.

Truth Tables enumerate ordered assignments and outputs under the Care Contract.
Karnaugh Maps use Gray-code axes, legal power-of-two wrapping groups, and separate
per-output Care Domains. Unsupported dimensions return a closed unavailable outcome;
they do not silently change projection.

## Exact replacement pipeline

The retained design is managed .NET with no native optimizer, solver process, or
algorithm package in the trusted path:

```text
Boolean Region
  -> bounded multi-output Quine–McCluskey cubes
  -> Petrick exact cover for the admitted small region
  -> optional deterministic AIG cleanup and balancing
  -> declarative Teaching Gate Library mapping
  -> materialize complete authored candidate
  -> independent equivalence verification
  -> compare materialized Cost Profile
  -> publish one strict improvement or none
```

Every transformation is deterministic under canonical ordering and complete
tie-breakers. Cost is a versioned lexicographic profile over the materialized circuit,
not a vague simplicity score. A candidate that is equal or worse stays internal.

This pipeline deliberately favors explainability, locality, and bounded proof over
industrial synthesis coverage. Larger or unsupported regions return not-applicable or
inconclusive outcomes instead of an unproved “best effort.”

## Independent verification

The verifier must reconstruct behavior from the materialized replacement rather than
trust candidate truth tables or AIG annotations.

- Exhaustive packed evaluation is the default for the measured small-domain envelope.
- A fixed-order ROBDD path is a possible second verifier only after its own limits and
  failure independence are demonstrated.
- A counterexample is Verifier Disagreement and publishes no proposal.
- Policy exhaustion, cancellation, or a defect publishes no candidate, partial proof,
  or best-so-far result.

## Evidence required before activation

- eligibility fixtures for every accepted and rejected region shape;
- Truth Table and Karnaugh projection goldens with per-output don't-cares;
- exact-cover differential checks and deterministic tie cases;
- materialized library mapping for polarity, fan-in, and pin permutations;
- verifier mutation tests demonstrating counterexample detection;
- cancellation and policy exhaustion at each phase with no publication;
- freshness and atomic-application integration at the Workspace seam; and
- corpus measurements that set every Analysis Policy dimension.

## Primary sources

- W. V. Quine, [The Problem of Simplifying Truth Functions](https://www.jstor.org/stable/2268510), 1952.
- E. J. McCluskey, [Minimization of Boolean Functions](https://doi.org/10.1002/j.1538-7305.1956.tb03835.x), 1956.
- S. R. Petrick, _On the Minimization of Boolean Functions_, UNESCO International
  Conference on Information Processing, 1959.
- R. E. Bryant, [Graph-Based Algorithms for Boolean Function Manipulation](https://www.cs.cmu.edu/~bryant/pubdir/ieeetc86.pdf), 1986.
- A. Mishchenko, S. Chatterjee, and R. Brayton,
  [DAG-aware AIG rewriting](https://people.eecs.berkeley.edu/~alanmi/publications/2006/dac06_rwr.pdf), 2006.

[ADR 0006](../adr/0006-keep-simplification-managed-and-proof-gated.md) retains the
hard-to-reverse rationale. Secondary summaries are navigation only and do not define
the eventual contract.
