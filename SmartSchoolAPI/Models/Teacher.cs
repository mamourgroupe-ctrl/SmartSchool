namespace SmartSchoolAPI.Models;
public class Teacher {
    public int TeacherId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string SubjectSpecialty { get; set; } = string.Empty;
    // ÑÈØ ÇáãÚáã ÈÍÓÇÈ ÇáãÓÊÎÏã ÇáÎÇÕ Èå
    public int UserId { get; set; }
    public User User { get; set; } = null!;
}
