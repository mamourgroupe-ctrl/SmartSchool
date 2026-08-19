using System.Net.Http.Json;
namespace SmartSchoolMobile;
public partial class QuranProgressPage : ContentPage {
    private readonly HttpClient _httpClient = new() { BaseAddress = new Uri("http://localhost:5200") };
    public QuranProgressPage() {
        InitializeComponent();
        LoadProgressData();
    }
    private async void LoadProgressData() {
        try {
            var list = await _httpClient.GetFromJsonAsync<List<QuranProgressModel>>("api/QuranProgress");
            if (list != null) {
                ProgressCollectionView.ItemsSource = list;
            }
        } catch (Exception ex) {
            await DisplayAlert("خطأ", "تعذر جلب سجلات الحفظ: " + ex.Message, "موافق");
        }
    }
    private async void OnBackClicked(object sender, EventArgs e) {
        await Navigation.PopAsync();
    }
}
public class QuranProgressModel {
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string SurahName { get; set; } = string.Empty;
    public int FromAyah { get; set; }
    public int ToAyah { get; set; }
    public int MemorizationType { get; set; }
    public int Rating { get; set; }
    public int TajweedErrorsCount { get; set; }
    public string TeacherNotes { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}