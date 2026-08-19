using System.Net.Http.Json;
using SmartSchoolMobile.Services;
namespace SmartSchoolMobile;
public partial class AddStudentPage : ContentPage {
    private readonly ApiService _apiService;
    private readonly string _token;
    public AddStudentPage(ApiService apiService, string token) {
        InitializeComponent();
        _apiService = apiService;
        _token = token;
    }
    private async void OnSaveStudentClicked(object? sender, EventArgs e) {
        var firstName = FirstNameEntry.Text ?? string.Empty;
        var lastName = LastNameEntry.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName)) {
            MsgLabel.TextColor = Colors.Red;
            MsgLabel.Text = "الرجاء إدخال الاسم الأول واسم العائلة.";
            return;
        }
        var (success, message) = await _apiService.AddStudentAsync(firstName, lastName, _token);
        if (success) {
            MsgLabel.TextColor = Colors.Green;
            MsgLabel.Text = "تم إضافة الطالب بنجاح!";
            await Task.Delay(1000);
            await Navigation.PopAsync();
        } else {
            MsgLabel.TextColor = Colors.Red;
            MsgLabel.Text = message;
        }
    }
}