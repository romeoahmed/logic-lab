---
status: accepted
---

# Own four-state zero-delay semantics

Logic Lab defines its own deterministic gate-level semantics instead of claiming SystemVerilog compatibility. Nets resolve `0/1/X/Z`; ordinary logic consumes `Z` as unknown; state stores explicit `0/1/X` values; authoring may materialize all-`X` initial state before commit; only definite `0 <-> 1` clock edges trigger. Logical Time advances through atomic delta settlement, combinational feedback computes the Least Information Fixed Point from `X`, and only complete sequential working-state repetition proves Zero-time Oscillation.

This choice makes unknowns, feedback, derived clocks, rollback, Trace, and teaching diagnostics explainable without importing HDL scheduling regions, drive strengths, or analog timing. It deliberately trades compatibility and optimistic edge behavior for a small scalar oracle and reproducible outcomes. The normative rules are in [Simulation Runtime](../specs/simulation-runtime.md).
