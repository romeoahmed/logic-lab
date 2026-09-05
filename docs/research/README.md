# Research Index

Research records evidence, derivations, rejected alternatives, and dated qualification boundaries. It does not define current behavior, delivery status, or policy. Follow each record's authority note to the owning specification, contract, policy, or ADR.

| Record                                                          | Evidence scope                                                                   |
| --------------------------------------------------------------- | -------------------------------------------------------------------------------- |
| [Least-Fixed-Point Semantics](./least-fixed-point-semantics.md) | mathematical basis for zero-delay combinational feedback                         |
| [.NET Memory and Unsafe Code](./dotnet-memory-and-unsafe.md)    | buffer ownership, pool leases, stack storage, and binary-layout hazards          |
| [.NET Testing Platform](./testing-platform.md)                  | MTP, TUnit, FsCheck, bUnit, and Playwright evidence                              |
| [Compiler Representations](./compiler-representations.md)       | compiler pipeline and purpose-specific representation evidence                   |
| [Boolean Analysis proposal](../future/boolean-analysis.md)      | consolidated future design, evidence boundary, and primary sources               |
| [Blazor Web Platform](./blazor-web-platform.md)                 | hosting, render lifecycle, browser ownership, and Interactive Server constraints |
| [Diagram Presentation](./diagram-presentation.md)               | IEEE symbol, declarative geometry, and presentation rationale                    |
| [Engine Performance](./engine-performance.md)                   | dated implementation audit and BenchmarkDotNet decisions                         |

The [Engineering](../engineering.md) contract cites Microsoft
sources directly, so a second general-purpose .NET evidence summary would only
duplicate its authority. Research notes here remain focused on a derivation,
qualification boundary, or dated measurement that the owner should not absorb.

Keep dated measurements and source snapshots only while they support a live
decision. Remove completed migration inventories, superseded setup instructions,
and implementation-status snapshots; executable configuration and
[Delivery](../delivery.md) own current facts.

Cite each source at the claim it supports. Do not append a second link catalog when those sources are already cited inline; retain a closing bibliography only for whole-record primary material that cannot be attached to one claim.
