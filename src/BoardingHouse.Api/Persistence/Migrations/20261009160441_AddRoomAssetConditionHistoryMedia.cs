using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardingHouse.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomAssetConditionHistoryMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_room_asset_condition_histories_asset_id",
                table: "room_asset_condition_histories");

            migrationBuilder.CreateIndex(
                name: "ix_room_asset_condition_histories_asset_id_changed_at_id",
                table: "room_asset_condition_histories",
                columns: new[] { "asset_id", "changed_at", "id" });

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "room_asset_condition_histories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by",
                table: "room_asset_condition_histories",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "room_asset_condition_history_media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    condition_history_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_asset_condition_history_media", x => x.id);
                    table.ForeignKey(
                        name: "fk_room_asset_condition_history_media_media_assets_media_asset",
                        column: x => x.media_asset_id,
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_room_asset_condition_history_media_room_asset_condition_his",
                        column: x => x.condition_history_id,
                        principalTable: "room_asset_condition_histories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_room_asset_condition_history_media_condition_history_id",
                table: "room_asset_condition_history_media",
                column: "condition_history_id");

            migrationBuilder.CreateIndex(
                name: "ix_room_asset_condition_history_media_media_asset_id",
                table: "room_asset_condition_history_media",
                column: "media_asset_id",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "room_asset_condition_history_media");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "room_asset_condition_histories");

            migrationBuilder.DropColumn(
                name: "updated_by",
                table: "room_asset_condition_histories");

            migrationBuilder.DropIndex(
                name: "ix_room_asset_condition_histories_asset_id_changed_at_id",
                table: "room_asset_condition_histories");

            migrationBuilder.CreateIndex(
                name: "ix_room_asset_condition_histories_asset_id",
                table: "room_asset_condition_histories",
                column: "asset_id");
        }
    }
}
