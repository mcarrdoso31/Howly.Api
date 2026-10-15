using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BZ.Howly.Api.DAO.Migrations
{
    /// <inheritdoc />
    public partial class DocumentExamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExampleFileName",
                table: "ProcessDocuments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExampleId",
                table: "ProcessDocuments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentExamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileKey = table.Column<string>(type: "varchar(150)", unicode: false, maxLength: 150, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    UploadedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentExamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentExamples_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessDocuments_ExampleId",
                table: "ProcessDocuments",
                column: "ExampleId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentExamples_TenantId",
                table: "DocumentExamples",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProcessDocuments_DocumentExamples_ExampleId",
                table: "ProcessDocuments",
                column: "ExampleId",
                principalTable: "DocumentExamples",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProcessDocuments_DocumentExamples_ExampleId",
                table: "ProcessDocuments");

            migrationBuilder.DropTable(
                name: "DocumentExamples");

            migrationBuilder.DropIndex(
                name: "IX_ProcessDocuments_ExampleId",
                table: "ProcessDocuments");

            migrationBuilder.DropColumn(
                name: "ExampleFileName",
                table: "ProcessDocuments");

            migrationBuilder.DropColumn(
                name: "ExampleId",
                table: "ProcessDocuments");
        }
    }
}
