using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SmartSchoolAPI.Authorization;
using SmartSchoolAPI.Data;

namespace SmartSchoolAPI.Services;

public sealed class Stage1AccessService
{
    private readonly SchoolDbContext _db;
    private readonly ISchoolScope _scope;

    public Stage1AccessService(SchoolDbContext db, ISchoolScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public static bool TryUserId(ClaimsPrincipal user, out int userId) =>
        int.TryParse(user.FindFirst("UserId")?.Value, out userId);

    // The principal comes from ISchoolScope's authenticated HttpContext; callers
    // cannot supply a school identifier to grant themselves access.
    public Task<bool> CanAdminOrganizationAsync(int organizationId, ClaimsPrincipal user) =>
        _scope.CanAdministerOrganizationAsync(organizationId);

    public async Task<bool> CanAdminStudentAsync(int studentId, ClaimsPrincipal user)
    {
        if (!user.IsInRole(RoleNames.SuperAdmin) && !user.IsInRole(RoleNames.SchoolAdmin)) return false;
        return await _scope.ManagedStudentOrganizationAsync(studentId) is not null;
    }

    public Task<bool> CanAdminTeacherAsync(int teacherUserId, ClaimsPrincipal user) =>
        _scope.CanAdministerTeacherAsync(teacherUserId);

    public Task<int?> OrganizationForSectionAsync(int sectionId) =>
        _db.Sections.Where(x => x.SectionId == sectionId)
            .Select(x => (int?)x.SchoolClass.AcademicYear.OrganizationId)
            .SingleOrDefaultAsync();

    public Task<int?> OrganizationForAcademicYearAsync(int academicYearId) =>
        _db.AcademicYears.Where(x => x.AcademicYearId == academicYearId)
            .Select(x => (int?)x.OrganizationId)
            .SingleOrDefaultAsync();
}
