---
status: accepted
---

# Use one strict `.logiclab` carrier

The native V1 project carrier is one `.logiclab` ZIP profile containing a strict JSON
Project Document and canonical binary memory images. Project Format owns bounded
spooling, ZIP validation, strict V1 DTOs, digests, and encoding, and returns a complete
Import Candidate. Application creates Project Genesis and attempts Compilation before
opening an independent Workspace. Ordinary circuit errors remain editable with their
diagnostics; malformed data and operational failures reject opening under the
[Workspace contract](../contracts/editor-workspace.md#3-editor-workspace-interface).
Requiring an executable circuit would make unfinished exported projects impossible
to reopen for repair. Unsupported schema versions are rejected without a speculative
migration layer.

A single JSON document would bloat large memory images; multiple interchangeable codecs would create hypothetical seams and migration drift. One strict carrier centralizes untrusted-input defense and versioning. Runtime state, Trace, history, and arbitrary attachments are excluded. The byte contract is [Project Package V1](../specs/project-package-v1.md).
