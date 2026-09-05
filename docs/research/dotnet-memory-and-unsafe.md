# .NET Memory and Unsafe Code Evidence

> Sources reviewed: 2026-09-05
> Authority: platform evidence for [Engineering](../engineering.md#performance-and-publication)

## Ownership and views

A span describes a contiguous region without owning it. `ReadOnlySpan<T>` and
`ReadOnlyMemory<T>` restrict writes through that view; another alias can still change
its backing buffer. Microsoft's owner/consumer model separates the storage owner
from each consumer's lease. An asynchronous consumer keeps its lease until its task
completes, faults, or is cancelled, so requesting cancellation alone does not release
the buffer. An `IMemoryOwner<T>` must eventually be disposed or transferred, and a
transfer ends the previous owner's access.
[Memory usage guidelines](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines)

This supports Logic Lab's use of owned immutable values at Module seams and spans
inside synchronous kernels. `Memory<T>` can support an adapter's asynchronous I/O
while the adapter retains its backing storage; it does not itself provide snapshot
semantics. Native asynchronous calls also need ownership and pinning through actual
completion. The interface and adoption rules belong in Engineering.

C# 14 adds implicit span conversions and extends generic inference and overload
resolution. An SDK or language upgrade can therefore change which overload a call
selects. Prefer an unambiguous internal span signature, or an explicit `.AsSpan()`
when the selected overload matters.
[C# 14 span conversions](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14#implicit-span-conversions)

## Pool leases and clearing

`ArrayPool<T>.Rent` may return more elements than requested and does not promise
zeroed storage. The requested length defines the useful slice; the rental's length
is its capacity.
[Rent contract](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1.rent?view=net-10.0)

Return a rental to the same pool at most once and discard every view after return.
The `clearArray` option clears storage only if the pool retains it for reuse. This is
not an unconditional zeroization guarantee.
[Return contract](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1.return?view=net-10.0)

Even a `finally` block can return storage too early if failed work left a consumer
running. Keeping rent, use, and return together makes that lifetime easier to inspect.
[Pooling guidance](https://learn.microsoft.com/en-us/dotnet/standard/unsafe-code/best-practices#20-arraypooltshared-and-similar-pooling-apis)

Ordinary content can be cleared with `Span<T>.Clear`. When security requires
zeroization that an optimizer must not remove, `CryptographicOperations.ZeroMemory`
is the dedicated byte-buffer primitive. Clearing does not repair an escaped alias
or an early pool return.
[ZeroMemory contract](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.zeromemory?view=net-10.0)

## Stack storage

`stackalloc` storage lasts until method return. Repeating it in a loop accumulates
stack use, and contents are undefined until initialized. Available stack capacity
depends on the environment; the documentation's sample threshold is not a portable
budget. These facts support bounded, initialized span scratch rather than a copied
numeric limit or raw pointer convention.
[stackalloc reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/stackalloc)

## Unsafe access and binary layouts

The JIT can eliminate ordinary bounds checks and optimize safe copy and fill idioms.
Removing checks manually therefore needs measurements on the current runtime, not
an assumption that `Unsafe` is faster.
[Bounds-check guidance](https://learn.microsoft.com/en-us/dotnet/standard/unsafe-code/best-practices#9-unsafe-bounds-check-removal)

Raw struct reads also carry alignment, padding, and representation hazards; an
`unmanaged` constraint does not establish a portable serialization format. Rewriting
references or relying on private object layout adds GC invariants unrelated to the
circuit model. Logic Lab's field-by-field package and digest encodings avoid those
obligations.
[Binary-layout guidance](https://learn.microsoft.com/en-us/dotnet/standard/unsafe-code/best-practices#12-binary-deserialization-of-structs-with-paddings-or-non-blittable-members)

Pooling, SIMD, pinning, and unsafe access are separate choices with separate evidence.
[Engineering](../engineering.md#performance-and-publication) owns their adoption
requirements; [Engine Performance](./engine-performance.md) records actual profiles
and comparisons. No generic platform example establishes a production threshold.
