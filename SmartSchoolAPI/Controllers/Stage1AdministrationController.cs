using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchoolAPI.Authorization;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Models;
using SmartSchoolAPI.Security;
using SmartSchoolAPI.Services;

namespace SmartSchoolAPI.Controllers;

[Authorize]
[ApiController]
[Route("api")]
public sealed class Stage1AdministrationController : ControllerBase
{
    private readonly SchoolDbContext _db;
    private readonly Stage1AccessService _access;

    public Stage1AdministrationController(SchoolDbContext db, Stage1AccessService access)
    {
        _db = db;
        _access = access;
    }

    [Authorize(Roles = RoleNames.SuperAdmin)]
    [HttpPost("organizations")]
    public async Task<IActionResult> CreateOrganization([FromBody] CreateOrganizationCommand command)
    {
        if (await _db.Organizations.AnyAsync(x => x.Name == command.Name))
            return Conflict(new { message = "Organization name already exists." });

        var organization = new Organization { Name = command.Name.Trim() };
        _db.Organizations.Add(organization);
        await _db.SaveChangesAsync();

        if (Stage1AccessService.TryUserId(User, out var actor) && await _db.Users.AnyAsync(x => x.UserId == actor))
        {
            _db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = organization.OrganizationId, UserId = actor });
            _db.AuditLogs.Add(Audit(actor, "STAGE1_ORGANIZATION_CREATED", nameof(Organization), organization.OrganizationId));
            await _db.SaveChangesAsync();
        }

        return Created($"/api/organizations/{organization.OrganizationId}", new OrganizationResponse(organization.OrganizationId, organization.Name, organization.IsActive));
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.SchoolAdmin)]
    [HttpPost("organizations/{organizationId:int}/members")]
    public async Task<IActionResult> AddMember(int organizationId, [FromBody] AddOrganizationMemberCommand command)
    {
        if (!await _access.CanAdminOrganizationAsync(organizationId, User)) return NotFound();
        if (!await _db.Users.AnyAsync(x => x.UserId == command.UserId)) return NotFound();
        if (await _db.OrganizationMemberships.AnyAsync(x => x.OrganizationId == organizationId && x.UserId == command.UserId))
            return Conflict(new { message = "Organization membership already exists." });

        Stage1AccessService.TryUserId(User, out var actor);
        _db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = organizationId, UserId = command.UserId });
        _db.AuditLogs.Add(Audit(actor, "STAGE1_ORGANIZATION_MEMBER_ADDED", nameof(OrganizationMembership), $"{organizationId}:{command.UserId}"));
        await _db.SaveChangesAsync();
        return Created($"/api/organizations/{organizationId}/members/{command.UserId}", new CommandResult(true));
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.SchoolAdmin)]
    [HttpPost("organizations/{organizationId:int}/academic-years")]
    public async Task<IActionResult> CreateAcademicYear(int organizationId, [FromBody] CreateAcademicYearCommand command)
    {
        if (!await _access.CanAdminOrganizationAsync(organizationId, User)) return NotFound();
        if (command.EndDate <= command.StartDate) return BadRequest(new { message = "EndDate must be after StartDate." });
        if (await _db.AcademicYears.AnyAsync(x => x.OrganizationId == organizationId && x.Name == command.Name))
            return Conflict(new { message = "Academic year name already exists." });

        Stage1AccessService.TryUserId(User, out var actor);
        var academicYear = new AcademicYear
        {
            OrganizationId = organizationId,
            Name = command.Name.Trim(),
            StartDate = command.StartDate,
            EndDate = command.EndDate,
            IsActive = command.IsActive
        };
        _db.AcademicYears.Add(academicYear);
        _db.AuditLogs.Add(Audit(actor, "STAGE1_ACADEMIC_YEAR_CREATED", nameof(AcademicYear), organizationId));
        await _db.SaveChangesAsync();
        return Created($"/api/academic-years/{academicYear.AcademicYearId}", new AcademicYearResponse(academicYear.AcademicYearId, organizationId, academicYear.Name));
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.SchoolAdmin)]
    [HttpPost("academic-years/{academicYearId:int}/terms")]
    public async Task<IActionResult> CreateTerm(int academicYearId, [FromBody] CreateTermCommand command)
    {
        var academicYear = await _db.AcademicYears.SingleOrDefaultAsync(x => x.AcademicYearId == academicYearId);
        if (academicYear is null || !await _access.CanAdminOrganizationAsync(academicYear.OrganizationId, User)) return NotFound();
        if (command.EndDate <= command.StartDate || command.StartDate < academicYear.StartDate || command.EndDate > academicYear.EndDate)
            return BadRequest(new { message = "Term dates must be within the academic year." });
        if (await _db.Terms.AnyAsync(x => x.AcademicYearId == academicYearId && x.Name == command.Name))
            return Conflict(new { message = "Term name already exists." });

        Stage1AccessService.TryUserId(User, out var actor);
        var term = new Term { AcademicYearId = academicYearId, Name = command.Name.Trim(), StartDate = command.StartDate, EndDate = command.EndDate };
        _db.Terms.Add(term);
        _db.AuditLogs.Add(Audit(actor, "STAGE1_TERM_CREATED", nameof(Term), academicYearId));
        await _db.SaveChangesAsync();
        return Created($"/api/terms/{term.TermId}", new TermResponse(term.TermId, academicYearId, term.Name));
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.SchoolAdmin)]
    [HttpPost("academic-years/{academicYearId:int}/classes")]
    public async Task<IActionResult> CreateClassroom(int academicYearId, [FromBody] CreateClassroomCommand command)
    {
        var organizationId = await _access.OrganizationForAcademicYearAsync(academicYearId);
        if (organizationId is null || !await _access.CanAdminOrganizationAsync(organizationId.Value, User)) return NotFound();
        if (await _db.SchoolClasses.AnyAsync(x => x.AcademicYearId == academicYearId && x.Name == command.Name))
            return Conflict(new { message = "Classroom name already exists." });

        Stage1AccessService.TryUserId(User, out var actor);
        var classroom = new SchoolClass { AcademicYearId = academicYearId, Name = command.Name.Trim(), GradeLevel = command.GradeLevel?.Trim() };
        _db.SchoolClasses.Add(classroom);
        _db.AuditLogs.Add(Audit(actor, "STAGE1_CLASSROOM_CREATED", nameof(SchoolClass), academicYearId));
        await _db.SaveChangesAsync();
        return Created($"/api/classes/{classroom.SchoolClassId}", new ClassroomResponse(classroom.SchoolClassId, academicYearId, classroom.Name));
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.SchoolAdmin)]
    [HttpPost("classes/{schoolClassId:int}/sections")]
    public async Task<IActionResult> CreateSection(int schoolClassId, [FromBody] CreateSectionCommand command)
    {
        var organizationId = await _db.SchoolClasses.Where(x => x.SchoolClassId == schoolClassId)
            .Select(x => (int?)x.AcademicYear.OrganizationId).SingleOrDefaultAsync();
        if (organizationId is null || !await _access.CanAdminOrganizationAsync(organizationId.Value, User)) return NotFound();
        if (await _db.Sections.AnyAsync(x => x.SchoolClassId == schoolClassId && x.Name == command.Name))
            return Conflict(new { message = "Section name already exists." });

        if (command.TeacherId is not null)
        {
            var teacherUserId = await _db.Teachers.Where(x => x.TeacherId == command.TeacherId.Value).Select(x => (int?)x.UserId).SingleOrDefaultAsync();
            if (teacherUserId is null || !await _db.OrganizationMemberships.AnyAsync(x => x.OrganizationId == organizationId.Value && x.UserId == teacherUserId && x.IsActive))
                return BadRequest(new { message = "Teacher does not belong to the organization." });
        }

        Stage1AccessService.TryUserId(User, out var actor);
        var section = new Section { SchoolClassId = schoolClassId, Name = command.Name.Trim(), TeacherId = command.TeacherId };
        _db.Sections.Add(section);
        _db.AuditLogs.Add(Audit(actor, "STAGE1_SECTION_CREATED", nameof(Section), schoolClassId));
        await _db.SaveChangesAsync();
        return Created($"/api/sections/{section.SectionId}", new SectionResponse(section.SectionId, schoolClassId, section.Name, section.TeacherId));
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.SchoolAdmin)]
    [HttpPost("enrollments")]
    public async Task<IActionResult> CreateEnrollment([FromBody] CreateEnrollmentCommand command)
    {
        var term = await _db.Terms.Include(x => x.AcademicYear).SingleOrDefaultAsync(x => x.TermId == command.TermId);
        var section = await _db.Sections.Include(x => x.SchoolClass).SingleOrDefaultAsync(x => x.SectionId == command.SectionId);
        if (term is null || section is null || term.AcademicYearId != command.AcademicYearId || section.SchoolClass.AcademicYearId != command.AcademicYearId)
            return BadRequest(new { message = "Enrollment references must belong to the same academic year." });
        if (!await _access.CanAdminOrganizationAsync(term.AcademicYear.OrganizationId, User)) return NotFound();
        if (!await _db.Students.AnyAsync(x => x.StudentId == command.StudentId)) return NotFound();
        if (await _db.Enrollments.AnyAsync(x => x.StudentId == command.StudentId && x.TermId == command.TermId))
            return Conflict(new { message = "The student already has an enrollment for this term." });

        Stage1AccessService.TryUserId(User, out var actor);
        var enrollment = new Enrollment
        {
            StudentId = command.StudentId,
            AcademicYearId = command.AcademicYearId,
            TermId = command.TermId,
            SectionId = command.SectionId,
            Status = EnrollmentStatuses.Active,
            StartDate = command.StartDate
        };
        _db.Enrollments.Add(enrollment);
        _db.AuditLogs.Add(Audit(actor, "STAGE1_ENROLLMENT_CREATED", nameof(Enrollment), $"{command.StudentId}:{command.TermId}"));
        await _db.SaveChangesAsync();
        return Created($"/api/enrollments/{enrollment.EnrollmentId}", new EnrollmentResponse(enrollment.EnrollmentId, enrollment.StudentId, enrollment.SectionId, enrollment.Status));
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.SchoolAdmin)]
    [HttpPost("students/{studentId:int}/guardians")]
    public async Task<IActionResult> CreateGuardian(int studentId, [FromBody] CreateGuardianCommand command)
    {
        if (!await _access.CanAdminStudentAsync(studentId, User)) return NotFound();
        if (await _db.Users.AnyAsync(x => x.Username == command.Username)) return Conflict(new { message = "Username already exists." });

        var organizationId = await _db.Enrollments.Where(x => x.StudentId == studentId).OrderByDescending(x => x.StartDate)
            .Select(x => (int?)x.AcademicYear.OrganizationId).FirstOrDefaultAsync();
        if (organizationId is null) return Conflict(new { message = "Student must be enrolled before linking a guardian." });

        Stage1AccessService.TryUserId(User, out var actor);
        var user = new User { Username = command.Username.Trim(), PasswordHash = PasswordService.Hash(command.Password), Role = RoleNames.Parent };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var parent = new Parent
        {
            UserId = user.UserId,
            FirstName = command.FirstName.Trim(),
            LastName = command.LastName.Trim(),
            Phone = command.Phone?.Trim(),
            Email = command.Email?.Trim()
        };
        _db.Parents.Add(parent);
        await _db.SaveChangesAsync();

        _db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = organizationId.Value, UserId = user.UserId });
        _db.StudentParents.Add(new StudentParent { StudentId = studentId, ParentId = parent.ParentId, Relationship = command.Relationship.Trim(), IsAuthorized = command.IsAuthorized });
        _db.AuditLogs.Add(Audit(actor, "STAGE1_GUARDIAN_LINKED", nameof(StudentParent), $"{studentId}:{parent.ParentId}"));
        await _db.SaveChangesAsync();
        return Created($"/api/students/{studentId}/guardians/{parent.ParentId}", new GuardianResponse(parent.ParentId, parent.FirstName, parent.LastName, command.Relationship, command.IsAuthorized));
    }

    private static AuditLog Audit(int actor, string action, string entityName, object entityId) => new()
    {
        UserId = actor,
        Action = action,
        EntityName = entityName,
        EntityId = entityId.ToString()
    };
}

