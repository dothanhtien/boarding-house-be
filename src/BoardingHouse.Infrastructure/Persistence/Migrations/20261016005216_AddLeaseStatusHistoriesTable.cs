using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardingHouse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaseStatusHistoriesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lease_status_histories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    new_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lease_status_histories", x => x.id);
                    table.ForeignKey(
                        name: "fk_lease_status_histories_leases_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lease_status_histories_users_changed_by",
                        column: x => x.changed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lease_status_histories_changed_by",
                table: "lease_status_histories",
                column: "changed_by");

            migrationBuilder.CreateIndex(
                name: "ix_lease_status_histories_lease_id_changed_at",
                table: "lease_status_histories",
                columns: new[] { "lease_id", "changed_at" });

            migrationBuilder.DropIndex(
                name: "ix_lease_tenants_lease_id_user_id",
                table: "lease_tenants");

            migrationBuilder.CreateIndex(
                name: "ix_lease_tenants_lease_id_user_id",
                table: "lease_tenants",
                columns: new[] { "lease_id", "user_id" },
                unique: true,
                filter: "left_date IS NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_lease_tenants_lease_id_user_id",
                table: "lease_tenants");

            migrationBuilder.CreateIndex(
                name: "ix_lease_tenants_lease_id_user_id",
                table: "lease_tenants",
                columns: new[] { "lease_id", "user_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.DropTable(
                name: "lease_status_histories");
        }
    }
}
