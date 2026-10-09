using SmartSchoolMobile.Services;

namespace SmartSchoolMobile;
public partial class QuranProgressPage : ContentPage {
    private readonly ApiService _apiService;
    private readonly string _token;
    public QuranProgressPage(ApiService apiService, string token) {
        InitializeComponent();
        _apiService = apiService;
        _token = token;
        LoadStudents();
    }
    private async void LoadStudents() {
        var students = await _apiService.GetStudentsAsync(_token);
        var options = (students ?? new List<StudentDto>())
            .Select(s => new StudentOption { StudentId = s.StudentId, FullName = $"{s.FirstName} {s.LastName}" })
            .ToList();
        StudentsCollectionView.ItemsSource = options;
        StatusLabel.Text = options.Count == 0 ? "لا يوجد طلاب متاحون." : "اختر طالباً لعرض سجل حفظه.";
    }
    private async void OnStudentSelected(object sender, SelectionChangedEventArgs e) {
        if (e.CurrentSelection.FirstOrDefault() is not StudentOption selected) return;
        var records = await _apiService.GetQuranRecordsAsync(selected.StudentId, _token);
        ProgressCollectionView.ItemsSource = records;
        StatusLabel.Text = records == null
            ? "تعذر تحميل سجلات الحفظ."
            : (records.Count == 0 ? "لا توجد سجلات حفظ لهذا الطالب بعد." : $"عدد السجلات: {records.Count}");
    }
    private async void OnBackClicked(object sender, EventArgs e) {
        await Navigation.PopAsync();
    }
}
public class StudentOption {
    public int StudentId { get; set; }
    public string FullName { get; set; } = string.Empty;
}
