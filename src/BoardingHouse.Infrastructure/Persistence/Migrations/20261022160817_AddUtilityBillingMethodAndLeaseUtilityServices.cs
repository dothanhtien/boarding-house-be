using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardingHouse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUtilityBillingMethodAndLeaseUtilityServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "billing_method",
                table: "utility_services",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "lease_utility_services",
                columns: table => new
                {
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_price_override = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lease_utility_services", x => new { x.lease_id, x.service_id });
                    table.CheckConstraint("ck_lease_utility_services_unit_price_override", "unit_price_override IS NULL OR unit_price_override >= 0");
                    table.ForeignKey(
                        name: "fk_lease_utility_services_leases_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lease_utility_services_utility_services_service_id",
                        column: x => x.service_id,
                        principalTable: "utility_services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lease_utility_services_service_id",
                table: "lease_utility_services",
                column: "service_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lease_utility_services");

            migrationBuilder.DropColumn(
                name: "billing_method",
                table: "utility_services");
        }
    }
}
