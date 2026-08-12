# Diagnostics V1

> Status: normative structure, code, argument, ordering, and safety contract

Diagnostics are structured evidence produced by the Module that owns the behavior. This document solely owns code schemas and the cross-Module uniqueness index; each Module specification owns occurrence. It creates no central Diagnostic Module, generic `Result` type, exception translator, or localization dependency.

## 1. Diagnostic versus outcome

A Diagnostic explains a condition attached to completed or rejected work. An outcome variant states what happened. For example, `AdvanceFailed(ResourceLimit)` is an outcome; it may carry policy evidence but is not converted into an error-severity Diagnostic merely to avoid a typed variant.

Expected user, circuit, package, policy, concurrency, and eligibility conditions return typed outcomes plus zero or more Diagnostics. Exceptions are reserved for defects and infrastructure failures caught at the Application seam. Localized sentences, stack traces, exception type names, and log event text are never interface contracts.

## 2. Diagnostic record

```text
DiagnosticV1
  code: DiagnosticCode
  severity: Info | Warning | Error
  arguments: DiagnosticArgumentV1[]
  primary: SourceLocationV1 | null
  related: SourceLocationV1[]
```

The code determines the exact severity, ordered argument names and kinds, permitted location kinds, and owning Module. Producers cannot override them. Arguments use the catalog order and this closed value union:

```text
UnsignedDecimal     canonical unsigned 64-bit decimal string
StableToken         1–96 ASCII characters matching [A-Za-z0-9][A-Za-z0-9._-]*
ContractKey         { libraryId: StableName, contractId: StableName }
LogicValue          0 | 1 | X | Z
Digest              exactly 64 lowercase hexadecimal SHA-256 characters
CorrelationToken    16–64 lowercase ASCII characters matching [a-z0-9][a-z0-9_-]*
```

`StableName` has the exact syntax defined by [Project Document JSON V1](./project-document-json-v1.md). Correlation tokens are generated server-side, are non-secret, and carry no implementation detail.

No argument contains localized text, a user-authored name, raw JSON, annotation text, file-system path, URL, credential, stack trace, or exception message. Authorized presentation resolves safe display names from `primary`; logs normally record code and low-cardinality operation metadata without arguments or source IDs.

## 3. Source locations

```text
SourceLocationV1 =
  ProjectRoot { projectId }
  | ProjectResource { projectId, resourceKind: memoryImage, resourceId }
  | CircuitRoot { circuitDefinitionId, hierarchyPath: HierarchyPathV1 }
  | CircuitEntity {
      circuitDefinitionId,
      hierarchyPath: HierarchyPathV1,
      entityKind,
      entityId,
      portId?
    }
  | PackagePart { logicalPath, jsonPointer?, byteOffsetDecimal? }
```

`HierarchyPathV1` contains the entry Circuit Definition ID plus ordered `{ containingCircuitDefinitionId, componentInstanceId }` steps. Each step must resolve from the preceding definition target; bare locally scoped Component Instance IDs are never a path.

IDs remain opaque. `entityKind` is one of `definitionPort`, `componentInstance`, `net`, `junction`, `wireGeometry`, or `annotation`. A project-wide Memory Image never receives a fictional Circuit Definition container, and a Circuit Definition root never receives a duplicate entity ID. A Compiler or Runtime diagnostic is translated through its Source Map before crossing its Module seam; Runtime ordinals and internal node indexes are never locations.

Package paths are already validated logical names. JSON Pointer follows RFC 6901 and byte offset counts the original uncompressed part bytes. A location is omitted when revealing it would disclose unauthorized existence or when no honest source position exists.

Canonical Source Location order first uses the variant order shown above, then compares declared fields lexicographically. Canonical strings and opaque IDs use ordinal UTF-8 byte order; an absent optional field sorts before a present field; byte offsets compare as unsigned integers. Hierarchy Paths compare the entry Circuit Definition ID and then their step pairs lexicographically, with the shorter path first when one is a prefix.

## 4. Codes and evolution

Diagnostic codes are lowercase ASCII snake case with an owning prefix. They are never reused with different severity, arguments, or meaning. New codes are additive; removing a code requires a schema-major migration or an explicit supersession record. Unknown codes fail the release qualification test, while an older authorized presentation renders a safe generic fallback instead of guessing argument meaning.

