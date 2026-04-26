using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManagement.API.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetsAndClassifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Classification",
                table: "Transactions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Classification",
                table: "Categories",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    ExpectedIncome = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    NeedsPct = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    WantsPct = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    SavingsPct = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CategoryBudgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: false),
                    Classification = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    MonthlyCap = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryBudgets", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 1,
                column: "Classification",
                value: null);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 2,
                column: "Classification",
                value: null);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 3,
                column: "Classification",
                value: null);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 4,
                column: "Classification",
                value: "Need");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 5,
                column: "Classification",
                value: "Need");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 6,
                column: "Classification",
                value: "Need");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 7,
                column: "Classification",
                value: "Need");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 8,
                column: "Classification",
                value: "Want");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 9,
                column: "Classification",
                value: "Need");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 10,
                column: "Classification",
                value: "Want");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 11,
                column: "Classification",
                value: "Savings");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_UserId_Year_Month",
                table: "Budgets",
                columns: new[] { "UserId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategoryBudgets_UserId_CategoryId",
                table: "CategoryBudgets",
                columns: new[] { "UserId", "CategoryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Budgets");

            migrationBuilder.DropTable(
                name: "CategoryBudgets");

            migrationBuilder.DropColumn(
                name: "Classification",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Classification",
                table: "Categories");
        }
    }
}
