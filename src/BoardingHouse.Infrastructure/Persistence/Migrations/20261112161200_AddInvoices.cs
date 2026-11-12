using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardingHouse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invoice_number_sequences",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    billing_month = table.Column<DateOnly>(type: "date", nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_number_sequences", x => new { x.organization_id, x.billing_month });
                    table.CheckConstraint("ck_invoice_number_sequences_billing_month", "EXTRACT(DAY FROM billing_month) = 1");
                    table.CheckConstraint("ck_invoice_number_sequences_last_number", "last_number > 0");
                    table.ForeignKey(
                        name: "fk_invoice_number_sequences_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    billing_month = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    late_fee_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    discount_reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "PENDING"),
                    notes = table.Column<string>(type: "text", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.CheckConstraint("ck_invoices_amounts", "vat_amount >= 0 AND late_fee_amount >= 0 AND paid_amount >= 0 AND total_amount >= 0");
                    table.CheckConstraint("ck_invoices_billing_month", "EXTRACT(DAY FROM billing_month) = 1");
                    table.CheckConstraint("ck_invoices_discount", "discount_amount >= 0 AND (discount_amount = 0 OR discount_reason IS NOT NULL)");
                    table.CheckConstraint("ck_invoices_total_amount", "total_amount = subtotal + vat_amount + late_fee_amount - discount_amount");
                    table.CheckConstraint("ck_invoices_vat_rate", "vat_rate >= 0 AND vat_rate <= 100");
                    table.ForeignKey(
                        name: "fk_invoices_leases_lease_id",
                        column: x => x.lease_id,
                        principalTable: "leases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    source_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_items", x => x.id);
                    table.CheckConstraint("ck_invoice_items_quantity", "quantity >= 0");
                    table.CheckConstraint("ck_invoice_items_source", "(source_type = 'CUSTOM') = (source_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_invoice_items_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoice_status_histories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    new_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_status_histories", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoice_status_histories_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_status_histories_users_changed_by",
                        column: x => x.changed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_invoice_id",
                table: "invoice_items",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_source_type_source_id",
                table: "invoice_items",
                columns: new[] { "source_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_status_histories_changed_by",
                table: "invoice_status_histories",
                column: "changed_by");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_status_histories_invoice_id_changed_at",
                table: "invoice_status_histories",
                columns: new[] { "invoice_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_lease_id_billing_month",
                table: "invoices",
                columns: new[] { "lease_id", "billing_month" },
                unique: true,
                filter: "deleted_at IS NULL AND status <> 'CANCELLED'");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_organization_id_billing_month",
                table: "invoices",
                columns: new[] { "organization_id", "billing_month" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_organization_id_invoice_number",
                table: "invoices",
                columns: new[] { "organization_id", "invoice_number" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_status",
                table: "invoices",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_items");

            migrationBuilder.DropTable(
                name: "invoice_number_sequences");

            migrationBuilder.DropTable(
                name: "invoice_status_histories");

            migrationBuilder.DropTable(
                name: "invoices");
        }
    }
}
