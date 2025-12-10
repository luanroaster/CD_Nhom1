using Microsoft.AspNetCore.Mvc;
using PCSTORE.Services;
using PCSTORE.Models;
using Microsoft.AspNetCore.Http;

namespace PCSTORE.Controllers
{
    public class AuthController : Controller
    {
        private readonly AuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(AuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }


        // Chuyển toàn bộ đăng nhập admin sang AccountController cho thống nhất
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            return RedirectToAction("Login", "Account", new { returnUrl });
        }

        [HttpPost]
        public IActionResult Login(string username, string password, string? returnUrl = null)
        {
            return RedirectToAction("Login", "Account", new { username, password, returnUrl });
        }

        // Đăng xuất
        [HttpPost]
        public IActionResult Logout()
        {
            var username = HttpContext.Session.GetString("AdminUsername");
            
            HttpContext.Session.Clear();
            
            if (!string.IsNullOrEmpty(username))
            {
                _logger.LogInformation($"Admin {username} đã đăng xuất");
            }

            return RedirectToAction("Login", "Auth");
        }

        // Kiểm tra đăng nhập (có thể dùng như helper)
        public static bool IsAdminLoggedIn(HttpContext httpContext)
        {
            return httpContext.Session.GetString("IsAdmin") == "true";
        }
    }
}

