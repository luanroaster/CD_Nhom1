using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PCSTORE.Models;
using PCSTORE.Services;
using System.IO;

namespace PCSTORE.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly DataStoreService _dataStore;
        private readonly IWebHostEnvironment _environment;

        public HomeController(ILogger<HomeController> logger, DataStoreService dataStore, IWebHostEnvironment environment)
        {
            _logger = logger;
            _dataStore = dataStore;
            _environment = environment;
        }

        public IActionResult Index()
        {
            try
            {
                // Đọc từ DataStore
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();

                // Nếu không có danh mục, tạo danh mục mặc định
                if (categories.Count == 0)
                {
                    categories = GetDefaultCategories();
                }

                // Lọc sản phẩm nổi bật
                var featuredProducts = allProducts.Where(p => p.IsFeatured).Take(12).ToList();

                // Nếu không có sản phẩm nổi bật, lấy 12 sản phẩm đầu tiên
                if (featuredProducts.Count == 0)
                {
                    featuredProducts = allProducts.Take(12).ToList();
                }

                // Nếu không có sản phẩm, sử dụng dữ liệu mẫu
                if (allProducts.Count == 0)
                {
                    categories = GetDefaultCategories();
                    featuredProducts = GetDefaultProducts();
                }

                ViewBag.Categories = categories;
                ViewBag.FeaturedProducts = featuredProducts;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc dữ liệu");
                // Sử dụng dữ liệu mẫu nếu có lỗi
                ViewBag.Categories = GetDefaultCategories();
                ViewBag.FeaturedProducts = GetDefaultProducts();
            }

            return View();
        }

        private List<Category> GetDefaultCategories()
        {
            return new List<Category>
            {
                new Category { Id = 1, Name = "CPU - Bộ xử lý", Description = "Intel, AMD", ImageUrl = "https://via.placeholder.com/200x150?text=CPU" },
                new Category { Id = 2, Name = "Mainboard", Description = "ASUS, MSI, Gigabyte", ImageUrl = "https://via.placeholder.com/200x150?text=Mainboard" },
                new Category { Id = 3, Name = "RAM", Description = "DDR4, DDR5", ImageUrl = "https://via.placeholder.com/200x150?text=RAM" },
                new Category { Id = 4, Name = "VGA - Card đồ họa", Description = "NVIDIA, AMD", ImageUrl = "https://via.placeholder.com/200x150?text=VGA" },
                new Category { Id = 5, Name = "Ổ cứng SSD", Description = "Samsung, WD, Kingston", ImageUrl = "https://via.placeholder.com/200x150?text=SSD" },
                new Category { Id = 6, Name = "PSU - Nguồn", Description = "Corsair, Seasonic", ImageUrl = "https://via.placeholder.com/200x150?text=PSU" },
                new Category { Id = 7, Name = "Case - Vỏ máy", Description = "Nhiều thương hiệu", ImageUrl = "https://via.placeholder.com/200x150?text=Case" },
                new Category { Id = 8, Name = "Tản nhiệt", Description = "Air Cooler, AIO", ImageUrl = "https://via.placeholder.com/200x150?text=Cooler" }
            };
        }

        private List<Product> GetDefaultProducts()
        {
            return new List<Product>
            {
                new Product { Id = 1, Name = "CPU Intel Core i9-13900K", Price = 12990000, OldPrice = 13990000, ImageUrl = "https://via.placeholder.com/300x300?text=Intel+i9", CategoryId = 1, IsFeatured = true, Stock = 10 },
                new Product { Id = 2, Name = "VGA NVIDIA RTX 4090", Price = 45990000, OldPrice = 49990000, ImageUrl = "https://via.placeholder.com/300x300?text=RTX+4090", CategoryId = 4, IsFeatured = true, Stock = 5 },
                new Product { Id = 3, Name = "RAM DDR5 32GB Corsair", Price = 3990000, OldPrice = 4490000, ImageUrl = "https://via.placeholder.com/300x300?text=RAM+32GB", CategoryId = 3, IsFeatured = true, Stock = 20 },
                new Product { Id = 4, Name = "SSD Samsung 980 PRO 1TB", Price = 2990000, OldPrice = 3490000, ImageUrl = "https://via.placeholder.com/300x300?text=SSD+1TB", CategoryId = 5, IsFeatured = true, Stock = 15 },
                new Product { Id = 5, Name = "Mainboard ASUS ROG Z790", Price = 8990000, OldPrice = 9990000, ImageUrl = "https://via.placeholder.com/300x300?text=ASUS+Z790", CategoryId = 2, IsFeatured = true, Stock = 8 },
                new Product { Id = 6, Name = "PSU Corsair RM850x 850W", Price = 3490000, OldPrice = 3990000, ImageUrl = "https://via.placeholder.com/300x300?text=PSU+850W", CategoryId = 6, IsFeatured = true, Stock = 12 }
            };
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
