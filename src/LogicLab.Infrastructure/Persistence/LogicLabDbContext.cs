using Microsoft.EntityFrameworkCore;

namespace LogicLab.Infrastructure.Persistence;

#pragma warning disable CA1812 // EF Core's registered factory constructs this type by reflection.
internal sealed class LogicLabDbContext : DbContext
{
    public LogicLabDbContext(DbContextOptions<LogicLabDbContext> options)
        : base(options)
    {
    }

    internal DbSet<DurableProjectRecord> DurableProjects => Set<DurableProjectRecord>();

    internal DbSet<ProjectRevisionRecord> ProjectRevisions => Set<ProjectRevisionRecord>();

    internal DbSet<DurableCommandReceiptRecord> DurableCommandReceipts =>
        Set<DurableCommandReceiptRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var projects = modelBuilder.Entity<DurableProjectRecord>();
        projects.ToTable("durable_projects");
        projects.HasKey(project => project.Id);
        projects.Property(project => project.Id)
            .HasColumnName("durable_project_id")
            .HasMaxLength(64);
        projects.Property(project => project.ClaimWorkspaceId)
            .HasColumnName("claim_workspace_id")
            .HasMaxLength(64);
        projects.Property(project => project.InitialProjectRevisionId)
            .HasColumnName("initial_project_revision_id")
            .HasMaxLength(64);
        projects.Property(project => project.InitialDurableVersion)
            .HasColumnName("initial_durable_version")
            .HasMaxLength(64);
        projects.Property(project => project.SubjectId)
            .HasColumnName("subject_id")
            .HasMaxLength(512);
        projects.Property(project => project.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(256);
        projects.Property(project => project.DisplayNameSortKey)
            .HasColumnName("display_name_sort_key");
        projects.Property(project => project.CurrentProjectRevisionId)
            .HasColumnName("current_project_revision_id")
            .HasMaxLength(64);
        projects.Property(project => project.DurableVersion)
            .HasColumnName("durable_version")
            .HasMaxLength(64)
            .ValueGeneratedNever()
            .IsConcurrencyToken();
        projects.HasIndex(
                project => new
                {
                    project.SubjectId,
                    project.DisplayNameSortKey,
                    project.Id,
                },
                "ix_durable_projects_subject_sort_key_id")
            .IsUnique();
        projects.HasIndex(
                project => project.ClaimWorkspaceId,
                "ux_durable_projects_claim_workspace_id")
            .IsUnique();

        var revisions = modelBuilder.Entity<ProjectRevisionRecord>();
        revisions.ToTable("project_revisions");
        revisions.HasKey(revision => new
        {
            revision.DurableProjectId,
            revision.ProjectRevisionId,
        });
        revisions.Property(revision => revision.DurableProjectId)
            .HasColumnName("durable_project_id")
            .HasMaxLength(64);
        revisions.Property(revision => revision.ProjectRevisionId)
            .HasColumnName("project_revision_id")
            .HasMaxLength(64);
        revisions.Property(revision => revision.Payload)
            .HasColumnName("payload");
        revisions.HasOne<DurableProjectRecord>()
            .WithMany()
            .HasForeignKey(revision => revision.DurableProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        var receipts = modelBuilder.Entity<DurableCommandReceiptRecord>();
        receipts.ToTable("durable_command_receipts");
        receipts.HasKey(receipt => receipt.ReceiptSequence);
        receipts.Property(receipt => receipt.ReceiptSequence)
            .HasColumnName("receipt_sequence")
            .ValueGeneratedOnAdd();
        receipts.Property(receipt => receipt.WorkspaceId)
            .HasColumnName("workspace_id")
            .HasMaxLength(64);
        receipts.Property(receipt => receipt.AttachmentGeneration)
            .HasColumnName("attachment_generation")
            .HasMaxLength(20);
        receipts.Property(receipt => receipt.ClientIntentId)
            .HasColumnName("client_intent_id")
            .HasMaxLength(128);
        receipts.Property(receipt => receipt.CommandFingerprint)
            .HasColumnName("command_fingerprint")
            .HasMaxLength(64);
        receipts.Property(receipt => receipt.CommandKind)
            .HasColumnName("command_kind")
            .HasMaxLength(16);
        receipts.Property(receipt => receipt.OutcomeKind)
            .HasColumnName("outcome_kind")
            .HasMaxLength(16);
        receipts.Property(receipt => receipt.DurableProjectId)
            .HasColumnName("durable_project_id")
            .HasMaxLength(64);
        receipts.Property(receipt => receipt.DurableVersion)
            .HasColumnName("durable_version")
            .HasMaxLength(64);
        receipts.Property(receipt => receipt.ProjectRevisionId)
            .HasColumnName("project_revision_id")
            .HasMaxLength(64);
        receipts.Property(receipt => receipt.ExpectedDurableVersion)
            .HasColumnName("expected_durable_version")
            .HasMaxLength(64);
        receipts.Property(receipt => receipt.ActualDurableVersion)
            .HasColumnName("actual_durable_version")
            .HasMaxLength(64);
        receipts.HasIndex(
                receipt => new
                {
                    receipt.WorkspaceId,
                    receipt.AttachmentGeneration,
                    receipt.ClientIntentId,
                },
                "ux_durable_command_receipts_workspace_generation_intent")
            .IsUnique();
        receipts.HasOne<DurableProjectRecord>()
            .WithMany()
            .HasForeignKey(receipt => receipt.DurableProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
#pragma warning restore CA1812
