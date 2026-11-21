using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoingLife.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddMemoryChoiceMadeAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "memory_choice_made_at",
                table: "user_preference",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "memory_choice_made_at",
                table: "user_preference");
        }
    }
}
