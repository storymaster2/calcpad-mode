using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Calcpad.Server.Data;
using Calcpad.Server.Models.Auth;

namespace Calcpad.Server.Services
{
    public class AuthService
    {
        private readonly CalcpadAuthDbContext _db;
        private readonly IConfiguration _config;

        public AuthService(CalcpadAuthDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        public async Task<AuthResponse?> LoginAsync(LoginRequest request)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
            if (user == null || !user.IsActive)
                return null;

            if (!BCrypt.Net.BCrypt.EnhancedVerify(request.Password, user.PasswordHash))
                return null;

            user.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return CreateAuthResponse(user);
        }

        public async Task<AuthResponse?> RegisterAsync(RegisterRequest request)
        {
            // Check for existing username or email
            if (await _db.Users.AnyAsync(u => u.Username == request.Username))
                return null;
            if (await _db.Users.AnyAsync(u => u.Email == request.Email))
                return null;

            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(request.Password, 12),
                Role = request.Role ?? UserRole.Contributor
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            return CreateAuthResponse(user);
        }

        public async Task<UserDto?> GetUserByIdAsync(string id)
        {
            var user = await _db.Users.FindAsync(id);
            return user == null ? null : UserDto.FromUser(user);
        }

        public async Task<List<UserDto>> GetAllUsersAsync()
        {
            return await _db.Users
                .Select(u => UserDto.FromUser(u))
                .ToListAsync();
        }

        public async Task<bool> UpdateUserAsync(string id, UpdateUserRequest request)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null) return false;

            if (request.Role.HasValue)
                user.Role = request.Role.Value;
            if (request.IsActive.HasValue)
                user.IsActive = request.IsActive.Value;

            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteUserAsync(string id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null) return false;

            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
            return true;
        }

        private AuthResponse CreateAuthResponse(User user)
        {
            var expiryHours = int.Parse(_config["Jwt:ExpiryHours"] ?? "24");
            var expiresAt = DateTime.UtcNow.AddHours(expiryHours);

            return new AuthResponse
            {
                Token = GenerateToken(user, expiresAt),
                User = UserDto.FromUser(user),
                ExpiresAt = expiresAt.ToString("O")
            };
        }

        private string GenerateToken(User user, DateTime expiresAt)
        {
            var secret = _config["Jwt:Secret"] ?? "default-secret-key-minimum-32-characters";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim("userId", user.Id),
                new Claim("username", user.Username),
                new Claim("role", ((int)user.Role).ToString())
            };

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"] ?? "CalcpadAuth",
                audience: _config["Jwt:Audience"] ?? "CalcpadAuthClient",
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
