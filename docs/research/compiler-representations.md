# Compiler Representation Evidence

> Authority: evidence and design inference; [Architecture](../architecture.md),
> [Compiler](../specs/compiler.md), and [ADR 0002](../adr/0002-compile-authored-topology-to-purpose-specific-ir.md)
> remain normative.

## Conclusion

Compiler should remain one deep module. It accepts one immutable Project Revision and
publishes one sealed Compilation Artifact or nothing. Its Elaborated Graph, Simulation
IR, Source Map builders, ordering passes, and validators remain private.

An authored Project Revision, an Elaborated Graph, Simulation IR, and Source Map have
different identity and locality requirements. Combining them into one universal graph
would leak runtime ordinals into authored facts and enlarge the interface. A textual
or S-expression dump may help debugging, but it is never an executable or durable
format.

## External evidence

LLVM separates its typed in-memory representation from equivalent bitcode and
human-readable assembly. Transformations operate on the representation; the textual
form is for inspection. LLVM also distinguishes parser acceptance from semantic
well-formedness and supplies a verifier ([LLVM Language Reference](https://llvm.org/docs/LangRef.html#abstract),
[well-formedness](https://llvm.org/docs/LangRef.html#well-formedness)).

MLIR likewise separates in-memory, textual, and serialized forms. Typed operations
carry verification rules, while progressive lowering preserves high-level facts until
a target-specific representation can discard them ([MLIR Language Reference](https://mlir.llvm.org/docs/LangRef/),
[rationale](https://mlir.llvm.org/docs/Rationale/Rationale/#introduction-and-motivation)).

McCarthy's S-expression is recursive data; R6RS separately defines datum and program
syntax. Parenthesized shape alone does not enforce widths, Port direction, resolved
contracts, provenance, graph validity, or execution layout
([McCarthy, 1960](https://www-formal.stanford.edu/jmc/recursive/node3.html),
[R6RS chapter 4](https://www.r6rs.org/final/html/r6rs/r6rs-Z-H-7.html#node_chap_4)).

These sources support three project conclusions:

1. representation, syntax, and serialization are separate decisions;
2. every internal stage needs explicit invariants even when it has no public seam; and
3. a readable dump does not become a caller-facing format.

## Project shape

```text
Project Revision + entry + Library Snapshot
  -> validate local facts relied on by compilation
  -> elaborate hierarchy and connectivity with complete provenance
  -> canonical Elaborated Graph
  -> assign dense ordinals and lower Simulation IR
  -> build total Source Map
  -> validate and publish one sealed artifact
```

| Representation   | Retains                                                                           | Excludes                                                   |
| ---------------- | --------------------------------------------------------------------------------- | ---------------------------------------------------------- |
| Project Revision | stable authored identity, hierarchy declarations, Net membership, presentation    | execution ordinals, SCC layout, runtime state              |
| Elaborated Graph | resolved occurrences, Hierarchy Paths, widths, Drivers, diagnostic witnesses      | mutable Session state, dense storage commitments, geometry |
| Simulation IR    | dense ordinals, evaluator/Net graph, CSR adjacency, SCC plan, state/memory schema | authored edit structure, browser identity                  |
| Source Map       | total mapping from executable facts to stable source identity                     | renderer identity, localized messages                      |

A Net is a hyperedge over Terminal occurrences. Generated execution is therefore
`Evaluator -> Driver -> resolved Net -> consuming Evaluator`. Sequential outputs cut
combinational dependencies; Circuit Definition instances form a separate call graph
whose recursion is invalid.

| Problem               | Private implementation candidate  | Obligation                                        |
| --------------------- | --------------------------------- | ------------------------------------------------- |
| hierarchy recursion   | iterative DFS or SCC              | stable witness without CLR stack exposure         |
| combinational regions | Tarjan SCC                        | cover every node and order the condensation graph |
| fanout and Drivers    | compressed sparse row arrays      | validate offsets, lengths, and ordinals           |
| future stimuli        | min-heap plus stable sequence     | equal-time order never depends on heap ties       |
| delta work            | dense queue plus stamps or bitset | bounded deduplication without per-node allocation |

Tarjan establishes linear-time SCC construction
([paper](https://doi.org/10.1137/0201010)). These choices remain implementation,
not interfaces.

## .NET implications

C# records provide value equality but do not copy referenced arrays. Artifact
construction must own or copy exact-sized buffers and expose no writable alias
([record value equality](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/types/records#value-equality)).

Frozen collections favor infrequent construction and repeated lookup. Use them only
for bounded, trusted artifact indexes after measurement; arrays and CSR remain the
natural ordinal hot path ([FrozenDictionary](https://learn.microsoft.com/en-us/dotnet/api/system.collections.frozen.frozendictionary-2?view=net-10.0)).

SIMD belongs only in private packed-logic leaves after scalar differential evidence
and tail handling. It changes neither Compiler interface nor semantics
([.NET SIMD guidance](https://learn.microsoft.com/en-us/dotnet/standard/simd)).

## Rejected and deferred

| Option                                      | Disposition | Reason                                                                    |
| ------------------------------------------- | ----------- | ------------------------------------------------------------------------- |
| universal authored/executable/browser graph | reject      | incompatible identity, mutation, and locality requirements                |
| S-expression native or executable format    | reject      | duplicates `.logiclab` and weakens typed validation                       |
| public pass or visitor framework            | reject      | callers need outcomes, not transformation choreography                    |
| serialized Compilation Artifact             | defer       | no cross-process or durable consumer exists                               |
| incremental Compilation                     | defer       | full Compilation remains the oracle until corpus evidence justifies reuse |
| LLVM, MLIR, or Nanopass dependency          | reject      | their design evidence is useful; a general compiler framework is not      |
