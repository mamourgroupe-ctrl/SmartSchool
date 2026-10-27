using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SmartSchoolAPI.Authorization;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Models;

namespace SmartSchoolAPI.Services;

/// <summary>
/// Applies the authenticated principal's school and relationship constraints to
/// every school-owned read. No school identifier is accepted from the caller.
/// </summary>
public interface ISchoolScope
{
    IQueryable<Student> Students();
    IQueryable<Teacher> Teachers();
    IQueryable<Course> Courses();
    IQueryable<Enrollment> Enrollments();
    IQueryable<StudentParent> Guardians();
    IQueryable<AttendanceRecord> Attendance();
    IQueryable<BehaviorRecord> Behavior();
    IQueryable<QuranRecord> Quran();
    Task<bool> CanAdministerOrganizationAsync(int organizationId);
    Task<int?> SingleAdminOrganizationAsync();
    Task<bool> CanAdministerTeacherAsync(int teacherUserId);
    Task<int?> ManagedStudentOrganizationAsync(int studentId);
    Task<bool> CanViewStudentAsync(int studentId);
}

public sealed class SchoolScope : ISchoolScope
{
    private readonly SchoolDbContext _db;
    private readonly IHttpContextAccessor _http;

    public SchoolScope(SchoolDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    private ClaimsPrincipal User => _http.HttpContext?.User ?? new ClaimsPrincipal();
    private bool HasRole(string role) => User.Identity?.IsAuthenticated == true && User.IsInRole(role);
    private bool TryUserId(out int userId) => Stage1AccessService.TryUserId(User, out userId);

    public IQueryable<Student> Students()
    {
        var query = _db.Students.AsQueryable();
        if (!TryUserId(out var userId) || User.Identity?.IsAuthenticated != true) return query.Where(_ => false);
        if (HasRole(RoleNames.SuperAdmin)) return query;
        if (HasRole(RoleNames.SchoolAdmin))
            return query.Where(s => s.OrganizationId != null && s.Organization!.IsActive &&
                _db.OrganizationMemberships.Any(m => m.UserId == userId && m.IsActive && m.OrganizationId == s.OrganizationId));
        if (HasRole(RoleNames.Teacher))
            return query.Where(s => s.OrganizationId != null && s.Organization!.IsActive &&
                _db.Enrollments.Any(e => e.StudentId == s.StudentId && e.Status == EnrollmentStatuses.Active &&
                    e.AcademicYear.OrganizationId == s.OrganizationId &&
                    e.Term.AcademicYearId == e.AcademicYearId && e.Section.SchoolClass.AcademicYearId == e.AcademicYearId &&
                    e.Section.Teacher != null && e.Section.Teacher.UserId == userId &&
                    e.Section.Teacher.OrganizationId == s.OrganizationId &&
                    _db.OrganizationMemberships.Any(m => m.UserId == userId && m.IsActive && m.OrganizationId == s.OrganizationId)));
        if (HasRole(RoleNames.Parent))
            return query.Where(s => s.OrganizationId != null && s.Organization!.IsActive &&
                _db.OrganizationMemberships.Any(m => m.UserId == userId && m.IsActive && m.OrganizationId == s.OrganizationId) &&
                _db.StudentParents.Any(p => p.StudentId == s.StudentId && p.IsAuthorized && p.Parent.UserId == userId));
        if (HasRole(RoleNames.Student))
            // An unassigned user can still see their own basic profile, but not school records.
            return query.Where(s => s.UserId == userId && (s.OrganizationId == null || s.Organization!.IsActive));
        return query.Where(_ => false);
    }

    public IQueryable<Teacher> Teachers()
    {
        var query = _db.Teachers.AsQueryable();
        if (!TryUserId(out var userId) || User.Identity?.IsAuthenticated != true) return query.Where(_ => false);
        if (HasRole(RoleNames.SuperAdmin)) return query;
        if (HasRole(RoleNames.SchoolAdmin))
            return query.Where(t => t.OrganizationId != null && t.Organization!.IsActive &&
                _db.OrganizationMemberships.Any(m => m.UserId == userId && m.IsActive && m.OrganizationId == t.OrganizationId));
        if (HasRole(RoleNames.Teacher))
            return query.Where(t => t.UserId == userId && t.OrganizationId != null && t.Organization!.IsActive &&
                _db.OrganizationMemberships.Any(m => m.UserId == userId && m.IsActive && m.OrganizationId == t.OrganizationId));
        if (HasRole(RoleNames.Parent) || HasRole(RoleNames.Student))
        {
            var students = Students().Where(s => s.OrganizationId != null);
            return query.Where(t => t.OrganizationId != null && t.Organization!.IsActive &&
                _db.OrganizationMemberships.Any(m => m.UserId == t.UserId && m.IsActive && m.OrganizationId == t.OrganizationId) &&
                _db.Enrollments.Any(e => e.Status == EnrollmentStatuses.Active && e.Section.TeacherId == t.TeacherId &&
                    e.AcademicYear.OrganizationId == t.OrganizationId &&
                    e.Term.AcademicYearId == e.AcademicYearId && e.Section.SchoolClass.AcademicYearId == e.AcademicYearId &&
                    students.Any(s => s.StudentId == e.StudentId && s.OrganizationId == t.OrganizationId)));
        }
        return query.Where(_ => false);
    }

    public IQueryable<Course> Courses()
    {
        if (HasRole(RoleNames.SuperAdmin) && TryUserId(out _)) return _db.Courses;
        var teachers = Teachers().Where(t => t.OrganizationId != null);
        return _db.Courses.Where(c => teachers.Any(t => t.TeacherId == c.TeacherId));
    }

    public IQueryable<Enrollment> Enrollments()
    {
        if (HasRole(RoleNames.SuperAdmin) && TryUserId(out _)) return _db.Enrollments;
        var students = Students().Where(s => s.OrganizationId != null);
        return _db.Enrollments.Where(e => e.Student.OrganizationId != null &&
            e.AcademicYear.OrganizationId == e.Student.OrganizationId &&
            e.Term.AcademicYearId == e.AcademicYearId && e.Section.SchoolClass.AcademicYearId == e.AcademicYearId &&
            students.Any(s => s.StudentId == e.StudentId));
    }

    public IQueryable<StudentParent> Guardians()
    {
        if (HasRole(RoleNames.SuperAdmin) && TryUserId(out _)) return _db.StudentParents;
        var students = Students().Where(s => s.OrganizationId != null);
        return _db.StudentParents.Where(p => p.Student.OrganizationId != null &&
            students.Any(s => s.StudentId == p.StudentId) &&
            _db.OrganizationMemberships.Any(m => m.UserId == p.Parent.UserId && m.IsActive && m.OrganizationId == p.Student.OrganizationId));
    }

    public IQueryable<AttendanceRecord> Attendance()
    {
        if (HasRole(RoleNames.SuperAdmin) && TryUserId(out _)) return _db.AttendanceRecords;
        var students = Students().Where(s => s.OrganizationId != null);
        return _db.AttendanceRecords.Where(r => r.OrganizationId != null && r.Student.OrganizationId == r.OrganizationId &&
            students.Any(s => s.StudentId == r.StudentId));
    }

    public IQueryable<BehaviorRecord> Behavior()
    {
        if (HasRole(RoleNames.SuperAdmin) && TryUserId(out _)) return _db.BehaviorRecords;
        var students = Students().Where(s => s.OrganizationId != null);
        return _db.BehaviorRecords.Where(r => r.OrganizationId != null && r.Student.OrganizationId == r.OrganizationId &&
            students.Any(s => s.StudentId == r.StudentId));
    }

    public IQueryable<QuranRecord> Quran()
    {
        if (HasRole(RoleNames.SuperAdmin) && TryUserId(out _)) return _db.QuranRecords;
        var students = Students().Where(s => s.OrganizationId != null);
        return _db.QuranRecords.Where(r => r.OrganizationId != null && r.Student.OrganizationId == r.OrganizationId &&
            students.Any(s => s.StudentId == r.StudentId));
    }

    public async Task<bool> CanAdministerOrganizationAsync(int organizationId)
    {
        if (!TryUserId(out var userId) || User.Identity?.IsAuthenticated != true) return false;
        if (HasRole(RoleNames.SuperAdmin)) return await _db.Organizations.AnyAsync(o => o.OrganizationId == organizationId && o.IsActive);
        if (!HasRole(RoleNames.SchoolAdmin)) return false;
        return await _db.OrganizationMemberships.AnyAsync(m => m.UserId == userId && m.IsActive &&
            m.OrganizationId == organizationId && m.Organization.IsActive);
    }

    public async Task<int?> SingleAdminOrganizationAsync()
    {
        if ((!HasRole(RoleNames.SchoolAdmin) && !HasRole(RoleNames.SuperAdmin)) || !TryUserId(out var userId)) return null;
        var ids = await _db.OrganizationMemberships.Where(m => m.UserId == userId && m.IsActive && m.Organization.IsActive)
            .Select(m => m.OrganizationId).Distinct().Take(2).ToArrayAsync();
        return ids.Length == 1 ? ids[0] : null;
    }

    public async Task<bool> CanAdministerTeacherAsync(int teacherUserId)
    {
        if (!HasRole(RoleNames.SuperAdmin) && !HasRole(RoleNames.SchoolAdmin)) return false;
        return await Teachers().AnyAsync(t => t.UserId == teacherUserId && t.OrganizationId != null && t.Organization!.IsActive);
    }

    public async Task<int?> ManagedStudentOrganizationAsync(int studentId)
    {
        if (!HasRole(RoleNames.SuperAdmin) && !HasRole(RoleNames.SchoolAdmin) && !HasRole(RoleNames.Teacher)) return null;
        return await Students().Where(s => s.StudentId == studentId && s.OrganizationId != null && s.Organization!.IsActive)
            .Select(s => s.OrganizationId).SingleOrDefaultAsync();
    }

    public async Task<bool> CanViewStudentAsync(int studentId)
    {
        var students = Students().Where(s => s.StudentId == studentId);
        if (!HasRole(RoleNames.SuperAdmin)) students = students.Where(s => s.OrganizationId != null);
        return await students.AnyAsync();
    }
}
