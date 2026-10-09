using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Authorization;
using SmartSchoolAPI.Models;
using SmartSchoolAPI.Services;
namespace SmartSchoolAPI.Controllers;
[Authorize]
[Route("api/[controller]")]
[ApiController]
public class CoursesController : ControllerBase {
    private readonly SchoolDbContext _context;
    private readonly Stage1AccessService _access;
    public CoursesController(SchoolDbContext context, Stage1AccessService access) {
        _context = context;
        _access = access;
    }
    [HttpGet]
    public async Task<IActionResult> GetCourses([FromQuery] int? page, [FromQuery] int? pageSize) {
        var query = _context.Courses.AsQueryable();
        if (User.IsInRole(RoleNames.SuperAdmin)) {
            // Super administrators can list every course.
        } else if (User.IsInRole(RoleNames.SchoolAdmin) && Stage1AccessService.TryUserId(User, out var adminUserId)) {
            // School admins only see courses whose teacher belongs to their organization.
            query = query.Where(c => _context.OrganizationMemberships.Any(m =>
                m.UserId == c.Teacher.UserId && m.IsActive &&
                _context.OrganizationMemberships.Any(a => a.OrganizationId == m.OrganizationId && a.UserId == adminUserId && a.IsActive)));
        } else if (User.IsInRole(RoleNames.Teacher) && Stage1AccessService.TryUserId(User, out var teacherUserId)) {
            query = query.Where(c => c.Teacher.UserId == teacherUserId);
        } else if (User.IsInRole(RoleNames.Parent) && Stage1AccessService.TryUserId(User, out var parentUserId)) {
            // Parents only see courses taught to their linked, authorized children.
            query = query.Where(c => _context.Enrollments.Any(e =>
                e.Status == EnrollmentStatuses.Active && e.Section.TeacherId == c.TeacherId &&
                _context.StudentParents.Any(sp => sp.StudentId == e.StudentId && sp.IsAuthorized && sp.Parent.UserId == parentUserId)));
        } else if (User.IsInRole(RoleNames.Student) && Stage1AccessService.TryUserId(User, out var studentUserId)) {
            // Students only see courses taught in their active sections.
            query = query.Where(c => _context.Enrollments.Any(e =>
                e.Status == EnrollmentStatuses.Active && e.Section.TeacherId == c.TeacherId && e.Student.UserId == studentUserId));
        } else {
            // Fail closed for any other role.
            query = query.Where(c => false);
        }
        if (page is > 0 && pageSize is > 0 && pageSize <= 200) {
            query = query.Skip((page.Value - 1) * pageSize.Value).Take(pageSize.Value);
        }
        var courses = await query.Select(c => new { c.CourseId, c.CourseName, c.TeacherId }).ToListAsync();
        return Ok(courses);
    }
    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.SchoolAdmin)]
    [HttpPost]
    public async Task<IActionResult> CreateCourse([FromBody] CreateCourseDto dto) {
        var teacher = await _context.Teachers.SingleOrDefaultAsync(t => t.TeacherId == dto.TeacherId);
        if (teacher is null) {
            return BadRequest("المعلم المحدد غير موجود.");
        }
        // Tenant isolation: a school admin may only create courses for teachers
        // who belong to an organization they manage.
        if (!await _access.CanAdminTeacherAsync(teacher.UserId, User)) {
            return NotFound();
        }
        var course = new Course {
            CourseName = dto.CourseName,
            TeacherId = dto.TeacherId
        };
        _context.Courses.Add(course);
        await _context.SaveChangesAsync();
        return Ok(new { course.CourseId, course.CourseName, course.TeacherId });
    }
}
public class CreateCourseDto {
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(1)]
    public string CourseName { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
    public int TeacherId { get; set; }
}
