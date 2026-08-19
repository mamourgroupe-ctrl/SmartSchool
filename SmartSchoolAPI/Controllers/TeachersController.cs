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
public class TeachersController : ControllerBase {
    private readonly SchoolDbContext _context;
    public TeachersController(SchoolDbContext context) {
        _context = context;
    }
    [HttpGet]
    public async Task<IActionResult> GetTeachers() {
        var query = _context.Teachers.AsQueryable();
        if (User.IsInRole(RoleNames.Teacher) && int.TryParse(User.FindFirst("UserId")?.Value, out var userId)) query = query.Where(t => t.UserId == userId);
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
