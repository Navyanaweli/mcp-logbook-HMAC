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
        var token = _jwtService.GenerateToken(user);

        return Ok(new
        {
            message = "Login successful",
            username = user.Username,
            role = user.Role,
            tenantId = user.TenantId,
            token
        });
    }
}