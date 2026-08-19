using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SmartSchoolAPI.Authorization;
using SmartSchoolAPI.Data;

namespace SmartSchoolAPI.Services;

public sealed class Stage1AccessService
{
    private readonly SchoolDbContext _db;

    public Stage1AccessService(SchoolDbContext db) => _db = db;

    public static bool TryUserId(ClaimsPrincipal user, out int userId) =>
        int.TryParse(user.FindFirst("UserId")?.Value, out userId);

    public async Task<bool> CanAdminOrganizationAsync(int organizationId, ClaimsPrincipal user)
    {
        if (user.IsInRole(RoleNames.SuperAdmin)) return true;
        if (!user.IsInRole(RoleNames.SchoolAdmin) || !TryUserId(user, out var userId)) return false;

        return await _db.OrganizationMemberships.AnyAsync(x =>
            x.OrganizationId == organizationId && x.UserId == userId && x.IsActive);
    }

    public async Task<bool> CanAdminStudentAsync(int studentId, ClaimsPrincipal user)
    {
        if (user.IsInRole(RoleNames.SuperAdmin)) return true;
        if (!user.IsInRole(RoleNames.SchoolAdmin) || !TryUserId(user, out var userId)) return false;

        return await _db.Enrollments.AnyAsync(x =>
            x.StudentId == studentId &&
            _db.OrganizationMemberships.Any(m => m.OrganizationId == x.AcademicYear.OrganizationId && m.UserId == userId && m.IsActive));
    }

    public Task<int?> OrganizationForSectionAsync(int sectionId) =>
        _db.Sections.Where(x => x.SectionId == sectionId)
            .Select(x => (int?)x.SchoolClass.AcademicYear.OrganizationId)
            .SingleOrDefaultAsync();

    public Task<int?> OrganizationForAcademicYearAsync(int academicYearId) =>
        _db.AcademicYears.Where(x => x.AcademicYearId == academicYearId)
            .Select(x => (int?)x.OrganizationId)
            .SingleOrDefaultAsync();
}
