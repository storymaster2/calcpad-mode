using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Calcpad.Server.Models.Auth;
using Calcpad.Server.Services;

namespace Calcpad.Server.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AuthService? _authService;

        public AuthController(AuthService? authService = null)
        {
            _authService = authService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (_authService == null)
                return NotFound(new { error = "Auth is not enabled" });

            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { error = "Username and password are required" });

            var result = await _authService.LoginAsync(request);
            if (result == null)
                return Unauthorized(new { error = "Invalid username or password" });

            return Ok(result);
        }

        [HttpPost("register")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (_authService == null)
                return NotFound(new { error = "Auth is not enabled" });

            if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length < 3 || request.Username.Length > 30)
                return BadRequest(new { error = "Username must be 3-30 characters" });

            if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
                return BadRequest(new { error = "Valid email is required" });

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
                return BadRequest(new { error = "Password must be at least 6 characters" });

            if (request.Role.HasValue && !Enum.IsDefined(request.Role.Value))
                return BadRequest(new { error = "Invalid role" });

            var result = await _authService.RegisterAsync(request);
            if (result == null)
                return Conflict(new { error = "Username or email already exists" });

            return Created($"/api/user/{result.User.Id}", result);
        }

        [HttpGet("profile")]
        [Authorize]
        public async Task<IActionResult> GetProfile()
        {
            if (_authService == null)
                return NotFound(new { error = "Auth is not enabled" });

            var userId = User.FindFirst("userId")?.Value;
            if (userId == null)
                return Unauthorized();

            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null)
                return NotFound(new { error = "User not found" });

            return Ok(user);
        }
    }
}
