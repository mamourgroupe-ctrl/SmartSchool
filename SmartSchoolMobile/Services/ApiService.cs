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
            string refreshToken = string.Empty;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.Equals(
                    "accessToken",
                    StringComparison.OrdinalIgnoreCase) ||
                    prop.Name.Equals(
                    "token",
                    StringComparison.OrdinalIgnoreCase))
                {
                    token = prop.Value.GetString() ?? string.Empty;
                }
                else if (prop.Name.Equals(
                    "refreshToken",
                    StringComparison.OrdinalIgnoreCase))
                {
                    refreshToken = prop.Value.GetString() ?? string.Empty;
                }
            }

            await SecureStorage.Default.SetAsync(
                "access_token",
                token);

            if (!string.IsNullOrEmpty(refreshToken))
            {
                await SecureStorage.Default.SetAsync(
                    "refresh_token",
                    refreshToken);
            }

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

    public async Task<List<QuranRecordDto>?> GetQuranRecordsAsync(int studentId, string token)
    {
        try
        {
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/students/{studentId}/quran");

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content
                    .ReadFromJsonAsync<List<QuranRecordDto>>();
            }
        }
        catch
        {
        }

        return null;
    }

    public async Task<(bool Success, string Token)> RefreshAsync()
    {
        try
        {
            var refreshToken = await SecureStorage.Default.GetAsync("refresh_token");
            if (string.IsNullOrEmpty(refreshToken))
            {
                return (false, string.Empty);
            }

            var response = await _httpClient.PostAsJsonAsync(
                "/api/Auth/refresh",
                new { RefreshToken = refreshToken });

            if (!response.IsSuccessStatusCode)
            {
                return (false, string.Empty);
            }

            var contentString = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(contentString);

            string accessToken = string.Empty;
            string newRefreshToken = string.Empty;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.Equals(
                    "accessToken",
                    StringComparison.OrdinalIgnoreCase))
                {
                    accessToken = prop.Value.GetString() ?? string.Empty;
                }
                else if (prop.Name.Equals(
                    "refreshToken",
                    StringComparison.OrdinalIgnoreCase))
                {
                    newRefreshToken = prop.Value.GetString() ?? string.Empty;
                }
            }

            if (string.IsNullOrEmpty(accessToken))
            {
                return (false, string.Empty);
            }

            await SecureStorage.Default.SetAsync(
                "access_token",
                accessToken);

            if (!string.IsNullOrEmpty(newRefreshToken))
            {
                await SecureStorage.Default.SetAsync(
                    "refresh_token",
                    newRefreshToken);
            }

            return (true, accessToken);
        }
        catch
        {
            return (false, string.Empty);
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            var token = await SecureStorage.Default.GetAsync("access_token");
            if (!string.IsNullOrEmpty(token))
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    "/api/Auth/logout");

                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        token);

                await _httpClient.SendAsync(request);
            }
        }
        catch
        {
        }
        finally
        {
            SecureStorage.Default.Remove("access_token");
            SecureStorage.Default.Remove("refresh_token");
        }
    }

    public async Task<(bool Success, string Message)> AddStudentAsync(
        string username,
        string password,
        string firstName,
        string lastName,
        string token)
    {
        try
        {
            var studentDto = new
            {
                Username = username,
                Password = password,
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