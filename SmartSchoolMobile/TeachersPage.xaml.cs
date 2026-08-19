using SmartSchoolMobile.Services;
namespace SmartSchoolMobile;
public partial class TeachersPage : ContentPage {
    private readonly ApiService _apiService;
    private readonly string _token;
    public TeachersPage(ApiService apiService, string token) {
        InitializeComponent();
        _apiService = apiService;
        _token = token;
        LoadTeachers();
    }
    private async void LoadTeachers() {
        var teachers = await _apiService.GetTeachersAsync(_token);
        TeachersCollectionView.ItemsSource = teachers;
    }
    private async void OnBackClicked(object? sender, EventArgs e) {
        await Navigation.PopAsync();
    }
}