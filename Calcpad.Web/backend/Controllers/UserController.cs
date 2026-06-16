using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Calcpad.Server.Models.Auth;
using Calcpad.Server.Services;

namespace Calcpad.Server.Controllers
{
    [ApiController]
    [Route("api/user")]
    [Authorize(Policy = "AdminOnly")]
    public class UserController : ControllerBase
    {
        private readonly AuthService? _authService;

        public UserController(AuthService? authService = null)
        {
            _authService = authService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllUsers()
        {
            if (_authService == null)
                return NotFound(new { error = "Auth is not enabled" });

            var users = await _authService.GetAllUsersAsync();
            return Ok(users);
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetUser(string userId)
        {
            if (_authService == null)
                return NotFound(new { error = "Auth is not enabled" });

            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null)
                return NotFound(new { error = "User not found" });

            return Ok(user);
        }

        [HttpPut("{userId}")]
        public async Task<IActionResult> UpdateUser(string userId, [FromBody] UpdateUserRequest request)
        {
            if (_authService == null)
                return NotFound(new { error = "Auth is not enabled" });

            if (request.Role.HasValue && !Enum.IsDefined(request.Role.Value))
                return BadRequest(new { error = "Invalid role" });

            var updated = await _authService.UpdateUserAsync(userId, request);
            if (!updated)
                return NotFound(new { error = "User not found" });

            return Ok(new { message = "User updated" });
        }

        [HttpDelete("{userId}")]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            if (_authService == null)
                return NotFound(new { error = "Auth is not enabled" });

            var deleted = await _authService.DeleteUserAsync(userId);
            if (!deleted)
                return NotFound(new { error = "User not found" });

            return Ok(new { message = "User deleted" });
        }
    }
}
