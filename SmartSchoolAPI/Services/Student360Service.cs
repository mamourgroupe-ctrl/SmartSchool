using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SmartSchoolAPI.Authorization;

namespace SmartSchoolAPI.Services;

public sealed class Student360Service
{
    private readonly ISchoolScope _scope;

    public Student360Service(ISchoolScope scope) => _scope = scope;

    public async Task<Student360Overview?> GetAsync(int studentId, ClaimsPrincipal user)
    {
        if (!await CanViewAsync(studentId, user)) return null;

        var student = await _scope.Students()
            .Where(x => x.StudentId == studentId)
            .Select(x => new { x.StudentId, x.FirstName, x.LastName })
            .SingleOrDefaultAsync();
        if (student is null) return null;

        var canManage = user.IsInRole(RoleNames.SuperAdmin) || await CanManageAsync(studentId, user);
        var enrollments = await _scope.Enrollments()
            .Where(x => x.StudentId == studentId)
            .OrderByDescending(x => x.StartDate)
            .Select(x => new EnrollmentItem(x.EnrollmentId, x.Status, x.Section.Name, x.Section.SchoolClass.Name, x.StartDate, x.EndDate))
            .ToListAsync();
        var guardians = await _scope.Guardians()
            .Where(x => x.StudentId == studentId && x.IsAuthorized)
            .OrderBy(x => x.Parent.LastName)
            .Select(x => new GuardianItem(x.ParentId, x.Parent.FirstName, x.Parent.LastName, x.Relationship))
            .ToListAsync();
        var attendance = await _scope.Attendance()
            .Where(x => x.StudentId == studentId)
            .OrderByDescending(x => x.Date)
            .Take(30)
            .Select(x => new AttendanceItem(x.Date, x.Status, x.Notes))
            .ToListAsync();
        var behaviorQuery = _scope.Behavior().Where(x => x.StudentId == studentId);
        if (!canManage) behaviorQuery = behaviorQuery.Where(x => x.Visibility == BehaviorVisibility.ParentVisible);
        var behavior = await behaviorQuery
            .OrderByDescending(x => x.Date)
            .Take(30)
            .Select(x => new BehaviorItem(x.Date, x.Type, x.Category, x.Description, x.Points, x.Visibility))
            .ToListAsync();
        var quran = await _scope.Quran()
            .Where(x => x.StudentId == studentId)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.QuranRecordId)
            .Take(30)
            .Select(x => new QuranItem(x.Date, x.SurahName, x.FromAyah, x.ToAyah, x.MemorizationType, x.Rating, x.TajweedErrorsCount))
            .ToListAsync();

        return new Student360Overview(student.StudentId, student.FirstName, student.LastName, enrollments, guardians, attendance, behavior, quran);
    }

    public async Task<bool> CanManageAsync(int studentId, ClaimsPrincipal user) =>
        await _scope.ManagedStudentOrganizationAsync(studentId) is not null;

    public Task<int?> ManagedOrganizationAsync(int studentId) => _scope.ManagedStudentOrganizationAsync(studentId);

    public Task<bool> CanViewAsync(int studentId, ClaimsPrincipal user) => _scope.CanViewStudentAsync(studentId);
}

public static class EnrollmentStatuses
{
    public const string Active = "ACTIVE";
    public const string Withdrawn = "WITHDRAWN";
}

public static class AttendanceStatuses
{
    public const string Present = "PRESENT";
    public const string Absent = "ABSENT";
    public const string Late = "LATE";
    public const string Excused = "EXCUSED";
    public const string EarlyLeave = "EARLY_LEAVE";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Present, Absent, Late, Excused, EarlyLeave
    };
}

public static class BehaviorTypes
{
    public const string Positive = "POSITIVE";
    public const string Incident = "INCIDENT";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Positive, Incident };
}

public static class BehaviorVisibility
{
    public const string Internal = "INTERNAL";
    public const string ParentVisible = "PARENT_VISIBLE";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Internal, ParentVisible };
}

public sealed record EnrollmentItem(int EnrollmentId, string Status, string Section, string SchoolClass, DateOnly StartDate, DateOnly? EndDate);
public sealed record GuardianItem(int ParentId, string FirstName, string LastName, string Relationship);
public sealed record AttendanceItem(DateOnly Date, string Status, string? Notes);
public sealed record BehaviorItem(DateOnly Date, string Type, string Category, string Description, int Points, string Visibility);
public sealed record QuranItem(DateOnly Date, string SurahName, int FromAyah, int ToAyah, string MemorizationType, int Rating, int TajweedErrorsCount);
public sealed record Student360Overview(int StudentId, string FirstName, string LastName, IReadOnlyList<EnrollmentItem> Enrollments, IReadOnlyList<GuardianItem> Guardians, IReadOnlyList<AttendanceItem> Attendance, IReadOnlyList<BehaviorItem> Behavior, IReadOnlyList<QuranItem> Quran);
