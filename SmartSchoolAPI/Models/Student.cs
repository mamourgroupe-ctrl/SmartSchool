namespace SmartSchoolAPI.Models;
public class Student {
    public int StudentId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    // Null means legacy/unassigned and must not be exposed to a school.
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
}
