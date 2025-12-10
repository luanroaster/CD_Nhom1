using Microsoft.AspNetCore.Mvc;
using PCSTORE.Models;
using PCSTORE.Services;
using System.IO;

namespace PCSTORE.Controllers
{
    public class ProductsController : Controller
    {
        private readonly DataStoreService _dataStore;
        private readonly ILogger<ProductsController> _logger;

        public ProductsController(DataStoreService dataStore, ILogger<ProductsController> logger)
        {
            _dataStore = dataStore;
            _logger = logger;
        }

        // CPU - Bộ vi xử lý
        public IActionResult CPU(string? brand = null, string? priceRange = null, string? socket = null, string? sortBy = "name")
        {
            var viewModel = GetProductsByCategoryName("CPU");
            
            // Filter theo hãng
            if (!string.IsNullOrEmpty(brand))
            {
                viewModel.Products = viewModel.Products.Where(p => 
                    p.Name.Contains(brand, StringComparison.OrdinalIgnoreCase) ||
                    p.Description.Contains(brand, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }
            
            // Filter theo khoảng giá
            if (!string.IsNullOrEmpty(priceRange))
            {
                var ranges = priceRange.Split('-');
                if (ranges.Length == 2)
                {
                    if (decimal.TryParse(ranges[0], out decimal min) && decimal.TryParse(ranges[1], out decimal max))
                    {
                        viewModel.Products = viewModel.Products.Where(p => p.Price >= min && p.Price <= max).ToList();
                    }
                }
            }
            
            // Filter theo socket
            if (!string.IsNullOrEmpty(socket))
            {
                viewModel.Products = viewModel.Products.Where(p => 
                    p.Name.Contains(socket, StringComparison.OrdinalIgnoreCase) ||
                    p.Description.Contains(socket, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }
            
            // Sort
            viewModel.Products = sortBy switch
            {
                "price-asc" => viewModel.Products.OrderBy(p => p.Price).ToList(),
                "price-desc" => viewModel.Products.OrderByDescending(p => p.Price).ToList(),
                "name-desc" => viewModel.Products.OrderByDescending(p => p.Name).ToList(),
                _ => viewModel.Products.OrderBy(p => p.Name).ToList()
            };
            
            ViewBag.Brand = brand;
            ViewBag.PriceRange = priceRange;
            ViewBag.Socket = socket;
            ViewBag.SortBy = sortBy;
            
            return View("Index", viewModel);
        }

        // Mainboard - Bo mạch chủ
        public IActionResult Mainboard()
        {
            return View("Index", GetProductsByCategoryName("Mainboard"));
        }

        // RAM - Bộ nhớ trong
        public IActionResult RAM(string? brand = null, string? priceRange = null, string? sortBy = "name")
        {
            try
            {
                // Sử dụng CategoryId trực tiếp để đảm bảo tìm đúng
                _logger.LogInformation("[RAM] Bắt đầu lấy sản phẩm RAM với CategoryId = 3");
                
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();
                
                _logger.LogInformation($"[RAM] Tổng số sản phẩm trong database: {allProducts.Count}");
                _logger.LogInformation($"[RAM] Tổng số category: {categories.Count}");
                
                // Log tất cả RAM products
                var allRamProducts = allProducts.Where(p => p.CategoryId == 3).ToList();
                _logger.LogInformation($"[RAM] Tìm thấy {allRamProducts.Count} sản phẩm với CategoryId = 3");
                
                foreach (var ram in allRamProducts.Take(10))
                {
                    _logger.LogInformation($"[RAM] Product: Id={ram.Id}, Name={ram.Name.Substring(0, Math.Min(60, ram.Name.Length))}, CategoryId={ram.CategoryId}");
                }
                
                var viewModel = GetProductsByCategoryId(3);
                
                _logger.LogInformation($"[RAM] Sau GetProductsByCategoryId: {viewModel.Products.Count} sản phẩm");
                
                // Filter theo hãng
                if (!string.IsNullOrEmpty(brand))
                {
                    viewModel.Products = viewModel.Products.Where(p => 
                        p.Name.Contains(brand, StringComparison.OrdinalIgnoreCase) ||
                        p.Description.Contains(brand, StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                }
                
                // Filter theo khoảng giá
                if (!string.IsNullOrEmpty(priceRange))
                {
                    var ranges = priceRange.Split('-');
                    if (ranges.Length == 2)
                    {
                        if (decimal.TryParse(ranges[0], out decimal min) && decimal.TryParse(ranges[1], out decimal max))
                        {
                            viewModel.Products = viewModel.Products.Where(p => p.Price >= min && p.Price <= max).ToList();
                        }
                    }
                }
                
                // Sort
                viewModel.Products = sortBy switch
                {
                    "price-asc" => viewModel.Products.OrderBy(p => p.Price).ToList(),
                    "price-desc" => viewModel.Products.OrderByDescending(p => p.Price).ToList(),
                    "name-desc" => viewModel.Products.OrderByDescending(p => p.Name).ToList(),
                    _ => viewModel.Products.OrderBy(p => p.Name).ToList()
                };
                
                _logger.LogInformation($"[RAM] Cuối cùng: {viewModel.Products.Count} sản phẩm sau filter và sort");
                
                ViewBag.Brand = brand;
                ViewBag.PriceRange = priceRange;
                ViewBag.SortBy = sortBy;
                
                return View("Index", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RAM] Lỗi khi lấy sản phẩm RAM");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "RAM - Bộ nhớ",
                    Products = new List<Product>(),
                    CategoryId = 3
                });
            }
        }

        // GPU - Card đồ họa
        public IActionResult GPU()
        {
            // Sử dụng CategoryId trực tiếp để đảm bảo tìm đúng
            var viewModel = GetProductsByCategoryId(4);
            return View("Index", viewModel);
        }

        // PSU - Nguồn máy tính
        public IActionResult PSU()
        {
            // Sử dụng CategoryId trực tiếp để đảm bảo tìm đúng
            var viewModel = GetProductsByCategoryId(5);
            return View("Index", viewModel);
        }

        // SSD - Ổ cứng SSD
        public IActionResult SSD()
        {
            try
            {
                // Sử dụng CategoryId trực tiếp để đảm bảo tìm đúng
                _logger.LogInformation("[SSD] Bắt đầu lấy sản phẩm SSD với CategoryId = 7");
                
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();
                
                _logger.LogInformation($"[SSD] Tổng số sản phẩm trong database: {allProducts.Count}");
                _logger.LogInformation($"[SSD] Tổng số category: {categories.Count}");
                
                // Log tất cả SSD products
                var allSsdProducts = allProducts.Where(p => p.CategoryId == 7).ToList();
                _logger.LogInformation($"[SSD] Tìm thấy {allSsdProducts.Count} sản phẩm với CategoryId = 7");
                
                foreach (var ssd in allSsdProducts.Take(10))
                {
                    _logger.LogInformation($"[SSD] Product: Id={ssd.Id}, Name={ssd.Name.Substring(0, Math.Min(60, ssd.Name.Length))}, CategoryId={ssd.CategoryId}");
                }
                
                var viewModel = GetProductsByCategoryId(7);
                
                _logger.LogInformation($"[SSD] Sau GetProductsByCategoryId: {viewModel.Products.Count} sản phẩm");
                
                return View("Index", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SSD] Lỗi khi lấy sản phẩm SSD");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "SSD - Ổ cứng thể rắn",
                    Products = new List<Product>(),
                    CategoryId = 7
                });
            }
        }

        // HDD - Ổ cứng HDD
        public IActionResult HDD()
        {
            try
            {
                // Sử dụng CategoryId trực tiếp để đảm bảo tìm đúng
                _logger.LogInformation("[HDD] Bắt đầu lấy sản phẩm HDD với CategoryId = 8");
                
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();
                
                _logger.LogInformation($"[HDD] Tổng số sản phẩm trong database: {allProducts.Count}");
                _logger.LogInformation($"[HDD] Tổng số category: {categories.Count}");
                
                // Log tất cả HDD products
                var allHddProducts = allProducts.Where(p => p.CategoryId == 8).ToList();
                _logger.LogInformation($"[HDD] Tìm thấy {allHddProducts.Count} sản phẩm với CategoryId = 8");
                
                foreach (var hdd in allHddProducts.Take(10))
                {
                    _logger.LogInformation($"[HDD] Product: Id={hdd.Id}, Name={hdd.Name.Substring(0, Math.Min(60, hdd.Name.Length))}, CategoryId={hdd.CategoryId}");
                }
                
                var viewModel = GetProductsByCategoryId(8);
                
                _logger.LogInformation($"[HDD] Sau GetProductsByCategoryId: {viewModel.Products.Count} sản phẩm");
                
                return View("Index", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HDD] Lỗi khi lấy sản phẩm HDD");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "HDD - Ổ cứng HDD",
                    Products = new List<Product>(),
                    CategoryId = 8
                });
            }
        }

