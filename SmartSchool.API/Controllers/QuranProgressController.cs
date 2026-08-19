using Microsoft.AspNetCore.Mvc;
using SmartSchool.API.Models;
[Route("api/[controller]")]
[ApiController]
public class QuranProgressController : ControllerBase {
    private static readonly List<QuranProgress> _progressList = new() {
        new QuranProgress { Id = 1, StudentId = 1, SurahName = "سورة الملك", FromAyah = 1, ToAyah = 15, MemorizationType = 1, Rating = 9, TajweedErrorsCount = 2, TeacherNotes = "يحتاج إلى تثبيت الآيات الأخيرة", Date = DateTime.Now }
    };
    [HttpGet]
    public IActionResult GetAll() => Ok(_progressList);
    [HttpPost]
    public IActionResult AddProgress([FromBody] QuranProgress progress) {
        progress.Id = _progressList.Count > 0 ? _progressList.Max(p => p.Id) + 1 : 1;
        _progressList.Add(progress);
        return Ok(progress);
    }
}