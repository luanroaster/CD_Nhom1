using Microsoft.AspNetCore.Mvc;
using PCSTORE.Services;
using PCSTORE.Models;
using PCSTORE.Filters;
using System.IO;

namespace PCSTORE.Controllers
{
    [AdminAuthorize]
    public class AdminController : Controller
    {
        private readonly ExcelService _excelService;
        private readonly DataStoreService _dataStore;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<AdminController> _logger;
        private readonly ImageSearchService _imageSearchService;

        public AdminController(ExcelService excelService, DataStoreService dataStore, IWebHostEnvironment environment, ILogger<AdminController> logger, ImageSearchService imageSearchService)
        {
            _excelService = excelService;
            _dataStore = dataStore;
            _environment = environment;
            _logger = logger;
            _imageSearchService = imageSearchService;
        }

        // Trang quản lý sản phẩm
        public IActionResult Products()
        {
            var products = _dataStore.GetAllProducts();
            var categories = _dataStore.GetAllCategories();
            
            ViewBag.Products = products;
            ViewBag.Categories = categories;
            ViewBag.ProductCount = products.Count;
            
            return View();
        }

        // Trang chọn danh mục để thêm sản phẩm
        [HttpGet]
        public IActionResult SelectCategory()
        {
            var categories = _dataStore.GetAllCategories();
            ViewBag.Categories = categories;
            return View();
        }

        // Trang thêm/sửa sản phẩm
        [HttpGet]
        public IActionResult ProductForm(int? id, int? categoryId)
        {
            var categories = _dataStore.GetAllCategories();
            ViewBag.Categories = categories;

            if (id.HasValue && id.Value > 0)
            {
                var product = _dataStore.GetProductById(id.Value);
                if (product == null)
                {
                    return RedirectToAction("Products");
                }
                return View(product);
            }

            // Nếu có categoryId, tạo product mới với categoryId đã chọn
            var newProduct = new Product();
            if (categoryId.HasValue && categoryId.Value > 0)
            {
                newProduct.CategoryId = categoryId.Value;
            }
            return View(newProduct);
        }

