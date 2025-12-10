using Microsoft.AspNetCore.Mvc;
using PCSTORE.Filters;
using PCSTORE.Services;
using PCSTORE.Models;
using System.Text.Json;

namespace PCSTORE.Controllers
{
    [AdminAuthorize]
    public class AdminInventoryController : Controller
    {
        private readonly DataStoreService _dataStore;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<AdminInventoryController> _logger;
        private readonly string _inventoryPath;
        private readonly string _supplierPath;
        private List<Inventory> _inventories = new List<Inventory>();
        private List<Supplier> _suppliers = new List<Supplier>();

        public AdminInventoryController(
            DataStoreService dataStore,
            IWebHostEnvironment environment,
            ILogger<AdminInventoryController> logger)
        {
            _dataStore = dataStore;
            _environment = environment;
            _logger = logger;
            var dataDir = Path.Combine(_environment.ContentRootPath, "Data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            _inventoryPath = Path.Combine(dataDir, "inventory.json");
            _supplierPath = Path.Combine(dataDir, "suppliers.json");
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                if (System.IO.File.Exists(_inventoryPath))
                {
                    var json = System.IO.File.ReadAllText(_inventoryPath);
                    _inventories = JsonSerializer.Deserialize<List<Inventory>>(json) ?? new List<Inventory>();
                }
                if (System.IO.File.Exists(_supplierPath))
                {
                    var json = System.IO.File.ReadAllText(_supplierPath);
                    _suppliers = JsonSerializer.Deserialize<List<Supplier>>(json) ?? new List<Supplier>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi load inventory data");
            }
        }

        private void SaveInventories()
        {
            try
            {
                var json = JsonSerializer.Serialize(_inventories, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(_inventoryPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi save inventory");
            }
        }

        private void SaveSuppliers()
        {
            try
            {
                var json = JsonSerializer.Serialize(_suppliers, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(_supplierPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi save suppliers");
            }
        }

        public IActionResult Index()
        {
            LoadData();
            var products = _dataStore.GetAllProducts();
            
            ViewBag.Products = products;
            ViewBag.Inventories = _inventories.OrderByDescending(i => i.CreatedAt).Take(50).ToList();
            ViewBag.Suppliers = _suppliers;
            ViewBag.LowStockProducts = products.Where(p => p.Stock < 10).OrderBy(p => p.Stock).ToList();
            
            return View();
        }

        [HttpPost]
        public IActionResult ImportStock(int productId, int quantity, string supplierName, string notes)
        {
            try
            {
                var product = _dataStore.GetProductById(productId);
                if (product == null)
                {
                    TempData["Error"] = "Không tìm thấy sản phẩm.";
                    return RedirectToAction("Index");
                }

                // Cập nhật stock
                product.Stock += quantity;
                _dataStore.SaveData();

                // Lưu lịch sử nhập kho
                LoadData();
                var adminUsername = HttpContext.Session.GetString("AdminUsername") ?? "Admin";
                var inventory = new Inventory
                {
                    Id = _inventories.Count > 0 ? _inventories.Max(i => i.Id) + 1 : 1,
                    ProductId = productId,
                    ProductName = product.Name,
                    Quantity = quantity,
                    Type = "Import",
                    SupplierName = supplierName,
                    Notes = notes,
                    CreatedBy = adminUsername
                };
                _inventories.Add(inventory);
                SaveInventories();

                TempData["Success"] = $"Đã nhập {quantity} sản phẩm vào kho thành công.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi nhập kho");
                TempData["Error"] = "Có lỗi xảy ra khi nhập kho.";
                return RedirectToAction("Index");
            }
        }

        [HttpGet]
        public IActionResult SupplierForm(int? id)
        {
            LoadData();
            if (id.HasValue && id.Value > 0)
            {
                var supplier = _suppliers.FirstOrDefault(s => s.Id == id.Value);
                if (supplier == null)
                {
                    return RedirectToAction("Index");
                }
                return View(supplier);
            }
            return View(new Supplier());
        }

        [HttpPost]
        public IActionResult SupplierForm(Supplier supplier)
        {
            if (string.IsNullOrWhiteSpace(supplier.Name))
            {
                TempData["Error"] = "Vui lòng nhập tên nhà cung cấp.";
                return View(supplier);
            }

            try
            {
                LoadData();
                if (supplier.Id > 0)
                {
                    var existing = _suppliers.FirstOrDefault(s => s.Id == supplier.Id);
                    if (existing != null)
                    {
                        existing.Name = supplier.Name;
                        existing.ContactPerson = supplier.ContactPerson;
                        existing.Phone = supplier.Phone;
                        existing.Email = supplier.Email;
                        existing.Address = supplier.Address;
                        existing.Notes = supplier.Notes;
                        existing.IsActive = supplier.IsActive;
                    }
                }
                else
                {
                    supplier.Id = _suppliers.Count > 0 ? _suppliers.Max(s => s.Id) + 1 : 1;
                    supplier.CreatedAt = DateTime.Now;
                    _suppliers.Add(supplier);
                }
                SaveSuppliers();
                TempData["Success"] = supplier.Id > 0 ? "Đã cập nhật nhà cung cấp thành công." : "Đã thêm nhà cung cấp mới thành công.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu nhà cung cấp");
                TempData["Error"] = "Có lỗi xảy ra khi lưu nhà cung cấp.";
                return View(supplier);
            }
        }
    }
}

