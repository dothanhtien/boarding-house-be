using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardingHouse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUtilityReadings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "utility_readings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_on = table.Column<DateOnly>(type: "date", nullable: false),
                    reading_month = table.Column<DateOnly>(type: "date", nullable: false),
                    billed_lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    previous_value = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    current_value = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    consumption = table.Column<decimal>(type: "numeric(18,2)", nullable: true, computedColumnSql: "current_value - previous_value", stored: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_utility_readings", x => x.id);
                    table.CheckConstraint("ck_utility_readings_current_value", "current_value >= 0");
                    table.CheckConstraint("ck_utility_readings_previous_value", "previous_value IS NULL OR (previous_value >= 0 AND previous_value <= current_value)");
                    table.CheckConstraint("ck_utility_readings_read_on", "read_on >= reading_month - INTERVAL '1 month' AND read_on < reading_month + INTERVAL '1 month'");
                    table.CheckConstraint("ck_utility_readings_reading_month", "EXTRACT(DAY FROM reading_month) = 1");
                    table.ForeignKey(
                        name: "fk_utility_readings_leases_billed_lease_id",
                        column: x => x.billed_lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_utility_readings_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_utility_readings_utility_services_service_id",
                        column: x => x.service_id,
                        principalTable: "utility_services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_utility_readings_billed_lease_id",
                table: "utility_readings",
                column: "billed_lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_utility_readings_room_id_reading_month_service_id",
                table: "utility_readings",
                columns: new[] { "room_id", "reading_month", "service_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_utility_readings_room_id_service_id_read_on",
                table: "utility_readings",
                columns: new[] { "room_id", "service_id", "read_on" });

            migrationBuilder.CreateIndex(
                name: "ix_utility_readings_service_id",
                table: "utility_readings",
                column: "service_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "utility_readings");
        }
    }
}
