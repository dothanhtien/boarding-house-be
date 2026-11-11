using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardingHouse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoomAssetConditionHistoryForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_room_asset_condition_histories_room_assets_asset_id",
                table: "room_asset_condition_histories");

            migrationBuilder.DropForeignKey(
                name: "fk_room_asset_condition_history_media_room_asset_condition_his",
                table: "room_asset_condition_history_media");

            migrationBuilder.AddForeignKey(
                name: "fk_room_asset_condition_histories_room_assets_asset_id",
                table: "room_asset_condition_histories",
                column: "asset_id",
                principalTable: "room_assets",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_room_asset_condition_history_media_room_asset_condition_his",
                table: "room_asset_condition_history_media",
                column: "condition_history_id",
                principalTable: "room_asset_condition_histories",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_room_asset_condition_histories_room_assets_asset_id",
                table: "room_asset_condition_histories");

            migrationBuilder.DropForeignKey(
                name: "fk_room_asset_condition_history_media_room_asset_condition_his",
                table: "room_asset_condition_history_media");

            migrationBuilder.AddForeignKey(
                name: "fk_room_asset_condition_histories_room_assets_asset_id",
                table: "room_asset_condition_histories",
                column: "asset_id",
                principalTable: "room_assets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_room_asset_condition_history_media_room_asset_condition_his",
                table: "room_asset_condition_history_media",
                column: "condition_history_id",
                principalTable: "room_asset_condition_histories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
