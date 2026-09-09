# Component evidence

[The component catalog](../docs/specs/component-contract-catalog-v1.md#7-evidence-manifest)
owns the manifest contract. `manifest.json` contains its canonical, sorted rows;
`evidence.json` maps each reference to reviewed contract scopes and executable test
selectors. Neither file is generated from test names or coverage percentages.

The offline `LogicLab.Conformance` executable checks the current library's Contract
IDs and schema digests, reference kinds and scopes, and exact passed case counts in
MTP TRX reports. Unknown or duplicate JSON properties, missing fields, inconsistent
TRX summaries, duplicate reports, skipped cases, and failed cases reject verification.
Only a successful check writes the normalized output manifest.

After restoring and building the Release solution, run:

```sh
bash .github/scripts/verify-component-evidence.sh /tmp/logiclab-component-evidence
```

The output directory must not exist. Set the isolated PostgreSQL test connection as
described in [README](../README.md#verify-a-checkout), and install the pinned
Playwright browser first. The script runs the entire test solution and retains the
fresh reports, verified manifest, [qualification corpus](../qualification/README.md),
source commit, working-tree status, and `dotnet --info`. TRX does
not authenticate source contents: preexisting reports are useful for inspection,
but never substitute for this same-checkout build and test sequence. Local changes
are recorded and do not constitute evidence for an unmodified Git commit.

CI and Release share [the verification workflow](../.github/workflows/verify.yml).
Release checks out the exact commit returned by that successful workflow, then
checks that the requested release tag still resolves to it before any deployment.
Failed test or benchmark runs retain available reports and browser artifacts for
diagnosis. Their failed exit status still blocks Release; an uploaded report is not
a verified manifest. Source and runtime metadata are captured before tests start.
The Web publish includes both JSON files under `conformance/`; they are outside
`wwwroot` and have no runtime dependency on the checker.

When a contract changes, review its semantic and boundary fixtures first. Use
`dotnet run --project tools/LogicLab.Conformance -- schema` to inspect current schema
digests, update only the reviewed manifest rows, and update evidence selectors when
tests intentionally change. Do not reduce case counts to suppress a missing case.
The checker validates references and results; reviewers still own whether the
selected independent oracle proves the stated behavior.

The shared authoring fixture builds valid input data. It is not a semantic oracle.
Independent port snapshots cover minimum, ordinary, and packed-boundary shapes;
compiler tests additionally exercise exact policy admission boundaries. State tests
distinguish compatible migration from Restart using changed initial data, and check
atomic rejection for changed state width and contract/Port replacement. V1 accepts
only its fixed core library snapshot, so changed library IDs, versions, digests, and
foreign Contract Keys are rejected at the package boundary before Hot Swap.

Runtime kernels retain finite control tables, scalar/vector differential properties,
and exhaustive information-order checks. Browser references prove keyboard palette
activation, placement, inspection, history, and real-font scene projection. These
checks do not calibrate production policy values or qualify an Azure deployment;
[Delivery](../docs/delivery.md) remains the completion ledger.