Internal typed variants map one-to-one to these wire codes. RFC 9457 adapters use a stable problem `type` whose final segment equals the applicable outcome reason code; `title` and `detail` remain localized. Codes are not HTTP status codes and do not determine authorization disclosure.

## 5. Circuit Authoring diagnostics

| Code | Severity | Ordered arguments |
|---|---|---|
| `authoring_duplicate_id` | Error | `entityKind:StableToken` |
| `authoring_missing_reference` | Error | `referenceKind:StableToken` |
| `authoring_invalid_width` | Error | `actual:UnsignedDecimal` |
| `authoring_width_mismatch` | Error | `expected:UnsignedDecimal`, `actual:UnsignedDecimal` |
| `authoring_terminal_already_connected` | Error | none |
| `authoring_invalid_parameter` | Error | `contractKey:ContractKey`, `parameterId:StableToken`, `rule:StableToken` |
| `authoring_invalid_text` | Error | `field:StableToken`, `rule:StableToken` |
| `authoring_invalid_coordinate` | Error | `field:StableToken`, `rule:StableToken` |
| `authoring_invalid_memory_image` | Error | `rule:StableToken` |
| `authoring_invalid_route` | Error | `rule:StableToken` |
| `authoring_delete_has_dependents` | Error | `dependentKind:StableToken`, `dependentCount:UnsignedDecimal` |
| `authoring_invalid_split` | Error | `rule:StableToken` |
| `authoring_symbol_variant_incompatible` | Error | `variantId:StableToken`, `contractKey:ContractKey` |
| `authoring_symbol_profile_unresolved` | Error | `profileId:StableToken`, `profileVersion:StableToken` |

`rule` is a closed token owned by Circuit Authoring, such as `missingPartition`, `overlappingPartition`, `nonOrthogonal`, or `parameterKind`; it is not free-form prose.

## 6. Compiler diagnostics

| Code | Severity | Ordered arguments |
|---|---|---|
| `compiler_entry_definition_missing` | Error | none |
| `compiler_hierarchy_recursion` | Error | `cycleLength:UnsignedDecimal` |
| `compiler_library_version_mismatch` | Error | `libraryId:StableToken`, `expectedVersion:StableToken`, `actualVersion:StableToken` |
| `compiler_library_digest_mismatch` | Error | `libraryId:StableToken`, `expected:Digest`, `actual:Digest` |
| `compiler_contract_unresolved` | Error | `contractKey:ContractKey` |
| `compiler_port_unresolved` | Error | `contractKey:ContractKey`, `portId:StableToken` |
| `compiler_required_terminal_unconnected` | Error | none |
| `compiler_parameter_schema_mismatch` | Error | `contractKey:ContractKey`, `parameterId:StableToken`, `rule:StableToken` |
| `compiler_width_mismatch` | Error | `expected:UnsignedDecimal`, `actual:UnsignedDecimal` |
| `compiler_illegal_port_direction` | Error | `direction:StableToken` |
| `compiler_policy_exhausted` | Error | `policyId:StableToken`, `policyRevision:StableToken`, `dimension:StableToken`, `observed:UnsignedDecimal` |
| `compiler_internal_invariant` | Error | `correlation:CorrelationToken` |

Recursion uses the related-location sequence as the canonical witness path. Internal invariant correlations are opaque, non-secret lookup tokens and carry no implementation detail.

## 7. Simulation diagnostics

| Code | Severity | Ordered arguments |
|---|---|---|
| `simulation_net_undriven` | Warning | none |
| `simulation_unknown_driver` | Warning | `driverCount:UnsignedDecimal` |
| `simulation_contention` | Error | `zeroDrivers:UnsignedDecimal`, `oneDrivers:UnsignedDecimal`, `unknownDrivers:UnsignedDecimal` |
| `simulation_indeterminate_feedback` | Warning | `unknownCoordinates:UnsignedDecimal` |
| `simulation_indefinite_clock_edge` | Warning | `previous:LogicValue`, `current:LogicValue` |
| `simulation_control_conflict` | Error | `controlKind:StableToken` |
| `simulation_contract_defect` | Error | `contractKey:ContractKey`, `rule:StableToken`, `correlation:CorrelationToken` |

