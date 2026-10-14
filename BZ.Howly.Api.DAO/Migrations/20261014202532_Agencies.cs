using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BZ.Howly.Api.DAO.Migrations
{
    /// <inheritdoc />
    public partial class Agencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AgencyId",
                table: "ProcessTypeSteps",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AgencyId",
                table: "ProcessSteps",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Agencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Acronym = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Sphere = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    State = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Website = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ProtocolSystem = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Agencies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessTypeSteps_AgencyId",
                table: "ProcessTypeSteps",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessSteps_AgencyId",
                table: "ProcessSteps",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Agencies_TenantId_Acronym",
                table: "Agencies",
                columns: new[] { "TenantId", "Acronym" });

            migrationBuilder.AddForeignKey(
                name: "FK_ProcessSteps_Agencies_AgencyId",
                table: "ProcessSteps",
                column: "AgencyId",
                principalTable: "Agencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProcessTypeSteps_Agencies_AgencyId",
                table: "ProcessTypeSteps",
                column: "AgencyId",
                principalTable: "Agencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProcessSteps_Agencies_AgencyId",
                table: "ProcessSteps");

            migrationBuilder.DropForeignKey(
                name: "FK_ProcessTypeSteps_Agencies_AgencyId",
                table: "ProcessTypeSteps");

            migrationBuilder.DropTable(
                name: "Agencies");

            migrationBuilder.DropIndex(
                name: "IX_ProcessTypeSteps_AgencyId",
                table: "ProcessTypeSteps");

            migrationBuilder.DropIndex(
                name: "IX_ProcessSteps_AgencyId",
                table: "ProcessSteps");

            migrationBuilder.DropColumn(
                name: "AgencyId",
                table: "ProcessTypeSteps");

            migrationBuilder.DropColumn(
                name: "AgencyId",
                table: "ProcessSteps");
        }
    }
}
