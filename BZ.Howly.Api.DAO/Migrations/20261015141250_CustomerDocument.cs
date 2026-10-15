using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BZ.Howly.Api.DAO.Migrations
{
    /// <inheritdoc />
    public partial class CustomerDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Renamed, not recreated: the CNPJs already saved are kept. Not fixed-length anymore (a CPF has 11 digits).
            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId_Cnpj",
                table: "Customers");

            migrationBuilder.RenameColumn(
                name: "Cnpj",
                table: "Customers",
                newName: "Document");

            migrationBuilder.AlterColumn<string>(
                name: "Document",
                table: "Customers",
                type: "varchar(14)",
                unicode: false,
                maxLength: 14,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "char(14)",
                oldUnicode: false,
                oldFixedLength: true,
                oldMaxLength: 14);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_Document",
                table: "Customers",
                columns: new[] { "TenantId", "Document" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId_Document",
                table: "Customers");

            migrationBuilder.AlterColumn<string>(
                name: "Document",
                table: "Customers",
                type: "char(14)",
                unicode: false,
                fixedLength: true,
                maxLength: 14,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(14)",
                oldUnicode: false,
                oldMaxLength: 14);

            migrationBuilder.RenameColumn(
                name: "Document",
                table: "Customers",
                newName: "Cnpj");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_Cnpj",
                table: "Customers",
                columns: new[] { "TenantId", "Cnpj" },
                unique: true);
        }
    }
}
