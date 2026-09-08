using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogicLab.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class UseInvariantCatalogOrdering : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "durable_project_id",
            table: "durable_projects",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            collation: "C",
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "durable_project_id",
            table: "durable_projects",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64,
            oldCollation: "C");
    }
}
