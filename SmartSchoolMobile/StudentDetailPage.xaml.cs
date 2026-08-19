namespace SmartSchoolMobile;
public partial class StudentDetailPage : ContentPage {
    public StudentDetailPage(StudentDto student) {
        InitializeComponent();
        if (student != null) {
            // استخدام Id بدلاً من StudentId لتصحيح الخطأ
            Title = $"تفاصيل الطالب: {student.FirstName} {student.LastName}";
        }
    }
    private async void OnBackClicked(object sender, EventArgs e) {
        await Navigation.PopAsync();
    }
}