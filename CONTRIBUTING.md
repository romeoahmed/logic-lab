# Contributing

Start with the [local setup](./README.md#get-started),
[documentation map](./docs/README.md), and [repository guidelines](./AGENTS.md).
[Delivery](./docs/delivery.md) identifies the current implementation frontier.

## Changes

- Keep each change focused. Read its implementation, tests, and owning specification
  or contract before editing. [Architecture](./docs/architecture.md) defines ownership.
- Update the authoritative document when behavior changes; avoid copying the same
  rule into several files. Fix links when renaming files.
- Use the pinned SDK and centrally managed packages. Regenerate dependency locks with
  the package manager.
- Match tests to observable behavior. Do not pin private structure or duplicate
  compiler checks. [Engineering](./docs/engineering.md) owns the test and performance
  policies.
- Discuss large changes in an issue before opening a pull request. Focused fixes can
  go directly to review.

## Verification

```sh
dotnet build logic-lab.slnx --nologo
dotnet test --solution logic-lab.slnx
dotnet format logic-lab.slnx --verify-no-changes
git diff --check
```

Set `LOGICLAB_TEST_POSTGRES_CONNECTION_STRING` to an administrative connection for a
local PostgreSQL 18 instance. Integration tests create and remove isolated databases;
use a disposable instance.

Run the checks affected by the change and report their results. Documentation changes
need link and terminology checks; visual changes need actual browser interaction and
screenshots; deployment changes need the checks in the
[release runbook](./docs/deployment/runbook.md).

## Pull requests

Use imperative Conventional Commit subjects, for example
`fix: preserve probe order on hot swap`. Explain the problem and resulting behavior,
link affected modules, authoritative documents and issues, and report verification.
Call out contract, format, dependency, security, or deployment changes. Include
screenshots for visible changes.

Review the diff for unrelated edits, generated artifacts, secrets, stale names, and
broken links before submitting.

## Contribution license

Unless you explicitly state otherwise, any contribution intentionally submitted
for inclusion in Logic Lab by you, as defined in the Apache License, Version 2.0,
is dual-licensed under MIT OR Apache-2.0 without additional terms or conditions.
