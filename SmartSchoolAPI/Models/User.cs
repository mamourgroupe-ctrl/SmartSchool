using System.Text.Json.Serialization;
namespace SmartSchoolAPI.Models;
public class User {
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    [JsonIgnore]
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
