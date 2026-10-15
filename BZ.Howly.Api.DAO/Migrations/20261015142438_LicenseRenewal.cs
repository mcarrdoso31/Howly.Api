using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BZ.Howly.Api.DAO.Migrations
{
    /// <inheritdoc />
    public partial class LicenseRenewal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "LicenseExpiresOn",
                table: "Processes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RenewalProcessId",
                table: "Processes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RenewalReminderSentAt",
                table: "Processes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RenewalStartsOn",
                table: "Processes",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Processes_RenewalProcessId",
                table: "Processes",
                column: "RenewalProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_Processes_TenantId_RenewalStartsOn",
                table: "Processes",
                columns: new[] { "TenantId", "RenewalStartsOn" });

            migrationBuilder.AddForeignKey(
                name: "FK_Processes_Processes_RenewalProcessId",
                table: "Processes",
                column: "RenewalProcessId",
                principalTable: "Processes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Processes_Processes_RenewalProcessId",
                table: "Processes");

            migrationBuilder.DropIndex(
                name: "IX_Processes_RenewalProcessId",
                table: "Processes");

            migrationBuilder.DropIndex(
                name: "IX_Processes_TenantId_RenewalStartsOn",
                table: "Processes");

            migrationBuilder.DropColumn(
                name: "LicenseExpiresOn",
                table: "Processes");

            migrationBuilder.DropColumn(
                name: "RenewalProcessId",
                table: "Processes");

            migrationBuilder.DropColumn(
                name: "RenewalReminderSentAt",
                table: "Processes");

            migrationBuilder.DropColumn(
                name: "RenewalStartsOn",
                table: "Processes");
        }
    }
}
