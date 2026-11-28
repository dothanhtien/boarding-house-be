using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardingHouse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentGatewayCredentialHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "credentials_version",
                table: "payment_transactions",
                type: "integer",
                nullable: true);

            // Existing accounts start at OrganizationPaymentGateway.FirstCredentialsVersion (the model has no default)
            migrationBuilder.AddColumn<int>(
                name: "credentials_version",
                table: "organization_payment_gateways",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "organization_payment_gateway_credential_histories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_payment_gateway_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    merchant_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    secret_encrypted = table.Column<string>(type: "text", nullable: false),
                    replaced_by = table.Column<Guid>(type: "uuid", nullable: false),
                    replaced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_payment_gateway_credential_histories", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_payment_gateway_credential_histories_organizat",
                        column: x => x.organization_payment_gateway_id,
                        principalTable: "organization_payment_gateways",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_organization_payment_gateway_credential_histories_users_rep",
                        column: x => x.replaced_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_org_payment_gateway_credential_histories_gateway_id_version",
                table: "organization_payment_gateway_credential_histories",
                columns: new[] { "organization_payment_gateway_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_payment_gateway_credential_histories_replaced_",
                table: "organization_payment_gateway_credential_histories",
                column: "replaced_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organization_payment_gateway_credential_histories");

            migrationBuilder.DropColumn(
                name: "credentials_version",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "credentials_version",
                table: "organization_payment_gateways");
        }
    }
}
