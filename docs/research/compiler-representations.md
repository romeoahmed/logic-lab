# Compiler Representations Research

> Scope: compiler structure, intermediate representations, S-expressions, the local Chez Scheme implementation, and directly relevant .NET 10 representation choices
> Authority: evidence and design inference; normative ownership remains in [Architecture](../../ARCHITECTURE.md), [Compiler](../specs/compiler.md), [ADR 0002](../adr/0002-compile-authored-topology-to-purpose-specific-ir.md), and the linked specifications

This note marks external or repository observations as **Source fact** and project conclusions as **Logic Lab inference**. The suggested Wikipedia pages were treated only as navigation seeds; no conclusion below depends on them.

## 1. Findings

- **Logic Lab inference:** ADR 0002 is the right architecture. An authored Project Revision, an Elaborated Graph, Simulation IR, Boolean Region, and Source Map have different invariants and consumers; merging them would enlarge the Compiler interface and leak runtime ordinals into authored identity.
- **Logic Lab inference:** Compiler should remain one deep Module. Callers request Compilation or Boolean Region extraction; pass order, mutable builders, validators, canonicalization, dense numbering, and layout remain implementation.
- **Logic Lab inference:** S-expressions are useful as a readable notation and debugging projection, not as Logic Lab's authoritative Project Format or in-memory IR. No S-expression product format or evaluator is justified for V1.
- **Logic Lab inference:** Build each artifact with private mutable state, validate it, then publish one owned immutable result. Purpose-specific IR should be strongly typed; textual dumps, if later needed for tests, are derived and non-durable.

## 2. What primary implementations establish

### 2.1 Representation and syntax are separate choices

