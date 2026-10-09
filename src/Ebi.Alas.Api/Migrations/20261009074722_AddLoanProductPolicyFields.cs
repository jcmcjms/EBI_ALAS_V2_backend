using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebi.Alas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanProductPolicyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdvanceInterestRate",
                table: "LoanProducts",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "AmortizationMode",
                table: "LoanProducts",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "DIM");

            migrationBuilder.AddColumn<decimal>(
                name: "ApplicationChargeRate",
                table: "LoanProducts",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "ChargeAdvanceInterest",
                table: "LoanProducts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "DocStampFee",
                table: "LoanProducts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InsuranceFee",
                table: "LoanProducts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncedAt",
                table: "LoanProducts",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<decimal>(
                name: "MaxAmount",
                table: "LoanProducts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinAmount",
                table: "LoanProducts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NotarialFee",
                table: "LoanProducts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdvanceInterestRate",
                table: "LoanProducts");

            migrationBuilder.DropColumn(
                name: "AmortizationMode",
                table: "LoanProducts");

            migrationBuilder.DropColumn(
                name: "ApplicationChargeRate",
                table: "LoanProducts");

            migrationBuilder.DropColumn(
                name: "ChargeAdvanceInterest",
                table: "LoanProducts");

            migrationBuilder.DropColumn(
                name: "DocStampFee",
                table: "LoanProducts");

            migrationBuilder.DropColumn(
                name: "InsuranceFee",
                table: "LoanProducts");

            migrationBuilder.DropColumn(
                name: "LastSyncedAt",
                table: "LoanProducts");

            migrationBuilder.DropColumn(
                name: "MaxAmount",
                table: "LoanProducts");

            migrationBuilder.DropColumn(
                name: "MinAmount",
                table: "LoanProducts");

            migrationBuilder.DropColumn(
                name: "NotarialFee",
                table: "LoanProducts");
        }
    }
}
