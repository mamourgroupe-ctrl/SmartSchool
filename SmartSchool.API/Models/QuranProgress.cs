namespace SmartSchool.API.Models;
public class QuranProgress {
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string SurahName { get; set; } = string.Empty;
    public int FromAyah { get; set; }
    public int ToAyah { get; set; }
    public int MemorizationType { get; set; } // 1: حفظ جديد, 2: مراجعة
    public int Rating { get; set; } // من 10
    public int TajweedErrorsCount { get; set; }
    public string TeacherNotes { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Now;
}