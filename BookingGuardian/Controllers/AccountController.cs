using BookingGuardian.Data;
using BookingGuardian.Helpers;
using BookingGuardian.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BookingGuardian.Controllers
{
    public class AccountController : Controller
    {
        private readonly BookingDbContext _dbContext;
        private readonly IConfiguration _configuration;
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

        public AccountController(BookingDbContext dbContext, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult Login() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login([FromForm] LoginRequest request)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            
            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                ViewBag.Error = "Invalid email or password.";
                return View();
            }

            var token = GenerateJwtToken(user);
            var isHttps = Request.IsHttps;
            Response.Cookies.Append("JWT_TOKEN", token, new CookieOptions 
            { 
                HttpOnly = true, 
                Secure = isHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.Add(SessionLifetime)
            });

            return RedirectToAction("Index", "Home");
        }

        public IActionResult Logout()
        {
            Response.Cookies.Delete("JWT_TOKEN", new CookieOptions { Path = "/" });
            return RedirectToAction("Login");
        }

        private string GenerateJwtToken(User user)
        {
            var jwtSecret = _configuration["JWT_SECRET"] 
                ?? _configuration["JwtSettings:Secret"];
            
            var key = Encoding.ASCII.GetBytes(jwtSecret!);
            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Name, user.FullName),
                    new Claim(ClaimTypes.Role, user.Role)
                }),
                Expires = DateTime.UtcNow.Add(SessionLifetime),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public class LoginRequest
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }
    }
}
