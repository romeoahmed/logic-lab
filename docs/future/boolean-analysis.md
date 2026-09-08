# Boolean Analysis Proposal

> Status: deferred, non-normative, and outside V1

Possible future capabilities are Truth Tables, Karnaugh Maps, and verified circuit
simplification. Their order is recorded in [Delivery](../delivery.md#future-capability-plan).
They add no current module, interface, policy, diagnostic, or test obligation.

Before implementation, define the user workflow, eligible binary combinational
regions, care-domain provenance, and an independent equivalence check. A replacement
must be reviewed and applied atomically against the revision it was verified for.
Unknown values and untested inputs must never be treated as don't-cares.

Do not reserve V1 fields or interfaces for this work. Algorithms, solver choices,
limits, and proof strategies require a new accepted design with representative
measurements; the earlier proposal is not an implementation constraint.
[ADR 0006](../adr/0006-keep-simplification-managed-and-proof-gated.md) records the
deferred rationale.
