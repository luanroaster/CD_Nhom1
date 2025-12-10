using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Linq;
using PCSTORE.Models;

namespace PCSTORE.Services
{
    public class CustomerService
    {
        private readonly string _customerPath;
        private readonly ILogger<CustomerService> _logger;
        private readonly object _lock = new object();
        private List<Customer> _customers = new List<Customer>();

        public CustomerService(IWebHostEnvironment env, ILogger<CustomerService> logger)
        {
            _logger = logger;
            var dataDir = Path.Combine(env.ContentRootPath, "Data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            _customerPath = Path.Combine(dataDir, "customers.json");
            Load();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_customerPath))
                {
                    var json = File.ReadAllText(_customerPath, Encoding.UTF8);
                    var list = JsonSerializer.Deserialize<List<Customer>>(json);
                    _customers = list ?? new List<Customer>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi load danh sách khách hàng");
                _customers = new List<Customer>();
            }
        }

        private void Save()
        {
            try
            {
                lock (_lock)
                {
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true
                    };
                    var json = JsonSerializer.Serialize(_customers, options);
                    File.WriteAllText(_customerPath, json, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu danh sách khách hàng");
            }
        }

        private static string HashPassword(string password)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }

        public Customer? GetByUsername(string username)
        {
            return _customers.FirstOrDefault(c =>
                c.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        public Customer? GetById(int id) => _customers.FirstOrDefault(c => c.Id == id);

        public Customer Register(string username, string password, string fullName, string email, string phone)
        {
            if (GetByUsername(username) != null)
            {
                throw new InvalidOperationException("Tên đăng nhập đã tồn tại.");
            }

            var customer = new Customer
            {
                Id = _customers.Count > 0 ? _customers.Max(c => c.Id) + 1 : 1,
                Username = username,
                PasswordHash = HashPassword(password),
                FullName = fullName,
                Email = email,
                Phone = phone,
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            _customers.Add(customer);
            Save();
            return customer;
        }

        public Customer? Authenticate(string username, string password)
        {
            var hash = HashPassword(password);
            var customer = _customers.FirstOrDefault(c =>
                c.Username.Equals(username, StringComparison.OrdinalIgnoreCase) &&
                c.PasswordHash == hash &&
                c.IsActive);
            return customer;
        }

        public bool UsernameExists(string username) => GetByUsername(username) != null;

        public List<Customer> GetAllCustomers()
        {
            return _customers.ToList();
        }

        public void SaveCustomers(List<Customer> customers)
        {
            _customers = customers;
            Save();
        }
    }
}


