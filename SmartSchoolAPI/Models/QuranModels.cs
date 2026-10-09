namespace SmartSchoolAPI.Models;

public sealed class QuranRecord
{
    public long QuranRecordId { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public string SurahName { get; set; } = string.Empty;
    public int FromAyah { get; set; }
    public int ToAyah { get; set; }
    public string MemorizationType { get; set; } = QuranMemorizationTypes.New;
    public int Rating { get; set; }
    public int TajweedErrorsCount { get; set; }
    public string? TeacherNotes { get; set; }
    public DateOnly Date { get; set; }
    public int RecordedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class QuranMemorizationTypes
{
    public const string New = "NEW";
    public const string Review = "REVIEW";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { New, Review };
}
