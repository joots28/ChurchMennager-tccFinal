using ChurchManager.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChurchManager.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261126002130_AddDonationPaymentMethodAndTransactionGuard")]
public partial class AddDonationPaymentMethodAndTransactionGuard : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PaymentMethod",
            table: "Donations",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "PIX");

        migrationBuilder.CreateIndex(
            name: "UX_Transactions_DonationId",
            table: "Transactions",
            column: "DonationId",
            unique: true,
            filter: "\"DonationId\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_Transactions_DonationId",
            table: "Transactions");

        migrationBuilder.DropColumn(
            name: "PaymentMethod",
            table: "Donations");
    }
}
