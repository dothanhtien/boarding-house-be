using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BoardingHouse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlinePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "organization_payment_gateways",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gateway = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    merchant_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    secret_encrypted = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_payment_gateways", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_payment_gateways_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gateway = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    gateway_transaction_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    payment_url = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    request_payload = table.Column<string>(type: "jsonb", nullable: true),
                    response_payload = table.Column<string>(type: "jsonb", nullable: true),
                    webhook_payload = table.Column<string>(type: "jsonb", nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    needs_review = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_transactions", x => x.id);
                    table.CheckConstraint("ck_payment_transactions_checkout_payment", "type <> 'CHECKOUT' OR payment_id IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_payment_transactions_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_organization_payment_gateways_organization_id_gateway",
                table: "organization_payment_gateways",
                columns: new[] { "organization_id", "gateway" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_created_at",
                table: "payment_transactions",
                column: "created_at",
                filter: "needs_review");

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_gateway_gateway_transaction_id",
                table: "payment_transactions",
                columns: new[] { "gateway", "gateway_transaction_id" },
                unique: true,
                filter: "gateway_transaction_id IS NOT NULL AND status = 'SUCCESS'");

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_payment_id_created_at",
                table: "payment_transactions",
                columns: new[] { "payment_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "organization_payment_gateways");

            migrationBuilder.DropTable(
                name: "payment_transactions");
        }
    }
}
