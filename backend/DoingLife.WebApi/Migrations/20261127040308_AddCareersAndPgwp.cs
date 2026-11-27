using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoingLife.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCareersAndPgwp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "pgwp_eligible",
                table: "program_intakes",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pgwp_notes",
                table: "program_intakes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pgwp_eligible",
                table: "program_intakes");

            migrationBuilder.DropColumn(
                name: "pgwp_notes",
                table: "program_intakes");
        }
    }
}
