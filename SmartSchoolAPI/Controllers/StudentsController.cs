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
public class StudentsController : ControllerBase
{
    private readonly SchoolDbContext _context;
    private readonly ISchoolScope _scope;

    public StudentsController(SchoolDbContext context, ISchoolScope scope)
    {
        _context = context;
        _scope = scope;
    }

    [HttpGet]
    public async Task<IActionResult> GetStudents([FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var query = _scope.Students();
        if (page is > 0 && pageSize is > 0 && pageSize <= 200)
            query = query.Skip((page.Value - 1) * pageSize.Value).Take(pageSize.Value);
        return Ok(await query.Select(s => new { s.StudentId, s.FirstName, s.LastName }).ToListAsync());
    }

    [Authorize(Roles = RoleNames.SuperAdmin + "," + RoleNames.SchoolAdmin)]
    [HttpPost]
    public async Task<IActionResult> CreateStudent([FromBody] CreateStudentDto dto)
    {
        // This endpoint has no server-owned school resource in its route. It is
        // safe only if the principal has exactly one active school membership.
        var organizationId = await _scope.SingleAdminOrganizationAsync();
        if (organizationId is null) return Forbid();

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var user = new User { Username = dto.Username, PasswordHash = PasswordService.Hash(dto.Password), Role = RoleNames.Student };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        var student = new Student { FirstName = dto.FirstName, LastName = dto.LastName, UserId = user.UserId, OrganizationId = organizationId };
        _context.Students.Add(student);
        _context.OrganizationMemberships.Add(new OrganizationMembership { UserId = user.UserId, OrganizationId = organizationId.Value });
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return Ok(new { student.StudentId, student.FirstName, student.LastName });
    }
}

public class CreateStudentDto
{
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(3)]
    public string Username { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(8)]
    public string Password { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(1)]
    public string FirstName { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(1)]
    public string LastName { get; set; } = string.Empty;
}
