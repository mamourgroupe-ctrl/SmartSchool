namespace SmartSchoolAPI.Models;

public sealed class Organization
{
    public int OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class OrganizationMembership
{
    public long OrganizationMembershipId { get; set; }
    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AcademicYear
{
    public int AcademicYearId { get; set; }
    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsActive { get; set; }
}

public sealed class Term
{
    public int TermId { get; set; }
    public int AcademicYearId { get; set; }
    public AcademicYear AcademicYear { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}

public sealed class SchoolClass
{
    public int SchoolClassId { get; set; }
    public int AcademicYearId { get; set; }
    public AcademicYear AcademicYear { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? GradeLevel { get; set; }
}

public sealed class Section
{
    public int SectionId { get; set; }
    public int SchoolClassId { get; set; }
    public SchoolClass SchoolClass { get; set; } = null!;
    public int? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class Parent
{
    public int ParentId { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
}

public sealed class StudentParent
{
    public int StudentParentId { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public int ParentId { get; set; }
    public Parent Parent { get; set; } = null!;
    public string Relationship { get; set; } = string.Empty;
    public bool IsAuthorized { get; set; } = true;
}

public sealed class Enrollment
{
    public int EnrollmentId { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public int AcademicYearId { get; set; }
    public AcademicYear AcademicYear { get; set; } = null!;
    public int TermId { get; set; }
    public Term Term { get; set; } = null!;
    public int SectionId { get; set; }
    public Section Section { get; set; } = null!;
    public string Status { get; set; } = "ACTIVE";
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}

public sealed class AttendanceRecord
{
    public long AttendanceRecordId { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public DateOnly Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public int RecordedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class BehaviorRecord
{
    public long BehaviorRecordId { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public string Type { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Visibility { get; set; } = "INTERNAL";
    public int Points { get; set; }
    public int RecordedByUserId { get; set; }
    public DateOnly Date { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
