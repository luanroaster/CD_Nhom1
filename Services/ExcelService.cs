using OfficeOpenXml;
using PCSTORE.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Web;

namespace PCSTORE.Services
{
    public class ExcelService
    {
        public List<Product> ReadProductsFromExcel(string filePath)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            var allProducts = new List<Product>();

            if (!File.Exists(filePath))
            {
                return allProducts;
            }

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                // Đọc từ tất cả các sheet
                foreach (var worksheet in package.Workbook.Worksheets)
                {
                    // Bỏ qua sheet Categories/Danh mục
                    if (worksheet.Name.ToLower().Contains("category") || 
                        worksheet.Name.ToLower().Contains("danh mục"))
                    {
                        continue;
                    }

                    var products = ReadProductsFromSheet(worksheet);
                    allProducts.AddRange(products);
                }
            }

            return allProducts;
        }

        private List<Product> ReadProductsFromSheet(ExcelWorksheet worksheet)
        {
            var products = new List<Product>();
            var rowCount = worksheet.Dimension?.Rows ?? 0;
            var colCount = worksheet.Dimension?.Columns ?? 0;

            if (rowCount < 2) return products; // Không có dữ liệu (chỉ có header)

            // Đọc header để xác định cột
            var headers = new Dictionary<string, int>();
            for (int col = 1; col <= colCount; col++)
            {
                var headerValue = worksheet.Cells[1, col].Value?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(headerValue))
                {
                    headers[headerValue.ToLower()] = col;
                }
            }

            // Đọc dữ liệu từ dòng 2 trở đi
            for (int row = 2; row <= rowCount; row++)
                {
                    try
                    {
                        var product = new Product();

                        // Map các cột phổ biến - tìm kiếm linh hoạt
                        var idCol = FindColumn(headers, "id", "stt", "số thứ tự");
                        if (idCol.HasValue)
                        {
                            var idValue = worksheet.Cells[row, idCol.Value].Value?.ToString();
                            if (int.TryParse(idValue, out int id))
                                product.Id = id;
                            else if (row > 1) // Nếu không có ID, dùng số thứ tự
                                product.Id = row - 1;
                        }
                        else if (row > 1)
                        {
                            product.Id = row - 1;
                        }

                        var nameCol = FindColumn(headers, "tên", "name", "tên sản phẩm", "sản phẩm", "product");
                        if (nameCol.HasValue)
                        {
                            product.Name = worksheet.Cells[row, nameCol.Value].Value?.ToString()?.Trim() ?? "";
                        }

                        var descCol = FindColumn(headers, "mô tả", "description", "mô tả sản phẩm", "chi tiết", "details");
                        if (descCol.HasValue)
                        {
                            product.Description = worksheet.Cells[row, descCol.Value].Value?.ToString()?.Trim() ?? "";
                        }

                        var priceCol = FindColumn(headers, "giá", "price", "giá bán", "giá thành", "đơn giá");
                        if (priceCol.HasValue)
                        {
                            var priceStr = worksheet.Cells[row, priceCol.Value].Value?.ToString()?.Replace(",", "").Replace(".", "");
                            if (decimal.TryParse(priceStr, out decimal price))
                                product.Price = price;
                        }

                        var oldPriceCol = FindColumn(headers, "giá cũ", "oldprice", "giá gốc", "giá niêm yết", "giá trước");
                        if (oldPriceCol.HasValue)
                        {
                            var oldPriceStr = worksheet.Cells[row, oldPriceCol.Value].Value?.ToString()?.Replace(",", "").Replace(".", "");
                            if (decimal.TryParse(oldPriceStr, out decimal oldPrice))
                                product.OldPrice = oldPrice;
                        }

                        var imgCol = FindColumn(headers, "hình ảnh", "image", "imageurl", "ảnh", "url", "link ảnh");
                        if (imgCol.HasValue)
                        {
                            product.ImageUrl = worksheet.Cells[row, imgCol.Value].Value?.ToString()?.Trim() ?? "";
                        }
                        else
                        {
                            // Nếu không có ảnh, dùng placeholder
                            product.ImageUrl = "https://via.placeholder.com/300x300?text=" + Uri.EscapeDataString(product.Name.Length > 20 ? product.Name.Substring(0, 20) : product.Name);
                        }

                        var catCol = FindColumn(headers, "danh mục", "category", "categoryid", "loại", "nhóm");
                        if (catCol.HasValue)
                        {
                            var catValue = worksheet.Cells[row, catCol.Value].Value?.ToString();
                            if (int.TryParse(catValue, out int catId))
                                product.CategoryId = catId;
                            else
                                product.CategoryId = 1; // Mặc định
                        }
                        else
                        {
                            product.CategoryId = 1; // Mặc định
                        }

                        var featuredCol = FindColumn(headers, "nổi bật", "featured", "is featured", "hot", "ưu tiên");
                        if (featuredCol.HasValue)
                        {
                            var featuredValue = worksheet.Cells[row, featuredCol.Value].Value?.ToString()?.ToLower();
                            product.IsFeatured = featuredValue == "true" || featuredValue == "1" || featuredValue == "có" || featuredValue == "yes" || featuredValue == "x";
                        }

                        var stockCol = FindColumn(headers, "tồn kho", "stock", "số lượng", "sl", "quantity");
                        if (stockCol.HasValue)
                        {
                            var stockValue = worksheet.Cells[row, stockCol.Value].Value?.ToString();
                            if (int.TryParse(stockValue, out int stock))
                                product.Stock = stock;
                            else
                                product.Stock = 10; // Mặc định
                        }
                        else
                        {
                            product.Stock = 10; // Mặc định
                        }

                        // Chỉ thêm sản phẩm nếu có tên
                        if (!string.IsNullOrEmpty(product.Name))
                        {
                            products.Add(product);
                        }
                    }
                    catch
                    {
                        // Bỏ qua dòng lỗi và tiếp tục
                        continue;
                    }
                }

            return products;
        }

        public List<Category> ReadCategoriesFromExcel(string filePath)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            var categories = new List<Category>();

            if (!File.Exists(filePath))
            {
                return categories;
            }

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                // Tìm sheet có chứa danh mục (có thể là sheet thứ 2 hoặc sheet có tên "Categories")
                ExcelWorksheet? worksheet = null;
                
                foreach (var sheet in package.Workbook.Worksheets)
                {
                    if (sheet.Name.ToLower().Contains("category") || sheet.Name.ToLower().Contains("danh mục"))
                    {
                        worksheet = sheet;
                        break;
                    }
                }

                // Nếu không tìm thấy, dùng sheet đầu tiên
                worksheet ??= package.Workbook.Worksheets[0];

                var rowCount = worksheet.Dimension?.Rows ?? 0;
                var colCount = worksheet.Dimension?.Columns ?? 0;

                if (rowCount < 2) return categories;

                // Đọc header
                var headers = new Dictionary<string, int>();
                for (int col = 1; col <= colCount; col++)
                {
                    var headerValue = worksheet.Cells[1, col].Value?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(headerValue))
                    {
                        headers[headerValue.ToLower()] = col;
                    }
                }

                // Đọc dữ liệu
                for (int row = 2; row <= rowCount; row++)
                {
                    try
                    {
                        var category = new Category();

                        if (headers.ContainsKey("id") || headers.ContainsKey("stt"))
                        {
                            var idCol = headers.ContainsKey("id") ? headers["id"] : headers["stt"];
                            if (int.TryParse(worksheet.Cells[row, idCol].Value?.ToString(), out int id))
                                category.Id = id;
                        }

                        if (headers.ContainsKey("tên") || headers.ContainsKey("name") || headers.ContainsKey("tên danh mục"))
                        {
                            var nameCol = headers.ContainsKey("tên") ? headers["tên"] :
                                         headers.ContainsKey("name") ? headers["name"] : headers["tên danh mục"];
                            category.Name = worksheet.Cells[row, nameCol].Value?.ToString()?.Trim() ?? "";
                        }

                        if (headers.ContainsKey("mô tả") || headers.ContainsKey("description"))
                        {
                            var descCol = headers.ContainsKey("mô tả") ? headers["mô tả"] : headers["description"];
                            category.Description = worksheet.Cells[row, descCol].Value?.ToString()?.Trim() ?? "";
                        }

                        if (headers.ContainsKey("hình ảnh") || headers.ContainsKey("image") || headers.ContainsKey("imageurl"))
                        {
                            var imgCol = headers.ContainsKey("hình ảnh") ? headers["hình ảnh"] :
                                        headers.ContainsKey("image") ? headers["image"] : headers["imageurl"];
                            category.ImageUrl = worksheet.Cells[row, imgCol].Value?.ToString()?.Trim() ?? "";
                        }

                        if (!string.IsNullOrEmpty(category.Name))
                        {
                            categories.Add(category);
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            return categories;
        }

        private int? FindColumn(Dictionary<string, int> headers, params string[] possibleNames)
        {
            foreach (var name in possibleNames)
            {
                if (headers.ContainsKey(name))
                    return headers[name];
                
                // Tìm kiếm không phân biệt hoa thường và có thể chứa khoảng trắng
                var key = headers.Keys.FirstOrDefault(k => k.Replace(" ", "").Equals(name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
                if (key != null)
                    return headers[key];
            }
            return null;
        }

        // Chuyển đổi Excel sang JSON và tự động tìm hình ảnh
        public void ConvertExcelToJson(string excelPath, string jsonPath, bool autoSearchImages = true)
        {
            var products = ReadProductsFromExcel(excelPath);
            var categories = ReadCategoriesFromExcel(excelPath);

            // Tự động tìm hình ảnh nếu chưa có
            // Lưu ý: Tính năng này đã được chuyển sang ImageSearchService riêng biệt
            // Hình ảnh sẽ được cập nhật tự động khi khởi động ứng dụng
            if (autoSearchImages)
            {
                // Sử dụng placeholder tạm thời, hình ảnh sẽ được cập nhật tự động sau
                foreach (var product in products)
                {
                    if (string.IsNullOrEmpty(product.ImageUrl) || product.ImageUrl.Contains("placeholder"))
                    {
                        var shortName = product.Name.Length > 20 ? product.Name.Substring(0, 20) : product.Name;
                        var encodedName = Uri.EscapeDataString(shortName);
                        product.ImageUrl = $"https://via.placeholder.com/300x300?text={encodedName}";
                    }
                }

                foreach (var category in categories)
                {
                    if (string.IsNullOrEmpty(category.ImageUrl) || category.ImageUrl.Contains("placeholder"))
                    {
                        var shortName = category.Name.Length > 20 ? category.Name.Substring(0, 20) : category.Name;
                        var encodedName = Uri.EscapeDataString(shortName);
                        category.ImageUrl = $"https://via.placeholder.com/300x300?text={encodedName}";
                    }
                }
            }

            // Tạo object để lưu
            var data = new
            {
                Categories = categories,
                Products = products,
                LastUpdated = DateTime.Now
            };

            // Lưu vào JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(jsonPath, json, System.Text.Encoding.UTF8);
        }

        // Đọc dữ liệu từ JSON
        public (List<Category> Categories, List<Product> Products) ReadFromJson(string jsonPath)
        {
            if (!File.Exists(jsonPath))
            {
                return (new List<Category>(), new List<Product>());
            }

            try
            {
                var json = File.ReadAllText(jsonPath, System.Text.Encoding.UTF8);
                
                // Sử dụng JsonDocument để parse linh hoạt hơn, xử lý các trường hợp đặc biệt
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    var categories = new List<Category>();
                    var products = new List<Product>();

                    // Parse Categories
                    if (root.TryGetProperty("Categories", out var categoriesElement) && categoriesElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var catElement in categoriesElement.EnumerateArray())
                        {
                            try
                            {
                                // Bỏ qua nếu Id là array hoặc không hợp lệ
                                if (catElement.TryGetProperty("Id", out var idElement))
                                {
                                    if (idElement.ValueKind == JsonValueKind.Number)
                                    {
                                        var category = JsonSerializer.Deserialize<Category>(catElement.GetRawText());
                                        if (category != null && category.Id > 0)
                                        {
                                            categories.Add(category);
                                        }
                                    }
                                    // Bỏ qua nếu Id là array hoặc kiểu khác
                                }
                            }
                            catch
                            {
                                // Bỏ qua category không hợp lệ
                                continue;
                            }
                        }
                    }

                    // Parse Products
                    if (root.TryGetProperty("Products", out var productsElement) && productsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var prodElement in productsElement.EnumerateArray())
                        {
                            try
                            {
                                var product = JsonSerializer.Deserialize<Product>(prodElement.GetRawText());
                                if (product != null && !string.IsNullOrWhiteSpace(product.Name))
                                {
                                    products.Add(product);
                                }
                            }
                            catch
                            {
                                // Bỏ qua product không hợp lệ
                                continue;
                            }
                        }
                    }

                    return (categories, products);
                }
            }
            catch (Exception ex)
            {
                // Nếu JsonDocument không parse được, thử cách cũ
                try
                {
                    var json = File.ReadAllText(jsonPath, System.Text.Encoding.UTF8);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        AllowTrailingCommas = true
                    };
                    var data = JsonSerializer.Deserialize<JsonData>(json, options);
                    return (data?.Categories ?? new List<Category>(), data?.Products ?? new List<Product>());
                }
                catch
                {
                    // Trả về danh sách rỗng nếu không parse được
                    return (new List<Category>(), new List<Product>());
                }
            }
        }

        private class JsonData
        {
            public List<Category> Categories { get; set; } = new List<Category>();
            public List<Product> Products { get; set; } = new List<Product>();
            public DateTime LastUpdated { get; set; }
        }
    }

}

