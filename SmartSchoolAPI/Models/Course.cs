namespace SmartSchoolAPI.Models;
public class Course {
    public int CourseId { get; set; }
    public string CourseName { get; set; } = string.Empty;
    // ÑÈØ ÇáãÇÏÉ ÈÇáãÚáã ÇáĞí íÏÑÓåÇ
    public int TeacherId { get; set; }
    public Teacher Teacher { get; set; } = null!;
}
