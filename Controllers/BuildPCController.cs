using Microsoft.AspNetCore.Mvc;
using PCSTORE.Models;
using PCSTORE.Services;
using System.IO;

namespace PCSTORE.Controllers
{
    public class BuildPCController : Controller
    {
        private readonly DataStoreService _dataStore;
        private readonly ILogger<BuildPCController> _logger;
        private readonly AIChatService _aiChatService;

        public BuildPCController(DataStoreService dataStore, ILogger<BuildPCController> logger, AIChatService aiChatService)
        {
            _dataStore = dataStore;
            _logger = logger;
            _aiChatService = aiChatService;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult BuildAI()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetProductsByCategory(int categoryId)
        {
            try
            {
                var allProducts = _dataStore.GetAllProducts();
                var products = allProducts.Where(p => p.CategoryId == categoryId).ToList();
                return Json(products);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy sản phẩm theo danh mục");
                return Json(new List<Product>());
            }
        }

        [HttpGet]
        public IActionResult GetAllProducts()
        {
            try
            {
                var allProducts = _dataStore.GetAllProducts();
                return Json(allProducts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy tất cả sản phẩm");
                return Json(new List<Product>());
            }
        }

        [HttpGet]
        public IActionResult GetCategories()
        {
            try
            {
                var categories = _dataStore.GetAllCategories();
                
                // Nếu không có danh mục, trả về danh mục mặc định
                if (categories.Count == 0)
                {
                    categories = GetDefaultCategories();
                }
                
                return Json(categories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh mục");
                return Json(GetDefaultCategories());
            }
        }

        private List<Category> GetDefaultCategories()
        {
            return new List<Category>
            {
                new Category { Id = 1, Name = "CPU - Bộ vi xử lý", Description = "Intel, AMD" },
                new Category { Id = 2, Name = "Main - Bo mạch chủ", Description = "ASUS, MSI, Gigabyte" },
                new Category { Id = 3, Name = "RAM - Bộ nhớ trong", Description = "DDR4, DDR5" },
                new Category { Id = 4, Name = "VGA - Card Màn Hình", Description = "NVIDIA, AMD" },
                new Category { Id = 5, Name = "PSU - Nguồn máy tính", Description = "Corsair, Seasonic" },
                new Category { Id = 6, Name = "Case - Vỏ máy tính", Description = "Nhiều thương hiệu" },
                new Category { Id = 7, Name = "SSD - Ổ cứng SSD", Description = "Samsung, WD, Kingston" },
                new Category { Id = 8, Name = "HDD - Ổ cứng HDD", Description = "Seagate, WD, Toshiba" },
                new Category { Id = 9, Name = "Monitor - Màn hình", Description = "ASUS, MSI, LG, Samsung" },
                new Category { Id = 10, Name = "Fan - Fan tản nhiệt", Description = "Noctua, Corsair, Cooler Master" },
                new Category { Id = 11, Name = "Tản Nhiệt Nước", Description = "AIO Water Cooling" },
                new Category { Id = 12, Name = "Tản Nhiệt Khí", Description = "Air Cooler" },
                new Category { Id = 13, Name = "Keyboard - Bàn phím", Description = "Corsair, Razer, Logitech" },
                new Category { Id = 14, Name = "Mouse - Chuột", Description = "Logitech, Razer, SteelSeries" },
                new Category { Id = 15, Name = "Speaker - Loa", Description = "Logitech, Creative, Edifier" },
                new Category { Id = 16, Name = "Headphone - Tai nghe", Description = "HyperX, SteelSeries, Razer" }
            };
        }

        [HttpPost]
        public async Task<IActionResult> BuildAIRecommend([FromBody] BuildAIRequest request)
        {
            try
            {
                if (request == null || request.Budget <= 0)
                {
                    return Json(new { success = false, message = "Vui lòng nhập ngân sách hợp lệ." });
                }

                var usage = string.IsNullOrWhiteSpace(request.Usage) ? "Chung" : request.Usage;
                var special = request.SpecialRequirements ?? string.Empty;

                var response = _aiChatService.GenerateConfigFromData(request.Budget, usage, special);
                return Json(new { success = true, message = response });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xây dựng cấu hình AI");
                return Json(new
                {
                    success = false,
                    message = "Xin lỗi, hệ thống đang bận. Vui lòng thử lại sau hoặc liên hệ hotline."
                });
            }
        }

        public class BuildAIRequest
        {
            public decimal Budget { get; set; }
            public string Usage { get; set; } = string.Empty;
            public string SpecialRequirements { get; set; } = string.Empty;
        }
    }
}