        // Case - Vỏ máy tính
        public IActionResult Case()
        {
            try
            {
                // Sử dụng CategoryId trực tiếp để đảm bảo tìm đúng
                _logger.LogInformation("[Case] Bắt đầu lấy sản phẩm Case với CategoryId = 6");
                
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();
                
                _logger.LogInformation($"[Case] Tổng số sản phẩm trong database: {allProducts.Count}");
                _logger.LogInformation($"[Case] Tổng số category: {categories.Count}");
                
                // Log tất cả Case products
                var allCaseProducts = allProducts.Where(p => p.CategoryId == 6).ToList();
                _logger.LogInformation($"[Case] Tìm thấy {allCaseProducts.Count} sản phẩm với CategoryId = 6");
                
                foreach (var c in allCaseProducts.Take(10))
                {
                    _logger.LogInformation($"[Case] Product: Id={c.Id}, Name={c.Name.Substring(0, Math.Min(60, c.Name.Length))}, CategoryId={c.CategoryId}");
                }
                
                var viewModel = GetProductsByCategoryId(6);
                
                _logger.LogInformation($"[Case] Sau GetProductsByCategoryId: {viewModel.Products.Count} sản phẩm");
                
                return View("Index", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Case] Lỗi khi lấy sản phẩm Case");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "Case - Vỏ máy tính",
                    Products = new List<Product>(),
                    CategoryId = 6
                });
            }
        }

        // Monitor - Màn hình
        public IActionResult Monitor()
        {
            try
            {
                // Sử dụng CategoryId trực tiếp để đảm bảo tìm đúng
                _logger.LogInformation("[Monitor] Bắt đầu lấy sản phẩm Monitor với CategoryId = 9");
                
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();
                
                _logger.LogInformation($"[Monitor] Tổng số sản phẩm trong database: {allProducts.Count}");
                _logger.LogInformation($"[Monitor] Tổng số category: {categories.Count}");
                
                // Log tất cả Monitor products
                var allMonitorProducts = allProducts.Where(p => p.CategoryId == 9).ToList();
                _logger.LogInformation($"[Monitor] Tìm thấy {allMonitorProducts.Count} sản phẩm với CategoryId = 9");
                
                foreach (var m in allMonitorProducts.Take(10))
                {
                    _logger.LogInformation($"[Monitor] Product: Id={m.Id}, Name={m.Name.Substring(0, Math.Min(60, m.Name.Length))}, CategoryId={m.CategoryId}");
                }
                
                var viewModel = GetProductsByCategoryId(9);
                
                _logger.LogInformation($"[Monitor] Sau GetProductsByCategoryId: {viewModel.Products.Count} sản phẩm");
                
                return View("Index", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Monitor] Lỗi khi lấy sản phẩm Monitor");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "Monitor - Màn hình",
                    Products = new List<Product>(),
                    CategoryId = 9
                });
            }
        }

        // Fan - Fan tản nhiệt
        public IActionResult Fan()
        {
            try
            {
                // Sử dụng CategoryId trực tiếp để đảm bảo tìm đúng
                _logger.LogInformation("[Fan] Bắt đầu lấy sản phẩm Fan tản nhiệt với CategoryId = 10");
                
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();
                
                _logger.LogInformation($"[Fan] Tổng số sản phẩm trong database: {allProducts.Count}");
                _logger.LogInformation($"[Fan] Tổng số category: {categories.Count}");
                
                // Log tất cả Fan products
                var allFanProducts = allProducts.Where(p => p.CategoryId == 10).ToList();
                _logger.LogInformation($"[Fan] Tìm thấy {allFanProducts.Count} sản phẩm với CategoryId = 10");
                
                foreach (var fan in allFanProducts.Take(10))
                {
                    _logger.LogInformation($"[Fan] Product: Id={fan.Id}, Name={fan.Name.Substring(0, Math.Min(60, fan.Name.Length))}, CategoryId={fan.CategoryId}");
                }
                
                var viewModel = GetProductsByCategoryId(10);
                
                _logger.LogInformation($"[Fan] Sau GetProductsByCategoryId: {viewModel.Products.Count} sản phẩm");
                
                return View("Index", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Fan] Lỗi khi lấy sản phẩm Fan tản nhiệt");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "Fan - Fan tản nhiệt",
                    Products = new List<Product>(),
                    CategoryId = 10
                });
            }
        }

        // WaterCooling - Tản nhiệt nước
        public IActionResult WaterCooling()
        {
            try
            {
                // Sử dụng CategoryId trực tiếp để đảm bảo tìm đúng
                _logger.LogInformation("[WaterCooling] Bắt đầu lấy sản phẩm Tản nhiệt nước với CategoryId = 11");
                
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();
                
                _logger.LogInformation($"[WaterCooling] Tổng số sản phẩm trong database: {allProducts.Count}");
                _logger.LogInformation($"[WaterCooling] Tổng số category: {categories.Count}");
                
                // Log tất cả WaterCooling products
                var allWaterCoolingProducts = allProducts.Where(p => p.CategoryId == 11).ToList();
                _logger.LogInformation($"[WaterCooling] Tìm thấy {allWaterCoolingProducts.Count} sản phẩm với CategoryId = 11");
                
                foreach (var wc in allWaterCoolingProducts.Take(10))
                {
                    _logger.LogInformation($"[WaterCooling] Product: Id={wc.Id}, Name={wc.Name.Substring(0, Math.Min(60, wc.Name.Length))}, CategoryId={wc.CategoryId}");
                }
                
                var viewModel = GetProductsByCategoryId(11);
                
                _logger.LogInformation($"[WaterCooling] Sau GetProductsByCategoryId: {viewModel.Products.Count} sản phẩm");
                
                return View("Index", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WaterCooling] Lỗi khi lấy sản phẩm Tản nhiệt nước");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "Tản nhiệt nước - Water Cooling",
                    Products = new List<Product>(),
                    CategoryId = 11
                });
            }
        }

        // AirCooling - Tản nhiệt khí
        public IActionResult AirCooling()
        {
            try
            {
                // Sử dụng CategoryId trực tiếp để đảm bảo tìm đúng
                _logger.LogInformation("[AirCooling] Bắt đầu lấy sản phẩm Tản nhiệt khí với CategoryId = 12");
                
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();
                
                _logger.LogInformation($"[AirCooling] Tổng số sản phẩm trong database: {allProducts.Count}");
                _logger.LogInformation($"[AirCooling] Tổng số category: {categories.Count}");
                
                // Log tất cả AirCooling products
                var allAirCoolingProducts = allProducts.Where(p => p.CategoryId == 12).ToList();
                _logger.LogInformation($"[AirCooling] Tìm thấy {allAirCoolingProducts.Count} sản phẩm với CategoryId = 12");
                
                foreach (var ac in allAirCoolingProducts.Take(10))
                {
                    _logger.LogInformation($"[AirCooling] Product: Id={ac.Id}, Name={ac.Name.Substring(0, Math.Min(60, ac.Name.Length))}, CategoryId={ac.CategoryId}");
                }
                
                var viewModel = GetProductsByCategoryId(12);
                
                _logger.LogInformation($"[AirCooling] Sau GetProductsByCategoryId: {viewModel.Products.Count} sản phẩm");
                
                return View("Index", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AirCooling] Lỗi khi lấy sản phẩm Tản nhiệt khí");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "Tản nhiệt khí - Air Cooling",
                    Products = new List<Product>(),
                    CategoryId = 12
                });
            }
        }

        // Keyboard - Bàn phím
        public IActionResult Keyboard()
        {
            try
            {
                _logger.LogInformation("[Keyboard] Bắt đầu lấy sản phẩm Keyboard với CategoryId = 13");
                
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();
                
                _logger.LogInformation($"[Keyboard] Tổng số sản phẩm trong database: {allProducts.Count}");
                _logger.LogInformation($"[Keyboard] Tổng số category: {categories.Count}");
                
                var allKeyboardProducts = allProducts.Where(p => p.CategoryId == 13).ToList();
                _logger.LogInformation($"[Keyboard] Tìm thấy {allKeyboardProducts.Count} sản phẩm với CategoryId = 13");
                
                var viewModel = GetProductsByCategoryId(13);
                
                _logger.LogInformation($"[Keyboard] Sau GetProductsByCategoryId: {viewModel.Products.Count} sản phẩm");
                
                return View("Index", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Keyboard] Lỗi khi lấy sản phẩm Keyboard");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "Keyboard - Bàn phím",
                    Products = new List<Product>(),
                    CategoryId = 13
                });
            }
        }

        // Mouse - Chuột
        public IActionResult Mouse()
        {
            try
            {
                _logger.LogInformation("[Mouse] Bắt đầu lấy sản phẩm Mouse với CategoryId = 14");
                
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();
                
                _logger.LogInformation($"[Mouse] Tổng số sản phẩm trong database: {allProducts.Count}");
                _logger.LogInformation($"[Mouse] Tổng số category: {categories.Count}");
                
                var allMouseProducts = allProducts.Where(p => p.CategoryId == 14).ToList();
                _logger.LogInformation($"[Mouse] Tìm thấy {allMouseProducts.Count} sản phẩm với CategoryId = 14");
                
                var viewModel = GetProductsByCategoryId(14);
                
                _logger.LogInformation($"[Mouse] Sau GetProductsByCategoryId: {viewModel.Products.Count} sản phẩm");
                
                return View("Index", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Mouse] Lỗi khi lấy sản phẩm Mouse");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "Mouse - Chuột",
                    Products = new List<Product>(),
                    CategoryId = 14
                });
            }
        }

        // Speaker - Loa
        public IActionResult Speaker()
        {
            try
            {
                _logger.LogInformation("[Speaker] Bắt đầu lấy sản phẩm Speaker với CategoryId = 15");
                
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();
                
                _logger.LogInformation($"[Speaker] Tổng số sản phẩm trong database: {allProducts.Count}");
                _logger.LogInformation($"[Speaker] Tổng số category: {categories.Count}");
                
                var allSpeakerProducts = allProducts.Where(p => p.CategoryId == 15).ToList();
                _logger.LogInformation($"[Speaker] Tìm thấy {allSpeakerProducts.Count} sản phẩm với CategoryId = 15");
                
                var viewModel = GetProductsByCategoryId(15);
                
                _logger.LogInformation($"[Speaker] Sau GetProductsByCategoryId: {viewModel.Products.Count} sản phẩm");
                
                return View("Index", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Speaker] Lỗi khi lấy sản phẩm Speaker");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "Speaker - Loa",
                    Products = new List<Product>(),
                    CategoryId = 15
                });
            }
        }

        // Headphone - Tai nghe
        public IActionResult Headphone()
        {
            try
            {
                _logger.LogInformation("[Headphone] Bắt đầu lấy sản phẩm Headphone với CategoryId = 16");
                
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();
                
                _logger.LogInformation($"[Headphone] Tổng số sản phẩm trong database: {allProducts.Count}");
                _logger.LogInformation($"[Headphone] Tổng số category: {categories.Count}");
                
                var allHeadphoneProducts = allProducts.Where(p => p.CategoryId == 16).ToList();
                _logger.LogInformation($"[Headphone] Tìm thấy {allHeadphoneProducts.Count} sản phẩm với CategoryId = 16");
                
                var viewModel = GetProductsByCategoryId(16);
                
                _logger.LogInformation($"[Headphone] Sau GetProductsByCategoryId: {viewModel.Products.Count} sản phẩm");
                
                return View("Index", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Headphone] Lỗi khi lấy sản phẩm Headphone");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "Headphone - Tai nghe",
                    Products = new List<Product>(),
                    CategoryId = 16
                });
            }
        }

        // PC - Hiển thị tất cả linh kiện PC
        public IActionResult PC(string? brand = null, string? priceRange = null, string? sortBy = "name")
        {
            try
            {
                _logger.LogInformation("[PC] Bắt đầu lấy tất cả linh kiện PC");
                
                var allProducts = _dataStore.GetAllProducts();
                // Linh kiện PC: CPU (1), Mainboard (2), RAM (3), GPU (4), PSU (5), SSD (7), HDD (8), Case (6), Fan (10), Water Cooling (11), Air Cooling (12)
                var pcComponentIds = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12 };
                var pcProducts = allProducts.Where(p => pcComponentIds.Contains(p.CategoryId)).ToList();
                
                _logger.LogInformation($"[PC] Tìm thấy {pcProducts.Count} sản phẩm linh kiện PC");
                
                // Filter theo hãng
                if (!string.IsNullOrEmpty(brand))
                {
                    pcProducts = pcProducts.Where(p =>
                        p.Name.Contains(brand, StringComparison.OrdinalIgnoreCase) ||
                        p.Description.Contains(brand, StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                }

                // Filter theo khoảng giá
                if (!string.IsNullOrEmpty(priceRange))
                {
                    var ranges = priceRange.Split('-');
                    if (ranges.Length == 2)
                    {
                        if (decimal.TryParse(ranges[0], out decimal min) && decimal.TryParse(ranges[1], out decimal max))
                        {
                            pcProducts = pcProducts.Where(p => p.Price >= min && p.Price <= max).ToList();
                        }
                    }
                }

                // Sort
                pcProducts = sortBy switch
                {
                    "price-asc" => pcProducts.OrderBy(p => p.Price).ToList(),
                    "price-desc" => pcProducts.OrderByDescending(p => p.Price).ToList(),
                    "name-desc" => pcProducts.OrderByDescending(p => p.Name).ToList(),
                    _ => pcProducts.OrderBy(p => p.Name).ToList()
                };
                
                ViewBag.Brand = brand;
                ViewBag.PriceRange = priceRange;
                ViewBag.SortBy = sortBy;
                
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "PC - Linh kiện máy tính",
                    Products = pcProducts,
                    CategoryId = 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PC] Lỗi khi lấy sản phẩm PC");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "PC - Linh kiện máy tính",
                    Products = new List<Product>(),
                    CategoryId = 0
                });
            }
        }

        // PC AI - PC với tính năng AI
        public IActionResult PCAI(string? brand = null, string? priceRange = null, string? sortBy = "name")
        {
            try
            {
                _logger.LogInformation("[PC AI] Bắt đầu lấy sản phẩm PC AI");
                
                var allProducts = _dataStore.GetAllProducts();
                // PC AI có thể là CPU có AI features (Intel Core Ultra, AMD Ryzen AI) hoặc GPU có AI (NVIDIA RTX với Tensor Cores)
                var pcAIProducts = allProducts.Where(p => 
                    (p.CategoryId == 1 && (p.Name.Contains("Core Ultra", StringComparison.OrdinalIgnoreCase) || 
                                           p.Name.Contains("Ryzen AI", StringComparison.OrdinalIgnoreCase) ||
                                           p.Name.Contains("AI", StringComparison.OrdinalIgnoreCase))) ||
                    (p.CategoryId == 4 && (p.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                                           p.Name.Contains("Tensor", StringComparison.OrdinalIgnoreCase))) ||
                    // Hoặc hiển thị tất cả linh kiện PC nếu không có sản phẩm AI cụ thể
                    (new[] { 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12 }.Contains(p.CategoryId))
                ).ToList();
                
                // Nếu không có sản phẩm AI cụ thể, hiển thị tất cả linh kiện PC
                if (pcAIProducts.Count == 0 || !pcAIProducts.Any(p => p.Name.Contains("AI", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase)))
                {
                    var pcComponentIds = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12 };
                    pcAIProducts = allProducts.Where(p => pcComponentIds.Contains(p.CategoryId)).ToList();
                }
                
                _logger.LogInformation($"[PC AI] Tìm thấy {pcAIProducts.Count} sản phẩm");
                
                // Filter theo hãng
                if (!string.IsNullOrEmpty(brand))
                {
                    pcAIProducts = pcAIProducts.Where(p =>
                        p.Name.Contains(brand, StringComparison.OrdinalIgnoreCase) ||
                        p.Description.Contains(brand, StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                }

                // Filter theo khoảng giá
                if (!string.IsNullOrEmpty(priceRange))
                {
                    var ranges = priceRange.Split('-');
                    if (ranges.Length == 2)
                    {
                        if (decimal.TryParse(ranges[0], out decimal min) && decimal.TryParse(ranges[1], out decimal max))
                        {
                            pcAIProducts = pcAIProducts.Where(p => p.Price >= min && p.Price <= max).ToList();
                        }
                    }
                }

                // Sort
                pcAIProducts = sortBy switch
                {
                    "price-asc" => pcAIProducts.OrderBy(p => p.Price).ToList(),
                    "price-desc" => pcAIProducts.OrderByDescending(p => p.Price).ToList(),
                    "name-desc" => pcAIProducts.OrderByDescending(p => p.Name).ToList(),
                    _ => pcAIProducts.OrderBy(p => p.Name).ToList()
                };
                
                ViewBag.Brand = brand;
                ViewBag.PriceRange = priceRange;
                ViewBag.SortBy = sortBy;
                
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "PC AI - Máy tính AI",
                    Products = pcAIProducts,
                    CategoryId = 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PC AI] Lỗi khi lấy sản phẩm PC AI");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "PC AI - Máy tính AI",
                    Products = new List<Product>(),
                    CategoryId = 0
                });
            }
        }

        // Linh kiện PC - Tương tự PC
        public IActionResult LinhKienPC(string? brand = null, string? priceRange = null, string? sortBy = "name")
        {
            // Sử dụng cùng logic với PC nhưng với tên category khác
            var result = PC(brand, priceRange, sortBy);
            if (result is ViewResult viewResult && viewResult.Model is ProductListViewModel viewModel)
            {
                viewModel.CategoryName = "Linh kiện PC";
            }
            return result;
        }

        // Laptop
        public IActionResult Laptop(string? brand = null, string? priceRange = null, string? sortBy = "name")
        {
            try
            {
                _logger.LogInformation("[Laptop] Bắt đầu lấy sản phẩm Laptop với CategoryId = 17");
                
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();
                
                _logger.LogInformation($"[Laptop] Tổng số sản phẩm trong database: {allProducts.Count}");
                _logger.LogInformation($"[Laptop] Tổng số category: {categories.Count}");
                
                var allLaptopProducts = allProducts.Where(p => p.CategoryId == 17).ToList();
                _logger.LogInformation($"[Laptop] Tìm thấy {allLaptopProducts.Count} sản phẩm với CategoryId = 17");
                
                var viewModel = GetProductsByCategoryId(17);
                
                // Filter theo hãng
                if (!string.IsNullOrEmpty(brand))
                {
                    viewModel.Products = viewModel.Products.Where(p =>
                        p.Name.Contains(brand, StringComparison.OrdinalIgnoreCase) ||
                        p.Description.Contains(brand, StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                }

                // Filter theo khoảng giá
                if (!string.IsNullOrEmpty(priceRange))
                {
                    var ranges = priceRange.Split('-');
                    if (ranges.Length == 2)
                    {
                        if (decimal.TryParse(ranges[0], out decimal min) && decimal.TryParse(ranges[1], out decimal max))
                        {
                            viewModel.Products = viewModel.Products.Where(p => p.Price >= min && p.Price <= max).ToList();
                        }
                    }
                }

                // Sort
                viewModel.Products = sortBy switch
                {
                    "price-asc" => viewModel.Products.OrderBy(p => p.Price).ToList(),
                    "price-desc" => viewModel.Products.OrderByDescending(p => p.Price).ToList(),
                    "name-desc" => viewModel.Products.OrderByDescending(p => p.Name).ToList(),
                    _ => viewModel.Products.OrderBy(p => p.Name).ToList()
                };
                
                ViewBag.Brand = brand;
                ViewBag.PriceRange = priceRange;
                ViewBag.SortBy = sortBy;
                
                _logger.LogInformation($"[Laptop] Sau filter và sort: {viewModel.Products.Count} sản phẩm");
                
                return View("Index", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Laptop] Lỗi khi lấy sản phẩm Laptop");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "Laptop - Máy tính xách tay",
                    Products = new List<Product>(),
                    CategoryId = 17
                });
            }
        }

        // Thiết bị văn phòng
        public IActionResult ThietBiVanPhong(string? brand = null, string? priceRange = null, string? sortBy = "name")
        {
            try
            {
                _logger.LogInformation("[ThietBiVanPhong] Bắt đầu lấy sản phẩm Thiết bị văn phòng với CategoryId = 18");
                
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();
                
                _logger.LogInformation($"[ThietBiVanPhong] Tổng số sản phẩm trong database: {allProducts.Count}");
                _logger.LogInformation($"[ThietBiVanPhong] Tổng số category: {categories.Count}");
                
                // Tìm sản phẩm thiết bị văn phòng: Printer, Scanner, hoặc CategoryId = 18
                var officeProducts = allProducts.Where(p => 
                    p.CategoryId == 18 ||
                    p.Name.Contains("Printer", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("Máy in", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("Scanner", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("Máy scan", StringComparison.OrdinalIgnoreCase)
                ).ToList();
                
                _logger.LogInformation($"[ThietBiVanPhong] Tìm thấy {officeProducts.Count} sản phẩm");
                
                // Filter theo hãng
                if (!string.IsNullOrEmpty(brand))
                {
                    officeProducts = officeProducts.Where(p =>
                        p.Name.Contains(brand, StringComparison.OrdinalIgnoreCase) ||
                        p.Description.Contains(brand, StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                }

                // Filter theo khoảng giá
                if (!string.IsNullOrEmpty(priceRange))
                {
                    var ranges = priceRange.Split('-');
                    if (ranges.Length == 2)
                    {
                        if (decimal.TryParse(ranges[0], out decimal min) && decimal.TryParse(ranges[1], out decimal max))
                        {
                            officeProducts = officeProducts.Where(p => p.Price >= min && p.Price <= max).ToList();
                        }
                    }
                }

                // Sort
                officeProducts = sortBy switch
                {
                    "price-asc" => officeProducts.OrderBy(p => p.Price).ToList(),
                    "price-desc" => officeProducts.OrderByDescending(p => p.Price).ToList(),
                    "name-desc" => officeProducts.OrderByDescending(p => p.Name).ToList(),
                    _ => officeProducts.OrderBy(p => p.Name).ToList()
                };
                
                ViewBag.Brand = brand;
                ViewBag.PriceRange = priceRange;
                ViewBag.SortBy = sortBy;
                
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "Thiết bị văn phòng",
                    Products = officeProducts,
                    CategoryId = 18
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ThietBiVanPhong] Lỗi khi lấy sản phẩm Thiết bị văn phòng");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "Thiết bị văn phòng",
                    Products = new List<Product>(),
                    CategoryId = 18
                });
            }
        }

        // Phím chuột ghế gear - Tổng hợp Keyboard, Mouse, Speaker, Headphone
        public IActionResult PhimChuotGheGear(string? brand = null, string? priceRange = null, string? sortBy = "name")
        {
            try
            {
                _logger.LogInformation("[PhimChuotGheGear] Bắt đầu lấy sản phẩm Phím chuột ghế gear");
                
                var allProducts = _dataStore.GetAllProducts();
                // Keyboard (13), Mouse (14), Speaker (15), Headphone (16)
                var gearCategoryIds = new[] { 13, 14, 15, 16 };
                var gearProducts = allProducts.Where(p => gearCategoryIds.Contains(p.CategoryId)).ToList();
                
                _logger.LogInformation($"[PhimChuotGheGear] Tìm thấy {gearProducts.Count} sản phẩm");
                
                // Filter theo hãng
                if (!string.IsNullOrEmpty(brand))
                {
                    gearProducts = gearProducts.Where(p =>
                        p.Name.Contains(brand, StringComparison.OrdinalIgnoreCase) ||
                        p.Description.Contains(brand, StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                }

                // Filter theo khoảng giá
                if (!string.IsNullOrEmpty(priceRange))
                {
                    var ranges = priceRange.Split('-');
                    if (ranges.Length == 2)
                    {
                        if (decimal.TryParse(ranges[0], out decimal min) && decimal.TryParse(ranges[1], out decimal max))
                        {
                            gearProducts = gearProducts.Where(p => p.Price >= min && p.Price <= max).ToList();
                        }
                    }
                }

                // Sort
                gearProducts = sortBy switch
                {
                    "price-asc" => gearProducts.OrderBy(p => p.Price).ToList(),
                    "price-desc" => gearProducts.OrderByDescending(p => p.Price).ToList(),
                    "name-desc" => gearProducts.OrderByDescending(p => p.Name).ToList(),
                    _ => gearProducts.OrderBy(p => p.Name).ToList()
                };
                
                ViewBag.Brand = brand;
                ViewBag.PriceRange = priceRange;
                ViewBag.SortBy = sortBy;
                
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "Phím chuột ghế gear",
                    Products = gearProducts,
                    CategoryId = 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PhimChuotGheGear] Lỗi khi lấy sản phẩm Phím chuột ghế gear");
                return View("Index", new ProductListViewModel
                {
                    CategoryName = "Phím chuột ghế gear",
                    Products = new List<Product>(),
                    CategoryId = 0
                });
            }
        }

        private ProductListViewModel GetProductsByCategoryId(int categoryId)
        {
            try
            {
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();
                
                _logger.LogInformation($"[DEBUG] CategoryId {categoryId}: Tổng số sản phẩm: {allProducts.Count}, Tổng số category: {categories.Count}");
                
                // Log tất cả category để debug
                foreach (var cat in categories)
                {
                    _logger.LogInformation($"[DEBUG] Category: Id={cat.Id}, Name={cat.Name}");
                }
                
                var category = categories.FirstOrDefault(c => c.Id == categoryId);
                var products = allProducts.Where(p => p.CategoryId == categoryId).ToList();
                
                _logger.LogInformation($"[DEBUG] CategoryId {categoryId}: Tìm thấy {products.Count} sản phẩm, Category: {category?.Name ?? "Không tìm thấy"}");
                
                // Log một vài sản phẩm để debug
                if (allProducts.Count > 0)
                {
                    var sampleProducts = allProducts.Take(5).ToList();
                    foreach (var p in sampleProducts)
                    {
                        _logger.LogInformation($"[DEBUG] Sample Product: Id={p.Id}, Name={p.Name.Substring(0, Math.Min(50, p.Name.Length))}, CategoryId={p.CategoryId}");
                    }
                }
                
                // Nếu không có sản phẩm, thử tìm bằng cách khác (cho tất cả các category đã có dữ liệu)
                var categoriesWithData = new[] { 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
                if (products.Count == 0 && category != null && categoriesWithData.Contains(categoryId))
                {
                    // Thử tìm bằng tên category
                    products = allProducts.Where(p => 
                        p.CategoryId == categoryId || 
                        (category.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase) && p.Name.Contains("Card", StringComparison.OrdinalIgnoreCase)) ||
                        (category.Name.Contains("PSU", StringComparison.OrdinalIgnoreCase) && p.Name.Contains("Nguồn", StringComparison.OrdinalIgnoreCase)) ||
                        (category.Name.Contains("RAM", StringComparison.OrdinalIgnoreCase) && p.Name.Contains("RAM", StringComparison.OrdinalIgnoreCase)) ||
                        (category.Name.Contains("SSD", StringComparison.OrdinalIgnoreCase) && p.Name.Contains("SSD", StringComparison.OrdinalIgnoreCase)) ||
                        (category.Name.Contains("HDD", StringComparison.OrdinalIgnoreCase) && p.Name.Contains("HDD", StringComparison.OrdinalIgnoreCase)) ||
                        (category.Name.Contains("Case", StringComparison.OrdinalIgnoreCase) && (p.Name.Contains("Case", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Vỏ", StringComparison.OrdinalIgnoreCase))) ||
                        (category.Name.Contains("Monitor", StringComparison.OrdinalIgnoreCase) && (p.Name.Contains("Monitor", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Màn hình", StringComparison.OrdinalIgnoreCase))) ||
                        (category.Name.Contains("Fan", StringComparison.OrdinalIgnoreCase) && (p.Name.Contains("Fan", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Quạt", StringComparison.OrdinalIgnoreCase))) ||
                        (category.Name.Contains("Tản nhiệt nước", StringComparison.OrdinalIgnoreCase) && p.Name.Contains("Tản nhiệt nước", StringComparison.OrdinalIgnoreCase)) ||
                        (category.Name.Contains("Tản nhiệt khí", StringComparison.OrdinalIgnoreCase) && p.Name.Contains("Tản nhiệt khí", StringComparison.OrdinalIgnoreCase)) ||
                        (category.Name.Contains("Keyboard", StringComparison.OrdinalIgnoreCase) && (p.Name.Contains("Keyboard", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Bàn phím", StringComparison.OrdinalIgnoreCase))) ||
                        (category.Name.Contains("Mouse", StringComparison.OrdinalIgnoreCase) && (p.Name.Contains("Mouse", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Chuột", StringComparison.OrdinalIgnoreCase))) ||
                        (category.Name.Contains("Speaker", StringComparison.OrdinalIgnoreCase) && (p.Name.Contains("Speaker", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Loa", StringComparison.OrdinalIgnoreCase))) ||
                        (category.Name.Contains("Headphone", StringComparison.OrdinalIgnoreCase) && (p.Name.Contains("Headphone", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Tai nghe", StringComparison.OrdinalIgnoreCase)))
                    ).ToList();
                    _logger.LogInformation($"[DEBUG] Sau khi tìm lại: {products.Count} sản phẩm");
                    
                    // Nếu vẫn không tìm thấy, log warning
                    if (products.Count == 0)
                    {
                        _logger.LogWarning($"[DEBUG] KHÔNG TÌM THẤY SẢN PHẨM NÀO CHO CategoryId {categoryId} ({category.Name})");
                    }
                }
                
                // Nếu không có sản phẩm thật thì chỉ log cảnh báo, KHÔNG hiển thị dữ liệu mẫu
                if (products.Count == 0)
                {
                    _logger.LogWarning($"[DEBUG] Không tìm thấy sản phẩm cho CategoryId {categoryId}, không hiển thị sample products");
                    
                    // Ghi log gợi ý thêm dữ liệu cho một số category chính
                    if (categoryId == 3)
                    {
                        _logger.LogWarning("[RAM] Không tìm thấy RAM, có thể cần thêm lại RAM vào database");
                    }
                    else if (categoryId == 6)
                    {
                        _logger.LogWarning("[Case] Không tìm thấy Case, có thể cần thêm lại Case vào database");
                    }
                    else if (categoryId == 7)
                    {
                        _logger.LogWarning("[SSD] Không tìm thấy SSD, có thể cần thêm lại SSD vào database");
                    }
                    else if (categoryId == 8)
                    {
                        _logger.LogWarning("[HDD] Không tìm thấy HDD, có thể cần thêm lại HDD vào database");
                    }
                    else if (categoryId == 9)
                    {
                        _logger.LogWarning("[Monitor] Không tìm thấy Monitor, có thể cần thêm lại Monitor vào database");
                    }
                    else if (categoryId == 10)
                    {
                        _logger.LogWarning("[Fan] Không tìm thấy Fan tản nhiệt, có thể cần thêm lại Fan tản nhiệt vào database");
                    }
                    else if (categoryId == 11)
                    {
                        _logger.LogWarning("[WaterCooling] Không tìm thấy Tản nhiệt nước, có thể cần thêm lại Tản nhiệt nước vào database");
                    }
                    else if (categoryId == 12)
                    {
                        _logger.LogWarning("[AirCooling] Không tìm thấy Tản nhiệt khí, có thể cần thêm lại Tản nhiệt khí vào database");
                    }
                }
                
                // Đảm bảo category name được set đúng, đặc biệt cho Monitor
                string categoryName = category?.Name ?? $"Category {categoryId}";
                if (categoryId == 9 && category == null)
                {
                    categoryName = "Monitor - Màn hình";
                }
                
                return new ProductListViewModel
                {
                    CategoryName = categoryName,
                    Products = products,
                    CategoryId = categoryId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi lấy sản phẩm cho CategoryId {categoryId}");
                return new ProductListViewModel
                {
                    CategoryName = $"Category {categoryId}",
                    Products = new List<Product>(),
                    CategoryId = categoryId
                };
            }
        }

        private ProductListViewModel GetProductsByCategoryName(string categoryName)
        {
            try
            {
                // Đọc từ DataStore
                var allProducts = _dataStore.GetAllProducts();
                var categories = _dataStore.GetAllCategories();

                // Tìm category ID dựa trên tên - tìm chính xác hơn
                var category = categories.FirstOrDefault(c => 
                {
                    var cName = c.Name.ToLower();
                    var searchName = categoryName.ToLower();
                    
                    // Tìm trực tiếp
                    if (cName.Contains(searchName) || searchName.Contains(cName))
                        return true;
                    
                    // Special cases
                    if (searchName == "ram" && (cName.Contains("ram") || cName.Contains("bộ nhớ")))
                        return true;
                    if (searchName == "gpu" && (cName.Contains("gpu") || cName.Contains("card")))
                        return true;
                    if (searchName == "psu" && (cName.Contains("psu") || cName.Contains("nguồn")))
                        return true;
                    if (searchName == "vga" && (cName.Contains("gpu") || cName.Contains("card")))
                        return true;
                    if (searchName == "nguồn" && (cName.Contains("psu") || cName.Contains("nguồn")))
                        return true;
                    
                    return false;
                });

                // Nếu không tìm thấy, sử dụng mapping mặc định
                int categoryId = category?.Id ?? GetCategoryIdByName(categoryName);

                var products = allProducts.Where(p => p.CategoryId == categoryId).ToList();

                _logger.LogInformation($"[GetProductsByCategoryName] CategoryName: {categoryName}, CategoryId: {categoryId}, Found {products.Count} products (chỉ hiển thị dữ liệu thật, không dùng sample)");

                return new ProductListViewModel
                {
                    CategoryName = category?.Name ?? categoryName,
                    Products = products,
                    CategoryId = categoryId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi lấy sản phẩm cho {categoryName}");
                // Nếu lỗi, trả về danh sách rỗng (không dùng dữ liệu mẫu)
                return new ProductListViewModel
                {
                    CategoryName = categoryName,
                    Products = new List<Product>(),
                    CategoryId = GetCategoryIdByName(categoryName)
                };
            }
        }

        private int GetCategoryIdByName(string categoryName)
        {
            return categoryName.ToLower() switch
            {
                "cpu" => 1,
                "mainboard" or "main" => 2,
                "ram" => 3,
                "gpu" or "vga" => 4,
                "psu" => 5,
                "ssd" => 7,
                "hdd" => 8,
                "case" or "vỏ máy" => 6,
                "màn hình" or "monitor" => 9,
                "fan" => 10,
                "tản nhiệt nước" or "watercooling" => 11,
                "tản nhiệt khí" or "aircooling" => 12,
                "bàn phím" or "keyboard" => 13,
                "chuột" or "mouse" => 14,
                "loa" or "speaker" => 15,
                "tai nghe" or "headphone" => 16,
                _ => 1
            };
        }

        private List<Product> GetSampleProducts(string categoryName, int categoryId)
        {
            return categoryName.ToLower() switch
            {
                "cpu" => new List<Product>
                {
                    new Product { Id = 1, Name = "CPU Intel Core i9-13900K", Price = 12990000, CategoryId = categoryId, Stock = 10, ImageUrl = "https://via.placeholder.com/300x300?text=Intel+i9" },
                    new Product { Id = 2, Name = "CPU AMD Ryzen 9 7950X", Price = 11990000, CategoryId = categoryId, Stock = 8, ImageUrl = "https://via.placeholder.com/300x300?text=AMD+Ryzen+9" }
                },
                "mainboard" => new List<Product>
                {
                    new Product { Id = 3, Name = "Mainboard ASUS ROG Z790", Price = 8990000, CategoryId = categoryId, Stock = 5, ImageUrl = "https://via.placeholder.com/300x300?text=ASUS+Z790" },
                    new Product { Id = 4, Name = "Mainboard MSI B650", Price = 5990000, CategoryId = categoryId, Stock = 7, ImageUrl = "https://via.placeholder.com/300x300?text=MSI+B650" }
                },
                "ram" => new List<Product>
                {
                    new Product { Id = 5, Name = "RAM DDR5 32GB Corsair Vengeance", Price = 3990000, CategoryId = categoryId, Stock = 15, ImageUrl = "https://via.placeholder.com/300x300?text=RAM+32GB" },
                    new Product { Id = 6, Name = "RAM DDR4 16GB Kingston Fury", Price = 1990000, CategoryId = categoryId, Stock = 20, ImageUrl = "https://via.placeholder.com/300x300?text=RAM+16GB" }
                },
                "gpu" or "vga" => new List<Product>
                {
                    new Product { Id = 7, Name = "VGA NVIDIA RTX 4090", Price = 45990000, CategoryId = categoryId, Stock = 3, ImageUrl = "https://via.placeholder.com/300x300?text=RTX+4090" },
                    new Product { Id = 8, Name = "VGA AMD RX 7900 XTX", Price = 24990000, CategoryId = categoryId, Stock = 5, ImageUrl = "https://via.placeholder.com/300x300?text=RX+7900" }
                },
                "psu" => new List<Product>
                {
                    new Product { Id = 9, Name = "PSU Corsair RM850x 850W", Price = 3490000, CategoryId = categoryId, Stock = 12, ImageUrl = "https://via.placeholder.com/300x300?text=PSU+850W" },
                    new Product { Id = 10, Name = "PSU Seasonic Focus GX-750", Price = 2990000, CategoryId = categoryId, Stock = 10, ImageUrl = "https://via.placeholder.com/300x300?text=PSU+750W" }
                },
                "ssd" => new List<Product>
                {
                    new Product { Id = 11, Name = "SSD Samsung 980 PRO 1TB", Price = 2990000, CategoryId = categoryId, Stock = 15, ImageUrl = "https://via.placeholder.com/300x300?text=SSD+1TB" },
                    new Product { Id = 12, Name = "SSD WD Black SN850X 2TB", Price = 5990000, CategoryId = categoryId, Stock = 8, ImageUrl = "https://via.placeholder.com/300x300?text=SSD+2TB" }
                },
                "hdd" => new List<Product>
                {
                    new Product { Id = 13, Name = "HDD Seagate Barracuda 2TB", Price = 1990000, CategoryId = categoryId, Stock = 20, ImageUrl = "https://via.placeholder.com/300x300?text=HDD+2TB" },
                    new Product { Id = 14, Name = "HDD WD Blue 4TB", Price = 2990000, CategoryId = categoryId, Stock = 12, ImageUrl = "https://via.placeholder.com/300x300?text=HDD+4TB" }
                },
                "case" => new List<Product>
                {
                    new Product { Id = 15, Name = "Case Corsair 4000D Airflow", Price = 2990000, CategoryId = categoryId, Stock = 10, ImageUrl = "https://via.placeholder.com/300x300?text=Case+Corsair" },
                    new Product { Id = 16, Name = "Case NZXT H7 Flow", Price = 3990000, CategoryId = categoryId, Stock = 8, ImageUrl = "https://via.placeholder.com/300x300?text=Case+NZXT" }
                },
                "monitor" or "màn hình" => new List<Product>
                {
                    new Product { Id = 17, Name = "Màn hình ASUS ROG Swift 27\" 4K", Price = 12990000, CategoryId = categoryId, Stock = 5, ImageUrl = "https://via.placeholder.com/300x300?text=Monitor+27" },
                    new Product { Id = 18, Name = "Màn hình LG UltraGear 24\" FHD", Price = 4990000, CategoryId = categoryId, Stock = 10, ImageUrl = "https://via.placeholder.com/300x300?text=Monitor+24" }
                },
                "fan" => new List<Product>
                {
                    new Product { Id = 19, Name = "Fan Noctua NF-A12x25", Price = 890000, CategoryId = categoryId, Stock = 30, ImageUrl = "https://via.placeholder.com/300x300?text=Fan+Noctua" },
                    new Product { Id = 20, Name = "Fan Corsair LL120 RGB", Price = 1290000, CategoryId = categoryId, Stock = 25, ImageUrl = "https://via.placeholder.com/300x300?text=Fan+RGB" }
                },
                "watercooling" or "tản nhiệt nước" => new List<Product>
                {
                    new Product { Id = 21, Name = "AIO Cooler Master MasterLiquid 360", Price = 3990000, CategoryId = categoryId, Stock = 8, ImageUrl = "https://via.placeholder.com/300x300?text=AIO+360" },
                    new Product { Id = 22, Name = "AIO NZXT Kraken X73", Price = 4990000, CategoryId = categoryId, Stock = 6, ImageUrl = "https://via.placeholder.com/300x300?text=AIO+NZXT" }
                },
                "aircooling" or "tản nhiệt khí" => new List<Product>
                {
                    new Product { Id = 23, Name = "CPU Cooler Noctua NH-D15", Price = 2990000, CategoryId = categoryId, Stock = 12, ImageUrl = "https://via.placeholder.com/300x300?text=Air+Cooler" },
                    new Product { Id = 24, Name = "CPU Cooler Be Quiet! Dark Rock Pro 4", Price = 2490000, CategoryId = categoryId, Stock = 10, ImageUrl = "https://via.placeholder.com/300x300?text=Dark+Rock" }
                },
                "keyboard" or "bàn phím" => new List<Product>
                {
                    new Product { Id = 25, Name = "Bàn phím Corsair K70 RGB", Price = 3990000, CategoryId = categoryId, Stock = 15, ImageUrl = "https://via.placeholder.com/300x300?text=Keyboard+Corsair" },
                    new Product { Id = 26, Name = "Bàn phím Razer BlackWidow V3", Price = 3490000, CategoryId = categoryId, Stock = 12, ImageUrl = "https://via.placeholder.com/300x300?text=Keyboard+Razer" }
                },
                "mouse" or "chuột" => new List<Product>
                {
                    new Product { Id = 27, Name = "Chuột Logitech G Pro X Superlight", Price = 2990000, CategoryId = categoryId, Stock = 20, ImageUrl = "https://via.placeholder.com/300x300?text=Mouse+Logitech" },
                    new Product { Id = 28, Name = "Chuột Razer DeathAdder V3", Price = 2490000, CategoryId = categoryId, Stock = 18, ImageUrl = "https://via.placeholder.com/300x300?text=Mouse+Razer" }
                },
                "speaker" or "loa" => new List<Product>
                {
                    new Product { Id = 29, Name = "Loa Logitech Z623 2.1", Price = 2990000, CategoryId = categoryId, Stock = 10, ImageUrl = "https://via.placeholder.com/300x300?text=Speaker+Logitech" },
                    new Product { Id = 30, Name = "Loa Creative T15 Wireless", Price = 1990000, CategoryId = categoryId, Stock = 15, ImageUrl = "https://via.placeholder.com/300x300?text=Speaker+Creative" }
                },
                "headphone" or "tai nghe" => new List<Product>
                {
                    new Product { Id = 31, Name = "Tai nghe HyperX Cloud II", Price = 2490000, CategoryId = categoryId, Stock = 20, ImageUrl = "https://via.placeholder.com/300x300?text=Headphone+HyperX" },
                    new Product { Id = 32, Name = "Tai nghe SteelSeries Arctis 7", Price = 3990000, CategoryId = categoryId, Stock = 12, ImageUrl = "https://via.placeholder.com/300x300?text=Headphone+SteelSeries" }
                },
                _ => new List<Product>
                {
                    new Product { Id = 999, Name = $"Sản phẩm {categoryName} mẫu", Price = 1000000, CategoryId = categoryId, Stock = 5, ImageUrl = "https://via.placeholder.com/300x300?text=Sample" }
                }
            };
        }
    }

    public class ProductListViewModel
    {
        public string CategoryName { get; set; } = string.Empty;
        public List<Product> Products { get; set; } = new List<Product>();
        public int CategoryId { get; set; }
    }
}

