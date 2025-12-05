using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionAndReminderTrackingToTasksAndNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Tasks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "EndTime",
                table: "Tasks",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Tasks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderAcknowledgedAtUtc",
                table: "Tasks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAtUtc",
                table: "Tasks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartTime",
                table: "Tasks",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "TravelTime",
                table: "Tasks",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Tasks",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "Notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AggregateType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceToken = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DeviceName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserDevices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_OverdueReminders",
                table: "Tasks",
                columns: new[] { "UserId", "ReminderAtUtc", "ReminderAcknowledgedAtUtc", "ReminderSentAtUtc" },
                filter: "[ReminderAtUtc] IS NOT NULL AND [ReminderAcknowledgedAtUtc] IS NULL AND [ReminderSentAtUtc] IS NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_UserId_UpdatedAtUtc",
                table: "Tasks",
                columns: new[] { "UserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_UserId",
                table: "Notes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_UserId_Date",
                table: "Notes",
                columns: new[] { "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_UserId_UpdatedAtUtc",
                table: "Notes",
                columns: new[] { "UserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserDevices_DeviceToken",
                table: "UserDevices",
                column: "DeviceToken");

            migrationBuilder.CreateIndex(
                name: "IX_UserDevices_UserId_IsActive",
                table: "UserDevices",
                columns: new[] { "UserId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notes");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "UserDevices");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_OverdueReminders",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_UserId_UpdatedAtUtc",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ReminderAcknowledgedAtUtc",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ReminderSentAtUtc",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "TravelTime",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Tasks");
        }
    }
}