- **Source fact:** LLVM IR is an SSA-based, typed common representation. LLVM deliberately supplies equivalent in-memory, bitcode, and human-readable assembly forms; the textual notation exists for inspection and visualization, while transformations operate on the in-memory form ([LLVM Language Reference, Abstract and Introduction](https://llvm.org/docs/LangRef.html#abstract)).
- **Source fact:** LLVM distinguishes parser acceptance from well-formed IR and provides a verifier for invariants that are not purely grammatical ([LLVM Language Reference, Well-Formedness](https://llvm.org/docs/LangRef.html#well-formedness)).
- **Source fact:** MLIR similarly separates human-readable, in-memory, and serialized forms. Its typed operations attach semantic restrictions and verification hooks, and its rationale uses progressive lowering to retain high-level information until a target-specific representation needs to discard it ([MLIR Language Reference](https://mlir.llvm.org/docs/LangRef/), [MLIR Rationale](https://mlir.llvm.org/docs/Rationale/Rationale/#introduction-and-motivation)).

**Logic Lab inference:** A syntax that can print an IR does not define the IR's ownership or in-memory layout. Logic Lab should not expose object layout, arrays, ordinals, or a debug printer as a durable contract. Validation must cover graph and semantic invariants after parsing or construction, not merely value shape.

### 2.2 S-expressions are recursively structured data, not automatically a typed IR

- **Source fact:** McCarthy defines an S-expression recursively: an atomic symbol is an S-expression, and an ordered pair of S-expressions is an S-expression; list notation abbreviates nested pairs ([McCarthy, 1960, Section 3a](https://www-formal.stanford.edu/jmc/recursive/node3.html)).
- **Source fact:** R6RS separates lexical syntax, datum syntax, and program syntax. Syntactic data are recursive external representations of objects; program syntax imposes further structure and meaning. The report explicitly notes both the power and the potential contextual confusion of programs also being data ([R6RS, Chapter 4](https://www.r6rs.org/final/html/r6rs/r6rs-Z-H-7.html#node_chap_4)).

**Logic Lab inference:** Parenthesized data gives compact recursive notation and easy tree rewriting, but it does not by itself enforce widths, Port direction, resolved Component Contracts, acyclicity, monotonicity, provenance, or dense-layout invariants. Those facts belong in typed Compiler-owned representations and validators. Logic Lab must never evaluate an imported S-expression or treat it as executable extension code.

### 2.3 Circuit graph and storage evidence

A Net is a hyperedge over Terminal occurrences, so the generated execution relation is bipartite: `Evaluator -> Driver -> resolved Net -> consuming Evaluator`. Sequential outputs cut combinational dependencies; Circuit Definition instances form a separate call graph whose recursion is invalid in V1.

| Problem | Internal choice | Obligation |
|---|---|---|
| affected topology validation | disjoint-set union | preserve authoritative Net identity and deterministic split/merge rules |
| definition recursion | iterative DFS or SCC | return a stable witness without CLR stack exposure |
| combinational regions | Tarjan SCC in `O(V + E)` | retain cycles and topologically order only the condensation graph |
| fanout and Drivers | compressed sparse row arrays | check every offset, length, and Compilation-local ordinal |
| future stimuli | binary min-heap with explicit stable sequence | equal-time order cannot depend on heap ties |
| delta work | dense queue plus generation stamps or bitset | deduplicate without per-node allocation |

Tarjan's original paper establishes the linear-time SCC result ([DOI](https://doi.org/10.1137/0201010)). These are Compiler or Simulation Runtime implementation choices, never new public interfaces.

## 3. Chez Scheme case study

The inspected checkout is the clean local `../ChezScheme/` repository at commit [`814fa4e063665ef24a48a530ad5534c386c46501`](https://github.com/cisco/ChezScheme/tree/814fa4e063665ef24a48a530ad5534c386c46501), dated 2026-06-10. It is not exactly tagged (`git describe --always` reports `814fa4e`), so the commit, not an inferred release number, identifies the evidence.

### 3.1 Observed pipeline

- **Source fact:** Chez documents a pipeline from an annotated S-expression, through syntax objects and macro expansion, into the core `Lsrc` representation, front-end optimization, many later intermediate languages, and finally machine code. Chez emits code directly and implements its own linker ([local `../ChezScheme/IMPLEMENTATION.md`, “Compilation Pipeline”](https://github.com/cisco/ChezScheme/blob/814fa4e063665ef24a48a530ad5534c386c46501/IMPLEMENTATION.md#compilation-pipeline)).
- **Source fact:** `compile.ss` sequences expansion, validation, `cp0`, type analysis, `cpletrec`, checking, commonization, and the Nanopass backend. It also reconstructs readable S-expressions with `$uncprep` for expansion/optimization output ([local `../ChezScheme/s/compile.ss`](https://github.com/cisco/ChezScheme/blob/814fa4e063665ef24a48a530ad5534c386c46501/s/compile.ss), [local `../ChezScheme/s/cprep.ss`](https://github.com/cisco/ChezScheme/blob/814fa4e063665ef24a48a530ad5534c386c46501/s/cprep.ss)).
- **Source fact:** `cpnanopass.ss` composes many named transformations from `L1` through `L16`, including closure conversion, SCC identification, primitive expansion, calling conventions, basic blocks, register allocation, instruction selection, block ordering, and code generation ([local `../ChezScheme/s/cpnanopass.ss`](https://github.com/cisco/ChezScheme/blob/814fa4e063665ef24a48a530ad5534c386c46501/s/cpnanopass.ss)).
- **Source fact:** `np-languages.ss` formally defines the successive languages with `define-language`; they are not one universal list-shaped graph ([local `../ChezScheme/s/np-languages.ss`](https://github.com/cisco/ChezScheme/blob/814fa4e063665ef24a48a530ad5534c386c46501/s/np-languages.ss)).

### 3.2 What Nanopass does—and does not—imply

- **Source fact:** The Nanopass guide describes small, single-purpose passes as easier to inspect, debug, test, and insert. It also identifies raw S-expression costs: pattern-matching/rebuild overhead, traversal boilerplate, and an unenforced grammar. `define-language` and `define-pass` address those costs with formal grammars, record-based terms, generated traversal, and output-language checks ([Nanopass framework guide](https://github.com/nanopass/nanopass-framework-scheme/blob/main/doc/user-guide.stex); original framework paper: [Sarkar, Waddell, and Dybvig, 2004](https://doi.org/10.1145/1016850.1016878); commercial compiler report: [Keep and Dybvig, 2013](https://doi.org/10.1145/2500365.2500618)).

**Logic Lab inference:** Chez supports three disciplined choices:

1. Give each internal stage an explicit grammar or type and invariant.
2. Keep transformations small enough that their precondition and postcondition are testable.
3. Preserve an unparser for inspection without making its S-expression the authoritative in-memory representation.

It does **not** justify copying Chez's number of passes, adding a pass framework, or publishing pass interfaces. Logic Lab has a smaller domain and branching outputs rather than one machine-code target. Plain internal functions over closed C# types are sufficient until repeated traversal or invariant boilerplate is measured.

## 4. Recommended Logic Lab compiler shape

### 4.1 Branch after common semantic validation

```text
Project Revision + entry + Library Snapshot
  -> validate and elaborate hierarchy/connectivity
  -> canonical Elaborated Graph + complete source provenance
       -> lower to dense Simulation IR + Source Map
       -> extract selected binary acyclic Boolean Region + Care Contract
```

**Logic Lab inference:** This is a branching translation, not an obligation to force every purpose through one linear IR. The common Elaborated Graph should retain only facts reused by more than one lowering. Simulation layout and Boolean proof facts stay in their owning outputs.

| Representation | Retains | Deliberately excludes |
|---|---|---|
| Project Revision | stable authored identity, hierarchy declarations, explicit Net membership, presentation | execution ordinals, SCC layout, proof eligibility |
| Elaborated Graph | resolved occurrence identity, Hierarchy Path, validated widths/Drivers/contracts, diagnostic witnesses | mutable Session state, dense storage commitments, geometry |
| Simulation IR | dense ordinals, evaluator/Net graph, CSR adjacency, SCC plan, state/memory schema | authored edit structure, Boolean proof machinery |
| Boolean Region | binary acyclic network, ordered inputs/outputs, Care Contract, source bindings | four-state Session state, scheduler layout, arbitrary authored topology |
| Source Map | total mapping between runtime/analysis facts and stable source identity | browser identity invention, localized messages |

### 4.2 Internal pass obligations

**Logic Lab inference:** Every internal transformation should have a narrow typed input/output and one dominant postcondition. Useful stage checks are:

- all references resolve before hierarchy elaboration;
- every elaborated occurrence has one complete Hierarchy Path and source binding;
- canonical ordering is fixed before dense ordinal assignment;
- SCC membership and condensation order cover each combinational node exactly once;
- CSR offsets are monotone, terminal offsets equal backing-array lengths, and all ordinals are in range;
- Boolean extraction proves binary, combinational, acyclic eligibility and a complete Care Contract before publishing a Region;
- Source Map coverage is total for every externally observable diagnostic, probe, Trace, and replacement binding.

These checks are internal Compiler verification, not new public seams. The Compiler interface remains the test surface; stage validators run as part of that implementation and map defects to the existing internal-invariant outcome.

### 4.3 Publication, caching, and incrementality

**Logic Lab inference:** Full Compilation from an immutable Project Revision is the semantic oracle. Mutable dictionaries, lists, union-find, and graph builders may exist only during a Compilation attempt. Publication transfers exact-sized owned arrays and immutable lookup values into one sealed artifact; failure or cancellation publishes none.

An incremental compiler is not a V1 requirement. If measurement later justifies reuse, cache entries must be keyed by the complete Compilation Artifact Key, remain implementation, and pass full-versus-incremental differential tests including diagnostics, Source Map, ordinals, and artifact digests. An edit-local authored change does not imply an edit-local dense layout.

## 5. Directly relevant .NET 10 implications

- **Source fact:** C# records generate value equality and convenient copying, but referenced arrays are neither copied nor made immutable; mutating a shared array is visible through every record that references it ([C# record types, Value equality](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/types/records#value-equality)).
  **Logic Lab inference:** `record` is not an immutability proof. Artifact constructors should be internal, take ownership of exact-sized buffers, expose no writable array or mutable enumerator, and validate before publication. Closed result variants can still be sealed records.
- **Source fact:** `FrozenDictionary<TKey,TValue>` is optimized for infrequent construction and frequent lookup; creation is relatively expensive, and official guidance says to initialize it only with trusted keys because keys affect construction time ([.NET API reference](https://learn.microsoft.com/en-us/dotnet/api/system.collections.frozen.frozendictionary-2?view=net-10.0), [.NET 10 source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Collections.Immutable/src/System/Collections/Frozen/FrozenDictionary.cs)).
  **Logic Lab inference:** Consider it only for bounded, repeatedly queried artifact indexes over controlled stable-ID value types. Arrays/CSR remain correct for ordinal hot paths; ordinary dictionaries remain better for transient builders. Benchmark before retaining a frozen lookup.
- **Source fact:** Current .NET SIMD guidance recommends cross-platform fixed-width `Vector64/128/256/512<T>` as the starting point for new vectorized algorithms, reserves ISA-specific intrinsics for capabilities not exposed above, and requires measurement because vectorization adds complexity ([Use SIMD and hardware intrinsics in .NET](https://learn.microsoft.com/en-us/dotnet/standard/simd)).
  **Logic Lab inference:** SIMD belongs only in a private packed Logic Vector leaf after the scalar oracle, differential properties, correct tails, supported-width fallback, and representative benchmarks. It does not change Compiler interfaces or IR semantics.
- **Logic Lab inference:** System.Text.Json source generation has no Compiler role in V1 because Compilation Artifacts are derived, opaque, and not a file or browser contract. Project Format owns strict serialization DTOs. Do not create an IR schema or source-generated serializer merely to make internal state inspectable.

No C# 14 feature changes the architectural conclusion. Strong ownership, closed outcomes, checked builders, arrays/CSR, and explicit loops are sufficient; performance features remain internal and evidence-gated.

## 6. Rejected and deferred options

| Option | Disposition | Reason |
|---|---|---|
| one universal graph from editor through Runtime and analysis | reject | conflicting identity, mutation, locality, and proof requirements; weak Compiler depth |
| S-expression as native project or executable format | reject | duplicates strict `.logiclab`, weakens typed invariants, and invites data/code confusion |
| public pass pipeline or visitor interface | reject | callers need outcomes, not transformation choreography; no second adapter exists |
| serialized Compilation Artifact | defer | artifacts are derived from complete provenance; no cross-process deployment need is established |
| incremental Compilation | defer | preserve full Compilation as oracle; justify reuse with corpus evidence and equivalence tests |
| LLVM/MLIR/Nanopass dependency | reject | their principles are evidence; Logic Lab needs no general compiler framework or native toolchain |

## 7. Sources

Primary sources inspected in full or in the directly relevant sections:

- John McCarthy, [Recursive Functions of Symbolic Expressions and Their Computation by Machine, Part I](https://doi.org/10.1145/367177.367199) and the [author-hosted text](https://www-formal.stanford.edu/jmc/recursive/recursive.html).
- [Revised⁶ Report on the Algorithmic Language Scheme](https://www.r6rs.org/final/html/r6rs/r6rs.html), especially Chapter 4.
- LLVM [Language Reference](https://llvm.org/docs/LangRef.html) and MLIR [Language Reference](https://mlir.llvm.org/docs/LangRef/) plus [Rationale](https://mlir.llvm.org/docs/Rationale/Rationale/).
- Sarkar, Waddell, and Dybvig, [A Nanopass Infrastructure for Compiler Education](https://doi.org/10.1145/1016850.1016878); Keep and Dybvig, [A Nanopass Framework for Commercial Compiler Development](https://doi.org/10.1145/2500365.2500618).
- Chez Scheme source and documentation at the exact commit identified in Section 3.
- Microsoft Learn pages and `dotnet/runtime` v10.0.0 source linked at the individual claims in Section 5.
