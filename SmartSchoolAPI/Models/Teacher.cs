namespace SmartSchoolAPI.Models;
public class Teacher {
    public int TeacherId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string SubjectSpecialty { get; set; } = string.Empty;
    // —»ÿ «·„⁄·„ »Õ”«» «·„” Œœ„ «·Œ«’ »Â
    // Null means legacy/unassigned and must not be exposed to a school.
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
}
