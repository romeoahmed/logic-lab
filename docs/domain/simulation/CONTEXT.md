# Simulation

Simulation defines how a Compilation Artifact evolves under Logic Lab's four-state, zero-delay, discrete-time semantics and how that evolution is observed.

## Language

**Logic Value**:
One bit in state `0`, `1`, unknown or conflicting `X`, or high-impedance `Z`. `Z` means no effective drive contribution; it is not another spelling of `X`.
_Avoid_: Boolean, nullable Boolean, unconnected

**Driver**:
One Component output or external stimulus contribution to a Net. A Net value is resolved from all of its Drivers.
_Avoid_: Net value, receiver Port, last writer

**Compilation Artifact**:
An immutable executable circuit and Source Map produced from one Project Revision, entry Circuit Definition, Library Snapshot, and Compiler semantic version.
_Avoid_: Project Revision, Project Document

**Source Map**:
The mapping from Compilation-local ordinals to stable source identity and Hierarchy Path.
_Avoid_: Compilation Artifact, Project Document

**Logical Time**:
A non-negative integer instant at which external stimuli or Clock Source transitions occur. It is independent of wall-clock time.
_Avoid_: `DateTime`, animation frame, gate delay

**Stimulus Batch**:
All external Driver changes applied together at one Logical Time before propagation begins.
_Avoid_: last-writer-wins list, Edit Transaction, individual pointer event

**Delta Step**:
One causal propagation round at the current Logical Time without advancing time.
_Avoid_: physical delay, Logical-time Advance

**Quiescent Boundary**:
A committed Session state with no pending propagation at the current Logical Time.
_Avoid_: paused UI, empty event calendar, finished simulation

**Logical-time Advance**:
The atomic attempt to move from one Quiescent Boundary through the next Stimulus Batch and all resulting Delta Steps to the next Quiescent Boundary.
_Avoid_: one Delta Step, partial commit, frame update

**Combinational Feedback Region**:
A strongly connected dependency region containing no state boundary. It is evaluated from canonical `X` to its Least Information Fixed Point.
_Avoid_: sequential loop, physical oscillator, implicit latch

**Information Order**:
The partial order `X <= 0`, `X <= 1`, and `X <= Z` used to define Conservative Merge and legal refinement within one combinational solver epoch. The maximal values are incomparable; this domain is not a lattice.
_Avoid_: numeric ordering, signal strength, event priority

**Least Information Fixed Point**:
The fixed point reached by evaluating a Combinational Feedback Region from all-`X`; it is below every other fixed point in the Information Order. A retained `X` does not by itself distinguish no Boolean fixed point from several incomparable Boolean fixed points.
_Avoid_: guessed stable state, maximal fixed point, physical equilibrium

**Indeterminate Feedback**:
A settled Combinational Feedback Region whose Least Information Fixed Point retains `X`. Further classification requires separate analysis.
_Avoid_: resource exhaustion, definite contention, Zero-time Oscillation

**Sequential Component**:
A Component with persistent state and explicit level-sensitive or edge-triggering transition rules.
_Avoid_: delayed gate, Combinational Feedback Region

**Definite Edge**:
A direct single-bit `0` to `1` or `1` to `0` clock transition. A transition involving `X` or `Z` is not a Definite Edge.
_Avoid_: possible edge, analog threshold crossing

**Trigger Batch**:
All Sequential Components activated by the same settled causal transition, sampled from one pre-commit state and committed together.
_Avoid_: all components once per Logical Time, arbitrary enumeration order

**Conservative Merge**:
The bitwise meet of a nonempty set of possible results: identical results, including `Z`, remain unchanged; any difference produces `X`.
_Avoid_: random branch, last writer, forced zero

**Zero-time Oscillation**:
A proven repeated complete working state caused by sequential or generated-clock activity that cannot reach a Quiescent Boundary at the current Logical Time.
_Avoid_: an unresolved combinational fixed point, timeout, physical frequency

**Simulation Session**:
One Workspace's private running instance of a Compilation Artifact, including committed Logical Time, state, future stimuli, Probes, and Trace.
_Avoid_: Durable Project, Transaction History

**State Migration**:
The conservative transfer of compatible Sequential Component state between Compilation Artifacts.
_Avoid_: Project migration, event-queue copy

**Session Version**:
A monotonic version of committed Simulation Session state.
_Avoid_: Logical Time, Compilation version, event sequence

**Probe**:
A Session observation of one source-bound Net identified by stable source identity and Hierarchy Path.
_Avoid_: Wire Geometry marker, permanent project property

**Trace**:
The bounded, ordered observation record for active Probes across committed Quiescent Boundaries.
_Avoid_: simulation state, permanent audit log, Transaction History

**Trace Gap**:
An explicit unavailable range created by retention or artifact change.
_Avoid_: flat waveform, missing value silently filled with `X`
