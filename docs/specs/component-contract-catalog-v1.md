# Component Contract Catalog V1

> Status: normative exact catalog
> Library: `logiclab.core` version `1.0.0`

This catalog closes the V1 Component Contract surface. It owns common library rules, exact identities, Port generators, parameters, state shapes, and required evidence. [Simulation Runtime](./simulation-runtime.md) owns four-state execution. A contract not listed here is not a V1 built-in.

## 1. Contract identity and resolution

A Component Contract Key is `(libraryId, contractId)`. A Project Document references exactly one version and digest for each `libraryId`; a Compilation resolves all keys against that immutable Library Snapshot. Duplicate library IDs, unresolved keys, and a digest/version mismatch are errors.

Contract IDs and Port IDs below are stable, case-sensitive ASCII. Display names are localized separately. Reusing a Contract ID in another compatible Library version never changes its Ports, parameter meaning, state shape, truth tables, or package encoding. An incompatible change receives a new Contract ID or Library major version; V1 never loads two versions of `logiclab.core` into one Library Snapshot.

Every instance stores every parameter explicitly. Defaults are authoring conveniences applied before commit, never hidden Compiler inputs. Parameters are ordered by the catalog order below. A parameter that changes Ports or state shape makes Hot Swap incompatible unless both old and new resolved schemas are identical.

## 2. Common invariants and notation

- Widths and generated counts are positive and checked before allocation.
- Connected Ports have equal width; conversion requires an explicit `topology.*extend`, `topology.split`, or `topology.concat` instance.
- Port identity and order come from this catalog, never symbol coordinates or display labels.
- Vector operations are bitwise unless this catalog declares unsigned arithmetic; Logic Vector has no implicit signed interpretation.
- Ordinary outputs produce `0/1/X`; only `logic.tristate` can produce `Z` as a Driver contribution.
- Parameters that change Ports or state participate in Compilation provenance and Hot Swap compatibility.
- No contract executes user code, reflection-selected behavior, a native plug-in, or an external process.

| Notation      | Meaning                                                                                |
| ------------- | -------------------------------------------------------------------------------------- |
| `u32+`        | positive checked unsigned 32-bit integer, additionally bounded by active policy        |
| `u64+`        | positive checked unsigned 64-bit integer                                               |
| `logic[w]`    | exactly `w` authored bits in `0/1/X`; `Z` is invalid for initial state or constants    |
| `enum{...}`   | one exact closed ASCII value                                                           |
| `slices[]`    | nonempty ordered `(offset, length)` pairs with positive length and checked containment |
| `widths[]`    | nonempty ordered positive widths whose checked sum is the result width                 |
| `memoryImage` | reference to one Memory Image with exactly the required width and depth                |

`A:in[w]` means input Port ID `A` and width `w`. A generated family such as `A0..A{n-1}` uses ascending numeric Port order. Bit index zero is least significant. Ordinary inputs normalize `Z` to `X` before evaluation. Checked shape arithmetic and policy admission happen before allocation or Port generation.

## 3. Sources, sinks, and topology

