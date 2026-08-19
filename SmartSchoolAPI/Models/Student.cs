namespace SmartSchoolAPI.Models;
public class Student {
    public int StudentId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
}
