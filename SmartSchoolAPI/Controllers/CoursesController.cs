using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Models;
namespace SmartSchoolAPI.Controllers;
[Authorize]
[Route("api/[controller]")]
[ApiController]
public class CoursesController : ControllerBase {
    private readonly SchoolDbContext _context;
    public CoursesController(SchoolDbContext context) {
        _context = context;
    }
    [HttpGet]
    public async Task<IActionResult> GetCourses() {
        var courses = await _context.Courses.Select(c => new { c.CourseId, c.CourseName, c.TeacherId }).ToListAsync();
        return Ok(courses);
    }
    [HttpPost]
    public async Task<IActionResult> CreateCourse([FromBody] CreateCourseDto dto) {
        var teacherExists = await _context.Teachers.AnyAsync(t => t.TeacherId == dto.TeacherId);
        if (!teacherExists) {
            return BadRequest("المعلم المحدد غير موجود.");
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
