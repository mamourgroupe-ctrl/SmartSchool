using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSchoolAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddQuranRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuranRecords",
                columns: table => new
                {
                    QuranRecordId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StudentId = table.Column<int>(type: "INTEGER", nullable: false),
                    SurahName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    FromAyah = table.Column<int>(type: "INTEGER", nullable: false),
                    ToAyah = table.Column<int>(type: "INTEGER", nullable: false),
                    MemorizationType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    TajweedErrorsCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TeacherNotes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RecordedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuranRecords", x => x.QuranRecordId);
                    table.ForeignKey(
                        name: "FK_QuranRecords_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuranRecords_StudentId_Date",
                table: "QuranRecords",
                columns: new[] { "StudentId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuranRecords");
        }
    }
}
