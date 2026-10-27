using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchoolAPI.Authorization;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Models;
using SmartSchoolAPI.Services;

namespace SmartSchoolAPI.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class CoursesController : ControllerBase
{
    private readonly SchoolDbContext _context;
    private readonly Stage1AccessService _access;
    private readonly ISchoolScope _scope;

    public CoursesController(SchoolDbContext context, Stage1AccessService access, ISchoolScope scope)
    {
        _context = context;
        _access = access;
        _scope = scope;
    }

    [HttpGet]
    public async Task<IActionResult> GetCourses([FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var query = _scope.Courses();
        if (page is > 0 && pageSize is > 0 && pageSize <= 200)
            query = query.Skip((page.Value - 1) * pageSize.Value).Take(pageSize.Value);
        return Ok(await query.Select(c => new { c.CourseId, c.CourseName, c.TeacherId }).ToListAsync());
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.SchoolAdmin)]
    [HttpPost]
    public async Task<IActionResult> CreateCourse([FromBody] CreateCourseDto dto)
    {
        var teacher = await _scope.Teachers().SingleOrDefaultAsync(t => t.TeacherId == dto.TeacherId);
        if (teacher is null || !await _access.CanAdminTeacherAsync(teacher.UserId, User)) return NotFound();
        var course = new Course { CourseName = dto.CourseName, TeacherId = dto.TeacherId };
        _context.Courses.Add(course);
        await _context.SaveChangesAsync();
        return Ok(new { course.CourseId, course.CourseName, course.TeacherId });
    }
}

public class CreateCourseDto
{
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(1)]
    public string CourseName { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
    public int TeacherId { get; set; }
}
