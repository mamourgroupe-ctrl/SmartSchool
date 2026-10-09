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
public sealed class QuranController : ControllerBase
{
    private readonly SchoolDbContext _db;
    private readonly Student360Service _student360;

    public QuranController(SchoolDbContext db, Student360Service student360)
    {
        _db = db;
        _student360 = student360;
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.SchoolAdmin + "," + RoleNames.Teacher)]
    [HttpPost("quran")]
    public async Task<IActionResult> Record(int studentId, [FromBody] QuranRecordCommand command)
    {
        if (!QuranMemorizationTypes.All.Contains(command.MemorizationType))
            return BadRequest(new { message = "Invalid memorization type." });
        if (command.FromAyah < 1 || command.ToAyah < command.FromAyah)
            return BadRequest(new { message = "Ayah range is invalid." });
        if (command.Rating is < 0 or > 10)
            return BadRequest(new { message = "Rating must be between 0 and 10." });
        if (command.TajweedErrorsCount < 0)
            return BadRequest(new { message = "Tajweed error count cannot be negative." });
        if (!await _student360.CanManageAsync(studentId, User)) return NotFound();

        Stage1AccessService.TryUserId(User, out var actor);
        var record = new QuranRecord
        {
            StudentId = studentId,
            SurahName = command.SurahName.Trim(),
            FromAyah = command.FromAyah,
            ToAyah = command.ToAyah,
            MemorizationType = command.MemorizationType,
            Rating = command.Rating,
            TajweedErrorsCount = command.TajweedErrorsCount,
            TeacherNotes = command.TeacherNotes?.Trim(),
            Date = command.Date,
            RecordedByUserId = actor
        };
        _db.QuranRecords.Add(record);
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actor,
            Action = "QURAN_RECORD_CREATED",
            EntityName = nameof(QuranRecord),
            EntityId = $"{studentId}:{command.Date:yyyy-MM-dd}:{command.SurahName.Trim()}"
        });
        await _db.SaveChangesAsync();
        return Created($"/api/students/{studentId}/quran/{record.QuranRecordId}",
            new QuranRecordResponse(record.QuranRecordId, record.StudentId, record.SurahName, record.FromAyah, record.ToAyah, record.MemorizationType, record.Rating, record.TajweedErrorsCount, record.Date));
    }

    [HttpGet("quran")]
    public async Task<IActionResult> List(int studentId)
    {
        if (!await _student360.CanViewAsync(studentId, User)) return NotFound();
        var records = await _db.QuranRecords
            .Where(x => x.StudentId == studentId)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.QuranRecordId)
            .Select(x => new QuranRecordResponse(x.QuranRecordId, x.StudentId, x.SurahName, x.FromAyah, x.ToAyah, x.MemorizationType, x.Rating, x.TajweedErrorsCount, x.Date))
            .ToListAsync();
        return Ok(records);
    }

    [HttpGet("quran/summary")]
    public async Task<IActionResult> Summary(int studentId)
    {
        if (!await _student360.CanViewAsync(studentId, User)) return NotFound();
        var records = await _db.QuranRecords
            .Where(x => x.StudentId == studentId)
            .Select(x => new { x.MemorizationType, x.FromAyah, x.ToAyah, x.Rating, x.TajweedErrorsCount, x.Date })
            .ToListAsync();
        DateOnly? lastRecordDate = records.Count == 0 ? null : records.Max(x => x.Date);
        var summary = new QuranSummaryResponse(
            records.Count,
            records.Where(x => x.MemorizationType == QuranMemorizationTypes.New).Sum(x => x.ToAyah - x.FromAyah + 1),
            records.Where(x => x.MemorizationType == QuranMemorizationTypes.Review).Sum(x => x.ToAyah - x.FromAyah + 1),
            records.Count == 0 ? 0d : Math.Round(records.Average(x => (double)x.Rating), 2),
            records.Sum(x => x.TajweedErrorsCount),
            lastRecordDate);
        return Ok(summary);
    }
}

public sealed record QuranRecordCommand(
    [param: Required] DateOnly Date,
    [param: Required, StringLength(80)] string SurahName,
    [param: Range(1, int.MaxValue)] int FromAyah,
    [param: Range(1, int.MaxValue)] int ToAyah,
    [param: Required, StringLength(16)] string MemorizationType,
    [param: Range(0, 10)] int Rating,
    [param: Range(0, int.MaxValue)] int TajweedErrorsCount,
    [param: StringLength(1000)] string? TeacherNotes);

public sealed record QuranRecordResponse(long QuranRecordId, int StudentId, string SurahName, int FromAyah, int ToAyah, string MemorizationType, int Rating, int TajweedErrorsCount, DateOnly Date);

public sealed record QuranSummaryResponse(int TotalRecords, int NewMemorizationAyahs, int ReviewAyahs, double AverageRating, int TotalTajweedErrors, DateOnly? LastRecordDate);
