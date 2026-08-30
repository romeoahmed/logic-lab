# .NET Memory and Unsafe Code Research

> Verified 2026-07-29 (Asia/Shanghai)
> Scope: .NET 10, C# 14, and optimization choices for Logic Lab
> Authority: platform evidence and design recommendations; normative ownership remains in `ARCHITECTURE.md` and focused specifications

## 1. Conclusion

Logic Lab should begin with owned managed storage and safe, idiomatic loops. Spans are synchronous views, not ownership; memory and pool types add lifetime obligations, not automatic performance. Unsafe code is justified only for a measured leaf kernel whose safe implementation remains the semantic oracle.

This is stricter than the platform requires. .NET permits `Memory<T>` across asynchronous work and supports explicit pinning and unsafe access, but Logic Lab has no native production dependency that would justify those lifetime obligations.

## 2. Platform facts and Logic Lab decisions

| Concern | Verified platform fact | Logic Lab decision |
|---|---|---|
| Synchronous input | `Span<T>` and `ReadOnlySpan<T>` are non-owning contiguous views; slicing changes only reference/offset metadata. A span is a `ref struct` and cannot be stored in an ordinary heap object ([memory types](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/), [`ref struct` rules](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/ref-struct)). | Use `ReadOnlySpan<T>` for read-only internal kernels and `Span<T>` only when mutation is intentional. External Module interfaces continue to use owned immutable domain values; packed layout stays hidden. |
| Asynchronous work | Since C# 13, an async method may contain a span or other `ref struct`, but it cannot access that value across an `await` boundary ([C# 13](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-13#ref-and-unsafe-in-iterators-and-async-methods)). `Memory<T>` can be retained on the heap; its lease lasts until a returned task completes, faults, or is cancelled ([usage rules 3–4](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines#usage-guidelines)). | Put span work in a synchronous helper on one side of each `await`. An Infrastructure adapter may use `Memory<T>` for a BCL async call only while it owns the backing store through the task's terminal state; it does not publish that memory as a domain value or Module result. |
| Read-only views | `ReadOnlySpan<T>` and `ReadOnlyMemory<T>` prevent mutation through that view; the owner or another consumer may still mutate the backing buffer. The official model therefore requires one consumer at a time unless external synchronization exists ([owners, consumers, and leases](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines#owners-consumers-and-lifetime-management)). | Do not treat `ReadOnlyMemory<T>` as an immutable snapshot. Copy or transfer an owned immutable value where snapshot semantics cross a Module seam. |
| Lifetime annotation | `scoped` narrows a ref's ref-safe-context, or a `ref struct` value's safe-context, to the current function; it promises that the value/reference does not escape ([C# 11 ref-safety specification](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-11.0/low-level-struct-improvements.md#scoped-modifier)). | Use `scoped` only when an internal signature needs to state a non-escape contract. It is neither ownership transfer nor a performance hint, and it cannot make a dangling owner valid. |

### C# 14 qualification

C# 14 makes array, string, `Span<T>`, and `ReadOnlySpan<T>` conversions first-class for applicability, inference, and overload resolution, and often prefers a read-only span target ([C# 14 overview](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14#implicit-span-conversions), [language design](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-14.0/first-class-span-types.md)). This can change which existing overload binds. Logic Lab should avoid parallel array/span overload families at Module interfaces, prefer one read-only span overload inside a kernel, and use an explicit `.AsSpan()` where binding must be obvious.

The inspected unsafe-code article also described a new caller-unsafe model, but identified it as a C# 15/.NET 11 preview. It is not part of the C# 14/.NET 10 baseline; `AllowUnsafeBlocks` and the original unsafe-context rules still apply ([unsafe code](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/unsafe-code#two-models-for-unsafe-code)).

## 3. Ownership and pooling

`IMemoryOwner<T>` represents the single owner of a `Memory<T>` buffer. Holding an owner means it must eventually be disposed or transferred, but not both; accepting an `IMemoryOwner<T>` parameter means accepting ownership. The old owner must stop using the buffer after transfer, and disposal must wait until every consumer is finished ([usage rules 7–8](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines#rule-7-if-you-have-an-imemoryownert-reference-you-must-at-some-point-dispose-of-it-or-transfer-its-ownership-but-not-both), [`IMemoryOwner<T>`](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.imemoryowner-1?view=net-10.0)). `MemoryPool<T>.Rent` returns such an owner whose memory is at least the requested length ([`MemoryPool<T>.Rent`](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.memorypool-1.rent?view=net-10.0)).

`ArrayPool<T>.Rent` returns an array at least as large as requested and the array may contain old data. It must be returned to the same pool exactly once; after `Return`, the caller owns nothing and must not retain any array, span, or memory view. `clearArray` defaults to false and clears only when the pool retains the array for reuse ([`Rent`](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1.rent?view=net-10.0), [`Return`](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1.return?view=net-10.0)). The .NET 10 shared-pool source confirms bucket-sized, potentially uninitialized allocation and conditional clearing; these are implementation observations, not extra interface promises ([source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Buffers/SharedArrayPool.cs)).

Logic Lab policy:

1. Allocate normally until allocation profiles show a material hot spot. Pooling is not a default architecture.
2. Prefer `ArrayPool<T>` for synchronous, nonescaping scratch wholly contained in one leaf method. Immediately slice to the logical length; never interpret `array.Length` as requested length.
3. Use `MemoryPool<T>` only when an internal adapter genuinely needs explicit ownership across asynchronous work. Do not transfer a pooled owner across a public Module seam.
4. Keep rent/use/return in one auditable scope. `try/finally` is correct only after all possible consumers have stopped; if failed code could still be using a buffer, abandoning it is safer than returning it early ([pooling guidance](https://learn.microsoft.com/en-us/dotnet/standard/unsafe-code/best-practices#20-arraypooltshared-and-similar-pooling-apis)).
5. Never retain a pooled view in a field, task, callback, cache, result, diagnostic, or Trace. Cancellation does not shorten the lease until the operation is terminal.

## 4. Stack storage and initialization

`stackalloc` storage is released only when the method returns, is not garbage-collected, and does not need pinning. Its contents are undefined unless initialized. Stack capacity depends on the environment; allocations in a loop accumulate until method return, and excessive allocation can terminate the process ([`stackalloc`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/stackalloc), [unsafe guidance](https://learn.microsoft.com/en-us/dotnet/standard/unsafe-code/best-practices#14-stackalloc)).

Logic Lab should consume every `stackalloc` as `Span<T>` or `ReadOnlySpan<T>`, never as a raw pointer. Use only a small compile-time constant or a nonnegative, checked length below one centrally measured byte cap; allocate or rent above it. The official example of 1,024 bytes is illustrative, not a portable guarantee, so no exact threshold becomes a domain invariant. Never place `stackalloc` inside a loop, and clear or fully initialize it before reading.

## 5. Pinning and native lifetime

`fixed` keeps a managed referent at one address only for the statement and the pointer may not escape that scope. It supports arrays, strings, spans, and types exposing `GetPinnableReference` ([`fixed`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/fixed)). `Memory<T>.Pin` instead returns a `MemoryHandle`; the GC may not move the storage until that handle is disposed ([`Memory<T>.Pin`](https://learn.microsoft.com/en-us/dotnet/api/system.memory-1.pin?view=net-10.0)). A pinned `GCHandle` must be explicitly freed or it leaks the handle and keeps the object pinned ([`GCHandle`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.gchandle?view=net-10.0)).

Logic Lab requires none of these in V1: managed Engine, Boolean Analysis, Project Format, and Web code do not call native simulation or solver libraries. A future native interop ADR must keep synchronous pointers inside `fixed`, keep async `MemoryHandle`/`GCHandle` ownership until the native completion callback, free on every synchronous and asynchronous failure path, and expose a safe managed interface. `Unsafe.AsPointer` is not a substitute for pinning; the GC does not track the resulting pointer ([unsafe guidance §§1–2](https://learn.microsoft.com/en-us/dotnet/standard/unsafe-code/best-practices#1-untracked-managed-pointers-unsafeaspointer-and-friends)).

## 6. Unsafe and binary-memory hazards

The safe implementation is the default because the .NET 10 JIT already eliminates many ordinary bounds checks and can generate equivalent code for idiomatic `CopyTo`, `SequenceEqual`, `Fill`, and `Clear`. Removing checks manually is justified only after measuring current-runtime code in a representative hot path ([bounds and coalescing guidance](https://learn.microsoft.com/en-us/dotnet/standard/unsafe-code/best-practices#9-unsafe-bounds-check-removal)).

Any future unsafe leaf kernel must make these obligations explicit:

- **Bounds:** validate all offsets, element counts, byte counts, and checked products before the unsafe block. `Debug.Assert` is useful evidence but provides no release safety.
- **Overlap:** define whether overlap is legal. Prefer `Span<T>.CopyTo`/`TryCopyTo`, which guarantee copying the original source even when ranges overlap; .NET 10 implements this with `Memmove` ([interface](https://learn.microsoft.com/en-us/dotnet/api/system.span-1.copyto?view=net-10.0), [source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Span.cs#L317-L358)).
- **Alignment and atomicity:** do not use coalesced or unaligned reads/writes in lock-free or shared-state logic. Managed objects can move and change alignment unless pinned; unaligned access can tear, lose atomicity, incur platform penalties, or fault on some platforms ([unaligned access](https://learn.microsoft.com/en-us/dotnet/standard/unsafe-code/best-practices#11-unaligned-memory-access)).
- **Endianness:** package and digest encodings use explicit `BinaryPrimitives.Read*LittleEndian`/`Write*LittleEndian`, never host layout or `BitConverter.IsLittleEndian` branches spread through callers ([`BinaryPrimitives`](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.binary.binaryprimitives?view=net-10.0)).
- **Layout:** never deserialize `project.json`, memory-image headers, or digests with `MemoryMarshal.Read<T>`/`Unsafe.ReadUnaligned<T>`. `where T : unmanaged` does not prove blittability or absence of padding, and bitwise struct operations can disclose padding or misread `bool` and other non-blittable values ([binary serialization hazards](https://learn.microsoft.com/en-us/dotnet/standard/unsafe-code/best-practices#12-binary-deserialization-of-structs-with-paddings-or-non-blittable-members)).
- **Managed references:** never reinterpret objects or structs containing GC references, manufacture invalid byrefs, bypass the GC write barrier, mutate strings, or depend on object headers, padding, private BCL layout, LOH placement, or literal addresses ([unsafe guidance §§3–7](https://learn.microsoft.com/en-us/dotnet/standard/unsafe-code/best-practices#3-internal-implementation-details-of-the-runtime-and-libraries)).

`MemoryMarshal` and `Unsafe` therefore stay internal and absent by default. A permitted use is a small, proven primitive-only packed kernel with explicit byte order, length and overlap preconditions, a safe wrapper, and no GC-containing type. Project Format remains field-by-field even if a raw layout appears faster.

## 7. Clearing and data exposure

Ordinary scratch that might retain references or project bytes is cleared before reuse when retention or cross-request disclosure matters. For pooled arrays, slice by logical length while processing but clear the entire rented array when the policy requires eliminating data visible to a later renter. `Span<T>.Clear` is suitable for ordinary content; `CryptographicOperations.ZeroMemory` is the byte-buffer primitive when zeroization must not be removed by optimization. Its .NET 10 implementation is deliberately non-inlined and non-optimized before calling `Clear` ([API](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.zeromemory?view=net-10.0), [source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/CryptographicOperations.cs#L60-L66)).

Clearing is not a substitute for ownership: a returned array is invalid even when cleared, and a read-only view is not a secret snapshot. Logic Lab should not pool authentication tokens, cryptographic key material, raw upload credentials, or other secrets unless a later security design proves the full lifetime and zeroization path.

## 8. Evidence gate and safe fallback

An optimization may replace safe code only when all of the following are present:

1. a production-shaped allocation/CPU profile identifies the kernel rather than a speculative microbenchmark;
2. a benchmark on every supported runtime/architecture compares safe baseline, pooled/vectorized candidate, cold and steady state, allocation, and realistic small/tail/adversarial sizes;
3. the safe implementation remains available as the oracle and fallback, with property-based differential tests over empty, one-element, non-word-aligned, maximum-policy, overlapping, cancelled, and malformed inputs;
4. fuzzing targets every parser or unsafe wrapper, and mutation tests show bounds, tail masks, byte order, overlap, and pool-lifetime checks can fail;
5. CI builds projects with `AllowUnsafeBlocks=false` unless the project owns an approved unsafe kernel, treats unsafe-related warnings as errors, and reruns the comparison when the .NET runtime changes; and
6. the measured gain is material end to end. If it disappears on .NET 10's JIT or another supported architecture, delete the unsafe path.

These are Logic Lab proof requirements, derived from Microsoft's direction to prefer safe idioms, measure real-world impact, test the current JIT, and fuzz unsafe code ([unsafe best practices](https://learn.microsoft.com/en-us/dotnet/standard/unsafe-code/best-practices)). Parallelization, pooling, SIMD, pinning, and unsafe code are independent choices; evidence for one does not authorize another.

## 9. Source and access record

The Microsoft Learn, C# specification, .NET 10 API, and tagged `dotnet/runtime` v10.0.0 sources linked at individual claims were accessed on 2026-07-29. Exact stack thresholds, pool break-even points, SIMD widths, pin-duration budgets, and the supported architecture matrix remain unqualified until representative deployment evidence exists.
