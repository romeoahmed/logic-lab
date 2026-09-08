# Simulation Glossary

Simulation describes how a compiled circuit evolves under four-state, zero-delay,
discrete-time semantics and how that evolution is observed. The
[runtime specification](../specs/simulation-runtime.md) owns execution rules.

## Values and compiled circuits

**Logic Value**: One bit in state `0`, `1`, unknown or conflicting `X`, or
high-impedance `Z`. `Z` contributes no effective drive; it differs from `X`.

**Driver**: One Component output or external stimulus contribution to a Net.
A Net value is resolved from all of its Drivers.

**Compilation Artifact**: An immutable executable circuit and Source Map produced
from one Project Revision, entry Circuit Definition, Library Snapshot, and Compiler
semantic version.

**Source Map**: The mapping from Compilation-local ordinals to stable source identity
and Hierarchy Path.

## Time and transitions

**Logical Time**: A non-negative integer instant at which external stimuli or Clock
Source transitions occur, independent of wall-clock time.

**Stimulus Batch**: External Driver changes scheduled together for one Logical Time.
Compatible batches at that time are applied together before propagation begins.

**Delta Step**: One causal propagation round at the current Logical Time, without
advancing time.

**Quiescent Boundary**: A committed Session state with no pending propagation at the
current Logical Time. Future events may still be scheduled.

**Logical-time Advance**: The atomic attempt to process the next Logical Time with
stimuli or Clock Source transitions, including all resulting Delta Steps, and reach
the next Quiescent Boundary.

**Sequential Component**: A Component with persistent state and explicit
level-sensitive or edge-triggering transition rules.

**Definite Edge**: A direct single-bit `0` to `1` or `1` to `0` clock transition.
Transitions involving `X` or `Z` do not qualify.

**Trigger Batch**: All Sequential Components activated by the same settled causal
transition, sampled from one pre-commit state and committed together.

## Uncertainty and feedback

**Information Order**: The partial order `X <= 0`, `X <= 1`, and `X <= Z` defining
Conservative Merge and legal refinement within one combinational solver epoch.
The maximal values are incomparable; this domain is not a lattice.

**Conservative Merge**: The bitwise meet of a nonempty set of possible results.
Identical results, including `Z`, remain unchanged; any difference produces `X`.

**Combinational Feedback Region**: A strongly connected dependency region with no
state boundary, evaluated from canonical `X` to its Least Information Fixed Point.

**Least Information Fixed Point**: The fixed point reached by evaluating a
Combinational Feedback Region from all-`X`, below every other fixed point in the
Information Order. A retained `X` alone cannot distinguish no Boolean fixed point
from several incomparable Boolean fixed points.

**Indeterminate Feedback**: A settled Combinational Feedback Region whose Least
Information Fixed Point retains `X`. Further classification requires separate analysis.

**Zero-time Oscillation**: A proven repeated complete working state caused by
sequential or generated-clock activity that prevents a Quiescent Boundary at the
current Logical Time. An unresolved combinational fixed point or a timeout alone
does not prove oscillation.

## Sessions and observation

**Simulation Session**: One Workspace's private instance of a Compilation Artifact,
including committed Logical Time, state, future stimuli, Probes, and Trace.

**State Migration**: Transfer of compatible Sequential Component state and RAM
contents between Compilation Artifacts. It does not copy the event queue.

**Session Version**: A monotonic version of committed Simulation Session state,
independent of Logical Time.

**Probe**: A Session observation of one source-bound Net identified by stable source
identity and Hierarchy Path.

**Trace**: The bounded, ordered observation record for active Probes across committed
Quiescent Boundaries.

**Trace Gap**: An explicitly unavailable observation range, such as evicted history
or a Probe without a retained baseline. It must remain distinguishable from a flat
waveform or a measured `X`.
