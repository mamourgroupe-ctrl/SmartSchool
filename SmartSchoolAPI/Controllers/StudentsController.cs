using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Authorization;
using SmartSchoolAPI.Models;
using SmartSchoolAPI.Security;
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
    public async Task<IActionResult> GetStudents() {
        var query = _context.Students.AsQueryable();
        if (User.IsInRole(RoleNames.Student) && int.TryParse(User.FindFirst("UserId")?.Value, out var userId)) query = query.Where(s => s.UserId == userId);
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
