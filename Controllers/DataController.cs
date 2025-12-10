using Microsoft.AspNetCore.Mvc;
using PCSTORE.Services;
using System.IO;

namespace PCSTORE.Controllers
{
    public class DataController : Controller
    {
        private readonly ExcelService _excelService;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<DataController> _logger;

        public DataController(ExcelService excelService, IWebHostEnvironment environment, ILogger<DataController> logger)
        {
            _excelService = excelService;
            _environment = environment;
            _logger = logger;
        }

        // Import dữ liệu từ Excel và chuyển sang JSON (tự động hoặc thủ công)
        [HttpPost]
        public IActionResult ImportFromExcel()
        {
            try
            {
                var excelPath = Path.Combine(_environment.ContentRootPath, "Data", "DATABASE.xlsx");
                var jsonPath = Path.Combine(_environment.ContentRootPath, "Data", "products.json");

                if (!System.IO.File.Exists(excelPath))
                {
                    return Json(new { success = false, message = "Không tìm thấy file DATABASE.xlsx" });
                }

                // Chuyển đổi Excel sang JSON và tự động tìm hình ảnh
                _excelService.ConvertExcelToJson(excelPath, jsonPath, autoSearchImages: true);

                var products = _excelService.ReadProductsFromExcel(excelPath);
                var categories = _excelService.ReadCategoriesFromExcel(excelPath);

                return Json(new
                {
                    success = true,
                    message = $"Đã import thành công {products.Count} sản phẩm và {categories.Count} danh mục",
                    productCount = products.Count,
                    categoryCount = categories.Count,
                    jsonPath = jsonPath
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi import từ Excel");
                return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }

        // Tự động import khi truy cập (GET request)
        [HttpGet]
        public IActionResult AutoImport()
        {
            try
            {
                var excelPath = Path.Combine(_environment.ContentRootPath, "Data", "DATABASE.xlsx");
                var jsonPath = Path.Combine(_environment.ContentRootPath, "Data", "products.json");

                // Chỉ import nếu chưa có file JSON hoặc file Excel mới hơn
                bool shouldImport = false;
                
                if (!System.IO.File.Exists(jsonPath))
                {
                    shouldImport = true;
                    _logger.LogInformation("File JSON chưa tồn tại, bắt đầu import từ Excel");
                }
                else if (System.IO.File.Exists(excelPath))
                {
                    var excelInfo = new FileInfo(excelPath);
                    var jsonInfo = new FileInfo(jsonPath);
                    
                    if (excelInfo.LastWriteTime > jsonInfo.LastWriteTime)
                    {
                        shouldImport = true;
                        _logger.LogInformation("File Excel mới hơn JSON, bắt đầu import lại");
                    }
                }

                if (shouldImport && System.IO.File.Exists(excelPath))
                {
                    // Đảm bảo thư mục Data tồn tại
                    var dataDir = Path.GetDirectoryName(jsonPath);
                    if (!Directory.Exists(dataDir))
                    {
                        Directory.CreateDirectory(dataDir);
                    }

                    // Chuyển đổi Excel sang JSON và tự động tìm hình ảnh
                    _excelService.ConvertExcelToJson(excelPath, jsonPath, autoSearchImages: true);

                    var products = _excelService.ReadProductsFromExcel(excelPath);
                    var categories = _excelService.ReadCategoriesFromExcel(excelPath);

                    return Json(new
                    {
                        success = true,
                        message = $"Đã tự động import {products.Count} sản phẩm và {categories.Count} danh mục",
                        productCount = products.Count,
                        categoryCount = categories.Count,
                        autoImport = true
                    });
                }
                else
                {
                    return Json(new
                    {
                        success = true,
                        message = "Dữ liệu đã được cập nhật, không cần import lại",
                        autoImport = false
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động import từ Excel");
                return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }

        // Xem dữ liệu JSON
        [HttpGet]
        public IActionResult ViewJsonData()
        {
            try
            {
                var jsonPath = Path.Combine(_environment.ContentRootPath, "Data", "products.json");
                var (categories, products) = _excelService.ReadFromJson(jsonPath);

                ViewBag.Categories = categories;
                ViewBag.Products = products;
                ViewBag.ProductCount = products.Count;
                ViewBag.CategoryCount = categories.Count;
                ViewBag.JsonPath = jsonPath;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc JSON");
                ViewBag.Error = ex.Message;
                return View();
            }
        }

        // API để lấy dữ liệu từ JSON
        [HttpGet]
        public IActionResult GetProductsFromJson()
        {
            try
            {
                var jsonPath = Path.Combine(_environment.ContentRootPath, "Data", "products.json");
                var (categories, products) = _excelService.ReadFromJson(jsonPath);

                return Json(new { categories, products });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc JSON");
                return Json(new { categories = new List<Models.Category>(), products = new List<Models.Product>() });
            }
        }
    }
}

