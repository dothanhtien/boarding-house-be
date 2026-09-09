using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardingHouse.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationSettingsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organization_settings",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_billing_day = table.Column<int>(type: "integer", nullable: true),
                    late_fee_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    late_fee_value = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    late_fee_grace_days = table.Column<int>(type: "integer", nullable: true),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "VND"),
                    bank_account_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    bank_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bank_account_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_settings", x => x.organization_id);
                    table.CheckConstraint("ck_organization_settings_default_billing_day", "default_billing_day IS NULL OR (default_billing_day BETWEEN 1 AND 28)");
                    table.CheckConstraint("ck_organization_settings_late_fee_pair", "(late_fee_type IS NULL) = (late_fee_value IS NULL)");
                    table.ForeignKey(
                        name: "fk_organization_settings_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organization_settings");
        }
    }
}
