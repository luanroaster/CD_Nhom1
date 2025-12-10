using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using PCSTORE.Services;

namespace PCSTORE.Controllers
{
    public class AccountController : Controller
    {
        private readonly CustomerService _customerService;
        private readonly AuthService _authService;
        private readonly ILogger<AccountController> _logger;

        public AccountController(CustomerService customerService, AuthService authService, ILogger<AccountController> logger)
        {
            _customerService = customerService;
            _authService = authService;
            _logger = logger;
        }

        // Bấm vào "Tài khoản": nếu đã đăng nhập thì hiện thông tin, nếu chưa thì đưa tới login
        public IActionResult Index()
        {
            // Nếu là admin thì chuyển luôn sang trang quản trị
            if (HttpContext.Session.GetString("IsAdmin") == "true")
            {
                return RedirectToAction("Products", "Admin");
            }

            var customerName = HttpContext.Session.GetString("CustomerName");
            if (!string.IsNullOrEmpty(customerName))
            {
                ViewBag.CustomerName = customerName;
                ViewBag.CustomerEmail = HttpContext.Session.GetString("CustomerEmail") ?? "";
                return View("Profile");
            }

            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Login(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "Vui lòng nhập đầy đủ tên đăng nhập và mật khẩu.";
                return View();
            }

            // 1) Thử đăng nhập admin trước
            var admin = _authService.Authenticate(username, password);
            if (admin != null)
            {
                HttpContext.Session.SetString("AdminId", admin.Id.ToString());
                HttpContext.Session.SetString("AdminUsername", admin.Username);
                HttpContext.Session.SetString("AdminFullName", admin.FullName);
                HttpContext.Session.SetString("IsAdmin", "true");

                _logger.LogInformation("Admin {Username} đã đăng nhập qua AccountController", username);
                return RedirectToAction("Products", "Admin");
            }

            // 2) Nếu không phải admin -> đăng nhập khách hàng
            var customer = _customerService.Authenticate(username, password);
            if (customer == null)
            {
                ViewBag.Error = "Tên đăng nhập hoặc mật khẩu không đúng.";
                return View();
            }

            HttpContext.Session.SetInt32("CustomerId", customer.Id);
            HttpContext.Session.SetString("CustomerName", customer.FullName);
            HttpContext.Session.SetString("CustomerUsername", customer.Username);
            HttpContext.Session.SetString("CustomerEmail", customer.Email);

            _logger.LogInformation("Khách hàng {Username} đã đăng nhập", username);
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Register(string username, string password, string confirmPassword, string fullName, string? email, string? phone)
        {
            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(confirmPassword) ||
                string.IsNullOrWhiteSpace(fullName))
            {
                ViewBag.Error = "Vui lòng nhập đầy đủ Họ tên, Tên đăng nhập và Mật khẩu.";
                return View();
            }

            if (password != confirmPassword)
            {
                ViewBag.Error = "Mật khẩu xác nhận không khớp.";
                return View();
            }

            if (_customerService.UsernameExists(username))
            {
                ViewBag.Error = "Tên đăng nhập đã tồn tại, vui lòng chọn tên khác.";
                return View();
            }

            var customer = _customerService.Register(username, password, fullName, email, phone);

            HttpContext.Session.SetInt32("CustomerId", customer.Id);
            HttpContext.Session.SetString("CustomerName", customer.FullName);
            HttpContext.Session.SetString("CustomerUsername", customer.Username);
            HttpContext.Session.SetString("CustomerEmail", customer.Email);

            _logger.LogInformation("Khách hàng {Username} đã đăng ký và đăng nhập", username);
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        public IActionResult Logout()
        {
            HttpContext.Session.Remove("CustomerId");
            HttpContext.Session.Remove("CustomerName");
            HttpContext.Session.Remove("CustomerUsername");
            HttpContext.Session.Remove("CustomerEmail");

            return RedirectToAction("Index", "Home");
        }
    }
}