        // Xử lý thêm/sửa sản phẩm
        [HttpPost]
        public IActionResult ProductForm(Product product, List<IFormFile>? images)
        {
            // Chuẩn hóa giá từ form (cho phép nhập 25.000.000 hoặc 25,000,000)
            try
            {
                var rawPrice = Request.Form["Price"].ToString();
                if (!string.IsNullOrWhiteSpace(rawPrice))
                {
                    rawPrice = rawPrice.Replace(".", "").Replace(",", "");
                    if (decimal.TryParse(rawPrice, out var parsedPrice))
                    {
                        product.Price = parsedPrice;
                    }
                }

                var rawOldPrice = Request.Form["OldPrice"].ToString();
                if (!string.IsNullOrWhiteSpace(rawOldPrice))
                {
                    rawOldPrice = rawOldPrice.Replace(".", "").Replace(",", "");
                    if (decimal.TryParse(rawOldPrice, out var parsedOldPrice))
                    {
                        product.OldPrice = parsedOldPrice;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi khi parse giá sản phẩm từ form, dùng lại giá mặc định trong model.");
            }

            // Bỏ qua ModelState phức tạp, chỉ kiểm tra đơn giản 3 trường bắt buộc
            if (string.IsNullOrWhiteSpace(product.Name) || product.Price <= 0 || product.CategoryId <= 0)
            {
                TempData["Error"] = "Vui lòng nhập Tên, Giá (>0) và chọn Danh mục.";
                ViewBag.Categories = _dataStore.GetAllCategories();
                return View(product);
            }

            try
            {
                // Xử lý upload hình ảnh nếu có
                if (images != null && images.Count > 0)
                {
                    try
                    {
                        var webRoot = _environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                        var uploadDir = Path.Combine(webRoot, "uploads", "products");
                        if (!Directory.Exists(uploadDir))
                        {
                            Directory.CreateDirectory(uploadDir);
                        }

                        var savedPaths = new List<string>();

                        foreach (var file in images)
                        {
                            if (file != null && file.Length > 0)
                            {
                                var ext = Path.GetExtension(file.FileName);
                                var fileName = $"{Guid.NewGuid()}{ext}";
                                var filePath = Path.Combine(uploadDir, fileName);

                                using (var stream = new FileStream(filePath, FileMode.Create))
                                {
                                    file.CopyTo(stream);
                                }

                                var relativePath = $"/uploads/products/{fileName}";
                                savedPaths.Add(relativePath);
                            }
                        }

                        if (savedPaths.Count > 0)
                        {
                            // Ảnh đầu tiên làm ảnh chính
                            product.ImageUrl = savedPaths[0];
                            // Các ảnh còn lại lưu trong ExtraImages (ngăn cách bằng ';')
                            if (savedPaths.Count > 1)
                            {
                                product.ExtraImages = string.Join(";", savedPaths.Skip(1));
                            }
                        }
                    }
                    catch (Exception exUpload)
                    {
                        // Không cho phép lỗi upload ảnh làm hỏng việc lưu sản phẩm
                        _logger.LogError(exUpload, "Lỗi khi upload hình ảnh sản phẩm, sẽ lưu sản phẩm mà không cập nhật ảnh mới.");
                        TempData["Error"] = "Upload hình ảnh thất bại, sản phẩm vẫn được lưu (không hoặc thiếu ảnh).";
                    }
                }

                if (product.Id == 0)
                {
                    _dataStore.AddProduct(product);
                    TempData["Success"] = "Đã thêm sản phẩm thành công!";
                }
                else
                {
                    _dataStore.UpdateProduct(product);
                    TempData["Success"] = "Đã cập nhật sản phẩm thành công!";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu sản phẩm");
                TempData["Error"] = $"Lỗi: {ex.Message}";
                ViewBag.Categories = _dataStore.GetAllCategories();
                return View(product);
            }

            return RedirectToAction("Products");
        }

        // Xóa sản phẩm
        [HttpPost]
        public IActionResult DeleteProduct(int id)
        {
            try
            {
                _dataStore.DeleteProduct(id);
                TempData["Success"] = "Đã xóa sản phẩm thành công!";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa sản phẩm");
                TempData["Error"] = $"Lỗi: {ex.Message}";
            }

            return RedirectToAction("Products");
        }
        
        // Xóa toàn bộ sản phẩm
        [HttpPost]
        public IActionResult ClearAllProducts()
        {
            try
            {
                _dataStore.ClearAllProducts();
                TempData["Success"] = "Đã xóa toàn bộ sản phẩm thành công!";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa toàn bộ sản phẩm");
                TempData["Error"] = $"Lỗi: {ex.Message}";
            }

            return RedirectToAction("Products");
        }

        // Import từ Excel
        public IActionResult ImportData()
        {
            var excelPath = Path.Combine(_environment.ContentRootPath, "Data", "DATABASE.xlsx");
            
            try
            {
                if (System.IO.File.Exists(excelPath))
                {
                    var products = _excelService.ReadProductsFromExcel(excelPath);
                    var categories = _excelService.ReadCategoriesFromExcel(excelPath);

                    // Hình ảnh sẽ được cập nhật tự động khi khởi động ứng dụng
                    // Tạm thời sử dụng placeholder
                    foreach (var product in products)
                    {
                        if (string.IsNullOrEmpty(product.ImageUrl) || product.ImageUrl.Contains("placeholder"))
                        {
                            var shortName = product.Name.Length > 20 ? product.Name.Substring(0, 20) : product.Name;
                            var encodedName = Uri.EscapeDataString(shortName);
                            product.ImageUrl = $"https://via.placeholder.com/300x300?text={encodedName}";
                        }
                    }

                    // Import vào DataStore (sẽ tự động lưu)
                    _dataStore.ImportFromExcel(products, categories);
                    
                    ViewBag.Success = $"Đã import thành công {products.Count} sản phẩm và {categories.Count} danh mục từ Excel.";
                }
                else
                {
                    ViewBag.Error = $"Không tìm thấy file Excel tại: {excelPath}";
                }

                var allProducts = _dataStore.GetAllProducts();
                var allCategories = _dataStore.GetAllCategories();
                
                ViewBag.Products = allProducts;
                ViewBag.Categories = allCategories;
                ViewBag.ProductCount = allProducts.Count;
                ViewBag.CategoryCount = allCategories.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi import dữ liệu");
                ViewBag.Error = $"Lỗi: {ex.Message}";
            }

            return View();
        }

        // Import từ products.json
        [HttpPost]
        public IActionResult ImportFromJson()
        {
            try
            {
                var jsonPath = Path.Combine(_environment.ContentRootPath, "Data", "products.json");
                
                if (!System.IO.File.Exists(jsonPath))
                {
                    TempData["Error"] = $"Không tìm thấy file products.json tại: {jsonPath}";
                    return RedirectToAction("Products");
                }

                _logger.LogInformation($"Bắt đầu import từ file JSON: {jsonPath}");

                // Đọc dữ liệu từ JSON
                var (categories, products) = _excelService.ReadFromJson(jsonPath);

                if (categories == null || products == null)
                {
                    TempData["Error"] = "Không thể đọc dữ liệu từ file JSON. Vui lòng kiểm tra định dạng file.";
                    return RedirectToAction("Products");
                }

                // Lọc bỏ các category có Id không hợp lệ (là array thay vì số)
                var validCategories = categories.Where(c => c.Id > 0).ToList();
                var invalidCategoryCount = categories.Count - validCategories.Count;
                if (invalidCategoryCount > 0)
                {
                    _logger.LogWarning($"Đã bỏ qua {invalidCategoryCount} danh mục có Id không hợp lệ");
                }

                // Lọc bỏ các sản phẩm có CategoryId không hợp lệ hoặc không có trong danh sách category hợp lệ
                var validCategoryIds = validCategories.Select(c => c.Id).ToHashSet();
                var validProducts = products.Where(p => validCategoryIds.Contains(p.CategoryId) && !string.IsNullOrWhiteSpace(p.Name)).ToList();
                var invalidProductCount = products.Count - validProducts.Count;
                if (invalidProductCount > 0)
                {
                    _logger.LogWarning($"Đã bỏ qua {invalidProductCount} sản phẩm có CategoryId không hợp lệ hoặc thiếu tên");
                }

                // Merge vào DataStore (không ghi đè, chỉ thêm mới)
                _dataStore.MergeFromExcel(validProducts, validCategories);

                // Reload dữ liệu để đảm bảo có dữ liệu mới nhất
                _dataStore.ReloadData();
                var finalProducts = _dataStore.GetAllProducts();
                var finalCategories = _dataStore.GetAllCategories();

                // Đếm số sản phẩm theo từng danh mục
                var productsByCategory = finalProducts
                    .GroupBy(p => p.CategoryId)
                    .ToDictionary(g => g.Key, g => g.Count());

                var categorySummary = string.Join(", ", productsByCategory.Select(kvp =>
                {
                    var category = finalCategories.FirstOrDefault(c => c.Id == kvp.Key);
                    return $"{category?.Name ?? $"Danh mục {kvp.Key}"}: {kvp.Value} sản phẩm";
                }));

                TempData["Success"] = $"Đã import thành công {validProducts.Count} sản phẩm và {validCategories.Count} danh mục từ products.json! " +
                    $"Tổng số sản phẩm hiện tại: {finalProducts.Count}. " +
                    $"Phân bố theo danh mục: {categorySummary}";

                _logger.LogInformation($"Đã import thành công {validProducts.Count} sản phẩm và {validCategories.Count} danh mục từ JSON");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi import từ JSON");
                TempData["Error"] = $"Lỗi khi import từ JSON: {ex.Message}";
            }

            return RedirectToAction("Products");
        }

        // Thêm toàn bộ CPU từ danh sách
        [HttpPost]
        public IActionResult AddAllCPUs()
        {
            try
            {
                // Đảm bảo category CPU tồn tại
                var categories = _dataStore.GetAllCategories();
                var cpuCategory = categories.FirstOrDefault(c => 
                    c.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) || 
                    c.Id == 1);
                
                int cpuCategoryId;
                if (cpuCategory == null)
                {
                    // Tạo category CPU nếu chưa có
                    cpuCategory = new Category
                    {
                        Id = 1,
                        Name = "CPU - Bộ vi xử lý",
                        Description = "Intel, AMD",
                        ImageUrl = "https://via.placeholder.com/200x150?text=CPU"
                    };
                    _dataStore.AddCategory(cpuCategory);
                    cpuCategoryId = 1;
                }
                else
                {
                    cpuCategoryId = cpuCategory.Id;
                }
                
                // Danh sách CPU từ hình ảnh
                var cpus = new List<(string Name, decimal Price, int Stock)>
                {
                    ("CPU Intel Core i7 14700F (Intel LGA1700 - 20 Core 28 Thread - Base 2.1Ghz - Turbo 5.4Ghz Cache 33MB)", 7990000, 6),
                    ("CPU Intel Core Ultra 5 225 (10C/10T, 3.3GHz Boost 4.9GHz, 20MB, LGA1851)", 4290000, 4),
                    ("CPU Intel Core Ultra 7 265K Tray New(Up 5.5GHz, 20 Nhân 20 Luồng, Arrow Lake-S)", 6990000, 5),
                    ("CPU Intel Core i5-12400F Tray NEW (Up to 4.4Ghz, 6 nhân 12 luống, 18MB Cache, 65W) - Socket Intel LGA 1700)", 2990000, 7),
                    ("CPU AMD Ryzen 5 5500GT (Up to 4.4 GHz | 6 Cores / 12 Threads | 19 MB Cache)", 2850000, 8),
                    ("CPU AMD Ryzen 5 5500 (3,6 GHz Boost 4,2 GHz | 6 Cores / 12 Threads | 16 MB Cache PCIe 3.0)", 1590000, 4),
                    ("CPU AMD Ryzen 5 7600 (3,8 GHz Boost 5,1 GHz | 6 Cores / 12 Threads | 32 MB Cache PCIe 5.0)", 3790000, 3),
                    ("CPU AMD Ryzen 5 7500F TRAY NEW Chính hãng (3.7GHz Boost 5.0GHz, 6C/12T, 32MB Cache)", 2990000, 6),
                    ("CPU AMD Ryzen 7 9700X Tray New (3.8 GHz Boost 5.5 GHz | 8 Cores / 16 Threads | 32 MB Cache)", 6990000, 4),
                    ("CPU Intel Core Ultra 9 285K (Up 5.7 GHz, 24 Nhân 24 Luồng, Arrow Lake-S)", 13000000, 5),
                    ("CPU Intel Core i5-13400F Tray New (Up To 4.60GHz, 10 Nhân 16 Luồng, 20 MB Cache, LGA 1700)", 3890000, 7),
                    ("CPU Intel Core Ultra 5 245KF (14 Nhân 14 Luông, Arrow Lake-S)", 4990000, 8),
                    ("Intel Xeon Processor E5-2680 v4 (35M Cache, 2.40 GHz turbo 3.30 Ghz) 14Cores / 28 Thread", 390000, 4),
                    ("CPU AMD Ryzen 5 3400G 3.7 GHz (4.2 GHz with boost) / 6MB / 4 cores 8 threads / Radeon Vega 11 / 65W", 1490000, 3),
                    ("CPU AMD Ryzen 7 7800X3D TRAY NEW (4,2 GHz Boost 5,0 GHz | 8 Cores / 16 Threads | 96 MB Cache PCIe 5.0)", 7890000, 6),
                    ("CPU AMD Ryzen 9 9950X (4.3 GHz Boost 5.7 GHz | 16 Cores / 32 Threads | 64 MB Cache)", 13490000, 4),
                    ("CPU Intel Core i5-13400F (Up To 4.60GHz, 10 Nhân 16 Luồng, 20 MB Cache, LGA 1700)", 4090000, 5),
                    ("CPU Intel Core I5 14600KF Tray NEW (Up 5.30 GHz, 14 Nhân 20 Luồng, 24MB Cache, Raptor Lake Refresh)", 4990000, 7),
                    ("CPU Intel Core i9-12900KF New Tray(3.9GHz Boost 5.2GHz, 16 Nhân 24 Luồng, 30MB Cache, 125W, Gen 12)", 7490000, 8),
                    ("Intel Xeon E5-2699 v3 (2.3 GHz, 45 MB, 18C/36T, 145 W, LGA 2011-3)", 990000, 4),
                    ("CPU AMD Ryzen Threadripper 9960X (4.2GHz Up to 5.4GHz | 24 Cores/48 Threads | 152 MB Cache PCIe 5.0)", 40990000, 3),
                    ("CPU Intel Xeon E5-2696 V4 2.20 GHz / 55MB / 22 Core / 44 Thread / Socket 2011-3", 1390000, 6),
                    ("CPU Intel Core i5-12400F (Upto 4.4Ghz, 6 nhân 12 luồng, 18MB Cache, 65W) - Socket Intel LGA 1700)", 2990000, 4),
                    ("CPU Intel Core Ultra 7 265K (Up 5.5GHz, 20 Nhân 20 Luồng, Arrow Lake-S)", 7590000, 5),
                    ("CPU Intel Xeon Gold 6138 (3.70GHz / 27.5 MB / 20 Cores, 40 Threads / LGA3647)", 690000, 7),
                    ("CPU AMD Ryzen 3 3200G (3.6-4.0Ghz / 4 core 4 thread / socket AM4)", 2200000, 8),
                    ("CPU Intel Core I7-13700F (2.10 GHz up to 5.20 GHz, 30 MB, LGA 1700)", 5990000, 4),
                    ("CPU AMD Ryzen 7 9800X3D (Up to 5.2GHz | 8 Cores Zen 5 | 96 MB Cache)", 10990000, 3),
                    ("CPU AMD Ryzen 9 9950X TRAY NEW (4.3 GHz Boost 5.7 GHz | 16 Cores / 32 Threads | 64 MB Cache)", 12490000, 6),
                    ("CPU Intel Core i9-12900KF (3.9GHz turbo 5.2Ghz | 16 nhân 24 luồng | 30MB Cache | 125W)", 7490000, 4),
                    ("CPU Intel Xeon E5-2696 V3 2.30 GHz / 45MB / 18 Cores 36 Threads / Socket 2011-3", 980000, 5),
                    ("CPU Intel Core i5-12600KF (20M Cache, up to 4.90 GHz, 10C16T, Socket 1700)", 3690000, 7),
                    ("CPU Intel Core i7 14700KF (Up 5.60 GHz, 20 Nhân 28 Luồng, 33MB Cache, Raptor Lake Refresh)", 7990000, 6),
                    ("CPU AMD EPYC 7742 TRAY NEW (64C/128T | 2.25GHz Boost 3.4GHz | 256M Cache)", 25000000, 4),
                    ("CPU Intel Xeon E5520 (4 nhân | 8 luống | 2.26GHz turbo 2.53GHz | 8MB Cache)", 500000, 5),
                    ("CPU Intel Xeon L5520 (4 Nhân | 8 Luồng | 2.26GHz Turbo 2.48GHz | 8MB Cache)", 400000, 7),
                    ("CPU Intel Core i5-12400 Tray (Up To 4.40GHz, 6 Nhân 12 Luồng, 18MB Cache, Alder Lake)", 4990000, 8),
                    ("CPU Intel Core i3 13100F (Up to 4.5 GHz / 4 Nhân / 8 Luông / Socket 1700)", 2890000, 4),
                    ("CPU AMD Ryzen 9 7950X3D (4,2 GHz Boost 5,7 GHz | 16 Cores / 32 Threads | 128 MB Cache PCIe 5.0)", 17900000, 3),
                    ("CPU Intel Xeon E5 2667 / 6 core / 12 threads / 2.9 turbo 3.5", 600000, 6),
                    ("CPU Intel Xeon E5 2673 v3 / 12 Core / 24 thread / 2.4 turbo 3.2 Ghz", 990000, 4),
                    ("CPU Intel Xeon Processor E5-2678 V3 (2.50 turbo 3.1GHz / 12Cores / 24 Thread)", 590000, 5),
                    ("CPU Intel Xeon E5-2689 (2.60 GHz, 20M Cache, 8C/16T)", 450000, 7),
                    ("CPU AMD RYZEN 5 3600 (3.6-4.2Ghz / 6 core 12 thread / socket AM4)", 3990000, 8),
                    ("CPU AMD Ryzen Threadripper 7970X (4.0GHz Up to 5.3GHz | 32 Cores / 64 Threads | 128MB Cache | PCIe 5.0)", 69990000, 4),
                    ("CPU Intel Xeon E5 2686 v4 / 2.3GHz / 45MB / 18 Core / 36 Thread / Socket 2011-3", 1490000, 3),
                    ("CPU AMD Ryzen Threadripper PRO 7985WX (3,2GHz Up to 5,1GHz | 64 Cores / 128 Threads | 128MB Cache PCIe 5.0)", 204990000, 6),
                    ("CPU Intel Pentium Gold G6405 (2 nhân | 4 luồng | 4.1 GHz | 4MB Cache)", 1990000, 4)
                };

                var existingProducts = _dataStore.GetAllProducts();
                var maxId = existingProducts.Count > 0 ? existingProducts.Max(p => p.Id) : 0;
                var addedCount = 0;

                foreach (var (name, price, stock) in cpus)
                {
                    // Kiểm tra xem sản phẩm đã tồn tại chưa
                    if (!existingProducts.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        var product = new Product
                        {
                            Id = ++maxId,
                            Name = name,
                            Description = name, // Dùng tên làm mô tả
                            Price = price,
                            OldPrice = 0,
                            CategoryId = cpuCategoryId,
                            Stock = stock,
                            IsFeatured = price >= 10000000, // Sản phẩm trên 10 triệu là nổi bật
                            ImageUrl = $"https://via.placeholder.com/300x300?text={Uri.EscapeDataString(name.Length > 20 ? name.Substring(0, 20) : name)}"
                        };

                        _dataStore.AddProduct(product);
                        addedCount++;
                    }
                }

                // Xóa file Excel nếu tồn tại
                var excelPath = Path.Combine(_environment.ContentRootPath, "Data", "DATABASE.xlsx");
                if (System.IO.File.Exists(excelPath))
                {
                    try
                    {
                        System.IO.File.Delete(excelPath);
                        _logger.LogInformation("Đã xóa file Excel: " + excelPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Không thể xóa file Excel, có thể đang được sử dụng");
                    }
                }

                TempData["Success"] = $"Đã thêm thành công {addedCount} CPU vào database!";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi thêm CPU");
                TempData["Error"] = $"Lỗi: {ex.Message}";
            }

            return RedirectToAction("Products");
        }

        // Thêm toàn bộ RAM từ danh sách (có thể gọi từ GET hoặc POST)
        [HttpGet]
        [HttpPost]
        public IActionResult AddAllRAMs()
        {
            try
            {
                // Đảm bảo category RAM tồn tại
                var categories = _dataStore.GetAllCategories();
                var ramCategory = categories.FirstOrDefault(c => 
                    c.Name.Contains("RAM", StringComparison.OrdinalIgnoreCase) || 
                    c.Id == 3);
                
                int ramCategoryId;
                if (ramCategory == null)
                {
                    // Tạo category RAM nếu chưa có
                    ramCategory = new Category
                    {
                        Id = 3,
                        Name = "RAM - Bộ nhớ",
                        Description = "Kingston, Corsair, G.Skill, TeamGroup",
                        ImageUrl = "https://via.placeholder.com/200x150?text=RAM"
                    };
                    _dataStore.AddCategory(ramCategory);
                    ramCategoryId = 3;
                }
                else
                {
                    ramCategoryId = ramCategory.Id;
                }
                
                // Danh sách RAM từ hình ảnh (32 sản phẩm)
                var rams = new List<(string Name, decimal Price, int Stock)>
                {
                    ("RAM Kingston HyperX Fury 16GB (1x16GB) DDR4 3200MHz (KF432C16BB1-16WP)", 2700000, 6),
                    ("RAM GSKILL RIPJAWS V 16GB DDR4 3200MHZ (F4-3200C16S-16GVK)", 2600000, 4),
                    ("RAM TeamGroup T-Force Delta RGB 16GB DDR5 6000MHz - Black", 3850000, 5),
                    ("RAM TeamGroup T-Force Vulcan 16GB (1x16GB) DDR5 6000MHz (Red)", 3750000, 7),
                    ("RAM Mixie 8GB DDR4 3200MHz tản nhiệt (Intel/AMD)", 1200000, 8),
                    ("RAM G.Skill Trident Z RGB 16GB (1x16GB) DDR4 3600MHz (F4-3600C18S-16GTZR)", 2800000, 4),
                    ("RAM Patriot EP Viper Venom 16GB DDR5 6000MHz", 3700000, 3),
                    ("RAM Kingmax Horizon HDH2MJ4 16GB (1x16GB) DDR5 6000MHz (KMAXD516GB6000HS)", 3700000, 6),
                    ("RAM Corsair Vengeance LPX 8GB (1x8GB) 3200MHz Black DDR4 - CMK8GX4M1E3200C16", 1450000, 4),
                    ("RAM Kingston FURY Beast 8GB (1x8GB) DDR4 3200MHz (KF432C16BB/8WP)", 1450000, 5),
                    ("RAM Lexar Thor 32GB (2x16GB) DDR5 6000MHz (LD5U16G60C36LG-RGD)", 7200000, 7),
                    ("RAM Kingston FURY Beast 32GB (1x32GB) DDR5 6000MHz (KF560C36BBE2-32, AMD EXPO + Intel X)", 7550000, 8),
                    ("RAM Kingmax Horizon HDH2MJ4 16GB (1x16GB) DDR5 5600MHz (KMAXD516GB5600HS)", 3600000, 4),
                    ("Ram Kingston FURY Beast RGB 64GB (2x32GB) DDR5 bus 5600Mhz (KF556C40BB2AK2-64)", 11400000, 3),
                    ("RAM Corsair Vengeance RGB 64GB (2x32GB) DDR5 6000MHz (CMH64GX5M2D6000C40)", 11400000, 6),
                    ("RAM Kingston 32GB DDR5 5600MHz (KVR56U46BD8-32, Không Tản)", 7200000, 4),
                    ("RAM Kingston 16GB (16x1) DDR5 buss 5600 Fury BEAST (KF556C40BB-16WP)", 3600000, 5),
                    ("RAM Kingston Value 16GB (1x16GB) DDR5 5600MHz (KVR56U46BS8-16WP)", 3600000, 7),
                    ("RAM G.Skill Trident Z5 RGB 32GB (2x16GB) DDR5 6400MHz (F5-6400J3239G16GX2-TZ5RK, Black)", 8000000, 8),
                    ("RAM DDR4 ECC 32GB Bus 2133Mhz", 2700000, 4),
                    ("Ram Máy Chủ ECC DDR4 16G/2133 ECC REGISTERED SERVER MEMORY", 1500000, 3),
                    ("RAM TEAMGROUP T-Create Expert 32GB (2x16GB) DDR5 6400MHz Black (Hỗ trợ AMD EXPO & Intel)", 6900000, 6),
                    ("Ram DDR4 ECC 32GB Bus 2400Mhz", 2700000, 4),
                    ("Ram TEAMGROUP Vulcan Z 8GB (1x8GB) DDR4 3200Mhz (Xám)", 1300000, 5),
                    ("RAM MÁY CHỦ ECC DDR3 16GB Bus 1600", 700000, 7),
                    ("RAM SAMSUNG ECC 32GB DDR4 3200MHz", 2800000, 8),
                    ("RAM Apacer NOX RGB Aura2 White 8GB DDR4 3200MHz", 1200000, 4),
                    ("Ram Samsung 16GB DDR4 2666MHz ECC REGISTERED SERVER MEMORY", 650000, 3),
                    ("Ram Kingston Fury Beast 16GB (1x16GB) DDR5 5200MHz (KF552C40BB-16)", 3700000, 6),
                    ("RAM Kingston 8GB (1x8GB) DDR4 3200Mhz (KVR32N22S8/8WP)", 1300000, 4),
                    ("Ram Kingston FURY Beast 8GB (1x8GB) DDR4 3200Mhz", 1400000, 5),
                    ("Ram DDR4 ECC 32GB 2666MHz", 2690000, 7)
                };

                var existingProducts = _dataStore.GetAllProducts();
                var maxId = existingProducts.Count > 0 ? existingProducts.Max(p => p.Id) : 0;
                var addedCount = 0;

                _logger.LogInformation($"Bắt đầu thêm RAM. Tổng số RAM trong danh sách: {rams.Count}, CategoryId: {ramCategoryId}");

                foreach (var (name, price, stock) in rams)
                {
                    // Kiểm tra xem sản phẩm đã tồn tại chưa
                    if (!existingProducts.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        var product = new Product
                        {
                            Id = ++maxId,
                            Name = name,
                            Description = name,
                            Price = price,
                            OldPrice = 0,
                            CategoryId = ramCategoryId,
                            Stock = stock,
                            IsFeatured = price >= 10000000,
                            ImageUrl = $"https://via.placeholder.com/300x300?text={Uri.EscapeDataString(name.Length > 20 ? name.Substring(0, 20) : name)}"
                        };

                        _dataStore.AddProduct(product);
                        addedCount++;
                        _logger.LogInformation($"Đã thêm RAM: {name.Substring(0, Math.Min(50, name.Length))}... (CategoryId: {ramCategoryId})");
                    }
                }

                // Reload dữ liệu để đảm bảo có dữ liệu mới nhất
                _dataStore.ReloadData();
                var finalProducts = _dataStore.GetAllProducts();
                var finalRamProducts = finalProducts.Where(p => p.CategoryId == ramCategoryId).ToList();

                if (addedCount > 0)
                {
                    TempData["Success"] = $"Đã thêm thành công {addedCount} RAM vào database! Tổng số RAM hiện tại: {finalRamProducts.Count}";
                    _logger.LogInformation($"Đã thêm {addedCount} RAM vào database. Tổng số RAM hiện tại: {finalRamProducts.Count}");
                }
                else
                {
                    TempData["Info"] = $"Tất cả RAM đã có trong database. Tổng số RAM hiện tại: {finalRamProducts.Count}";
                    _logger.LogInformation($"Tất cả RAM đã có trong database. Tổng số RAM hiện tại: {finalRamProducts.Count}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi thêm RAM");
                TempData["Error"] = $"Lỗi: {ex.Message}";
            }

            return RedirectToAction("Products");
        }

        // Cập nhật hình ảnh cho tất cả sản phẩm
        [HttpPost]
        public async Task<IActionResult> UpdateAllProductImages(int? maxProducts = 50, int? delayMs = 2000)
        {
            try
            {
                var max = maxProducts ?? 50;
                var delay = delayMs ?? 2000;
                
                _logger.LogInformation($"Bắt đầu cập nhật hình ảnh cho tối đa {max} sản phẩm");
                
                var updatedCount = await _imageSearchService.UpdateAllProductImagesAsync(_dataStore, max, delay);
                
                TempData["Success"] = $"Đã cập nhật hình ảnh cho {updatedCount}/{max} sản phẩm thành công!";
                _logger.LogInformation($"Hoàn thành: Đã cập nhật {updatedCount} sản phẩm");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật hình ảnh sản phẩm");
                TempData["Error"] = "Có lỗi xảy ra khi cập nhật hình ảnh: " + ex.Message;
            }
            
            return RedirectToAction("Products");
        }

        // Cập nhật hình ảnh cho một sản phẩm cụ thể
        [HttpPost]
        public async Task<IActionResult> UpdateProductImage(int id)
        {
            try
            {
                var success = await _imageSearchService.UpdateProductImageAsync(_dataStore, id);
                
                if (success)
                {
                    TempData["Success"] = "Đã cập nhật hình ảnh sản phẩm thành công!";
                }
                else
                {
                    TempData["Error"] = "Không tìm thấy hình ảnh phù hợp cho sản phẩm này.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi cập nhật hình ảnh cho sản phẩm ID {id}");
                TempData["Error"] = "Có lỗi xảy ra: " + ex.Message;
            }
            
            return RedirectToAction("Products");
        }
    }
}

