using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardingHouse.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomAssetsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "room_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    purchase_date = table.Column<DateOnly>(type: "date", nullable: true),
                    purchase_unit_price = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "GOOD"),
                    note = table.Column<string>(type: "text", nullable: true),
                    split_from_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_room_assets_room_assets_split_from_asset_id",
                        column: x => x.split_from_asset_id,
                        principalTable: "room_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_room_assets_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "room_asset_condition_histories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    new_condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_asset_condition_histories", x => x.id);
                    table.ForeignKey(
                        name: "fk_room_asset_condition_histories_room_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "room_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_room_asset_condition_histories_asset_id",
                table: "room_asset_condition_histories",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_room_assets_room_id",
                table: "room_assets",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "ix_room_assets_split_from_asset_id",
                table: "room_assets",
                column: "split_from_asset_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "room_asset_condition_histories");

            migrationBuilder.DropTable(
                name: "room_assets");
        }
    }
}