| Contract ID            | Ordered Ports                                    | Parameters in order                                                                                          | Exact contract                                                           |
| ---------------------- | ------------------------------------------------ | ------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------ |
| `source.input`         | `Q:out[w]`                                       | `width:u32+`, `initialValue:logic[w]`                                                                        | Session stimulus owns `Q`; creation publishes the initial value.         |
| `source.constant`      | `Q:out[w]`                                       | `width:u32+`, `value:logic[w]`                                                                               | Immutable Driver value.                                                  |
| `source.clock`         | `Q:out[1]`                                       | `initialValue:logic[1]` restricted to `0/1`, `firstTransition:u64+`, `highDuration:u64+`, `lowDuration:u64+` | Alternates known values; `firstTransition` is after Logical Time zero.   |
| `sink.output`          | `D:in[w]`                                        | `width:u32+`, `radix:enum{binary,hex,unsigned}`                                                              | Observation only; radix has no electrical effect.                        |
| `topology.split`       | `D:in[w]`, `Q0..Q{n-1}:out[slice.length]`        | `width:u32+`, `slices:slices[]` with `n >= 2`                                                                | `Qi[bit] = D[slices[i].offset + bit]`; overlap is valid.                 |
| `topology.concat`      | `D0..D{n-1}:in[widths[i]]`, `Q:out[sum(widths)]` | `inputWidths:widths[]` with `n >= 2`                                                                         | `D0` occupies the least-significant result bits, followed in Port order. |
| `topology.zero_extend` | `D:in[a]`, `Q:out[b]`                            | `inputWidth:u32+`, `outputWidth:u32+` with `b > a`                                                           | Copies normalized `D`; fills high bits with `0`.                         |
| `topology.sign_extend` | `D:in[a]`, `Q:out[b]`                            | `inputWidth:u32+`, `outputWidth:u32+` with `b > a`                                                           | Copies normalized `D`; repeats normalized `D[a-1]` into high bits.       |

Changing `slices` or `inputWidths` changes the generated Port schema. Removing a generated Port never silently reconnects its Terminal.

## 4. Combinational logic

| Contract ID                                                                   | Ordered Ports                                                   | Parameters in order                                                                                  | Exact contract                                                                                                                             |
| ----------------------------------------------------------------------------- | --------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| `logic.buffer`                                                                | `A:in[w]`, `Q:out[w]`                                           | `width:u32+`                                                                                         | Normalized identity per bit.                                                                                                               |
| `logic.not`                                                                   | `A:in[w]`, `Q:out[w]`                                           | `width:u32+`                                                                                         | Scalar NOT per bit.                                                                                                                        |
| `logic.and`, `logic.nand`, `logic.or`, `logic.nor`, `logic.xor`, `logic.xnor` | `A0..A{n-1}:in[w]`, `Q:out[w]`                                  | `width:u32+`, `fanIn:u32+` with `n >= 2`                                                             | Left fold of the named associative scalar oracle; complemented forms negate once after the fold.                                           |
| `logic.tristate`                                                              | `D:in[w]`, `EN:in[1]`, `Q:out[w]`                               | `width:u32+`, `enablePolarity:enum{activeHigh,activeLow}`                                            | Enabled copies normalized `D`; disabled produces `Z`; unknown enable uses Conservative Merge.                                              |
| `logic.mux`                                                                   | `D0..D{2^s-1}:in[w]`, `S:in[s]`, `Q:out[w]`                     | `width:u32+`, `selectorWidth:u32+`                                                                   | Known unsigned `S` selects matching `D`; unknown bits merge all reachable arms.                                                            |
| `logic.demux`                                                                 | `D:in[w]`, `S:in[s]`, `Q0..Q{2^s-1}:out[w]`                     | `width:u32+`, `selectorWidth:u32+`                                                                   | Selected output receives normalized `D`; every other output is `0`; unknown selection merges reachable cases per output.                   |
| `logic.decoder`                                                               | `A:in[s]`, `EN:in[1]`, `Q0..Q{2^s-1}:out[1]`                    | `selectorWidth:u32+`, `enablePolarity:enum{activeHigh,activeLow}`                                    | Disabled is all `0`; enabled emits one-hot index `A`; unknown input/control merges reachable vectors.                                      |
| `logic.priority_encoder`                                                      | `A0..A{n-1}:in[1]`, `Q:out[q]`, `VALID:out[1]`                  | `inputCount:u32+` with `n >= 2`, `priority:enum{lowestIndex,highestIndex}`; `q=max(1,ceil(log2(n)))` | Selects the first asserted input in declared priority. No assertion gives `VALID=0,Q=0`; unknown inputs merge every consistent assignment. |
| `logic.unsigned_compare`                                                      | `A:in[w]`, `B:in[w]`, `LT:out[1]`, `EQ:out[1]`, `GT:out[1]`     | `width:u32+`                                                                                         | Merges the three-bit comparison result over every binary assignment consistent with normalized inputs.                                     |
| `logic.adder`                                                                 | `A:in[w]`, `B:in[w]`, `CIN:in[1]`, `SUM:out[w]`, `COUT:out[1]`  | `width:u32+`                                                                                         | Least-significant-first full-adder carry chain.                                                                                            |
| `logic.subtractor`                                                            | `A:in[w]`, `B:in[w]`, `BIN:in[1]`, `DIFF:out[w]`, `BOUT:out[1]` | `width:u32+`                                                                                         | Least-significant-first full-subtractor borrow chain for `A-B-BIN`.                                                                        |
| `logic.shift`                                                                 | `D:in[w]`, `AMOUNT:in[s]`, `Q:out[w]`                           | `width:u32+`, `direction:enum{left,right}`; `s=max(1,ceil(log2(w)))`                                 | Logical zero-fill shift; a known amount `>=w` produces zero; unknown amount merges all reachable amounts.                                  |

