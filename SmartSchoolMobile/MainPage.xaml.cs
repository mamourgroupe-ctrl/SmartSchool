using System.Net.Http.Json;
namespace SmartSchoolMobile;
public partial class MainPage : ContentPage {
    private readonly HttpClient _httpClient = new() { BaseAddress = new Uri("http://localhost:5200") };
    public MainPage() {
        InitializeComponent();
    }
    private async void OnLoginClicked(object sender, EventArgs e) {
        try {
            var response = await _httpClient.GetFromJsonAsync<List<StudentDto>>("api/students");
            if (response != null) {
                StatusLabel.Text = $"Logged in successfully. Fetched {response.Count} students.";
                StatusLabel.TextColor = Colors.Green;
                StudentsCollectionView.ItemsSource = response;
                AddStudentButton.IsVisible = true;
                ViewTeachersButton.IsVisible = true;
            }
        } catch (Exception ex) {
            StatusLabel.Text = "Login failed: " + ex.Message;
            StatusLabel.TextColor = Colors.Red;
        }
    }
    private async void OnAddStudentClicked(object sender, EventArgs e) {
        await Navigation.PushAsync(new AddStudentPage(null!, ""));
    }
    private async void OnViewTeachersClicked(object sender, EventArgs e) {
        await Navigation.PushAsync(new TeachersPage(null!, ""));
    }
    private async void OnStudentSelected(object sender, SelectionChangedEventArgs e) {
        if (e.CurrentSelection.FirstOrDefault() is StudentDto selectedStudent) {
            await Navigation.PushAsync(new StudentDetailPage(selectedStudent));
        }
    }
    private async void OnViewQuranProgressClicked(object sender, EventArgs e) {
        await Navigation.PushAsync(new QuranProgressPage());
    }
}
public class StudentDto {
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}