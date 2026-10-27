using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSchoolAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddExplicitSchoolOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows deliberately stay NULL. Their historical school is not
            // established by the old schema; reconciliation requires human review.
            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_StudentId_Date",
                table: "AttendanceRecords");

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Teachers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Students",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "QuranRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "BehaviorRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "AttendanceRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teachers_OrganizationId",
                table: "Teachers",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Students_OrganizationId",
                table: "Students",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_QuranRecords_OrganizationId",
                table: "QuranRecords",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_BehaviorRecords_OrganizationId",
                table: "BehaviorRecords",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_OrganizationId_StudentId_Date",
                table: "AttendanceRecords",
                columns: new[] { "OrganizationId", "StudentId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_StudentId",
                table: "AttendanceRecords",
                column: "StudentId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceRecords_Organizations_OrganizationId",
                table: "AttendanceRecords",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "OrganizationId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BehaviorRecords_Organizations_OrganizationId",
                table: "BehaviorRecords",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "OrganizationId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_QuranRecords_Organizations_OrganizationId",
                table: "QuranRecords",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "OrganizationId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Students_Organizations_OrganizationId",
                table: "Students",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "OrganizationId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Teachers_Organizations_OrganizationId",
                table: "Teachers",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "OrganizationId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceRecords_Organizations_OrganizationId",
                table: "AttendanceRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_BehaviorRecords_Organizations_OrganizationId",
                table: "BehaviorRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_QuranRecords_Organizations_OrganizationId",
                table: "QuranRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_Students_Organizations_OrganizationId",
                table: "Students");

            migrationBuilder.DropForeignKey(
                name: "FK_Teachers_Organizations_OrganizationId",
                table: "Teachers");

            migrationBuilder.DropIndex(
                name: "IX_Teachers_OrganizationId",
                table: "Teachers");

            migrationBuilder.DropIndex(
                name: "IX_Students_OrganizationId",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_QuranRecords_OrganizationId",
                table: "QuranRecords");

            migrationBuilder.DropIndex(
                name: "IX_BehaviorRecords_OrganizationId",
                table: "BehaviorRecords");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_OrganizationId_StudentId_Date",
                table: "AttendanceRecords");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_StudentId",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "QuranRecords");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "BehaviorRecords");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "AttendanceRecords");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_StudentId_Date",
                table: "AttendanceRecords",
                columns: new[] { "StudentId", "Date" },
                unique: true);
        }
    }
}
