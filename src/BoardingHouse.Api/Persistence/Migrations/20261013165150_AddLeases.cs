using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardingHouse.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    billing_day = table.Column<int>(type: "integer", nullable: true),
                    monthly_rent = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    deposit_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    terminated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    termination_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leases", x => x.id);
                    table.CheckConstraint("ck_leases_billing_day", "billing_day IS NULL OR (billing_day BETWEEN 1 AND 28)");
                    table.CheckConstraint("ck_leases_deposit_amount", "deposit_amount >= 0");
                    table.CheckConstraint("ck_leases_end_date", "end_date IS NULL OR end_date > start_date");
                    table.CheckConstraint("ck_leases_monthly_rent", "monthly_rent > 0");
                    table.CheckConstraint("ck_leases_previous_lease", "previous_lease_id IS NULL OR previous_lease_id <> id");
                    table.ForeignKey(
                        name: "fk_leases_leases_previous_lease_id",
                        column: x => x.previous_lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leases_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leases_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lease_tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_representative = table.Column<bool>(type: "boolean", nullable: false),
                    joined_date = table.Column<DateOnly>(type: "date", nullable: true),
                    left_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lease_tenants", x => x.id);
                    table.CheckConstraint("ck_lease_tenants_left_date", "left_date IS NULL OR joined_date IS NULL OR left_date >= joined_date");
                    table.ForeignKey(
                        name: "fk_lease_tenants_leases_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lease_tenants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lease_tenants_lease_id_representative",
                table: "lease_tenants",
                column: "lease_id",
                unique: true,
                filter: "is_representative AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_lease_tenants_lease_id_user_id",
                table: "lease_tenants",
                columns: new[] { "lease_id", "user_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_lease_tenants_user_id",
                table: "lease_tenants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_leases_organization_id_lease_code",
                table: "leases",
                columns: new[] { "organization_id", "lease_code" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_leases_previous_lease_id",
                table: "leases",
                column: "previous_lease_id",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_leases_room_id",
                table: "leases",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "ix_leases_room_id_active",
                table: "leases",
                column: "room_id",
                unique: true,
                filter: "status = 'ACTIVE' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_leases_status",
                table: "leases",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lease_tenants");

            migrationBuilder.DropTable(
                name: "leases");
        }
    }
}