`2^s`, result widths, and possible-case sets use checked arithmetic and active policies. Policy exhaustion returns a structured failure and never substitutes a value. All rules must be monotone in the Simulation Information Order.

## 5. Sequential logic

Stored bits are only `0/1/X`. `Q` exposes current state and `QN` is scalar NOT of current state. `edge` is `rising` or `falling`; only the corresponding Definite Edge triggers. Unknown control evaluates every reachable control case and applies Conservative Merge.

| Contract ID                 | Ordered Ports                                                                                            | Parameters in order                                                                                        | State and transition                                                                                                                                                                                     |
| --------------------------- | -------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `sequential.sr_latch`       | `S:in[1]`, `R:in[1]`, `Q:out[1]`, `QN:out[1]`                                                            | `initialState:logic[1]`                                                                                    | Active-high `S/R`: `00` holds, `10` sets, `01` resets, `11` stores `X` and emits control conflict.                                                                                                       |
| `sequential.d_latch`        | `D:in[w]`, `EN:in[1]`, `Q:out[w]`                                                                        | `width:u32+`, `initialState:logic[w]`                                                                      | After any settled `D` or `EN` change, active-high `EN=1` stores normalized `D`; `EN=0` holds; unknown `EN` merges both cases.                                                                            |
| `sequential.dff`            | `D:in[w]`, `CLK:in[1]`, `Q:out[w]`                                                                       | `width:u32+`, `edge:enum{rising,falling}`, `initialState:logic[w]`                                         | Stores normalized `D` on the configured Definite Edge.                                                                                                                                                   |
| `sequential.jkff`           | `J:in[1]`, `K:in[1]`, `CLK:in[1]`, `Q:out[1]`, `QN:out[1]`                                               | `edge:enum{rising,falling}`, `initialState:logic[1]`                                                       | On edge: `00` hold, `10` set, `01` reset, `11` toggle.                                                                                                                                                   |
| `sequential.tff`            | `T:in[1]`, `CLK:in[1]`, `Q:out[1]`, `QN:out[1]`                                                          | `edge:enum{rising,falling}`, `initialState:logic[1]`                                                       | On edge: `0` holds and `1` toggles.                                                                                                                                                                      |
| `sequential.register`       | `D:in[w]`, `CLK:in[1]`, `EN:in[1]`, `Q:out[w]`                                                           | `width:u32+`, `edge:enum{rising,falling}`, `initialState:logic[w]`                                         | On edge, active-high `EN` stores normalized `D`; otherwise holds.                                                                                                                                        |
| `sequential.shift_register` | `PARALLEL:in[w]`, `SERIAL:in[1]`, `LOAD:in[1]`, `CLK:in[1]`, `EN:in[1]`, `Q:out[w]`, `SERIAL_OUT:out[1]` | `width:u32+`, `direction:enum{towardHigh,towardLow}`, `edge:enum{rising,falling}`, `initialState:logic[w]` | On edge: `LOAD=1` stores `PARALLEL`; otherwise `EN=1` shifts one bit; otherwise it holds. Unknown controls merge all reachable cases. `SERIAL_OUT` is the current state bit that the next shift removes. |
| `sequential.counter`        | `LOAD_VALUE:in[w]`, `LOAD:in[1]`, `CLK:in[1]`, `EN:in[1]`, `Q:out[w]`, `TERMINAL:out[1]`                 | `width:u32+`, `direction:enum{up,down}`, `edge:enum{rising,falling}`, `initialState:logic[w]`              | On edge: `LOAD` has priority, then `EN` counts modulo `2^w`, else hold. `TERMINAL` is `1` at all-ones for up or all-zero for down, and `X` when undecidable.                                             |

