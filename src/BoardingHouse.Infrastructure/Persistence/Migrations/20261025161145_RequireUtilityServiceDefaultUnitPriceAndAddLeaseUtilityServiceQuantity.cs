using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardingHouse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequireUtilityServiceDefaultUnitPriceAndAddLeaseUtilityServiceQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "default_unit_price",
                table: "utility_services",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldNullable: true);

            migrationBuilder.Sql("ALTER TABLE utility_services ALTER COLUMN default_unit_price DROP DEFAULT;");

            migrationBuilder.AddColumn<int>(
                name: "quantity",
                table: "lease_utility_services",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "ck_lease_utility_services_quantity",
                table: "lease_utility_services",
                sql: "quantity > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_lease_utility_services_quantity",
                table: "lease_utility_services");

            migrationBuilder.DropColumn(
                name: "quantity",
                table: "lease_utility_services");

            migrationBuilder.AlterColumn<decimal>(
                name: "default_unit_price",
                table: "utility_services",
                type: "numeric(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)");
        }
    }
}
