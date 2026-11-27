using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DoingLife.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddStudyCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "institutions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    dli_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    province = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    institution_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    website = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    last_verified_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_institutions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "campuses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    institution_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    last_verified_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campuses", x => x.id);
                    table.ForeignKey(
                        name: "fk_campuses_institutions_institution_id",
                        column: x => x.institution_id,
                        principalTable: "institutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "departments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    institution_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    last_verified_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_departments", x => x.id);
                    table.ForeignKey(
                        name: "fk_departments_institutions_institution_id",
                        column: x => x.institution_id,
                        principalTable: "institutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "study_programs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    institution_id = table.Column<int>(type: "integer", nullable: false),
                    department_id = table.Column<int>(type: "integer", nullable: true),
                    name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    credential = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    field_of_study = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    duration_text = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    open_to_international = table.Column<bool>(type: "boolean", nullable: false),
                    english_test = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    english_minimum = table.Column<int>(type: "integer", nullable: true),
                    academic_minimum = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    last_verified_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_study_programs", x => x.id);
                    table.ForeignKey(
                        name: "fk_study_programs_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_study_programs_institutions_institution_id",
                        column: x => x.institution_id,
                        principalTable: "institutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employment_stats",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    institution_id = table.Column<int>(type: "integer", nullable: false),
                    program_id = table.Column<int>(type: "integer", nullable: true),
                    source_program_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    period = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    employed = table.Column<int>(type: "integer", nullable: false),
                    available = table.Column<int>(type: "integer", nullable: false),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    last_verified_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employment_stats", x => x.id);
                    table.CheckConstraint("ck_employment_counts", "employed >= 0 AND available >= employed");
                    table.ForeignKey(
                        name: "fk_employment_stats_institutions_institution_id",
                        column: x => x.institution_id,
                        principalTable: "institutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_employment_stats_study_programs_program_id",
                        column: x => x.program_id,
                        principalTable: "study_programs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "program_intakes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    program_id = table.Column<int>(type: "integer", nullable: false),
                    campus_id = table.Column<int>(type: "integer", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    season = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    delivery_mode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    international_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    international_application_opens = table.Column<DateOnly>(type: "date", nullable: true),
                    international_deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    domestic_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    domestic_application_opens = table.Column<DateOnly>(type: "date", nullable: true),
                    domestic_deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    last_verified_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_program_intakes", x => x.id);
                    table.ForeignKey(
                        name: "fk_program_intakes_campuses_campus_id",
                        column: x => x.campus_id,
                        principalTable: "campuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_program_intakes_study_programs_program_id",
                        column: x => x.program_id,
                        principalTable: "study_programs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tuition_terms",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    program_id = table.Column<int>(type: "integer", nullable: false),
                    student_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    academic_year = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    year_of_study = table.Column<int>(type: "integer", nullable: false),
                    term_number = table.Column<int>(type: "integer", nullable: false),
                    term_label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    tuition = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    recreation_athletics_fee = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    one_card_support_fee = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    student_technology_fee = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    student_association_fee = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    books_supplies = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    tuition_rate_text = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    term_total_site = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    last_verified_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tuition_terms", x => x.id);
                    table.ForeignKey(
                        name: "fk_tuition_terms_study_programs_program_id",
                        column: x => x.program_id,
                        principalTable: "study_programs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_campuses_institution_id_source_name",
                table: "campuses",
                columns: new[] { "institution_id", "source_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_departments_institution_id_name",
                table: "departments",
                columns: new[] { "institution_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employment_stats_institution_id_source_program_name_period",
                table: "employment_stats",
                columns: new[] { "institution_id", "source_program_name", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employment_stats_program_id",
                table: "employment_stats",
                column: "program_id");

            migrationBuilder.CreateIndex(
                name: "ix_institutions_dli_number",
                table: "institutions",
                column: "dli_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_program_intakes_campus_id",
                table: "program_intakes",
                column: "campus_id");

            migrationBuilder.CreateIndex(
                name: "ix_program_intakes_program_id_start_date_campus_id",
                table: "program_intakes",
                columns: new[] { "program_id", "start_date", "campus_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_study_programs_department_id",
                table: "study_programs",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_study_programs_institution_id_name",
                table: "study_programs",
                columns: new[] { "institution_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tuition_terms_program_id_student_type_academic_year_year_of",
                table: "tuition_terms",
                columns: new[] { "program_id", "student_type", "academic_year", "year_of_study", "term_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employment_stats");

            migrationBuilder.DropTable(
                name: "program_intakes");

            migrationBuilder.DropTable(
                name: "tuition_terms");

            migrationBuilder.DropTable(
                name: "campuses");

            migrationBuilder.DropTable(
                name: "study_programs");

            migrationBuilder.DropTable(
                name: "departments");

            migrationBuilder.DropTable(
                name: "institutions");
        }
    }
}
