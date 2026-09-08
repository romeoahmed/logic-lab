---
status: deferred
---

# Keep simplification managed and proof-gated

The deferred proposal favored a managed .NET design:
teaching Truth Table/K-map, bounded multi-output QMC plus Petrick, AIG
cleanup/balance, declarative gate-library mapping, and independent exhaustive or
fixed-order ROBDD verification. ABC, mockturtle, Z3, SAT/CEC processes, native
libraries, and algorithm NuGet packages were considered research alternatives. At most one verified
strict improvement could leave the Module as a reviewable proposal.

This deferred choice sacrifices industrial optimization coverage for locality,
deterministic evidence, simple deployment, and a small qualifiable interface.
It is not accepted V1 architecture and creates no active policy or production
qualification work. Reactivation requires a fresh design; the scope boundary remains in the
[Boolean Analysis proposal](../future/boolean-analysis.md).
