using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BZ.Howly.Api.DAO.Migrations
{
    /// <inheritdoc />
    public partial class CustomerContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerContacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Phone = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                    ReceivesNotices = table.Column<bool>(type: "bit", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerContacts_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerContacts_CustomerId",
                table: "CustomerContacts",
                column: "CustomerId");

            // Each existing customer keeps its single contact, now as the first item of the list. The phone did not
            // exist before, so it starts empty and the customer is asked to review the data on the next sign in.
            migrationBuilder.Sql("""
                INSERT INTO CustomerContacts (Id, CustomerId, Name, Email, Phone, ReceivesNotices, Position)
                SELECT NEWID(), Id, ContactName, ContactEmail, '', 1, 0 FROM Customers
                """);
            migrationBuilder.Sql("UPDATE AspNetUsers SET MustConfirmProfile = 1 WHERE Id IN (SELECT UserId FROM Customers)");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "ContactName",
                table: "Customers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "Customers",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContactName",
                table: "Customers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // Back to a single contact: the first one of the list.
            migrationBuilder.Sql("""
                UPDATE c SET ContactName = cc.Name, ContactEmail = cc.Email
                FROM Customers c JOIN CustomerContacts cc ON cc.CustomerId = c.Id AND cc.Position = 0
                """);

            migrationBuilder.DropTable(
                name: "CustomerContacts");
        }
    }
}
