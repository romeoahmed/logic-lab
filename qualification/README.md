# Qualification corpus V1

`corpus-v1.json` freezes reviewed executable selectors and exact case counts for
representative local qualification scenarios. The fixtures own circuit contents,
operation order, viewports, cultures, concurrency, and assertions; the JSON is an
index, not another implementation of those inputs. Versioned fixtures must not be
silently changed to make a regression pass. Introduce a reviewed corpus revision
when their workload or acceptance meaning changes.

The corpus covers a fixed accepted/rejected authoring sequence, every core
contract's policy and state-migration boundaries, primary Workbench and localization
workflows, independent browser Workspaces, scheduling and retention, PostgreSQL
persistence, authentication, transfers, host readiness, and security. Component
semantics and presentation additionally require the complete
[component manifest](../conformance/README.md). The stable custom telemetry vocabulary
is defined by [Observability V1](../docs/contracts/observability.md).

## Reproduce

Restore and build the Release solution, install its Playwright Chromium, and supply
the isolated PostgreSQL connection described in [README](../README.md#verify-a-checkout).
Then run from the repository root:

```sh
bash .github/scripts/verify-component-evidence.sh /tmp/logiclab-qualification-v1
```

The destination must be new. The script records source metadata and `dotnet --info`
before running tests. One full test run produces both the verified component
manifest and verified corpus; unknown selectors, changed case counts, incomplete
reports, failures, and skips reject publication. Preserve the reports, source commit,
working-tree changes, SDK/runtime, operating system, architecture, browser version,
and PostgreSQL version with any dated measurement. A dirty checkout requires its
patch as well as the commit to reproduce the source.

`WorkbenchConcurrencyTests` runs one and eight independent browser contexts against
a local Kestrel host, with separate inputs, Probe identities, Step, Restart, and
Close. It attaches per-client elapsed times as
`independent-workspaces-v1-c1.json` and `independent-workspaces-v1-c8.json` under the
browser test output's `review-artifacts` directory. These are synthetic workflow
measurements with no latency or capacity acceptance threshold. The fixture's local
admission settings are not a production recommendation.

Comparative compiler rejection and Hot Swap workloads are versioned in the
[benchmark corpus](../benchmarks/LogicLab.Benchmarks/README.md). CI smoke-executes those
cases with `Dry`; retained performance measurements require a full benchmark job
and its environment record. A failed or empty benchmark run returns nonzero.

## Interpretation

The local corpus proves its declared behavior and isolation. It does not calibrate
production Module, Scheduling, Workspace, or Browser Policy values. A successful
boundary test proves enforcement, not that the selected limit is affordable.
Browser elapsed times are not frame traces or long-task measurements, and local
Chromium evidence does not qualify every supported device/browser combination.

Production calibration must rerun representative workloads on the selected profile,
retain counters and browser traces, include storage and queue saturation, and have
an owner accept the resulting envelopes. Framework telemetry requires inspection of
actual exported records. Proxy/TLS, managed identities, key continuity, restart,
backup/restore, release rollback, and alert delivery require the selected environment.
[Production Qualification](../docs/deployment/qualification.md) owns those decisions
and drill records; [Delivery](../docs/delivery.md) owns completion status.
