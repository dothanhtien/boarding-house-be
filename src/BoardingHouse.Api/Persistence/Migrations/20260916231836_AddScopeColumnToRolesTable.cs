using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Persistence.Converters;
using BoardingHouse.Api.Persistence.Seed;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardingHouse.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScopeColumnToRolesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "scope",
                table: "roles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            foreach (var seed in RbacSeeder.RoleSeeds)
            {
                var scopeValue = RoleScopeDbValueConverter.Instance.ConvertToProvider(seed.Scope) as string;
                migrationBuilder.Sql(
                    $"UPDATE roles SET scope = '{scopeValue}' WHERE slug = '{seed.Slug}' AND scope IS NULL;");
            }

            var defaultScopeValue = RoleScopeDbValueConverter.Instance.ConvertToProvider(RoleScope.Organization) as string;
            migrationBuilder.Sql(
                $"UPDATE roles SET scope = '{defaultScopeValue}' WHERE scope IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "scope",
                table: "roles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "scope",
                table: "roles");
        }
    }
}
