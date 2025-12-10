using PCSTORE.Models;
using System.Text.Json;
using System.IO;
using System.Linq;

namespace PCSTORE.Services
{
    public class DataStoreService
    {
        private readonly string _dataPath;
        private readonly ILogger<DataStoreService> _logger;
        private List<Product> _products;
        private List<Category> _categories;
        private readonly object _lock = new object();

        public DataStoreService(IWebHostEnvironment environment, ILogger<DataStoreService> logger)
        {
            _logger = logger;
            var dataDir = Path.Combine(environment.ContentRootPath, "Data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            _dataPath = Path.Combine(dataDir, "datastore.json");
            LoadData();
        }

        // Load dữ liệu từ file
        private void LoadData()
        {
            try
            {
                if (File.Exists(_dataPath))
                {
                    var json = File.ReadAllText(_dataPath, System.Text.Encoding.UTF8);
                    var data = JsonSerializer.Deserialize<DataStore>(json);
                    _products = data?.Products ?? new List<Product>();
                    _categories = data?.Categories ?? new List<Category>();
                    
                    // Log số lượng RAM products
                    var ramCount = _products.Count(p => p.CategoryId == 3);
                    _logger.LogInformation($"Đã load {_products.Count} sản phẩm và {_categories.Count} danh mục từ datastore (RAM: {ramCount})");
                }
                else
                {
                    _products = new List<Product>();
                    _categories = new List<Category>();
                    _logger.LogInformation("Chưa có dữ liệu, khởi tạo mới");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi load dữ liệu");
                _products = new List<Product>();
                _categories = new List<Category>();
            }
        }
        
        // Reload dữ liệu từ file (public method để có thể gọi từ bên ngoài)
        public void ReloadData()
        {
            LoadData();
        }

        // Lưu dữ liệu vào file
        public void SaveData()
        {
            try
            {
                lock (_lock)
                {
                    var data = new DataStore
                    {
                        Products = _products,
                        Categories = _categories,
                        LastUpdated = DateTime.Now
                    };

                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };

                    var json = JsonSerializer.Serialize(data, options);
                    File.WriteAllText(_dataPath, json, System.Text.Encoding.UTF8);
                    _logger.LogInformation($"Đã lưu {_products.Count} sản phẩm và {_categories.Count} danh mục vào datastore");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu dữ liệu");
            }
        }

        // Products CRUD
        public List<Product> GetAllProducts() => _products.ToList();
        
        public Product? GetProductById(int id) => _products.FirstOrDefault(p => p.Id == id);
        
        public void AddProduct(Product product)
        {
            if (product.Id == 0)
            {
                product.Id = _products.Count > 0 ? _products.Max(p => p.Id) + 1 : 1;
            }
            _products.Add(product);
            SaveData();
        }
        
        public void UpdateProduct(Product product)
        {
            var index = _products.FindIndex(p => p.Id == product.Id);
            if (index >= 0)
            {
                _products[index] = product;
                SaveData();
            }
        }
        
        public void DeleteProduct(int id)
        {
            var product = _products.FirstOrDefault(p => p.Id == id);
            if (product != null)
            {
                _products.Remove(product);
                SaveData();
            }
        }
        
        // Xóa toàn bộ sản phẩm
        public void ClearAllProducts()
        {
            _products.Clear();
            SaveData();
            _logger.LogInformation("Đã xóa toàn bộ sản phẩm khỏi datastore");
        }

        // Categories CRUD
        public List<Category> GetAllCategories() => _categories.ToList();
        
        public Category? GetCategoryById(int id) => _categories.FirstOrDefault(c => c.Id == id);
        
        public void AddCategory(Category category)
        {
            if (category.Id == 0)
            {
                category.Id = _categories.Count > 0 ? _categories.Max(c => c.Id) + 1 : 1;
            }
            _categories.Add(category);
            SaveData();
        }
        
        public void UpdateCategory(Category category)
        {
            var index = _categories.FindIndex(c => c.Id == category.Id);
            if (index >= 0)
            {
                _categories[index] = category;
                SaveData();
            }
        }
        
        public void DeleteCategory(int id)
        {
            var category = _categories.FirstOrDefault(c => c.Id == id);
            if (category != null)
            {
                // Xóa tất cả sản phẩm thuộc danh mục này
                _products.RemoveAll(p => p.CategoryId == id);
                _categories.Remove(category);
                SaveData();
            }
        }

        // Import từ Excel (sẽ ghi đè dữ liệu hiện tại)
        public void ImportFromExcel(List<Product> products, List<Category> categories)
        {
            _products = products;
            _categories = categories;
            SaveData();
            _logger.LogInformation($"Đã import {products.Count} sản phẩm và {categories.Count} danh mục từ Excel");
        }

        // Merge với dữ liệu Excel (không ghi đè, chỉ thêm mới)
        public void MergeFromExcel(List<Product> products, List<Category> categories)
        {
            // Merge categories
            foreach (var category in categories)
            {
                if (!_categories.Any(c => c.Id == category.Id))
                {
                    _categories.Add(category);
                }
            }

            // Merge products
            foreach (var product in products)
            {
                if (!_products.Any(p => p.Id == product.Id))
                {
                    _products.Add(product);
                }
            }

            SaveData();
            _logger.LogInformation($"Đã merge {products.Count} sản phẩm và {categories.Count} danh mục từ Excel");
        }

        private class DataStore
        {
            public List<Product> Products { get; set; } = new List<Product>();
            public List<Category> Categories { get; set; } = new List<Category>();
            public DateTime LastUpdated { get; set; }
        }
    }
}

