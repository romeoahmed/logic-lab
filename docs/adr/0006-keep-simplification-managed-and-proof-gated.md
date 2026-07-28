---
status: accepted
---

# Keep simplification managed and proof-gated

Production Boolean Analysis is self-developed managed .NET: teaching Truth Table/K-map, bounded multi-output QMC plus Petrick, AIG cleanup/balance, declarative gate-library mapping, and independent exhaustive or fixed-order ROBDD verification. ABC, mockturtle, Z3, SAT/CEC processes, native libraries, and algorithm NuGet packages are research-only. At most one verified strict improvement leaves the Module as a reviewable proposal.

This sacrifices industrial optimization coverage for locality, deterministic evidence, simple deployment, and a small qualifiable interface. Analysis and scheduling policies may stop work, but no incomplete or unverified candidate can be written back. The exact capability and evidence contract is [Boolean Analysis](../specs/boolean-analysis.md).
