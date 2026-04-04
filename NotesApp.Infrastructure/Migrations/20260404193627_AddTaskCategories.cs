using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaskCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CategoryId",
                table: "Tasks",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_UserCategory",
                table: "Tasks",
                columns: new[] { "UserId", "CategoryId" },
                filter: "[CategoryId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TaskCategories_UserId",
                table: "TaskCategories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskCategories_UserId_UpdatedAtUtc",
                table: "TaskCategories",
                columns: new[] { "UserId", "UpdatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_TaskCategories_CategoryId",
                table: "Tasks",
                column: "CategoryId",
                principalTable: "TaskCategories",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_TaskCategories_CategoryId",
                table: "Tasks");

            migrationBuilder.DropTable(
                name: "TaskCategories");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_CategoryId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_UserCategory",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Tasks");
        }
    }
}
