using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardingHouse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationBankBin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bank_bin",
                table: "organization_settings",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_organization_settings_bank_bin_account",
                table: "organization_settings",
                sql: "bank_bin IS NULL OR bank_account_number IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_organization_settings_bank_bin_account",
                table: "organization_settings");

            migrationBuilder.DropColumn(
                name: "bank_bin",
                table: "organization_settings");
        }
    }
}
