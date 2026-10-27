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
[Route("api/[controller]")]
[ApiController]
public class TeachersController : ControllerBase
{
    private readonly SchoolDbContext _context;
    private readonly ISchoolScope _scope;

    public TeachersController(SchoolDbContext context, ISchoolScope scope)
    {
        _context = context;
        _scope = scope;
    }

    [HttpGet]
    public async Task<IActionResult> GetTeachers([FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var query = _scope.Teachers();
        if (page is > 0 && pageSize is > 0 && pageSize <= 200)
            query = query.Skip((page.Value - 1) * pageSize.Value).Take(pageSize.Value);
        return Ok(await query.Select(t => new { t.TeacherId, t.FirstName, t.LastName, t.SubjectSpecialty }).ToListAsync());
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.SchoolAdmin)]
    [HttpPost]
    public async Task<IActionResult> CreateTeacher([FromBody] CreateTeacherDto dto)
    {
        var organizationId = await _scope.SingleAdminOrganizationAsync();
        if (organizationId is null) return Forbid();

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var user = new User { Username = dto.Username, PasswordHash = PasswordService.Hash(dto.Password), Role = RoleNames.Teacher };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        var teacher = new Teacher
        {
            FirstName = dto.FirstName, LastName = dto.LastName, SubjectSpecialty = dto.SubjectSpecialty,
            UserId = user.UserId, OrganizationId = organizationId
        };
        _context.Teachers.Add(teacher);
        _context.OrganizationMemberships.Add(new OrganizationMembership { UserId = user.UserId, OrganizationId = organizationId.Value });
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return Ok(new { teacher.TeacherId, teacher.FirstName, teacher.LastName, teacher.SubjectSpecialty });
    }
}

public class CreateTeacherDto
{
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
