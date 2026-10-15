using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BZ.Howly.Api.DAO.Migrations
{
    /// <inheritdoc />
    public partial class StepNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The two free texts of a step become one "notes" (what the customer sends, then what the team delivers).
            migrationBuilder.Sql("""
                UPDATE ProcessTypeSteps
                SET DeliveryChecklist = LEFT(CASE
                    WHEN DeliveryChecklist IS NULL THEN CustomerChecklist
                    ELSE CustomerChecklist + CHAR(13) + CHAR(10) + DeliveryChecklist END, 2000)
                WHERE CustomerChecklist IS NOT NULL;
                """);
            migrationBuilder.Sql("""
                UPDATE ProcessSteps
                SET DeliveryChecklist = LEFT(CASE
                    WHEN DeliveryChecklist IS NULL THEN CustomerChecklist
                    ELSE CustomerChecklist + CHAR(13) + CHAR(10) + DeliveryChecklist END, 2000)
                WHERE CustomerChecklist IS NOT NULL;
                """);

            // Agencies of the types come only from the catalog: a typed name is linked to the agency of the same acronym
            // in the consultancy, or left "to be defined". Processes keep theirs (it is their history).
            migrationBuilder.Sql("""
                UPDATE s SET AgencyId = a.Id
                FROM ProcessTypeSteps s
                JOIN ProcessTypes t ON t.Id = s.ProcessTypeId
                JOIN Agencies a ON a.TenantId = t.TenantId AND a.Acronym = s.AgencyName
                WHERE s.AgencyId IS NULL AND s.AgencyName IS NOT NULL;

                UPDATE ProcessTypeSteps SET AgencyName = NULL WHERE AgencyId IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "CustomerChecklist",
                table: "ProcessTypeSteps");

            migrationBuilder.DropColumn(
                name: "CustomerChecklist",
                table: "ProcessSteps");

            migrationBuilder.RenameColumn(
                name: "DeliveryChecklist",
                table: "ProcessTypeSteps",
                newName: "Notes");

            migrationBuilder.RenameColumn(
                name: "DeliveryChecklist",
                table: "ProcessSteps",
                newName: "Notes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Notes",
                table: "ProcessTypeSteps",
                newName: "DeliveryChecklist");

            migrationBuilder.RenameColumn(
                name: "Notes",
                table: "ProcessSteps",
                newName: "DeliveryChecklist");

            migrationBuilder.AddColumn<string>(
                name: "CustomerChecklist",
                table: "ProcessTypeSteps",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerChecklist",
                table: "ProcessSteps",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }
    }
}