Undriven, unknown-driver, and contention evidence is computed from final Driver contributions at a Quiescent Boundary. Cause changes that leave the Logic Value unchanged replace prior evidence; they do not trigger propagation. A contract defect fails the whole Logical-time Advance.

`simulation_contract_defect` identifies only a generated component evaluator whose output violates its Component Contract. Its closed `rule` values are:

| Rule | Meaning |
|---|---|
| `coordinate_shape` | An evaluator returned an output vector whose width differs from the compiled Driver width. |
| `information_order` | During cyclic settlement, an evaluator changed a previously non-`X` output bit instead of preserving it. |

A Runtime-owned Net resolver invariant failure must not be attributed to the evaluator that happened to trigger Net reevaluation; it fails as a generic `SimulationInternalDefect` unless a Runtime-owned diagnostic is defined by a later contract.

## 8. Project Format diagnostics

| Code | Severity | Ordered arguments |
|---|---|---|
| `package_illegal_entry` | Error | `rule:StableToken` |
| `package_duplicate_entry` | Error | none |
| `package_unsupported_feature` | Error | `feature:StableToken` |
| `package_limit_exceeded` | Error | `policyId:StableToken`, `policyRevision:StableToken`, `dimension:StableToken`, `observed:UnsignedDecimal` |
| `package_integrity_mismatch` | Error | `partKind:StableToken`, `check:StableToken` |
| `package_schema_version_unsupported` | Error | `actual:UnsignedDecimal` |
| `package_json_invalid` | Error | `rule:StableToken` |
| `package_unknown_member` | Error | none |
| `package_unknown_discriminator` | Error | none |
| `package_memory_invalid` | Error | `rule:StableToken` |
| `package_domain_invalid` | Error | `rule:StableToken` |

Raw illegal entry names, JSON values, and Memory Image bytes are never arguments. A validated logical path may appear only in `primary`.

## 9. Boolean Analysis and Diagram Presentation diagnostics

| Code | Severity | Ordered arguments |
|---|---|---|
| `analysis_policy_exhausted` | Warning | `policyId:StableToken`, `policyRevision:StableToken`, `dimension:StableToken`, `observed:UnsignedDecimal` |
| `analysis_verifier_disagreement` | Error | `correlation:CorrelationToken` |
| `analysis_replacement_rejected` | Warning | `rule:StableToken` |
| `presentation_variant_unresolved` | Error | `profileId:StableToken`, `variantId:StableToken` |
| `presentation_constraint_unsatisfied` | Error | `constraint:StableToken` |
| `presentation_unverified_fallback` | Warning | `contractKey:ContractKey` |
| `presentation_font_fingerprint_mismatch` | Error | `expected:Digest`, `actual:Digest` |
| `presentation_internal_invariant` | Error | `correlation:CorrelationToken` |

`TeachingProjectionUnavailable`, `Inconclusive`, and `NoImprovement` remain Boolean Analysis outcome variants, not warning Diagnostics. Workspace alone wraps exact Compiler ineligibility or teaching-projection unavailability as `NotApplicable`; it is not a Boolean Analysis outcome. A Verifier Disagreement is a defect outcome and no replacement crosses the seam.

## 10. Workspace and Web diagnostics

| Code | Severity | Ordered arguments |
|---|---|---|
| `workspace_compilation_stale` | Warning | none |
| `workspace_probe_unresolved` | Warning | `rule:StableToken` |
| `workspace_history_truncated` | Info | `removedRevisions:UnsignedDecimal` |
| `workspace_attachment_recovered` | Info | none |
| `web_renderer_unavailable` | Error | `reason:StableToken` |
| `web_browser_policy_exhausted` | Error | `policyId:StableToken`, `policyRevision:StableToken`, `dimension:StableToken`, `observed:UnsignedDecimal` |
| `web_browser_contract_rejected` | Error | `rule:StableToken`, `correlation:CorrelationToken` |
| `web_interop_failure` | Error | `correlation:CorrelationToken` |

