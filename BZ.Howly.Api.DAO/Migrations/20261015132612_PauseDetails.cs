using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BZ.Howly.Api.DAO.Migrations
{
    /// <inheritdoc />
    public partial class PauseDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpectedReturnOn",
                table: "Processes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PauseAgencyId",
                table: "Processes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PauseWaitingOn",
                table: "Processes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PauseWaitingOnName",
                table: "Processes",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReturnReminderSentAt",
                table: "Processes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Processes_PauseAgencyId",
                table: "Processes",
                column: "PauseAgencyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Processes_Agencies_PauseAgencyId",
                table: "Processes",
                column: "PauseAgencyId",
                principalTable: "Agencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Processes_Agencies_PauseAgencyId",
                table: "Processes");

            migrationBuilder.DropIndex(
                name: "IX_Processes_PauseAgencyId",
                table: "Processes");

            migrationBuilder.DropColumn(
                name: "ExpectedReturnOn",
                table: "Processes");

            migrationBuilder.DropColumn(
                name: "PauseAgencyId",
                table: "Processes");

            migrationBuilder.DropColumn(
                name: "PauseWaitingOn",
                table: "Processes");

            migrationBuilder.DropColumn(
                name: "PauseWaitingOnName",
                table: "Processes");

            migrationBuilder.DropColumn(
                name: "ReturnReminderSentAt",
                table: "Processes");
        }
    }
}