public sealed record CreateOrganizationCommand([param: Required, StringLength(120)] string Name);
public sealed record AddOrganizationMemberCommand([param: Range(1, int.MaxValue)] int UserId);
public sealed record CreateAcademicYearCommand([param: Required, StringLength(80)] string Name, DateOnly StartDate, DateOnly EndDate, bool IsActive = true);
public sealed record CreateTermCommand([param: Required, StringLength(80)] string Name, DateOnly StartDate, DateOnly EndDate);
public sealed record CreateClassroomCommand([param: Required, StringLength(80)] string Name, [param: StringLength(40)] string? GradeLevel);
public sealed record CreateSectionCommand([param: Required, StringLength(80)] string Name, int? TeacherId);
public sealed record CreateEnrollmentCommand([param: Range(1, int.MaxValue)] int StudentId, [param: Range(1, int.MaxValue)] int AcademicYearId, [param: Range(1, int.MaxValue)] int TermId, [param: Range(1, int.MaxValue)] int SectionId, DateOnly StartDate);
public sealed record CreateGuardianCommand([param: Required, StringLength(80)] string Username, [param: Required, MinLength(8)] string Password, [param: Required, StringLength(80)] string FirstName, [param: Required, StringLength(80)] string LastName, [param: Required, StringLength(60)] string Relationship, [param: StringLength(40)] string? Phone, [param: StringLength(160)] string? Email, bool IsAuthorized = true);

public sealed record OrganizationResponse(int OrganizationId, string Name, bool IsActive);
public sealed record AcademicYearResponse(int AcademicYearId, int OrganizationId, string Name);
public sealed record TermResponse(int TermId, int AcademicYearId, string Name);
public sealed record ClassroomResponse(int SchoolClassId, int AcademicYearId, string Name);
public sealed record SectionResponse(int SectionId, int SchoolClassId, string Name, int? TeacherId);
public sealed record EnrollmentResponse(int EnrollmentId, int StudentId, int SectionId, string Status);
public sealed record GuardianResponse(int ParentId, string FirstName, string LastName, string Relationship, bool IsAuthorized);
