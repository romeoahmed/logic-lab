using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogicLab.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class DurableProjectCatalogIndex : Migration
{
    private static readonly string[] AuthorizedCatalogKeyColumns =
    [
        "subject_id",
        "display_name_sort_key",
        "durable_project_id",
    ];

    private static readonly string[] DisplayNameSortColumns =
    [
        "display_name_sort_key",
        "durable_project_id",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_durable_projects_display_name_sort_key_id",
            table: "durable_projects");

        migrationBuilder.CreateIndex(
            name: "ix_durable_projects_subject_sort_key_id",
            table: "durable_projects",
            columns: AuthorizedCatalogKeyColumns,
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_durable_projects_subject_sort_key_id",
            table: "durable_projects");

        migrationBuilder.CreateIndex(
            name: "ix_durable_projects_display_name_sort_key_id",
            table: "durable_projects",
            columns: DisplayNameSortColumns,
            unique: true);
    }
}
