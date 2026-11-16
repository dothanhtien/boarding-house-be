using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardingHouse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceGenerationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_generate_invoices",
                table: "organization_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "invoice_generation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: true),
                    billing_month = table.Column<DateOnly>(type: "date", nullable: false),
                    triggered_by = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_count = table.Column<int>(type: "integer", nullable: false),
                    skipped = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_generation_runs", x => x.id);
                    table.CheckConstraint("ck_invoice_generation_runs_billing_month", "EXTRACT(DAY FROM billing_month) = 1");
                    table.CheckConstraint("ck_invoice_generation_runs_created_count", "created_count >= 0");
                    table.CheckConstraint("ck_invoice_generation_runs_finished_at", "finished_at >= started_at");
                    table.ForeignKey(
                        name: "fk_invoice_generation_runs_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_generation_runs_properties_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_generation_runs_users_triggered_by",
                        column: x => x.triggered_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_generation_runs_organization_id_billing_month",
                table: "invoice_generation_runs",
                columns: new[] { "organization_id", "billing_month" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_generation_runs_property_id",
                table: "invoice_generation_runs",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_generation_runs_triggered_by",
                table: "invoice_generation_runs",
                column: "triggered_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_generation_runs");

            migrationBuilder.DropColumn(
                name: "auto_generate_invoices",
                table: "organization_settings");
        }
    }
}
