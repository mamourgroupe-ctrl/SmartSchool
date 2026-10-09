using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Authorization;
using SmartSchoolAPI.Models;
using SmartSchoolAPI.Security;
using SmartSchoolAPI.Services;
namespace SmartSchoolAPI.Controllers;
[Authorize]
[Route("api/[controller]")]
[ApiController]
public class TeachersController : ControllerBase {
    private readonly SchoolDbContext _context;
    public TeachersController(SchoolDbContext context) {
        _context = context;
    }
    [HttpGet]
    public async Task<IActionResult> GetTeachers([FromQuery] int? page, [FromQuery] int? pageSize) {
        var query = _context.Teachers.AsQueryable();
        if (User.IsInRole(RoleNames.SuperAdmin)) {
            // Super administrators can list every teacher.
        } else if (User.IsInRole(RoleNames.SchoolAdmin) && Stage1AccessService.TryUserId(User, out var adminUserId)) {
            // School admins only see teachers who belong to their organization.
            query = query.Where(t => _context.OrganizationMemberships.Any(m =>
                m.UserId == t.UserId && m.IsActive &&
                _context.OrganizationMemberships.Any(a => a.OrganizationId == m.OrganizationId && a.UserId == adminUserId && a.IsActive)));
        } else if (User.IsInRole(RoleNames.Teacher) && Stage1AccessService.TryUserId(User, out var teacherUserId)) {
            query = query.Where(t => t.UserId == teacherUserId);
        } else if (User.IsInRole(RoleNames.Parent) && Stage1AccessService.TryUserId(User, out var parentUserId)) {
            // Parents only see teachers of their linked, authorized children.
            query = query.Where(t => _context.Enrollments.Any(e =>
                e.Status == EnrollmentStatuses.Active && e.Section.TeacherId == t.TeacherId &&
                _context.StudentParents.Any(sp => sp.StudentId == e.StudentId && sp.IsAuthorized && sp.Parent.UserId == parentUserId)));
        } else if (User.IsInRole(RoleNames.Student) && Stage1AccessService.TryUserId(User, out var studentUserId)) {
            // Students only see teachers of their active sections.
            query = query.Where(t => _context.Enrollments.Any(e =>
                e.Status == EnrollmentStatuses.Active && e.Section.TeacherId == t.TeacherId && e.Student.UserId == studentUserId));
        } else {
            // Fail closed for any other role.
            query = query.Where(t => false);
        }
        if (page is > 0 && pageSize is > 0 && pageSize <= 200) {
            query = query.Skip((page.Value - 1) * pageSize.Value).Take(pageSize.Value);
        }
        var teachers = await query.Select(t => new { t.TeacherId, t.FirstName, t.LastName, t.SubjectSpecialty }).ToListAsync();
        return Ok(teachers);
    }
    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.SchoolAdmin)]
    [HttpPost]
    public async Task<IActionResult> CreateTeacher([FromBody] CreateTeacherDto dto) {
        var user = new User {
            Username = dto.Username,
            PasswordHash = PasswordService.Hash(dto.Password),
            Role = RoleNames.Teacher
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        var teacher = new Teacher {
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            SubjectSpecialty = dto.SubjectSpecialty,
            UserId = user.UserId
        };
        _context.Teachers.Add(teacher);
        await _context.SaveChangesAsync();
        return Ok(new { teacher.TeacherId, teacher.FirstName, teacher.LastName, teacher.SubjectSpecialty });
    }
}
public class CreateTeacherDto {
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(3)]
    public string Username { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(8)]
    public string Password { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(1)]
    public string FirstName { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(1)]
    public string LastName { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(1)]
    public string SubjectSpecialty { get; set; } = string.Empty;
}
