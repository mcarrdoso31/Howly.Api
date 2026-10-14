using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BZ.Howly.Api.DAO.Migrations
{
    /// <inheritdoc />
    public partial class TenantProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogoKey",
                table: "Tenants",
                type: "varchar(150)",
                unicode: false,
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LogoUpdatedAt",
                table: "Tenants",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_City",
                table: "Tenants",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_Cnpj",
                table: "Tenants",
                type: "char(14)",
                unicode: false,
                fixedLength: true,
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_Email",
                table: "Tenants",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_LegalName",
                table: "Tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_Phone",
                table: "Tenants",
                type: "varchar(11)",
                unicode: false,
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_PostalCode",
                table: "Tenants",
                type: "char(8)",
                unicode: false,
                fixedLength: true,
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_State",
                table: "Tenants",
                type: "char(2)",
                unicode: false,
                fixedLength: true,
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_Street",
                table: "Tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_TimeZone",
                table: "Tenants",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: false,
                // Rows that already exist count their deadlines in Brazil's main time zone.
                defaultValue: "America/Sao_Paulo");

            migrationBuilder.UpdateData(
                table: "Tenants",
                keyColumn: "Id",
                keyValue: new Guid("0e0a7c3e-5f2b-4c1d-9a00-000000000001"),
                columns: new[] { "LogoKey", "LogoUpdatedAt", "Profile_City", "Profile_Cnpj", "Profile_Email", "Profile_LegalName", "Profile_Phone", "Profile_PostalCode", "Profile_State", "Profile_Street", "Profile_TimeZone" },
                values: new object[] { null, null, null, null, null, null, null, null, null, null, "America/Sao_Paulo" });

            migrationBuilder.UpdateData(
                table: "Tenants",
                keyColumn: "Id",
                keyValue: new Guid("0e0a7c3e-5f2b-4c1d-9a00-0000000000b1"),
                columns: new[] { "LogoKey", "LogoUpdatedAt", "Profile_City", "Profile_Cnpj", "Profile_Email", "Profile_LegalName", "Profile_Phone", "Profile_PostalCode", "Profile_State", "Profile_Street", "Profile_TimeZone" },
                values: new object[] { null, null, null, null, null, null, null, null, null, null, "America/Sao_Paulo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogoKey",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LogoUpdatedAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Profile_City",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Profile_Cnpj",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Profile_Email",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Profile_LegalName",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Profile_Phone",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Profile_PostalCode",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Profile_State",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Profile_Street",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Profile_TimeZone",
                table: "Tenants");
        }
    }
}
