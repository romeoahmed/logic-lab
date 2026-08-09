using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogicLab.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialDurableProjects : Migration
{
    private static readonly string[] DisplayNameSortColumns =
    [
        "display_name_sort_key",
        "durable_project_id",
    ];

    private static readonly string[] ReceiptIdentityColumns =
    [
        "workspace_id",
        "attachment_generation",
        "client_intent_id",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "durable_projects",
            columns: table => new
            {
                durable_project_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                claim_workspace_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                subject_id = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                display_name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                display_name_sort_key = table.Column<byte[]>(type: "BLOB", nullable: false),
                current_project_revision_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                durable_version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_durable_projects", x => x.durable_project_id);
            });

        migrationBuilder.CreateTable(
            name: "durable_command_receipts",
            columns: table => new
            {
                receipt_sequence = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                workspace_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                attachment_generation = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                client_intent_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                command_fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                command_kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                outcome_kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                durable_project_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                durable_version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                project_revision_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                expected_durable_version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                actual_durable_version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_durable_command_receipts", x => x.receipt_sequence);
                table.ForeignKey(
                    name: "FK_durable_command_receipts_durable_projects_durable_project_id",
                    column: x => x.durable_project_id,
                    principalTable: "durable_projects",
                    principalColumn: "durable_project_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "project_revisions",
            columns: table => new
            {
                durable_project_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                project_revision_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                payload = table.Column<byte[]>(type: "BLOB", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_project_revisions", x => new { x.durable_project_id, x.project_revision_id });
                table.ForeignKey(
                    name: "FK_project_revisions_durable_projects_durable_project_id",
                    column: x => x.durable_project_id,
                    principalTable: "durable_projects",
                    principalColumn: "durable_project_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_durable_command_receipts_durable_project_id",
            table: "durable_command_receipts",
            column: "durable_project_id");

        migrationBuilder.CreateIndex(
            name: "ux_durable_command_receipts_workspace_generation_intent",
            table: "durable_command_receipts",
            columns: ReceiptIdentityColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_durable_projects_display_name_sort_key_id",
            table: "durable_projects",
            columns: DisplayNameSortColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_durable_projects_claim_workspace_id",
            table: "durable_projects",
            column: "claim_workspace_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "durable_command_receipts");

        migrationBuilder.DropTable(
            name: "project_revisions");

        migrationBuilder.DropTable(
            name: "durable_projects");
    }
}
