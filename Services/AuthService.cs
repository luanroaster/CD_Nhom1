using PCSTORE.Models;
using System.Security.Cryptography;
using System.Text;

namespace PCSTORE.Services
{
    public class AuthService
    {
        private readonly ILogger<AuthService> _logger;
        private readonly List<Admin> _admins;

        public AuthService(ILogger<AuthService> logger)
        {
            _logger = logger;
            // Khởi tạo admin mặc định
            _admins = new List<Admin>
            {
                new Admin
                {
                    Id = 1,
                    Username = "admin",
                    Password = HashPassword("admin123"), // Mật khẩu mặc định: admin123
                    Email = "admin@pcstore.com",
                    FullName = "Administrator",
                    IsActive = true,
                    CreatedAt = DateTime.Now
                }
            };
        }

        // Hash mật khẩu
        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        // Xác thực đăng nhập
        public Admin? Authenticate(string username, string password)
        {
            var hashedPassword = HashPassword(password);
            var admin = _admins.FirstOrDefault(a => 
                a.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && 
                a.Password == hashedPassword && 
                a.IsActive);

            if (admin != null)
            {
                _logger.LogInformation($"Admin {username} đã đăng nhập thành công");
            }
            else
            {
                _logger.LogWarning($"Đăng nhập thất bại cho username: {username}");
            }

            return admin;
        }

        // Kiểm tra mật khẩu
        public bool VerifyPassword(string password, string hashedPassword)
        {
            var hashedInput = HashPassword(password);
            return hashedInput == hashedPassword;
        }

        // Lấy admin theo username
        public Admin? GetAdminByUsername(string username)
        {
            return _admins.FirstOrDefault(a => 
                a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        // Thêm admin mới (có thể mở rộng)
        public void AddAdmin(Admin admin)
        {
            admin.Password = HashPassword(admin.Password);
            admin.Id = _admins.Count > 0 ? _admins.Max(a => a.Id) + 1 : 1;
            _admins.Add(admin);
            _logger.LogInformation($"Đã thêm admin mới: {admin.Username}");
        }
    }
}

