using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotesApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncChangeAndSequenceState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastAckedSyncSequence",
                table: "UserDevices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CanonicalOccurrenceDate",
                table: "Tasks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeetingLink",
                table: "Tasks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecurringSeriesId",
                table: "Tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    BlobPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attachments_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringTaskRoots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringTaskRoots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    EntityFamily = table.Column<byte>(type: "tinyint", nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operation = table.Column<byte>(type: "tinyint", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OriginDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncSequenceStates",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NextSequence = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    MinRetainedSequence = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncSequenceStates", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "RecurringTaskSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RootId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RRuleString = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    StartsOnDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsBeforeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TravelTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false, defaultValue: 2),
                    MeetingLink = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReminderOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    MaterializedUpToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringTaskSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringTaskSeries_RecurringTaskRoots_RootId",
                        column: x => x.RootId,
                        principalTable: "RecurringTaskRoots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecurringTaskSeries_TaskCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "TaskCategories",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RecurringTaskExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurrenceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsDeletion = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    OverrideTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OverrideDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OverrideDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OverrideStartTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    OverrideEndTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    OverrideLocation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OverrideTravelTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    OverrideCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OverridePriority = table.Column<int>(type: "int", nullable: true),
                    OverrideMeetingLink = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OverrideReminderAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    HasAttachmentOverride = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    MaterializedTaskItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringTaskExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringTaskExceptions_RecurringTaskSeries_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "RecurringTaskSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecurringTaskExceptions_TaskCategories_OverrideCategoryId",
                        column: x => x.OverrideCategoryId,
                        principalTable: "TaskCategories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecurringTaskExceptions_Tasks_MaterializedTaskItemId",
                        column: x => x.MaterializedTaskItemId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RecurringTaskAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExceptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    BlobPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringTaskAttachments", x => x.Id);
                    table.CheckConstraint("CK_RecurringTaskAttachments_ExactlyOneFk", "([SeriesId] IS NOT NULL AND [ExceptionId] IS NULL) OR ([SeriesId] IS NULL AND [ExceptionId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_RecurringTaskAttachments_RecurringTaskExceptions_ExceptionId",
                        column: x => x.ExceptionId,
                        principalTable: "RecurringTaskExceptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringTaskAttachments_RecurringTaskSeries_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "RecurringTaskSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringTaskSubtasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExceptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Position = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringTaskSubtasks", x => x.Id);
                    table.CheckConstraint("CK_RecurringTaskSubtasks_ExactlyOneFk", "([SeriesId] IS NOT NULL AND [ExceptionId] IS NULL) OR ([SeriesId] IS NULL AND [ExceptionId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_RecurringTaskSubtasks_RecurringTaskExceptions_ExceptionId",
                        column: x => x.ExceptionId,
                        principalTable: "RecurringTaskExceptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringTaskSubtasks_RecurringTaskSeries_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "RecurringTaskSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_RecurringSeriesId_CanonicalOccurrenceDate",
                table: "Tasks",
                columns: new[] { "RecurringSeriesId", "CanonicalOccurrenceDate" },
                filter: "[RecurringSeriesId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_BlobPath",
                table: "Attachments",
                column: "BlobPath");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_TaskId",
                table: "Attachments",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_UserId_TaskId",
                table: "Attachments",
                columns: new[] { "UserId", "TaskId" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_UserId_UpdatedAtUtc",
                table: "Attachments",
                columns: new[] { "UserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskAttachments_BlobPath",
                table: "RecurringTaskAttachments",
                column: "BlobPath");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskAttachments_ExceptionId",
                table: "RecurringTaskAttachments",
                column: "ExceptionId",
                filter: "[ExceptionId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskAttachments_SeriesId",
                table: "RecurringTaskAttachments",
                column: "SeriesId",
                filter: "[SeriesId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskAttachments_UserId_UpdatedAtUtc",
                table: "RecurringTaskAttachments",
                columns: new[] { "UserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskExceptions_MaterializedTaskItemId",
                table: "RecurringTaskExceptions",
                column: "MaterializedTaskItemId",
                filter: "[MaterializedTaskItemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskExceptions_OverrideCategoryId",
                table: "RecurringTaskExceptions",
                column: "OverrideCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskExceptions_UserId_UpdatedAtUtc",
                table: "RecurringTaskExceptions",
                columns: new[] { "UserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_RecurringTaskExceptions_Series_OccurrenceDate",
                table: "RecurringTaskExceptions",
                columns: new[] { "SeriesId", "OccurrenceDate" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskRoots_UserId",
                table: "RecurringTaskRoots",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskRoots_UserId_UpdatedAtUtc",
                table: "RecurringTaskRoots",
                columns: new[] { "UserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskSeries_CategoryId",
                table: "RecurringTaskSeries",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskSeries_MaterializedUpToDate",
                table: "RecurringTaskSeries",
                column: "MaterializedUpToDate",
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskSeries_RootId",
                table: "RecurringTaskSeries",
                column: "RootId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskSeries_UserId_RootId",
                table: "RecurringTaskSeries",
                columns: new[] { "UserId", "RootId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskSeries_UserId_UpdatedAtUtc",
                table: "RecurringTaskSeries",
                columns: new[] { "UserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskSubtasks_ExceptionId",
                table: "RecurringTaskSubtasks",
                column: "ExceptionId",
                filter: "[ExceptionId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskSubtasks_SeriesId",
                table: "RecurringTaskSubtasks",
                column: "SeriesId",
                filter: "[SeriesId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskSubtasks_UserId_UpdatedAtUtc",
                table: "RecurringTaskSubtasks",
                columns: new[] { "UserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncChanges_UserId_Sequence",
                table: "SyncChanges",
                columns: new[] { "UserId", "Sequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_RecurringTaskSeries_RecurringSeriesId",
                table: "Tasks",
                column: "RecurringSeriesId",
                principalTable: "RecurringTaskSeries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_RecurringTaskSeries_RecurringSeriesId",
                table: "Tasks");

            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "RecurringTaskAttachments");

            migrationBuilder.DropTable(
                name: "RecurringTaskSubtasks");

            migrationBuilder.DropTable(
                name: "SyncChanges");

            migrationBuilder.DropTable(
                name: "SyncSequenceStates");

            migrationBuilder.DropTable(
                name: "RecurringTaskExceptions");

            migrationBuilder.DropTable(
                name: "RecurringTaskSeries");

            migrationBuilder.DropTable(
                name: "RecurringTaskRoots");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_RecurringSeriesId_CanonicalOccurrenceDate",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "LastAckedSyncSequence",
                table: "UserDevices");

            migrationBuilder.DropColumn(
                name: "CanonicalOccurrenceDate",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "MeetingLink",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "RecurringSeriesId",
                table: "Tasks");
        }
    }
}
