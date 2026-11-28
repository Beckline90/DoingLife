using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DoingLife.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_drafts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    dli_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    school_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    closed_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    publish_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_drafts", x => x.id);
                    table.CheckConstraint("ck_catalog_drafts_payload", "jsonb_typeof(payload) = 'object'");
                    table.CheckConstraint("ck_catalog_drafts_status", "status IN ('Open', 'ReadyForReview', 'Published', 'Discarded')");
                    table.ForeignKey(
                        name: "fk_catalog_drafts_users_closed_by_user_id",
                        column: x => x.closed_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_catalog_drafts_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_catalog_drafts_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_drafts_closed_by_user_id",
                table: "catalog_drafts",
                column: "closed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_drafts_created_by_user_id",
                table: "catalog_drafts",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_drafts_dli_number",
                table: "catalog_drafts",
                column: "dli_number",
                unique: true,
                filter: "status IN ('Open', 'ReadyForReview')");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_drafts_status",
                table: "catalog_drafts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_drafts_updated_by_user_id",
                table: "catalog_drafts",
                column: "updated_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_drafts");
        }
    }
}