Authentication, authorization, concurrency, attachment, idempotency, save conflict, cancellation, and infrastructure conditions are Workspace outcome reasons. They are not duplicated as Diagnostics. Web diagnostics describe a local renderer, policy, browser-record, or interop failure and never contain JavaScript exception text, browser payloads, user-authored text, stack traces, or device details. `web_renderer_unavailable.reason` is exactly `contextUnavailable | contextLost | fontUnavailable | assetFingerprintMismatch`. `web_browser_contract_rejected.rule` is exactly `invalidSnapshot | invalidPatch | invalidBatch`. Browser Policy owns its closed dimension tokens. These values are not free-form prose; [Browser Runtime §11](./browser-runtime.md#11-failure-and-recovery) owns their occurrence and recovery behavior.

## 11. Outcome reason registry

The browser and HTTP adapters use these exact reason codes for corresponding closed outcome variants:

| Owner | Reason codes |
|---|---|
| Circuit Authoring | `authoring_invalid` |
| Workspace | `authentication_required`, `workspace_not_found`, `workspace_expired`, `stale_workspace_attachment`, `idempotency_key_conflict`, `idempotency_window_expired`, `durable_claim_unresolved`, `durable_display_name_invalid`, `project_revision_precondition_failed`, `projection_version_precondition_failed`, `compilation_generation_unavailable`, `session_precondition_failed`, `run_generation_precondition_failed`, `operation_precondition_failed`, `durable_save_conflict`, `build_fingerprint_mismatch`, `hot_swap_incompatible`, `workspace_admission_rejected`, `workspace_cancelled`, `workspace_infrastructure_failure`, `workspace_internal_defect`, `operation_expired`, `proposal_stale`, `export_capacity_unavailable`, `export_expired`, `analysis_not_applicable` |
| Durable Project Catalog | `authentication_required`, `forbidden`, `project_catalog_request_invalid`, `project_catalog_cursor_invalid`, `project_catalog_cancelled`, `project_catalog_infrastructure_failure`, `project_catalog_internal_defect` |
| Compiler | `compilation_invalid`, `compilation_policy_exhausted`, `compilation_cancelled`, `compilation_infrastructure_failure`, `compilation_internal_defect` |
| Simulation | `no_scheduled_stimulus`, `zero_time_oscillation`, `simulation_resource_limit`, `simulation_cancelled`, `simulation_infrastructure_failure`, `simulation_internal_defect` |
| Boolean Analysis | `analysis_inconclusive`, `analysis_cancelled`, `analysis_no_improvement`, `analysis_internal_defect` |
| Project Format | `package_invalid`, `package_limit_exceeded`, `package_cancelled`, `package_infrastructure_failure`, `package_internal_defect` |
| Presentation | `layout_invalid`, `layout_cancelled`, `layout_internal_defect` |

An adapter maps these reasons to transport status and retry disposition without changing the code. `IEditorWorkspace` already gives an unauthorized Durable Workspace the same request-shaped outcome as an absent Workspace; adapters must preserve that indistinguishability. At other seams, a `forbidden` response may intentionally use the same HTTP status and body shape as `workspace_not_found` to hide unauthorized existence.

## 12. Ordering and deduplication

Each Module defines a stable phase ordinal. Within a phase, diagnostics sort by canonical primary location, code, canonical argument encoding, then related-location sequence. Severity does not reorder evidence. Exact duplicates collapse; distinct related locations or arguments remain distinct. No order depends on dictionary traversal, task completion, allocation, Runtime ordinal, localized text, or log timestamp.

Workspace preserves owning-Module order when composing authorization-safe Workspace evidence, Project Format, Circuit Authoring, Compiler, then Simulation or Boolean Analysis. Web appends one Diagram Presentation phase only after the matching Schematic Projection attempt, then current browser-local Web diagnostics in adapter order `Scene`, `Waveform` and canonical Diagnostic order within each adapter. Browser-local Diagnostics are not inserted into `WorkspaceProjectionV1` and do not increment Projection Version; replacing or recovering an adapter replaces its current local evidence. A view never interleaves Diagnostics from another Project Revision, Scene/Waveform version, or work arriving after publication.

## 13. Release and test obligations

- generate a uniqueness index from Module-owned definitions and compare it with this catalog;
- prove one-to-one typed variant, wire code, severity, argument schema, and localizer-key mapping;
- reject missing, extra, reordered, wrong-kind, and unsafe arguments;
- perturb collection, worklist, task, and culture order and require identical Diagnostics;
- verify Source Map translation and absence of Runtime ordinals;
- test authorization redaction, RFC 9457 mapping, logs, and unknown-code fallback;
- snapshot English and Simplified Chinese localization with long-argument cases; and
- scan structured logs and browser payloads for forbidden content classes.
