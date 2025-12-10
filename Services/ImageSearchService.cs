using System.Text.Json;
using System.Text.RegularExpressions;
using PCSTORE.Models;

namespace PCSTORE.Services
{
    /// <summary>
    /// Service để tìm kiếm và tải hình ảnh sản phẩm từ internet dựa trên tên sản phẩm
    /// </summary>
    public class ImageSearchService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ImageSearchService> _logger;
        private readonly IConfiguration _configuration;

        public ImageSearchService(HttpClient httpClient, ILogger<ImageSearchService> logger, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;
            
            // Set user agent để tránh bị chặn
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        /// <summary>
        /// Tìm kiếm URL hình ảnh từ tên sản phẩm
        /// Sử dụng nhiều nguồn để tìm hình ảnh phù hợp
        /// </summary>
        public async Task<string?> SearchImageUrlAsync(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName))
                return null;

            try
            {
                // Làm sạch tên sản phẩm để tìm kiếm tốt hơn
                var cleanName = CleanProductName(productName);
                
                // Thử 1: Sử dụng Bing Image Search (nếu có API key)
                var bingImageUrl = await SearchImageFromBingAsync(cleanName);
                if (!string.IsNullOrEmpty(bingImageUrl))
                {
                    _logger.LogInformation($"Tìm thấy hình ảnh từ Bing cho '{productName}'");
                    return bingImageUrl;
                }

                // Thử 2: Sử dụng placeholder với seed dựa trên tên sản phẩm
                var unsplashUrl = await SearchImageFromUnsplashAsync(cleanName);
                if (!string.IsNullOrEmpty(unsplashUrl))
                {
                    return unsplashUrl;
                }

                // Fallback: Sử dụng placeholder
                var hash = productName.GetHashCode();
                var seed = Math.Abs(hash);
                var picsumUrl = $"https://picsum.photos/seed/{seed}/400/400";
                return picsumUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi tìm kiếm hình ảnh cho '{productName}'");
                return null;
            }
        }

        /// <summary>
        /// Làm sạch tên sản phẩm để tìm kiếm tốt hơn
        /// </summary>
        private string CleanProductName(string productName)
        {
            // Loại bỏ các thông tin kỹ thuật chi tiết, chỉ giữ tên chính
            var cleaned = productName;
            
            // Loại bỏ các phần trong ngoặc đơn
            cleaned = Regex.Replace(cleaned, @"\([^)]*\)", "");
            
            // Loại bỏ các số và ký tự đặc biệt không cần thiết
            cleaned = Regex.Replace(cleaned, @"\d+[GM]B|\d+[GM]Hz|\d+Core|\d+Thread", "");
            
            // Lấy 5-7 từ đầu tiên (thường là tên chính)
            var words = cleaned.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Take(7)
                .ToArray();
            
            return string.Join(" ", words).Trim();
        }

        /// <summary>
        /// Tìm kiếm hình ảnh từ Pexels (miễn phí, cần API key nhưng có thể dùng không key với giới hạn)
        /// </summary>
        private async Task<string?> SearchImageFromPexelsAsync(string productName)
        {
            try
            {
                // Pexels có API miễn phí nhưng cần key
                // Nếu không có key, có thể sử dụng Unsplash thay thế
                // Ở đây tôi sẽ trả về null để fallback sang Unsplash
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Lỗi khi tìm kiếm từ Pexels cho '{productName}'");
                return null;
            }
        }

        /// <summary>
        /// Tìm kiếm hình ảnh từ Google Images
        /// </summary>
        private async Task<string?> SearchImageFromGoogleAsync(string productName)
        {
            try
            {
                // Tạo query tìm kiếm
                var searchQuery = Uri.EscapeDataString(productName + " product");
                var searchUrl = $"https://www.google.com/search?q={searchQuery}&tbm=isch&safe=active";
                
                var response = await _httpClient.GetStringAsync(searchUrl);
                
                // Parse HTML để tìm URL hình ảnh đầu tiên
                // Google Images embed JSON data trong script tags
                var imageUrl = ExtractImageUrlFromGoogleSearch(response);
                
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    return imageUrl;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Lỗi khi tìm kiếm từ Google Images cho '{productName}'");
            }

            return null;
        }

        /// <summary>
        /// Extract image URL từ Google Images search result
        /// </summary>
        private string? ExtractImageUrlFromGoogleSearch(string html)
        {
            try
            {
                // Tìm pattern: "ou":"https://..." trong JSON data
                var patterns = new[]
                {
                    @"\[""ou"",""([^""]+)""\]",
                    @"""ou"":\s*""([^""]+)""",
                    @"""url"":\s*""([^""]+)""",
                    @"src=""([^""]+)""[^>]*class=""[^""]*rg_i"
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var imageUrl = match.Groups[1].Value;
                        imageUrl = Uri.UnescapeDataString(imageUrl);
                        
                        // Kiểm tra xem có phải là URL hợp lệ không
                        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) && 
                            (uri.Scheme == "http" || uri.Scheme == "https") &&
                            (imageUrl.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                             imageUrl.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                             imageUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                             imageUrl.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                             imageUrl.Contains("googleusercontent.com") ||
                             imageUrl.Contains("imgur.com") ||
                             imageUrl.Contains("i.imgur.com")))
                        {
                            return imageUrl;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi khi parse HTML từ Google Images");
            }

            return null;
        }

        /// <summary>
        /// Tìm kiếm hình ảnh từ Bing Image Search API (nếu có API key)
        /// </summary>
        private async Task<string?> SearchImageFromBingAsync(string productName)
        {
            try
            {
                // Bing Image Search API cần API key
                // Nếu không có key, trả về null
                var apiKey = _configuration["BingImageSearchApiKey"];
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return null;
                }

                var query = Uri.EscapeDataString(productName);
                var url = $"https://api.bing.microsoft.com/v7.0/images/search?q={query}&count=1&safeSearch=Strict";
                
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);
                
                var response = await _httpClient.GetStringAsync(url);
                var jsonDoc = JsonDocument.Parse(response);
                
                if (jsonDoc.RootElement.TryGetProperty("value", out var valueArray) && 
                    valueArray.GetArrayLength() > 0)
                {
                    var firstImage = valueArray[0];
                    if (firstImage.TryGetProperty("contentUrl", out var contentUrl))
                    {
                        return contentUrl.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Lỗi khi tìm kiếm từ Bing cho '{productName}'");
            }

            return null;
        }

        /// <summary>
        /// Tìm kiếm hình ảnh từ Unsplash API
        /// </summary>
        private async Task<string?> SearchImageFromUnsplashAsync(string productName)
        {
            try
            {
                // Unsplash API miễn phí (có giới hạn 50 requests/giờ nếu không có key)
                // Tạo query từ tên sản phẩm
                var query = Uri.EscapeDataString(productName);
                var url = $"https://api.unsplash.com/search/photos?query={query}&per_page=1&client_id=YOUR_UNSPLASH_ACCESS_KEY";
                
                // Nếu không có API key, sử dụng Unsplash Source (không còn hoạt động)
                // Fallback: Sử dụng placeholder với seed dựa trên tên sản phẩm
                var hash = productName.GetHashCode();
                var seed = Math.Abs(hash);
                
                // Sử dụng Picsum Photos với seed (ảnh placeholder đẹp, ổn định)
                var picsumUrl = $"https://picsum.photos/seed/{seed}/400/400";
                
                // Thử tìm ảnh từ các nguồn khác dựa trên keyword
                var keywords = ExtractKeywords(productName);
                if (keywords.Count > 0)
                {
                    // Thử tìm ảnh từ Lorem Picsum với keyword
                    var keywordUrl = $"https://picsum.photos/seed/{Uri.EscapeDataString(keywords[0])}/400/400";
                    return keywordUrl;
                }
                
                return picsumUrl;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Lỗi khi tìm kiếm từ Unsplash cho '{productName}'");
            }

            return null;
        }

        /// <summary>
        /// Extract keywords từ tên sản phẩm để tìm kiếm ảnh tốt hơn
        /// </summary>
        private List<string> ExtractKeywords(string productName)
        {
            var keywords = new List<string>();
            var cleaned = CleanProductName(productName);
            
            // Tìm các từ khóa quan trọng (brand, model, type)
            var importantWords = new[] { "CPU", "GPU", "RAM", "SSD", "HDD", "PSU", "Mainboard", "Monitor", "Keyboard", "Mouse", "Intel", "AMD", "NVIDIA", "ASUS", "MSI", "Gigabyte" };
            
            foreach (var word in importantWords)
            {
                if (productName.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    keywords.Add(word);
                }
            }
            
            // Nếu không tìm thấy từ khóa quan trọng, lấy 2-3 từ đầu tiên
            if (keywords.Count == 0)
            {
                var words = cleaned.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                    .Take(3)
                    .ToArray();
                keywords.AddRange(words);
            }
            
            return keywords;
        }

        /// <summary>
        /// Tìm kiếm và cập nhật hình ảnh cho tất cả sản phẩm không có hình ảnh
        /// </summary>
        public async Task<int> UpdateAllProductImagesAsync(DataStoreService dataStore, int maxProducts = 100, int delayMs = 2000)
        {
            var products = dataStore.GetAllProducts()
                .Where(p => string.IsNullOrWhiteSpace(p.ImageUrl) || 
                           p.ImageUrl.Contains("placeholder") || 
                           p.ImageUrl.Contains("via.placeholder"))
                .Take(maxProducts)
                .ToList();

            int updatedCount = 0;

            foreach (var product in products)
            {
                try
                {
                    _logger.LogInformation($"Đang tìm hình ảnh cho sản phẩm: {product.Name} (ID: {product.Id})");
                    
                    var imageUrl = await SearchImageUrlAsync(product.Name);
                    
                    if (!string.IsNullOrEmpty(imageUrl))
                    {
                        product.ImageUrl = imageUrl;
                        dataStore.UpdateProduct(product);
                        updatedCount++;
                        _logger.LogInformation($"✓ Đã cập nhật hình ảnh cho sản phẩm ID {product.Id}");
                    }
                    else
                    {
                        _logger.LogWarning($"✗ Không tìm thấy hình ảnh cho sản phẩm ID {product.Id}: {product.Name}");
                    }

                    // Delay để tránh bị rate limit
                    await Task.Delay(delayMs);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Lỗi khi cập nhật hình ảnh cho sản phẩm ID {product.Id}");
                }
            }

            return updatedCount;
        }

        /// <summary>
        /// Tìm kiếm và cập nhật hình ảnh cho một sản phẩm cụ thể
        /// </summary>
        public async Task<bool> UpdateProductImageAsync(DataStoreService dataStore, int productId)
        {
            var product = dataStore.GetProductById(productId);
            if (product == null)
                return false;

            try
            {
                var imageUrl = await SearchImageUrlAsync(product.Name);
                
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    product.ImageUrl = imageUrl;
                    dataStore.UpdateProduct(product);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi cập nhật hình ảnh cho sản phẩm ID {productId}");
            }

            return false;
        }
    }
}

