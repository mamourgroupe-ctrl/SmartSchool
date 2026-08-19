using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchoolAPI.Authorization;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Models;
using SmartSchoolAPI.Services;

namespace SmartSchoolAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/students/{studentId:int}")]
public sealed class Student360Controller : ControllerBase
{
    private readonly SchoolDbContext _db;
    private readonly Student360Service _student360;

    public Student360Controller(SchoolDbContext db, Student360Service student360)
    {
        _db = db;
        _student360 = student360;
    }

    [HttpGet("360")]
    public async Task<IActionResult> Get(int studentId)
    {
        var model = await _student360.GetAsync(studentId, User);
        return model is null ? NotFound() : Ok(model);
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.SchoolAdmin + "," + RoleNames.Teacher)]
    [HttpPost("attendance")]
    public async Task<IActionResult> RecordAttendance(int studentId, [FromBody] AttendanceCommand command)
    {
        if (!AttendanceStatuses.All.Contains(command.Status)) return BadRequest(new { message = "Invalid attendance status." });
        if (!await _student360.CanManageAsync(studentId, User)) return NotFound();
        if (await _db.AttendanceRecords.AnyAsync(x => x.StudentId == studentId && x.Date == command.Date))
            return Conflict(new { message = "Attendance already exists for this student and date." });

        Stage1AccessService.TryUserId(User, out var actor);
        _db.AttendanceRecords.Add(new AttendanceRecord
        {
            StudentId = studentId,
            Date = command.Date,
            Status = command.Status,
            Notes = command.Notes,
            RecordedByUserId = actor
        });
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actor,
            Action = "STAGE1_ATTENDANCE_RECORDED",
            EntityName = nameof(AttendanceRecord),
            EntityId = $"{studentId}:{command.Date:yyyy-MM-dd}"
        });
        await _db.SaveChangesAsync();
        return Created($"/api/students/{studentId}/attendance/{command.Date}", new CommandResult(true));
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.SchoolAdmin + "," + RoleNames.Teacher)]
    [HttpPost("behavior")]
    public async Task<IActionResult> RecordBehavior(int studentId, [FromBody] BehaviorCommand command)
    {
        if (!BehaviorTypes.All.Contains(command.Type) || !BehaviorVisibility.All.Contains(command.Visibility))
            return BadRequest(new { message = "Invalid behavior type or visibility." });
        if (!await _student360.CanManageAsync(studentId, User)) return NotFound();

        Stage1AccessService.TryUserId(User, out var actor);
        _db.BehaviorRecords.Add(new BehaviorRecord
        {
            StudentId = studentId,
            Type = command.Type,
            Category = command.Category,
            Description = command.Description,
            Visibility = command.Visibility,
            Points = command.Points,
            Date = command.Date,
            RecordedByUserId = actor
        });
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actor,
            Action = "STAGE1_BEHAVIOR_RECORDED",
            EntityName = nameof(BehaviorRecord),
            EntityId = studentId.ToString()
        });
        await _db.SaveChangesAsync();
        return Created($"/api/students/{studentId}/behavior", new CommandResult(true));
    }
}

public sealed record AttendanceCommand(
    [param: Required] DateOnly Date,
    [param: Required, StringLength(16)] string Status,
    [param: StringLength(400)] string? Notes);

public sealed record BehaviorCommand(
    [param: Required] DateOnly Date,
    [param: Required, StringLength(16)] string Type,
    [param: Required, StringLength(80)] string Category,
    [param: Required, StringLength(1000)] string Description,
    [param: Required, StringLength(16)] string Visibility,
    int Points);

public sealed record CommandResult(bool Success);
