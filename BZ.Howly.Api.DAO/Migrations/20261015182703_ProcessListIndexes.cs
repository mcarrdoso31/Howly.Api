using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BZ.Howly.Api.DAO.Migrations
{
    /// <inheritdoc />
    public partial class ProcessListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Processes_TenantId_AssigneeId",
                table: "Processes");

            migrationBuilder.CreateIndex(
                name: "IX_Processes_TenantId_AssigneeId_UpdatedAt",
                table: "Processes",
                columns: new[] { "TenantId", "AssigneeId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Processes_TenantId_Status_UpdatedAt",
                table: "Processes",
                columns: new[] { "TenantId", "Status", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Processes_TenantId_AssigneeId_UpdatedAt",
                table: "Processes");

            migrationBuilder.DropIndex(
                name: "IX_Processes_TenantId_Status_UpdatedAt",
                table: "Processes");

            migrationBuilder.CreateIndex(
                name: "IX_Processes_TenantId_AssigneeId",
                table: "Processes",
                columns: new[] { "TenantId", "AssigneeId" });
        }
    }
}
