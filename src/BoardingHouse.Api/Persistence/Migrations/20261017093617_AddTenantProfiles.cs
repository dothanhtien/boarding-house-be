using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardingHouse.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    identity_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    identity_issued_date = table.Column<DateOnly>(type: "date", nullable: true),
                    identity_issued_place = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    identity_expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    permanent_address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    emergency_contact_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    emergency_contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_profiles", x => x.id);
                    table.CheckConstraint("ck_tenant_profiles_identity_dates", "identity_issued_date IS NULL OR identity_expiry_date IS NULL OR identity_expiry_date > identity_issued_date");
                    table.CheckConstraint("ck_tenant_profiles_identity_details", "identity_number IS NOT NULL OR (identity_issued_date IS NULL AND identity_issued_place IS NULL AND identity_expiry_date IS NULL)");
                    table.CheckConstraint("ck_tenant_profiles_identity_pair", "(identity_type IS NULL) = (identity_number IS NULL)");
                    table.ForeignKey(
                        name: "fk_tenant_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_profiles_identity_number",
                table: "tenant_profiles",
                column: "identity_number",
                unique: true,
                filter: "identity_number IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_profiles_user_id",
                table: "tenant_profiles",
                column: "user_id",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_profiles");
        }
    }
}
