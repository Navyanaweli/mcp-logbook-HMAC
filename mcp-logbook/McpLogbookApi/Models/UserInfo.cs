namespace McpLogbookApi.Models;

public class UserInfo
{
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
    public string TenantId { get; set; } = "";
}

public class RefreshRequest
{
    public string RefreshToken { get; set; } = "";
}

public class AuthResponse
{
    public string Message { get; set; } = "";
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime AccessTokenExpiry { get; set; }
    public DateTime RefreshTokenExpiry { get; set; }
}