For `towardHigh`, `SERIAL` enters state bit zero and `SERIAL_OUT` reads bit `w-1`; for `towardLow` the directions reverse. Synchronous `LOAD` is evaluated before `EN`. V1 has no hidden reset/set options; future control combinations require distinct contract IDs.

## 6. Memory

| Contract ID              | Ordered Ports                                             | Parameters in order                                               | Exact contract                                                                                      |
| ------------------------ | --------------------------------------------------------- | ----------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| `memory.rom`             | `A:in[a]`, `Q:out[w]`                                     | `addressWidth:u32+`, `wordWidth:u32+`, `initialImage:memoryImage` | Depth is exactly `2^a`; asynchronous read follows Simulation Runtime.                               |
| `memory.ram_single_port` | `A:in[a]`, `D:in[w]`, `WE:in[1]`, `CLK:in[1]`, `Q:out[w]` | `addressWidth:u32+`, `wordWidth:u32+`, `initialImage:memoryImage` | Depth is exactly `2^a`; asynchronous read and definite rising-edge write follow Simulation Runtime. |

The referenced Memory Image must match word width and depth exactly. V1 has no omitted-image state: an authoring convenience must create and bind an explicit all-`X` Memory Image before commit; Project Format supplies no implicit default.

A Circuit Definition instance is not a `logiclab.core` row. It behaves as a Component Contract whose ordered Ports are the definition's public contract. Compiler rejects recursive definition graphs with a stable witness path, creates a unique Hierarchy Path for every elaborated occurrence, and preserves authored identity through Source Map.

## 7. Evidence manifest

The released library contains one canonical manifest row per Contract ID:

```text
contractId
contractSchemaDigest
semanticOracleId
compilerLoweringId
parameterAndInvalidCaseFixtureIds[]
serializationFixtureIds[]
symbolVariantIds[]
propertySuiteIds[]
browserScenarioIds[]
```

Rows are sorted by Contract ID and reject unknown or duplicate evidence keys. `contractSchemaDigest` covers ordered Ports, parameter schemas, state shape, and referenced semantic rule version—not implementation types. Release fails when any catalog entry or required evidence reference is absent. The complete diagnostic codes and argument schemas are owned by [Diagnostics V1](./diagnostics-v1.md).

## 8. Required cross-contract evidence

- exact parameter and generated-Port snapshots for minimum, ordinary, and policy-edge shapes;
- rejection of missing, duplicate, reordered, unknown, wrong-kind, overflowed, and out-of-range parameters;
- exhaustive scalar/control tables and monotonicity checks where the domain is finite;
- property and differential tests for vector width, `X/Z`, possible-case Merge, state, and memory behavior;
- deterministic Compiler lowering and diagnostics under collection-order perturbation;
- strict Project Format round trips bound to exact Component Contract Keys;
- Geometry Plan and keyboard/browser coverage for every contract family; and
- Hot Swap compatibility matrices over parameter, Port, state, and library changes.

## 9. Deferred variants

Asynchronous or synchronous set/reset variants, clock enable combined with reset, arithmetic overflow flags, signed arithmetic, rotate/arithmetic shifts, multi-port or synchronous-read RAM, byte enables, read-during-write modes, metastability, and user executable contracts are outside V1. They are new Component Contracts, not optional unknown fields on the contracts above.
