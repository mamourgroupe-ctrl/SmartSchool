using SmartSchoolMobile.Services;

namespace SmartSchoolMobile;
public partial class MainPage : ContentPage {
    private readonly ApiService _apiService = new();
    private string _token = string.Empty;
    public MainPage() {
        InitializeComponent();
    }
    private async void OnLoginClicked(object sender, EventArgs e) {
        var username = UsernameEntry.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) {
            StatusLabel.Text = "الرجاء إدخال اسم المستخدم وكلمة المرور.";
            StatusLabel.TextColor = Colors.Red;
            return;
        }
        StatusLabel.Text = "جارٍ تسجيل الدخول...";
        StatusLabel.TextColor = Colors.Gray;
        var (success, message, token, students) = await _apiService.LoginAndGetStudentsAsync(username, password);
        if (!success || string.IsNullOrWhiteSpace(token)) {
            StatusLabel.Text = "فشل تسجيل الدخول: " + message;
            StatusLabel.TextColor = Colors.Red;
            return;
        }
        _token = token;
        StatusLabel.Text = $"تم تسجيل الدخول بنجاح. تم جلب {students?.Count ?? 0} طالب.";
        StatusLabel.TextColor = Colors.Green;
        StudentsCollectionView.ItemsSource = students;
        AddStudentButton.IsVisible = true;
        ViewTeachersButton.IsVisible = true;
        ViewQuranProgressButton.IsVisible = true;
    }
    private async void OnAddStudentClicked(object sender, EventArgs e) {
        await Navigation.PushAsync(new AddStudentPage(_apiService, _token));
    }
    private async void OnViewTeachersClicked(object sender, EventArgs e) {
        await Navigation.PushAsync(new TeachersPage(_apiService, _token));
    }
    private async void OnStudentSelected(object sender, SelectionChangedEventArgs e) {
        if (e.CurrentSelection.FirstOrDefault() is StudentDto selectedStudent) {
            await Navigation.PushAsync(new StudentDetailPage(selectedStudent));
        }
    }
    private async void OnViewQuranProgressClicked(object sender, EventArgs e) {
        await Navigation.PushAsync(new QuranProgressPage(_apiService, _token));
    }
}
