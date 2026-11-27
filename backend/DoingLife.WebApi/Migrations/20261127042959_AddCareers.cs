using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DoingLife.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCareers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "careers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    noc_code = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    noc_version = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    teer_category = table.Column<int>(type: "integer", nullable: false),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    last_verified_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_careers", x => x.id);
                    table.CheckConstraint("ck_careers_noc_code", "noc_code ~ '^[0-9]{5}$'");
                    table.CheckConstraint("ck_careers_teer", "teer_category BETWEEN 0 AND 5");
                });

            migrationBuilder.CreateTable(
                name: "program_careers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    program_id = table.Column<int>(type: "integer", nullable: false),
                    career_id = table.Column<int>(type: "integer", nullable: true),
                    example_title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    basis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    last_verified_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_program_careers", x => x.id);
                    table.ForeignKey(
                        name: "fk_program_careers_careers_career_id",
                        column: x => x.career_id,
                        principalTable: "careers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_program_careers_study_programs_program_id",
                        column: x => x.program_id,
                        principalTable: "study_programs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_careers_noc_code_noc_version",
                table: "careers",
                columns: new[] { "noc_code", "noc_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_program_careers_career_id",
                table: "program_careers",
                column: "career_id");

            migrationBuilder.CreateIndex(
                name: "ix_program_careers_program_id_example_title",
                table: "program_careers",
                columns: new[] { "program_id", "example_title" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "program_careers");

            migrationBuilder.DropTable(
                name: "careers");
        }
    }
}
