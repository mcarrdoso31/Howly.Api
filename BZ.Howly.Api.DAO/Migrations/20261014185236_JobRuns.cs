using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BZ.Howly.Api.DAO.Migrations
{
    /// <inheritdoc />
    public partial class JobRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobRuns",
                columns: table => new
                {
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    LastRunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRuns", x => x.Name);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobRuns");
        }
    }
}
