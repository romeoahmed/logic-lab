# Observability V1

This contract owns application telemetry names and permitted payloads.
[Web Host](../specs/web-host.md#8-health-shutdown-and-observability) owns lifecycle
behavior; [Production Qualification](../deployment/qualification.md) owns collection
and operational acceptance. Core modules return evidence and never select exporters.

## Queued-work traces

The Application source is `LogicLab.Application.Work`, exposed to the host through
`WorkTelemetry.ActivitySourceName`. Production explicitly subscribes to it through
OpenTelemetry `AddSource`; constructing an `ActivitySource` alone does not enable
collection. [Azure Monitor custom telemetry](https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-add-modify#collect-custom-telemetry)

`LogicLab.Work.compilation` and `LogicLab.Work.session` are internal spans covering
execution after dequeue. They inherit the scheduling Activity's context, never the
context in which the process-wide coordinator was constructed. Without a captured
parent, the coordinator retains its opaque log correlation and creates no work span.
Unexpected failures set Error status with a stable Workspace outcome code. Spans
contain no custom tags, baggage, exception events, authored identities, or payloads.
Their duration is execution time, not queue waiting time or end-user latency.

## Structured application logs

The following Event IDs are stable. The listed fields are the complete application
payload, apart from the logging template and standard trace metadata. All are Error
events. Localization never changes field names or values.

| Event ID | Event | Fields |
| ---: | --- | --- |
| 1001 | queued work failure | `Correlation`, `Lane`, `OutcomeCode` |
| 1002 | Simulation cleanup failure | `Correlation` |
| 1003 | Compilation failure | `Correlation`, `OutcomeCode` |
| 1004 | Session advance failure | `Correlation`, `Reason` |
| 1005 | durable repository failure | `Correlation` |
| 1006 | durable open failure | `Correlation`, `Stage`, `OutcomeCode` |
| 1007 | export preparation failure | `Correlation`, `OutcomeCode` |
| 1008 | unpublished export cleanup failure | `Correlation` |
| 1009 | import failure | `Correlation`, `Stage`, `OutcomeCode` |
| 1101 | catalog failure | `Correlation`, `Stage`, `OutcomeCode` |
| 2001 | authentication revocation failure | `Correlation`, `IdentityErrorCodes`, `OutcomeCode` |
| 2101 | Workbench observation of an internal diagnostic | `DiagnosticCode`, `Correlation` |

`Correlation` is an opaque 32-hex-digit trace or generated correlation token.
`Lane` is `compilation` or `session`. Stage, outcome, reason, diagnostic, and Identity
error values come from closed application/framework vocabularies, never exception
messages or caller-supplied prose. No event carries an exception object, project or
account identifier, URL, database command, cookie, token, annotation, memory word,
Trace value, or browser message. Correlations join records and are not metric labels.

The Workbench observation event follows the deduplication and recovery behavior
defined by [Web Host](../specs/web-host.md#8-health-shutdown-and-observability).

## Collection boundary

Framework HTTP, dependency, runtime, migration, and health telemetry retains its
provider-owned schema. It is not permission to collect request bodies, query values,
credentials, or project content. Production qualification must inspect the actual
exported records, sampling, retention, dashboard dimensions, alert destinations, and
failure drills. Local listener and fake-logger tests prove the custom records and
correlation boundary; they do not prove delivery to Application Insights.

There are no application-defined Meter instruments in this version. Add one only
with a named measurement, unit, aggregation, bounded label vocabulary, consuming
qualification scenario, and local listener evidence. A provisional policy value is
never presented as a measured capacity or latency threshold.
