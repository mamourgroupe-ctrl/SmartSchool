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
public class StudentsController : ControllerBase {
    private readonly SchoolDbContext _context;
    public StudentsController(SchoolDbContext context) {
        _context = context;
    }
    [HttpGet]
    public async Task<IActionResult> GetStudents([FromQuery] int? page, [FromQuery] int? pageSize) {
        var query = _context.Students.AsQueryable();
        if (User.IsInRole(RoleNames.SuperAdmin)) {
            // Super administrators can list every student.
        } else if (User.IsInRole(RoleNames.SchoolAdmin) && Stage1AccessService.TryUserId(User, out var adminUserId)) {
            // School admins only see students enrolled in their organization.
            query = query.Where(s => _context.Enrollments.Any(e =>
                e.StudentId == s.StudentId &&
                _context.OrganizationMemberships.Any(m => m.OrganizationId == e.AcademicYear.OrganizationId && m.UserId == adminUserId && m.IsActive)));
        } else if (User.IsInRole(RoleNames.Teacher) && Stage1AccessService.TryUserId(User, out var teacherUserId)) {
            // Teachers only see students actively enrolled in their assigned sections.
            query = query.Where(s => _context.Enrollments.Any(e =>
                e.StudentId == s.StudentId && e.Status == EnrollmentStatuses.Active &&
                e.Section.Teacher != null && e.Section.Teacher.UserId == teacherUserId &&
                _context.OrganizationMemberships.Any(m => m.OrganizationId == e.AcademicYear.OrganizationId && m.UserId == teacherUserId && m.IsActive)));
        } else if (User.IsInRole(RoleNames.Parent) && Stage1AccessService.TryUserId(User, out var parentUserId)) {
            // Parents only see their linked, authorized children.
            query = query.Where(s => _context.StudentParents.Any(sp =>
                sp.StudentId == s.StudentId && sp.IsAuthorized && sp.Parent.UserId == parentUserId));
        } else if (User.IsInRole(RoleNames.Student) && Stage1AccessService.TryUserId(User, out var studentUserId)) {
            query = query.Where(s => s.UserId == studentUserId);
        } else {
            // Fail closed for any other role.
            query = query.Where(s => false);
        }
        if (page is > 0 && pageSize is > 0 && pageSize <= 200) {
            query = query.Skip((page.Value - 1) * pageSize.Value).Take(pageSize.Value);
        }
        var students = await query.Select(s => new { s.StudentId, s.FirstName, s.LastName }).ToListAsync();
        return Ok(students);
    }
    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.SchoolAdmin)]
    [HttpPost]
    public async Task<IActionResult> CreateStudent([FromBody] CreateStudentDto dto) {
        var user = new User {
            Username = dto.Username,
            PasswordHash = PasswordService.Hash(dto.Password),
            Role = RoleNames.Student
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        var student = new Student {
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            UserId = user.UserId
        };
        _context.Students.Add(student);
        await _context.SaveChangesAsync();
        return Ok(new { student.StudentId, student.FirstName, student.LastName });
    }
}
public class CreateStudentDto {
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(3)]
    public string Username { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(8)]
    public string Password { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(1)]
    public string FirstName { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(1)]
    public string LastName { get; set; } = string.Empty;
}
