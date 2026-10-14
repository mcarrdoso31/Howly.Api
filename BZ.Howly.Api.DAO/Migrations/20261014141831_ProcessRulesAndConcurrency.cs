using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BZ.Howly.Api.DAO.Migrations
{
    /// <inheritdoc />
    public partial class ProcessRulesAndConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessEvents_ProcessId",
                table: "ProcessEvents");

            migrationBuilder.DropIndex(
                name: "IX_DocumentVersions_DocumentId",
                table: "DocumentVersions");

            migrationBuilder.AddColumn<long>(
                name: "PausedFor",
                table: "ProcessSteps",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PausedAt",
                table: "Processes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Processes",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "ProcessDocuments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_ProcessEvents_ProcessId_Sequence",
                table: "ProcessEvents",
                columns: new[] { "ProcessId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_DocumentId_Number",
                table: "DocumentVersions",
                columns: new[] { "DocumentId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessEvents_ProcessId_Sequence",
                table: "ProcessEvents");

            migrationBuilder.DropIndex(
                name: "IX_DocumentVersions_DocumentId_Number",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "PausedFor",
                table: "ProcessSteps");

            migrationBuilder.DropColumn(
                name: "PausedAt",
                table: "Processes");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Processes");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "ProcessDocuments");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessEvents_ProcessId",
                table: "ProcessEvents",
                column: "ProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_DocumentId",
                table: "DocumentVersions",
                column: "DocumentId");
        }
    }
}
