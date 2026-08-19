using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http.Headers;

namespace SmartSchoolMobile.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;

    private static string BaseUrl => Preferences.Get(
        "ApiBaseUrl",
        DeviceInfo.Platform == DevicePlatform.Android
            ? "http://10.0.2.2:5197"
            : "http://127.0.0.1:5197");

    public ApiService()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl)
        };
    }

    public async Task<(bool Success, string Message, string Token, List<StudentDto>? Students)> LoginAndGetStudentsAsync(
        string username,
        string password)
    {
        try
        {
            var loginDto = new
            {
                Username = username,
                Password = password
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/api/Auth/login",
                loginDto);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();

                return (
                    false,
                    $"Login failed ({response.StatusCode}): {err}",
                    string.Empty,
                    null);
            }

            var contentString = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(contentString);

            string token = string.Empty;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.Equals(
                    "token",
                    StringComparison.OrdinalIgnoreCase))
                {
                    token = prop.Value.GetString() ?? string.Empty;
                    break;
                }
            }

            await SecureStorage.Default.SetAsync(
                "access_token",
                token);

            var students = await GetStudentsAsync(token);

            return (
                true,
                "Data fetched successfully",
                token,
                students);
        }
        catch (Exception ex)
        {
            return (
                false,
                $"Error: {ex.Message}",
                string.Empty,
                null);
        }
    }

    public async Task<List<StudentDto>?> GetStudentsAsync(string token)
    {
        try
        {
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                "/api/Students");

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content
                    .ReadFromJsonAsync<List<StudentDto>>();
            }
        }
        catch
        {
        }

        return null;
    }

    public async Task<List<TeacherDto>?> GetTeachersAsync(string token)
    {
        try
        {
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                "/api/Teachers");

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content
                    .ReadFromJsonAsync<List<TeacherDto>>();
            }
        }
        catch
        {
        }

        return null;
    }

    public async Task<(bool Success, string Message)> AddStudentAsync(
        string firstName,
        string lastName,
        string token)
    {
        try
        {
            var studentDto = new
            {
                FirstName = firstName,
                LastName = lastName
            };

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/Students")
            {
                Content = JsonContent.Create(studentDto)
            };

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return (
                    true,
                    "Added successfully");
            }

            var err = await response.Content.ReadAsStringAsync();

            return (
                false,
                $"Failed: {err}");
        }
        catch (Exception ex)
        {
            return (
                false,
                $"Error: {ex.Message}");
        }
    }
}

public class StudentDto
{
    public int StudentId { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;
}

public class TeacherDto
{
    public int TeacherId { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;
}