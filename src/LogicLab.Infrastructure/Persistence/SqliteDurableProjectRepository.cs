using System.Globalization;
using System.Text;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LogicLab.Infrastructure.Persistence;

internal sealed class SqliteDurableProjectRepository : IDurableProjectRepository
{
    private const string ClaimCommand = "claim";
    private const string SaveCommand = "save";
    private const string StoredOutcome = "stored";
    private const string ConflictOutcome = "conflict";
    private readonly IDbContextFactory<LogicLabDbContext> contextFactory;
    private readonly int receiptRetentionCount;

    public SqliteDurableProjectRepository(
        IDbContextFactory<LogicLabDbContext> contextFactory,
        int receiptRetentionCount)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(receiptRetentionCount);
        this.contextFactory = contextFactory;
        this.receiptRetentionCount = receiptRetentionCount;
    }

    public async Task<DurableProjectClaimRepositoryOutcome> ClaimAsync(
        DurableProjectClaimRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await ClaimTransactionAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            var replay = await TryReadClaimReceiptAsync(
                request,
                CancellationToken.None).ConfigureAwait(false);
            if (replay is not null)
            {
                return replay;
            }

            try
            {
                var recovered = await TryRecoverExistingClaimAsync(
                    request,
                    CancellationToken.None).ConfigureAwait(false);
                if (recovered is not null)
                {
                    return recovered;
                }
            }
            catch (DbUpdateException)
            {
                replay = await TryReadClaimReceiptAsync(
                    request,
                    CancellationToken.None).ConfigureAwait(false);
                if (replay is not null)
                {
                    return replay;
                }
            }

            throw;
        }
    }

    private async Task<DurableProjectClaimRepositoryOutcome> ClaimTransactionAsync(
        DurableProjectClaimRequest request,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken).ConfigureAwait(false);
        var existing = await TryResolveExistingClaimAsync(
            context,
            request,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
            return existing;
        }

        context.DurableProjects.Add(new DurableProjectRecord
        {
            Id = request.DurableProjectId.Value,
            ClaimWorkspaceId = request.ClaimWorkspaceId.Value,
            SubjectId = request.SubjectId.Value,
            DisplayName = request.DisplayName.Value,
            DisplayNameSortKey = Encoding.UTF8.GetBytes(request.DisplayName.Value),
            CurrentProjectRevisionId = request.ProjectRevision.RevisionId.Value,
            DurableVersion = request.InitialDurableVersion.Value,
        });
        context.ProjectRevisions.Add(CreateRevisionRecord(
            request.DurableProjectId,
            request.ProjectRevision));
        context.DurableCommandReceipts.Add(CreateStoredReceipt(
            request.ReceiptKey,
            ClaimCommand,
            request.DurableProjectId,
            request.InitialDurableVersion,
            request.ProjectRevision.RevisionId));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PruneReceiptsAsync(
            context,
            request.DurableProjectId,
            cancellationToken).ConfigureAwait(false);
        await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        return new DurableProjectClaimStored(
            request.DurableProjectId,
            request.InitialDurableVersion,
            request.ProjectRevision.RevisionId,
            request.DisplayName);
    }

    private async Task<DurableProjectClaimRepositoryOutcome?>
        TryRecoverExistingClaimAsync(
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken).ConfigureAwait(false);
        var recovered = await TryResolveExistingClaimAsync(
            context,
            request,
            cancellationToken).ConfigureAwait(false);
        if (recovered is null)
        {
            return null;
        }

        await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        return recovered;
    }

    private async Task<DurableProjectClaimRepositoryOutcome?>
        TryResolveExistingClaimAsync(
            LogicLabDbContext context,
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken)
    {
        var existingReceipt = await FindReceiptAsync(
            context,
            request.ReceiptKey,
            cancellationToken).ConfigureAwait(false);
        if (existingReceipt is not null)
        {
            return await ResolveAuthorizedClaimReceiptAsync(
                context,
                existingReceipt,
                request,
                cancellationToken).ConfigureAwait(false);
        }

        var project = await context.DurableProjects.SingleOrDefaultAsync(
            candidate => candidate.ClaimWorkspaceId
                == request.ClaimWorkspaceId.Value,
            cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        if (!string.Equals(
            project.SubjectId,
            request.SubjectId.Value,
            StringComparison.Ordinal))
        {
            return new DurableProjectClaimForbidden();
        }

        var durableProjectId = new DurableProjectId(project.Id);
        var durableVersion = new DurableVersion(project.DurableVersion);
        var projectRevisionId = new ProjectRevisionId(
            project.CurrentProjectRevisionId);
        context.DurableCommandReceipts.Add(CreateStoredReceipt(
            request.ReceiptKey,
            ClaimCommand,
            durableProjectId,
            durableVersion,
            projectRevisionId));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PruneReceiptsAsync(
            context,
            durableProjectId,
            cancellationToken).ConfigureAwait(false);
        return new DurableProjectClaimStored(
            durableProjectId,
            durableVersion,
            projectRevisionId,
            new DurableDisplayName(project.DisplayName));
    }

    public async Task<DurableProjectSaveRepositoryOutcome> SaveAsync(
        DurableProjectSaveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await SaveTransactionAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await RecordConflictAfterRaceAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            var replay = await TryReadSaveReceiptAsync(
                request,
                CancellationToken.None).ConfigureAwait(false);
            if (replay is not null)
            {
                return replay;
            }

            var conflict = await TryRecordConflictAfterRaceAsync(
                request,
                cancellationToken).ConfigureAwait(false);
            if (conflict is not null)
            {
                return conflict;
            }

            throw;
        }
    }

    private async Task<DurableProjectSaveRepositoryOutcome> SaveTransactionAsync(
        DurableProjectSaveRequest request,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken).ConfigureAwait(false);
        var existingReceipt = await FindReceiptAsync(
            context,
            request.ReceiptKey,
            cancellationToken).ConfigureAwait(false);
        if (existingReceipt is not null)
        {
            return await ResolveAuthorizedSaveReceiptAsync(
                context,
                existingReceipt,
                request,
                cancellationToken).ConfigureAwait(false);
        }

        var project = await context.DurableProjects.SingleOrDefaultAsync(
            candidate => candidate.Id == request.DurableProjectId.Value,
            cancellationToken).ConfigureAwait(false);
        if (project is null
            || !string.Equals(
                project.SubjectId,
                request.SubjectId.Value,
                StringComparison.Ordinal))
        {
            return new DurableProjectSaveForbidden();
        }

        if (!string.Equals(
            project.DurableVersion,
            request.ExpectedDurableVersion.Value,
            StringComparison.Ordinal))
        {
            var conflict = CreateConflictReceipt(
                request,
                new DurableVersion(project.DurableVersion));
            context.DurableCommandReceipts.Add(conflict);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await PruneReceiptsAsync(
                context,
                request.DurableProjectId,
                cancellationToken).ConfigureAwait(false);
            await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
            return ResolveSaveReceipt(
                conflict,
                request.ReceiptKey,
                request.ProjectRevision.RevisionId);
        }

        var changesPointer = !string.Equals(
            project.CurrentProjectRevisionId,
            request.ProjectRevision.RevisionId.Value,
            StringComparison.Ordinal);
        if (changesPointer)
        {
            if (request.NextDurableVersion == request.ExpectedDurableVersion)
            {
                throw new InvalidOperationException(
                    "A changed Durable Project pointer requires a new Durable Version.");
            }

            await AddImmutableRevisionIfMissingAsync(
                context,
                request.DurableProjectId,
                request.ProjectRevision,
                cancellationToken).ConfigureAwait(false);
            project.CurrentProjectRevisionId = request.ProjectRevision.RevisionId.Value;
            project.DurableVersion = request.NextDurableVersion.Value;
        }

        var storedVersion = changesPointer
            ? request.NextDurableVersion
            : request.ExpectedDurableVersion;
        context.DurableCommandReceipts.Add(CreateStoredReceipt(
            request.ReceiptKey,
            SaveCommand,
            request.DurableProjectId,
            storedVersion,
            request.ProjectRevision.RevisionId));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PruneReceiptsAsync(
            context,
            request.DurableProjectId,
            cancellationToken).ConfigureAwait(false);
        await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        return new DurableProjectSaveStored(
            storedVersion,
            request.ProjectRevision.RevisionId);
    }

    private static async Task AddImmutableRevisionIfMissingAsync(
        LogicLabDbContext context,
        DurableProjectId durableProjectId,
        ProjectRevision projectRevision,
        CancellationToken cancellationToken)
    {
        var existing = await context.ProjectRevisions.FindAsync(
            [durableProjectId.Value, projectRevision.RevisionId.Value],
            cancellationToken).ConfigureAwait(false);
        var payload = ProjectRevisionPayloadSerializer.Serialize(projectRevision);
        if (existing is null)
        {
            context.ProjectRevisions.Add(new ProjectRevisionRecord
            {
                DurableProjectId = durableProjectId.Value,
                ProjectRevisionId = projectRevision.RevisionId.Value,
                Payload = payload,
            });
            return;
        }

        if (!existing.Payload.AsSpan().SequenceEqual(payload))
        {
            throw new InvalidOperationException(
                "A Project Revision identity cannot be associated with different payloads.");
        }
    }

    public async Task<DurableProjectClaimRepositoryOutcome?> TryReadClaimReceiptAsync(
        DurableProjectClaimRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken).ConfigureAwait(false);
        var receipt = await FindReceiptAsync(
            context,
            request.ReceiptKey,
            cancellationToken).ConfigureAwait(false);
        if (receipt is null)
        {
            return null;
        }

        return await ResolveAuthorizedClaimReceiptAsync(
            context,
            receipt,
            request,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<DurableProjectSaveRepositoryOutcome?> TryReadSaveReceiptAsync(
        DurableProjectSaveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken).ConfigureAwait(false);
        var receipt = await FindReceiptAsync(
            context,
            request.ReceiptKey,
            cancellationToken).ConfigureAwait(false);
        return receipt is null
            ? null
            : await ResolveAuthorizedSaveReceiptAsync(
                context,
                receipt,
                request,
                cancellationToken).ConfigureAwait(false);
    }

    private async Task<DurableProjectSaveRepositoryOutcome> RecordConflictAfterRaceAsync(
        DurableProjectSaveRequest request,
        CancellationToken cancellationToken)
    {
        return await TryRecordConflictAfterRaceAsync(request, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "The Durable Project disappeared during conflict recovery.");
    }

    private async Task<DurableProjectSaveRepositoryOutcome?>
        TryRecordConflictAfterRaceAsync(
            DurableProjectSaveRequest request,
            CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken).ConfigureAwait(false);
        var receipt = await FindReceiptAsync(
            context,
            request.ReceiptKey,
            cancellationToken).ConfigureAwait(false);
        if (receipt is not null)
        {
            return await ResolveAuthorizedSaveReceiptAsync(
                context,
                receipt,
                request,
                cancellationToken).ConfigureAwait(false);
        }

        var project = await context.DurableProjects.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == request.DurableProjectId.Value,
            cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        if (!string.Equals(
            project.SubjectId,
            request.SubjectId.Value,
            StringComparison.Ordinal))
        {
            return new DurableProjectSaveForbidden();
        }

        if (string.Equals(
            project.DurableVersion,
            request.ExpectedDurableVersion.Value,
            StringComparison.Ordinal))
        {
            return null;
        }

        var conflict = CreateConflictReceipt(
            request,
            new DurableVersion(project.DurableVersion));
        context.DurableCommandReceipts.Add(conflict);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PruneReceiptsAsync(
            context,
            request.DurableProjectId,
            cancellationToken).ConfigureAwait(false);
        await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        return ResolveSaveReceipt(
            conflict,
            request.ReceiptKey,
            request.ProjectRevision.RevisionId);
    }

    private static async Task<DurableCommandReceiptRecord?> FindReceiptAsync(
        LogicLabDbContext context,
        DurableCommandReceiptKey key,
        CancellationToken cancellationToken)
    {
        var attachmentGeneration = key.AttachmentGeneration.ToString(
            CultureInfo.InvariantCulture);
        return await context.DurableCommandReceipts.SingleOrDefaultAsync(
            receipt => receipt.WorkspaceId == key.WorkspaceId.Value
                && receipt.AttachmentGeneration == attachmentGeneration
                && receipt.ClientIntentId == key.ClientIntentId.Value,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task PruneReceiptsAsync(
        LogicLabDbContext context,
        DurableProjectId durableProjectId,
        CancellationToken cancellationToken)
    {
        var oldestRetainedSequence = await context.DurableCommandReceipts
            .AsNoTracking()
            .Where(receipt => receipt.DurableProjectId == durableProjectId.Value)
            .OrderByDescending(receipt => receipt.ReceiptSequence)
            .Skip(receiptRetentionCount - 1)
            .Select(receipt => (long?)receipt.ReceiptSequence)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (oldestRetainedSequence is null)
        {
            return;
        }

        await context.DurableCommandReceipts
            .Where(receipt => receipt.DurableProjectId == durableProjectId.Value
                && receipt.ReceiptSequence < oldestRetainedSequence.Value)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DurableProjectSaveRepositoryOutcome>
        ResolveAuthorizedSaveReceiptAsync(
            LogicLabDbContext context,
            DurableCommandReceiptRecord receipt,
            DurableProjectSaveRequest request,
            CancellationToken cancellationToken)
    {
        if (!string.Equals(
            receipt.DurableProjectId,
            request.DurableProjectId.Value,
            StringComparison.Ordinal))
        {
            return new DurableProjectSaveReceiptConflict();
        }

        return await IsOwnedBySubjectAsync(
                context,
                receipt.DurableProjectId,
                request.SubjectId,
                cancellationToken).ConfigureAwait(false)
            ? ResolveSaveReceipt(
                receipt,
                request.ReceiptKey,
                request.ProjectRevision.RevisionId)
            : new DurableProjectSaveForbidden();
    }

    private static async Task<DurableProjectClaimRepositoryOutcome>
        ResolveAuthorizedClaimReceiptAsync(
            LogicLabDbContext context,
            DurableCommandReceiptRecord receipt,
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken)
    {
        var project = await context.DurableProjects.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == receipt.DurableProjectId,
                cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "A Durable claim receipt must reference an existing project.");
        if (!string.Equals(
            project.SubjectId,
            request.SubjectId.Value,
            StringComparison.Ordinal))
        {
            return new DurableProjectClaimForbidden();
        }

        return ResolveClaimReceipt(
            receipt,
            request.ReceiptKey,
            project);
    }

    private static async Task<bool> IsOwnedBySubjectAsync(
        LogicLabDbContext context,
        string durableProjectId,
        AuthenticatedSubjectId subjectId,
        CancellationToken cancellationToken)
    {
        return await context.DurableProjects.AsNoTracking().AnyAsync(
            project => project.Id == durableProjectId
                && project.SubjectId == subjectId.Value,
            cancellationToken).ConfigureAwait(false);
    }

    private static DurableProjectClaimRepositoryOutcome ResolveClaimReceipt(
        DurableCommandReceiptRecord receipt,
        DurableCommandReceiptKey key,
        DurableProjectRecord project)
    {
        if (!Matches(receipt, key, ClaimCommand))
        {
            return new DurableProjectClaimReceiptConflict();
        }

        if (!string.Equals(receipt.OutcomeKind, StoredOutcome, StringComparison.Ordinal)
            || receipt.ProjectRevisionId is null
            || !string.Equals(
                receipt.DurableProjectId,
                project.Id,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Durable claim receipt is invalid.");
        }

        return new DurableProjectClaimStored(
            new DurableProjectId(receipt.DurableProjectId),
            new DurableVersion(receipt.DurableVersion),
            new ProjectRevisionId(receipt.ProjectRevisionId),
            new DurableDisplayName(project.DisplayName));
    }

    private static DurableProjectSaveRepositoryOutcome ResolveSaveReceipt(
        DurableCommandReceiptRecord receipt,
        DurableCommandReceiptKey key,
        ProjectRevisionId requestProjectRevisionId)
    {
        if (!Matches(receipt, key, SaveCommand))
        {
            return new DurableProjectSaveReceiptConflict();
        }

        return receipt.OutcomeKind switch
        {
            StoredOutcome when string.Equals(
                receipt.ProjectRevisionId,
                requestProjectRevisionId.Value,
                StringComparison.Ordinal) =>
                new DurableProjectSaveStored(
                    new DurableVersion(receipt.DurableVersion),
                    requestProjectRevisionId),
            ConflictOutcome when receipt.ExpectedDurableVersion is not null
                && receipt.ActualDurableVersion is not null =>
                new DurableProjectSaveRepositoryConflict(
                    new DurableVersion(receipt.ExpectedDurableVersion),
                    new DurableVersion(receipt.ActualDurableVersion)),
            _ => throw new InvalidOperationException(
                "The Durable save receipt is invalid."),
        };
    }

    private static bool Matches(
        DurableCommandReceiptRecord receipt,
        DurableCommandReceiptKey key,
        string commandKind)
    {
        return string.Equals(
                receipt.CommandFingerprint,
                key.CommandFingerprint.Value,
                StringComparison.Ordinal)
            && string.Equals(receipt.CommandKind, commandKind, StringComparison.Ordinal);
    }

    private static ProjectRevisionRecord CreateRevisionRecord(
        DurableProjectId durableProjectId,
        ProjectRevision projectRevision)
    {
        return new ProjectRevisionRecord
        {
            DurableProjectId = durableProjectId.Value,
            ProjectRevisionId = projectRevision.RevisionId.Value,
            Payload = ProjectRevisionPayloadSerializer.Serialize(projectRevision),
        };
    }

    private static DurableCommandReceiptRecord CreateStoredReceipt(
        DurableCommandReceiptKey key,
        string commandKind,
        DurableProjectId durableProjectId,
        DurableVersion durableVersion,
        ProjectRevisionId projectRevisionId)
    {
        return CreateReceipt(
            key,
            commandKind,
            StoredOutcome,
            durableProjectId,
            durableVersion,
            projectRevisionId.Value,
            expectedDurableVersion: null,
            actualDurableVersion: null);
    }

    private static DurableCommandReceiptRecord CreateConflictReceipt(
        DurableProjectSaveRequest request,
        DurableVersion actualDurableVersion)
    {
        return CreateReceipt(
            request.ReceiptKey,
            SaveCommand,
            ConflictOutcome,
            request.DurableProjectId,
            actualDurableVersion,
            projectRevisionId: null,
            request.ExpectedDurableVersion.Value,
            actualDurableVersion.Value);
    }

    private static DurableCommandReceiptRecord CreateReceipt(
        DurableCommandReceiptKey key,
        string commandKind,
        string outcomeKind,
        DurableProjectId durableProjectId,
        DurableVersion durableVersion,
        string? projectRevisionId,
        string? expectedDurableVersion,
        string? actualDurableVersion)
    {
        return new DurableCommandReceiptRecord
        {
            WorkspaceId = key.WorkspaceId.Value,
            AttachmentGeneration = key.AttachmentGeneration.ToString(
                CultureInfo.InvariantCulture),
            ClientIntentId = key.ClientIntentId.Value,
            CommandFingerprint = key.CommandFingerprint.Value,
            CommandKind = commandKind,
            OutcomeKind = outcomeKind,
            DurableProjectId = durableProjectId.Value,
            DurableVersion = durableVersion.Value,
            ProjectRevisionId = projectRevisionId,
            ExpectedDurableVersion = expectedDurableVersion,
            ActualDurableVersion = actualDurableVersion,
        };
    }

    private static async Task CommitAsync(
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is not (OutOfMemoryException
                or StackOverflowException
                or AccessViolationException))
        {
            throw new DurableProjectCommitUncertainException(exception);
        }
    }
}
