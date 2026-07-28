# Durable Project Catalog Contract

> Status: normative V1 Application seam contract
> Deployment: direct typed calls from Web to Application

This contract owns the authorized, bounded listing used by the Static SSR `/projects` page. It is a narrow Application use-case seam, not a generic repository: opening an item remains `EditorWorkspace.OpenAsync(OpenDurable)`, and persistence layout remains hidden in Infrastructure.

[Editor Workspace](./editor-workspace.md) owns Claim, open, save, and Durable Version behavior. [Web Host](../specs/web-host.md) owns the route and HTTP security. [Workspace and Persistence](../domain/workspace/CONTEXT.md) owns the terms Durable Project and Durable Display Name.

## 1. Interface and values

The Application exposes exactly one asynchronous catalog operation:

```text
ListAsync(DurableProjectPageRequest, CancellationToken)
  -> Task<DurableProjectPage | DurableProjectListRejected>

DurableProjectPageRequest
  pageSize: positive u32
  after: ProjectCatalogCursor | null

DurableProjectPage
  items: DurableProjectSummaryV1[]
  next: ProjectCatalogCursor | null

DurableProjectSummaryV1
  durableProjectId: DurableProjectId
  displayName: DurableDisplayName
```

The trusted Web-to-Application call context supplies the current authenticated subject and authorization service; neither is a browser field or serializable catalog value. `ListAsync` performs I/O naturally, forwards cancellation, creates no task queue, and returns owned immutable collections. It never exposes an EF entity, owner key, Project Document, Project Revision, Durable Version, authorization rule, storage key, or total project count. Opening a summary always reauthorizes and resolves the then-current revision through `OpenDurable`; a listed ID is only a locator.

`pageSize` is bounded by the captured Workspace Policy. An invalid page size returns `project_catalog_request_invalid` without issuing a repository query. A successful nonfinal page contains exactly `pageSize` items and a non-null `next`; the final page may be empty and has a null `next`. Authorization filtering and duplicate elimination happen before the page limit, so page size and totals reveal no unauthorized rows.

## 2. Order and cursor

V1 order is the invariant tuple `(Durable Display Name canonical UTF-8 bytes ascending, Durable Project ID ordinal ascending)`. Durable Display Name is immutable in V1, so the ordering key of an existing row never moves. Database collation and localized display collation are not observable ordering rules.

Paging is keyset-based; offset paging and an unbounded `ListAll` call do not exist. `ProjectCatalogCursor` is a bounded opaque protected string that binds the authenticated subject, ordering-contract version, last emitted tuple, and applicable policy revision. It contains no bearer authority, is never logged, and is revalidated on every call. Malformed, tampered, subject-mismatched, obsolete, or oversized cursors return `project_catalog_cursor_invalid`; Web recovers by requesting the first page. Normal Data Protection rotation preserves a cursor while the protecting key remains available; intentional key retirement or key-store loss invalidates it safely.

Each page is one consistent repository read. The cursor does not retain a database snapshot across HTTP requests. Concurrent Claim can therefore appear on a later page only when its key sorts after the cursor, and a newly authorized row that sorts before the cursor waits for the next first-page load. Authorization loss removes an item immediately. V1 has no rename or deletion, so an unchanged authorization view produces no duplicate existing row across pages.

## 3. Closed outcomes and V1 capability

`DurableProjectListRejected` contains one exact reason, ordered safe Diagnostics when applicable, and the `RetryDisposition` defined by the Editor Workspace contract. Its reason is exactly one of:

```text
authentication_required
forbidden
project_catalog_request_invalid
project_catalog_cursor_invalid
project_catalog_cancelled
project_catalog_infrastructure_failure
project_catalog_internal_defect
```

Authentication and authorization occur before cursor decoding or repository access. Unauthorized existence is never disclosed. Cancellation before publication returns `project_catalog_cancelled`; cancellation after the immutable page is published does not revoke it. Infrastructure and defect outcomes expose only an opaque correlation through the normal HTTP Problem Details adapter.

V1 catalog capability is intentionally closed to list and open. It has no rename, delete, archive, restore, search, filter, user-selectable sort, bulk action, folder, tag, sharing, ownership transfer, public link, or cross-user discovery operation. Claim creates the Durable Display Name; changing that capability requires a new typed Application intention and corresponding Workbench behavior rather than a generic update endpoint.

## 4. Required evidence

- authentication, per-row authorization, concealment, and reauthorization on open;
- empty, exact-size, partial-final, and multi-page listings with no unbounded materialization;
- invariant UTF-8/name/ID order independent of database collation and query enumeration;
- malformed, tampered, oversized, cross-subject, policy-revision, retained-key rotation, and retired/lost-key cursor cases;
- concurrent Claim and authorization-change paging scenarios with the consistency rules above;
- cancellation and repository failure with no partial page publication; and
- generated-query inspection proving keyset predicates, authorization-before-limit, projection-only reads, and the supporting index.
