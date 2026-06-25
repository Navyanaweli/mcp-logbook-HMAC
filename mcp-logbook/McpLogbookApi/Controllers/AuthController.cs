using McpLogbookApi.Models;
using McpLogbookApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace McpLogbookApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly JwtService _jwtService;

    public AuthController(JwtService jwtService)
    {
        _jwtService = jwtService;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] UserInfo user)
    {
        var response = _jwtService.GenerateTokens(user);
        return Ok(response);
    }

    [HttpPost("refresh")]
    public IActionResult Refresh([FromBody] RefreshRequest request)
    {
        var response = _jwtService.RefreshTokens(request.RefreshToken);

        if (response is null)
            return Unauthorized(new { message = "Invalid or expired refresh token." });

        return Ok(response);
    }

    [HttpPost("logout")]
    public IActionResult Logout([FromBody] RefreshRequest request)
    {
        var revoked = _jwtService.RevokeRefreshToken(request.RefreshToken);

        if (!revoked)
            return BadRequest(new { message = "Refresh token not found." });

        return Ok(new { message = "Logged out successfully. Refresh token revoked." });
    }
}