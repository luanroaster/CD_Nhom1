using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using PCSTORE.Models;

namespace PCSTORE.Services
{
    /// <summary>
    /// Dịch vụ AI đơn giản phục vụ:
    /// - Trả lời chat hỗ trợ khách hàng (rule-based, không gọi API ngoài).
    /// - Đề xuất cấu hình PC từ kho sản phẩm hiện có, KHÔNG vượt ngân sách.
    /// </summary>
    public class AIChatService
    {
        private readonly DataStoreService _dataStore;
        private readonly ILogger<AIChatService> _logger;
        private readonly string _googleApiKey;

        public AIChatService(DataStoreService dataStore, ILogger<AIChatService> logger, IConfiguration configuration)
        {
            _dataStore = dataStore;
            _logger = logger;
            _googleApiKey = configuration["GoogleApiKey"] ?? string.Empty;
        }

        /// <summary>
        /// Trả lời chat ưu tiên qua Google Gemini; nếu lỗi API sẽ fallback về rule-based nội bộ.
        /// </summary>
        public async Task<string> GetAIResponseAsync(string userMessage, List<ChatMessage> history)
        {
            // Chỉ trả lời từ dữ liệu nếu câu hỏi có từ khóa cụ thể về sản phẩm (không quá chung chung)
            // Tránh bắt nhầm các câu hỏi chung chung như "PC có giá bao nhiêu?"
            var normalized = NormalizeText(userMessage);
            bool hasSpecificProductKeyword = normalized.Contains("cpu") || normalized.Contains("gpu") || 
                normalized.Contains("vga") || normalized.Contains("ram") || normalized.Contains("ssd") || 
                normalized.Contains("hdd") || normalized.Contains("main") || normalized.Contains("mainboard") ||
                normalized.Contains("psu") || normalized.Contains("case") || normalized.Contains("monitor") ||
                normalized.Contains("man hinh") || normalized.Contains("tan nhiet") || normalized.Contains("cooler") ||
                normalized.Contains("intel") || normalized.Contains("amd") || normalized.Contains("ryzen") ||
                normalized.Contains("nvidia") || normalized.Contains("asus") || normalized.Contains("msi") ||
                normalized.Contains("gigabyte") || normalized.Contains("corsair") || normalized.Contains("samsung");
            
            // Chỉ dùng TryAnswerFromData nếu có từ khóa cụ thể về sản phẩm
            if (hasSpecificProductKeyword && TryAnswerFromData(userMessage, out var dataAnswer))
            {
                return dataAnswer;
            }

            // Thử xây cấu hình từ ngân sách nếu người dùng đưa giá trị tiền
            if (TryBuildConfigResponse(userMessage, out var configAnswer))
            {
                return configAnswer;
            }

            // Nếu chưa cấu hình key thì trả lời theo rule-based
            if (string.IsNullOrWhiteSpace(_googleApiKey))
            {
                _logger.LogWarning("GoogleApiKey chưa được cấu hình, dùng fallback rule-based.");
                return GetFallbackResponse(userMessage);
            }

            try
            {
                using var httpClient = new HttpClient();

                var url =
                    $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-pro-exp-02-05:generateContent?key={_googleApiKey}";

                var systemInstruction =
                    "Bạn là trợ lý AI chuyên nghiệp của cửa hàng PC STORE - cửa hàng chuyên bán linh kiện máy tính và PC. " +
                    "NHIỆM VỤ CHÍNH: Trả lời chính xác, đúng trọng tâm câu hỏi của khách hàng về sản phẩm, giá cả, cấu hình PC, đặt hàng, bảo hành. " +
                    "\n\nQUY TẮC TRẢ LỜI:" +
                    "\n1. LUÔN trả lời bằng tiếng Việt, ngắn gọn, rõ ràng, đúng trọng tâm câu hỏi. KHÔNG lan man, KHÔNG nói dài dòng." +
                    "\n2. Khi khách hỏi về sản phẩm: Chỉ đề cập sản phẩm có trong dữ liệu được cung cấp. Nếu không có, nói rõ 'Hiện chưa có sản phẩm này trong kho' và gợi ý liên hệ cửa hàng." +
                    "\n3. Khi khách hỏi về giá: Chỉ dùng giá từ dữ liệu. Luôn nhắc 'Giá có thể thay đổi, vui lòng kiểm tra trên website hoặc liên hệ hotline để biết giá mới nhất'." +
                    "\n4. Khi khách hỏi về cấu hình PC: Hướng dẫn sử dụng tính năng 'Xây Dựng Cấu Hình' hoặc 'Cấu Hình AI' trên website, hoặc hỏi ngân sách để tư vấn." +
                    "\n5. Khi khách hỏi chung chung: Trả lời ngắn gọn, sau đó hỏi lại để hiểu rõ nhu cầu cụ thể." +
                    "\n6. KHÔNG bịa thông tin, KHÔNG tạo sản phẩm/giá không có trong dữ liệu. Nếu không chắc, nói rõ và gợi ý liên hệ cửa hàng." +
                    "\n7. Nếu câu hỏi không liên quan đến PC/linh kiện: Trả lời ngắn gọn, lịch sự, sau đó hỏi xem có cần tư vấn về sản phẩm PC không.";

                // Chuyển lịch sử chat sang format Gemini
                var contents = new List<object>();

                // Đưa dữ liệu sản phẩm vào ngữ cảnh để giảm sai lệch/hallucination
                var productContext = BuildProductContext();
                if (!string.IsNullOrWhiteSpace(productContext))
                {
                    contents.Add(new
                    {
                        role = "user",
                        parts = new[] { new { text = productContext } }
                    });
                }

                foreach (var item in history.TakeLast(10))
                {
                    var role = string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase)
                        ? "user"
                        : "model";

                    contents.Add(new
                    {
                        role,
                        parts = new[] { new { text = item.Content } }
                    });
                }

                // Thêm câu hỏi hiện tại
                contents.Add(new
                {
                    role = "user",
                    parts = new[] { new { text = userMessage } }
                });

                var requestBody = new
                {
                    systemInstruction = new
                    {
                        role = "system",
                        parts = new[] { new { text = systemInstruction } }
                    },
                    contents,
                    generationConfig = new
                    {
                        temperature = 0.3, // Giảm từ 0.7 xuống 0.3 để trả lời tập trung, đúng trọng tâm hơn
                        topP = 0.8, // Giảm từ 0.9 xuống 0.8 để ít lan man hơn
                        maxOutputTokens = 1024 // Tăng từ 512 lên 1024 để có thể trả lời đầy đủ hơn khi cần
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                using var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(url, httpContent);
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("Gemini API quá tải (429), chuyển sang trả lời nội bộ.");
                        return GetFallbackResponse(userMessage);
                    }

                    _logger.LogWarning("Gemini API trả về lỗi: {StatusCode}", response.StatusCode);
                    return GetFallbackResponse(userMessage);
                }

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);

                var root = doc.RootElement;
                if (root.TryGetProperty("candidates", out var candidatesElem) &&
                    candidatesElem.ValueKind == JsonValueKind.Array &&
                    candidatesElem.GetArrayLength() > 0)
                {
                    var first = candidatesElem[0];
                    if (first.TryGetProperty("content", out var contentElem) &&
                        contentElem.TryGetProperty("parts", out var partsElem) &&
                        partsElem.ValueKind == JsonValueKind.Array &&
                        partsElem.GetArrayLength() > 0)
                    {
                        var text = partsElem[0].GetProperty("text").GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text.Trim();
                        }
                    }
                }

                // Nếu không parse được → fallback
                _logger.LogWarning("Không đọc được nội dung từ phản hồi Gemini, dùng fallback.");
                return GetFallbackResponse(userMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gọi Google Gemini API, dùng fallback.");
                return GetFallbackResponse(userMessage);
            }
        }

        private string BuildProductContext()
        {
            var products = _dataStore.GetAllProducts();
            if (products == null || products.Count == 0)
            {
                return "Dữ liệu sản phẩm: hiện chưa có sản phẩm nào trong kho.";
            }

            var categories = _dataStore.GetAllCategories()?
                .ToDictionary(c => c.Id, c => c.Name ?? $"Danh mục {c.Id}") ?? new Dictionary<int, string>();

            var sb = new StringBuilder();
            sb.AppendLine("Dữ liệu sản phẩm hiện có của PC STORE (dùng đúng thông tin này, không bịa thêm):");

            var ordered = products
                .OrderByDescending(p => p.IsFeatured)
                .ThenBy(p => p.CategoryId)
                .ThenBy(p => p.Price == 0 ? decimal.MaxValue : p.Price)
                .Take(50) // tránh bơm context quá dài
                .ToList();

            foreach (var p in ordered)
            {
                var catName = categories.TryGetValue(p.CategoryId, out var name) ? name : "Danh mục khác";
                var desc = string.IsNullOrWhiteSpace(p.Description) ? p.Specs : p.Description;
                desc ??= string.Empty;
                if (desc.Length > 120)
                {
                    desc = desc.Substring(0, 117) + "...";
                }

                var priceText = p.Price > 0 ? $"{p.Price:N0}₫" : "Liên hệ";
                var stockText = p.Stock > 0 ? $"Tồn: {p.Stock}" : "Hết hàng";
                sb.AppendLine($"- [{p.Id}] {p.Name} | {catName} | {priceText} | {stockText} | {desc}");
            }

            if (products.Count > ordered.Count)
            {
                sb.AppendLine($"(Tóm tắt {ordered.Count}/{products.Count} sản phẩm; cần thêm hãy yêu cầu rõ danh mục/tên)");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Cố gắng trả lời câu hỏi dựa trên dữ liệu sản phẩm/danh mục nội bộ.
        /// </summary>
        private bool TryAnswerFromData(string userMessage, out string answer)
        {
            answer = string.Empty;
            if (string.IsNullOrWhiteSpace(userMessage))
                return false;

            var normalized = NormalizeText(userMessage);
            var products = _dataStore.GetAllProducts();
            var categories = _dataStore.GetAllCategories();

            if (products == null || products.Count == 0)
                return false;

            // Xác định intent tìm sản phẩm
            bool isProductIntent =
                normalized.Contains("san pham") ||
                normalized.Contains("gia") ||
                normalized.Contains("bao nhieu") ||
                normalized.Contains("co khong") ||
                normalized.Contains("con hang") ||
                normalized.Contains("mua") ||
                normalized.Contains("dat") ||
                normalized.Contains("pc") ||
                normalized.Contains("cpu") ||
                normalized.Contains("gpu") ||
                normalized.Contains("vga") ||
                normalized.Contains("ram") ||
                normalized.Contains("ssd") ||
                normalized.Contains("hdd") ||
                normalized.Contains("main") ||
                normalized.Contains("mainboard") ||
                normalized.Contains("psu") ||
                normalized.Contains("case") ||
                normalized.Contains("man hinh") ||
                normalized.Contains("monitor");

            if (!isProductIntent)
                return false;

            // Keywords từ câu hỏi
            var keywords = ExtractKeywords(normalized);

            // Map danh mục theo id -> name và từ khóa
            var categoryLookup = categories.ToDictionary(c => c.Id, c => NormalizeText(c.Name));
            var categoryKeywords = BuildCategoryKeywords();

            // Nếu người dùng hỏi chung một danh mục, ưu tiên lọc theo danh mục
            var targetCategoryIds = categoryKeywords
                .Where(kvp => normalized.Contains(kvp.Key))
                .Select(kvp => kvp.Value)
                .SelectMany(x => x)
                .Distinct()
                .ToHashSet();

            var matches = products
                .Select(p =>
                {
                    var score = 0;
                    var nameNorm = NormalizeText(p.Name);
                    var brandNorm = NormalizeText(p.Brand);

                    // Điểm theo từ khóa trong tên
                    foreach (var kw in keywords)
                    {
                        if (nameNorm.Contains(kw))
                            score += 3;
                        else if (!string.IsNullOrWhiteSpace(brandNorm) && brandNorm.Contains(kw))
                            score += 2;
                    }

                    // Điểm theo danh mục nếu khớp
                    if (targetCategoryIds.Count > 0 && targetCategoryIds.Contains(p.CategoryId))
                    {
                        score += 3;
                    }

                    // Điểm theo model code
                    var modelNorm = NormalizeText(p.ModelCode ?? string.Empty);
                    foreach (var kw in keywords)
                    {
                        if (!string.IsNullOrWhiteSpace(modelNorm) && modelNorm.Contains(kw))
                        {
                            score += 2;
                        }
                    }

                    // Điểm nhẹ nếu người dùng hỏi giá/stock
                    if (normalized.Contains("gia") || normalized.Contains("bao nhieu"))
                        score += 1;
                    if (normalized.Contains("con hang") || normalized.Contains("stock") || normalized.Contains("ton"))
                        score += 1;

                    return new { Product = p, Score = score };
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Product.Price == 0 ? decimal.MaxValue : x.Product.Price)
                .Take(6)
                .ToList();

            if (matches.Count == 0)
            {
                // Không tìm thấy, trả lời hướng dẫn
                answer =
                    "Mình chưa tìm thấy sản phẩm khớp câu hỏi. Bạn vui lòng cho biết rõ tên/loại sản phẩm (ví dụ: \"CPU i5 12400\", \"RAM 16GB\", \"SSD 1TB\"), mình sẽ tra giúp ngay.";
                return true;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Mình tìm thấy vài sản phẩm phù hợp:");

            foreach (var item in matches)
            {
                var p = item.Product;
                var catName = categories.FirstOrDefault(c => c.Id == p.CategoryId)?.Name ?? "Danh mục khác";
                var priceText = p.Price > 0 ? $"{p.Price:N0}₫" : "Liên hệ";
                var stockText = p.Stock > 0 ? $"Còn hàng: {p.Stock}" : "Hết hàng tạm thời";
                sb.AppendLine($"• {p.Name} – {priceText} ({catName}) | {stockText}");
            }

            sb.AppendLine();
            sb.Append("Bạn muốn xem chi tiết hoặc so sánh sản phẩm nào?");

            answer = sb.ToString().Trim();
            return true;
        }

        /// <summary>
        /// Nhận diện câu hỏi dạng "tư vấn cấu hình pc X triệu" và trả lời ngay.
        /// </summary>
        private bool TryBuildConfigResponse(string userMessage, out string answer)
        {
            answer = string.Empty;
            if (string.IsNullOrWhiteSpace(userMessage))
                return false;

            var normalized = NormalizeText(userMessage);

            // Bắt số tiền (triệu hoặc vnd)
            var match = Regex.Match(normalized, @"(\d+)\s*(tr|trieu|trệu|trieu|trieu?|\b000\b|trd|trvnd|vnd)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                // Thử tìm số lớn mặc định xem là đồng
                var numberOnly = Regex.Match(normalized, @"(\d{7,})");
                if (!numberOnly.Success)
                    return false;

                if (!decimal.TryParse(numberOnly.Groups[1].Value, out var rawAmount))
                    return false;

                return BuildConfigFromAmount(rawAmount, userMessage, out answer);
            }

            if (!int.TryParse(match.Groups[1].Value, out var million))
                return false;

            var budget = million * 1_000_000m;
            return BuildConfigFromAmount(budget, userMessage, out answer);
        }

        private bool BuildConfigFromAmount(decimal budget, string userMessage, out string answer)
        {
            answer = string.Empty;
            var products = _dataStore.GetAllProducts();

            if (products == null || products.Count == 0)
            {
                // Không có dữ liệu nội bộ -> để Gemini xử lý thay vì trả về thông báo trống
                return false;
            }

            var usage = ExtractUsage(userMessage);
            // Dùng format ngắn gọn cho chatbox thay vì HTML dài
            var configText = GenerateConfigForChatbox(budget, usage, string.Empty);

            answer = configText;
            return true;
        }

        private string ExtractUsage(string message)
        {
            var normalized = NormalizeText(message);
            if (normalized.Contains("game") || normalized.Contains("gaming"))
                return "gaming";
            if (normalized.Contains("do hoa") || normalized.Contains("render") || normalized.Contains("3d"))
                return "đồ họa / render";
            if (normalized.Contains("van phong") || normalized.Contains("office"))
                return "văn phòng / học tập";
            return "phổ thông";
        }

        private static string NormalizeText(string input)
        {
            var normalized = input.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var ch in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != UnicodeCategory.NonSpacingMark)
                {
                    if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
                        sb.Append(ch);
                }
            }
            return sb.ToString().Trim();
        }

        private static List<string> ExtractKeywords(string normalized)
        {
            var stopwords = new HashSet<string>(new[]
            {
                "la","là","cho","cua","của","co","có","khong","không","nao","nào","gi","gì",
                "toi","tôi","ban","bạn","mot","một","muon","muốn","hoi","hỏi","ve","về","hay","va","và"
            });

            return normalized
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 3 && !stopwords.Contains(w))
                .Distinct()
                .Take(12)
                .ToList();
        }

        private static Dictionary<string, List<int>> BuildCategoryKeywords()
        {
            // key: keyword đã normalize -> list category ids
            return new Dictionary<string, List<int>>
            {
                { "cpu", new List<int>{1} },
                { "processor", new List<int>{1} },
                { "main", new List<int>{2} },
                { "mainboard", new List<int>{2} },
                { "bo mach", new List<int>{2} },
                { "ram", new List<int>{3} },
                { "gpu", new List<int>{4} },
                { "vga", new List<int>{4} },
                { "card man hinh", new List<int>{4} },
                { "psu", new List<int>{5} },
                { "nguon", new List<int>{5} },
                { "case", new List<int>{6} },
                { "vo may", new List<int>{6} },
                { "ssd", new List<int>{7} },
                { "hdd", new List<int>{8} },
                { "o cung", new List<int>{7,8} },
                { "man hinh", new List<int>{9} },
                { "monitor", new List<int>{9} },
                { "tan nhiet nuoc", new List<int>{11} },
                { "tan nhiet khi", new List<int>{12} }
            };
        }

        /// <summary>
        /// Sinh cấu hình PC cho chatbox - format ngắn gọn, dễ đọc.
        /// </summary>
        private string GenerateConfigForChatbox(decimal budget, string usageScenario, string specialRequirements)
        {
            if (budget <= 0)
            {
                budget = 5_000_000;
            }

            var allocations = GetAllocationsForUsage(usageScenario);
            allocations = ApplySpecialRequirements(allocations, specialRequirements);

            var specialLower = (specialRequirements ?? string.Empty).ToLower();
            bool preferRyzenOrAmd = specialLower.Contains("ryzen") || specialLower.Contains("amd");
            bool preferIntel = specialLower.Contains("intel");
            var allProducts = _dataStore.GetAllProducts();
            var selections = new List<ComponentSelection>();

            decimal minTotal = 0;

            // Chọn linh kiện (tái sử dụng logic từ GenerateConfigFromData)
            foreach (var allocation in allocations)
            {
                var products = allProducts
                    .Where(p => p.CategoryId == allocation.CategoryId && p.Price > 0)
                    .ToList();

                if (allocation.CategoryId == 1 && products.Count > 0)
                {
                    if (preferRyzenOrAmd)
                    {
                        var filtered = products.Where(p => (p.Name ?? string.Empty).ToLower().Contains("ryzen") || (p.Name ?? string.Empty).ToLower().Contains("amd")).ToList();
                        if (filtered.Count > 0) products = filtered;
                    }
                    else if (preferIntel)
                    {
                        var filtered = products.Where(p => (p.Name ?? string.Empty).ToLower().Contains("intel") || (p.Name ?? string.Empty).ToLower().Contains("core i")).ToList();
                        if (filtered.Count > 0) products = filtered;
                    }
                }

                products = products.OrderBy(p => p.Price).ToList();
                if (products.Count == 0) continue;

                minTotal += products.First().Price;
                var targetPrice = budget * allocation.Weight;
                var bestIndex = products.FindLastIndex(p => p.Price <= targetPrice);
                if (bestIndex < 0) bestIndex = 0;

                var selected = products[bestIndex];
                selections.Add(new ComponentSelection
                {
                    CategoryId = allocation.CategoryId,
                    CategoryName = allocation.Name,
                    ProductName = selected.Name,
                    Price = selected.Price,
                    Notes = allocation.Notes,
                    Options = products,
                    SelectedIndex = bestIndex
                });
            }

            if (minTotal > budget)
            {
                return $"⚠️ Ngân sách {budget:N0}₫ chưa đủ. Cấu hình tối thiểu cần khoảng {minTotal:N0}₫.\n\nGợi ý: Tăng ngân sách hoặc giảm yêu cầu (bỏ HDD, giảm GPU).";
            }

            // Điều chỉnh giá để không vượt ngân sách và tối ưu
            var total = selections.Sum(s => s.Price);
            while (total > budget)
            {
                var best = selections
                    .Select(s =>
                    {
                        if (s.Options == null || s.SelectedIndex <= 0)
                            return (sel: s, delta: 0m, newIndex: s.SelectedIndex);
                        var newIndex = s.SelectedIndex - 1;
                        return (sel: s, delta: s.Price - s.Options[newIndex].Price, newIndex);
                    })
                    .Where(x => x.delta > 0)
                    .OrderByDescending(x => x.delta)
                    .FirstOrDefault();

                if (best.sel == null || best.delta <= 0) break;

                best.sel.SelectedIndex = best.newIndex;
                best.sel.ProductName = best.sel.Options[best.newIndex].Name;
                best.sel.Price = best.sel.Options[best.newIndex].Price;
                total -= best.delta;
            }

            // Nâng cấp để gần ngân sách
            var remainingBudget = budget - total;
            var threshold = budget * 0.05m;
            if (remainingBudget >= threshold)
            {
                var priorityCategories = new[] { 1, 4, 3, 7, 2, 5, 11, 12, 6, 8 };
                foreach (var categoryId in priorityCategories)
                {
                    if (remainingBudget <= threshold) break;
                    var selection = selections.FirstOrDefault(s => s.CategoryId == categoryId);
                    if (selection == null || selection.Options == null || selection.SelectedIndex >= selection.Options.Count - 1) continue;
                    
                    var nextIndex = selection.SelectedIndex + 1;
                    var nextProduct = selection.Options[nextIndex];
                    var upgradeCost = nextProduct.Price - selection.Price;
                    
                    if (total + upgradeCost <= budget && upgradeCost <= remainingBudget)
                    {
                        selection.SelectedIndex = nextIndex;
                        selection.ProductName = nextProduct.Name;
                        selection.Price = nextProduct.Price;
                        total += upgradeCost;
                        remainingBudget -= upgradeCost;
                    }
                }
            }

            // Format ngắn gọn cho chatbox
            var sb = new StringBuilder();
            sb.AppendLine($"💻 CẤU HÌNH PC");
            sb.AppendLine($"Ngân sách: {budget:N0}₫ | Chi phí: {total:N0}₫");
            
            var diff = budget - total;
            if (diff > 0)
            {
                sb.AppendLine($"Còn dư: {diff:N0}₫");
            }
            
            sb.AppendLine($"\n📦 Linh kiện:");
            foreach (var item in selections)
            {
                // Rút gọn tên sản phẩm nếu quá dài
                var productName = item.ProductName;
                if (productName.Length > 50)
                {
                    productName = productName.Substring(0, 47) + "...";
                }
                sb.AppendLine($"• {item.CategoryName}: {productName} - {item.Price:N0}₫");
            }

            sb.AppendLine($"\n💡 Xem chi tiết tại mục 'Xây Dựng Cấu Hình' trên website.");
            sb.AppendLine($"📞 Hotline: 1900-xxxx");

            return sb.ToString();
        }

        /// <summary>
        /// Sinh cấu hình PC từ dữ liệu sản phẩm hiện có, luôn đảm bảo tổng giá <= ngân sách.
        /// </summary>
        public string GenerateConfigFromData(decimal budget, string usageScenario, string specialRequirements)
        {
            if (budget <= 0)
            {
                budget = 5_000_000; // ngân sách tối thiểu an toàn
            }

            var allocations = GetAllocationsForUsage(usageScenario);
            allocations = ApplySpecialRequirements(allocations, specialRequirements);

            var specialLower = (specialRequirements ?? string.Empty).ToLower();
            bool preferRyzenOrAmd = specialLower.Contains("ryzen") || specialLower.Contains("amd");
            bool preferIntel = specialLower.Contains("intel");
            var allProducts = _dataStore.GetAllProducts();
            var selections = new List<ComponentSelection>();

            decimal minTotal = 0;

            // Chọn linh kiện cho từng hạng mục dựa trên phần trăm ngân sách
            foreach (var allocation in allocations)
            {
                var products = allProducts
                    .Where(p => p.CategoryId == allocation.CategoryId && p.Price > 0)
                    .ToList();

                // Ưu tiên hãng CPU theo yêu cầu đặc biệt (RYZEN/AMD hoặc Intel)
                if (allocation.CategoryId == 1 && products.Count > 0)
                {
                    if (preferRyzenOrAmd)
                    {
                        var filtered = products
                            .Where(p =>
                            {
                                var name = (p.Name ?? string.Empty).ToLower();
                                return name.Contains("ryzen") || name.Contains("amd");
                            })
                            .ToList();
                        if (filtered.Count > 0)
                        {
                            products = filtered;
                        }
                    }
                    else if (preferIntel)
                    {
                        var filtered = products
                            .Where(p =>
                            {
                                var name = (p.Name ?? string.Empty).ToLower();
                                return name.Contains("intel") || name.Contains("core i");
                            })
                            .ToList();
                        if (filtered.Count > 0)
                        {
                            products = filtered;
                        }
                    }
                }

                products = products
                    .OrderBy(p => p.Price)
                    .ToList();

                if (products.Count == 0) continue;

                var cheapest = products.First().Price;
                minTotal += cheapest;

                var targetPrice = budget * allocation.Weight;
                // Chọn sản phẩm có giá gần target nhất nhưng không vượt quá target, nếu không có thì chọn rẻ nhất
                var bestIndex = products.FindLastIndex(p => p.Price <= targetPrice);
                if (bestIndex < 0) bestIndex = 0;

                var selected = products[bestIndex];

                selections.Add(new ComponentSelection
                {
                    CategoryId = allocation.CategoryId,
                    CategoryName = allocation.Name,
                    ProductName = selected.Name,
                    Price = selected.Price,
                    Notes = allocation.Notes,
                    Options = products,
                    SelectedIndex = bestIndex
                });
            }

            // Nếu cấu hình rẻ nhất vẫn vượt ngân sách -> báo không thể build trong ngân sách
            if (minTotal > budget)
            {
                var sbLow = new StringBuilder();
                sbLow.AppendLine("<div class=\"ai-config-result\">");
                sbLow.AppendLine("  <div class=\"alert alert-warning\">");
                sbLow.AppendLine("    <h5 class=\"alert-heading mb-1\"><i class=\"fas fa-triangle-exclamation me-2\"></i>Ngân sách chưa đủ</h5>");
                sbLow.AppendLine($"    <p class=\"mb-1\">Ngân sách hiện tại: <strong>{budget:N0}₫</strong>.</p>");
                sbLow.AppendLine($"    <p class=\"mb-1\">Cấu hình tối thiểu phù hợp cần khoảng <strong>{minTotal:N0}₫</strong>.</p>");
                sbLow.AppendLine("    <p class=\"mb-0\">Hãy tăng ngân sách hoặc giảm bớt yêu cầu (ví dụ: bỏ HDD, giảm GPU) rồi thử lại.</p>");
                sbLow.AppendLine("  </div>");
                sbLow.AppendLine("</div>");
                return sbLow.ToString();
            }

            // Tính tổng và nếu cần thì hạ cấu hình để KHÔNG vượt ngân sách
            var total = selections.Sum(s => s.Price);
            while (total > budget)
            {
                var best = selections
                    .Select(s =>
                    {
                        if (s.Options == null || s.SelectedIndex <= 0)
                            return (sel: s, delta: 0m, newIndex: s.SelectedIndex);

                        var newIndex = s.SelectedIndex - 1;
                        var current = s.Price;
                        var next = s.Options[newIndex].Price;
                        return (sel: s, delta: current - next, newIndex);
                    })
                    .Where(x => x.delta > 0)
                    .OrderByDescending(x => x.delta)
                    .FirstOrDefault();

                if (best.sel == null || best.delta <= 0)
                {
                    break; // không còn gì để giảm
                }

                best.sel.SelectedIndex = best.newIndex;
                best.sel.ProductName = best.sel.Options[best.newIndex].Name;
                best.sel.Price = best.sel.Options[best.newIndex].Price;
                total -= best.delta;
            }

            // Nâng cấp các linh kiện để tổng giá gần với ngân sách nhất có thể
            // Chỉ nâng cấp nếu còn dư >= 5% ngân sách để tránh nâng cấp quá nhỏ không có ý nghĩa
            var remainingBudget = budget - total;
            var threshold = budget * 0.05m; // Ngưỡng 5% ngân sách
            
            if (remainingBudget >= threshold)
            {
                // Ưu tiên nâng cấp các linh kiện quan trọng: CPU (1), GPU (4), RAM (3), SSD (7)
                // Sắp xếp theo độ quan trọng và khả năng nâng cấp
                var priorityCategories = new[] { 1, 4, 3, 7, 2, 5, 11, 12, 6, 8 };
                
                foreach (var categoryId in priorityCategories)
                {
                    if (remainingBudget <= threshold) break;
                    
                    var selection = selections.FirstOrDefault(s => s.CategoryId == categoryId);
                    if (selection == null || selection.Options == null) continue;
                    
                    // Kiểm tra xem có thể nâng cấp không
                    if (selection.SelectedIndex >= selection.Options.Count - 1) continue;
                    
                    var nextIndex = selection.SelectedIndex + 1;
                    var nextProduct = selection.Options[nextIndex];
                    var upgradeCost = nextProduct.Price - selection.Price;
                    
                    // Chỉ nâng cấp nếu không vượt quá ngân sách và chi phí nâng cấp hợp lý
                    if (total + upgradeCost <= budget && upgradeCost <= remainingBudget)
                    {
                        selection.SelectedIndex = nextIndex;
                        selection.ProductName = nextProduct.Name;
                        selection.Price = nextProduct.Price;
                        total += upgradeCost;
                        remainingBudget -= upgradeCost;
                    }
                }
                
                // Nếu vẫn còn dư nhiều, tiếp tục nâng cấp các linh kiện khác
                if (remainingBudget >= threshold)
                {
                    var otherSelections = selections
                        .Where(s => s.Options != null && 
                                   s.SelectedIndex < s.Options.Count - 1 &&
                                   !priorityCategories.Contains(s.CategoryId))
                        .OrderByDescending(s => s.Options[s.SelectedIndex + 1].Price - s.Price)
                        .ToList();
                    
                    foreach (var selection in otherSelections)
                    {
                        if (remainingBudget <= threshold) break;
                        
                        var nextIndex = selection.SelectedIndex + 1;
                        var nextProduct = selection.Options[nextIndex];
                        var upgradeCost = nextProduct.Price - selection.Price;
                        
                        if (total + upgradeCost <= budget && upgradeCost <= remainingBudget)
                        {
                            selection.SelectedIndex = nextIndex;
                            selection.ProductName = nextProduct.Name;
                            selection.Price = nextProduct.Price;
                            total += upgradeCost;
                            remainingBudget -= upgradeCost;
                        }
                    }
                }
            }

            // Render HTML kết quả đẹp, dễ đọc
            var sb = new StringBuilder();
            var encodedUsage = WebUtility.HtmlEncode(usageScenario);
            var encodedSpecial = WebUtility.HtmlEncode(specialRequirements ?? string.Empty);

            sb.AppendLine("<div class=\"ai-config-result\">");
            sb.AppendLine("  <div class=\"ai-config-summary row g-3 mb-3\">");
            sb.AppendLine("    <div class=\"col-sm-4\">");
            sb.AppendLine("      <div class=\"summary-card\">");
            sb.AppendLine("        <div class=\"label\">Ngân sách</div>");
            sb.AppendLine($"        <div class=\"value text-danger\">{budget:N0}₫</div>");
            sb.AppendLine("      </div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class=\"col-sm-4\">");
            sb.AppendLine("      <div class=\"summary-card\">");
            sb.AppendLine("        <div class=\"label\">Chi phí ước tính</div>");
            sb.AppendLine($"        <div class=\"value text-success\">{total:N0}₫</div>");
            sb.AppendLine("      </div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class=\"col-sm-4\">");
            sb.AppendLine("      <div class=\"summary-card\">");
            sb.AppendLine("        <div class=\"label\">Mục đích sử dụng</div>");
            sb.AppendLine($"        <div class=\"value\">{encodedUsage}</div>");
            sb.AppendLine("      </div>");
            sb.AppendLine("    </div>");
            if (!string.IsNullOrWhiteSpace(encodedSpecial))
            {
                sb.AppendLine("    <div class=\"col-12\">");
                sb.AppendLine("      <div class=\"summary-card special\">");
                sb.AppendLine("        <div class=\"label\">Yêu cầu đặc biệt</div>");
                sb.AppendLine($"        <div class=\"value\">{encodedSpecial}</div>");
                sb.AppendLine("      </div>");
                sb.AppendLine("    </div>");
            }
            sb.AppendLine("  </div>");

            sb.AppendLine("  <div class=\"table-responsive mb-3\">");
            sb.AppendLine("    <table class=\"table table-hover align-middle\">");
            sb.AppendLine("      <thead class=\"table-dark\">");
            sb.AppendLine("        <tr><th>#</th><th>Linh kiện</th><th>Sản phẩm</th><th class=\"text-end\">Giá (₫)</th><th>Ghi chú</th></tr>");
            sb.AppendLine("      </thead>");
            sb.AppendLine("      <tbody>");

            int index = 1;
            foreach (var item in selections)
            {
                var name = WebUtility.HtmlEncode(item.ProductName);
                var note = WebUtility.HtmlEncode(item.Notes);
                sb.AppendLine("        <tr>");
                sb.AppendLine($"          <td>{index++}</td>");
                sb.AppendLine($"          <td><span class=\"badge bg-secondary-subtle text-dark\">{item.CategoryName}</span></td>");
                sb.AppendLine($"          <td>{name}</td>");
                sb.AppendLine($"          <td class=\"text-end fw-semibold\">{item.Price:N0}</td>");
                sb.AppendLine($"          <td>{note}</td>");
                sb.AppendLine("        </tr>");
            }

            sb.AppendLine("      </tbody>");
            sb.AppendLine("    </table>");
            sb.AppendLine("  </div>");

            var diff = budget - total;
            if (diff > 0)
            {
                sb.AppendLine($"  <div class=\"alert alert-success\"><i class=\"fas fa-coins me-2\"></i>Còn dư khoảng {diff:N0}₫ – bạn có thể dùng để nâng cấp thêm RAM, SSD hoặc phụ kiện.</div>");
            }

            sb.AppendLine("  <div class=\"cta-box\">");
            sb.AppendLine("    <p class=\"mb-2 fw-semibold\">✔️ Sẵn sàng lên đơn!</p>");
            sb.AppendLine("    <p class=\"mb-0\">Liên hệ hotline <strong>1900-xxxx</strong> hoặc ghé PC STORE để được lắp ráp và bảo hành chính hãng.</p>");
            sb.AppendLine("  </div>");
            sb.AppendLine("</div>");

            return sb.ToString();
        }

        /// <summary>
        /// Điều chỉnh cấu hình theo "yêu cầu đặc biệt" của khách (ví dụ: không cần card rời, ưu tiên RAM 32GB, chọn tản nước / tản khí...).
        /// </summary>
        private List<ComponentAllocation> ApplySpecialRequirements(List<ComponentAllocation> allocations, string? specialRequirements)
        {
            if (allocations == null || allocations.Count == 0)
                return allocations;

            var s = (specialRequirements ?? string.Empty).ToLower().Trim();
            if (string.IsNullOrWhiteSpace(s))
                return allocations;

            var result = allocations.ToList();

            // 1) KHÔNG CẦN CARD RỜI (dùng iGPU)
            if ((s.Contains("không cần card") || s.Contains("không card") || s.Contains("không cần vga") ||
                 s.Contains("khong can card") || s.Contains("khong card") || s.Contains("khong can vga") ||
                 s.Contains("dùng igpu") || s.Contains("dung igpu") || s.Contains("card on") || s.Contains("card onboard")) &&
                result.Any(a => a.CategoryId == 4))
            {
                result = result.Where(a => a.CategoryId != 4).ToList();
            }

            // 2) ƯU TIÊN TẢN NHIỆT NƯỚC / TẢN NHIỆT KHÍ
            bool wantWater = s.Contains("tản nước") || s.Contains("tan nuoc") || s.Contains("water") || s.Contains("aio");
            bool wantAir = s.Contains("tản khí") || s.Contains("tan khi") || s.Contains("air cool");

            if (wantWater || wantAir)
            {
                var currentCooler = result.FirstOrDefault(a => a.CategoryId == 11 || a.CategoryId == 12);
                if (currentCooler != null)
                {
                    var weight = currentCooler.Weight;
                    result = result.Where(a => a.CategoryId != 11 && a.CategoryId != 12).ToList();

                    if (wantWater)
                    {
                        result.Add(new ComponentAllocation(
                            11,
                            "Tản nhiệt nước",
                            weight,
                            "Theo yêu cầu: ưu tiên tản nhiệt nước"
                        ));
                    }
                    else if (wantAir)
                    {
                        result.Add(new ComponentAllocation(
                            12,
                            "Tản nhiệt khí",
                            weight,
                            "Theo yêu cầu: ưu tiên tản nhiệt khí"
                        ));
                    }
                }
            }

            // 3) ƯU TIÊN RAM 32GB / RAM NHIỀU
            if (s.Contains("ram 32") || s.Contains("32gb") || s.Contains("32 gb") ||
                s.Contains("ram 64") || s.Contains("64gb") || s.Contains("64 gb") ||
                s.Contains("ưu tiên ram") || s.Contains("uu tien ram"))
            {
                result = result
                    .Select(a =>
                    {
                        if (a.CategoryId == 3) // RAM
                        {
                            var notes = string.IsNullOrEmpty(a.Notes)
                                ? "Ưu tiên RAM dung lượng lớn theo yêu cầu"
                                : a.Notes + " – ưu tiên RAM dung lượng lớn theo yêu cầu";
                            return new ComponentAllocation(a.CategoryId, a.Name, a.Weight * 1.3m, notes);
                        }
                        return a;
                    })
                    .ToList();
            }

            // 4) KHÔNG CẦN MÀN HÌNH (nếu sau này có thêm màn hình vào phân bổ)
            if ((s.Contains("không cần màn") || s.Contains("không màn") || s.Contains("khong can man") ||
                 s.Contains("khong man") || s.Contains("không cần màn hình") || s.Contains("khong can man hinh")) &&
                result.Any(a => a.CategoryId == 9))
            {
                result = result.Where(a => a.CategoryId != 9).ToList();
            }

            // Chuẩn hóa lại tổng trọng số nếu > 1 để vẫn đảm bảo không vượt ngân sách
            var sum = result.Sum(a => a.Weight);
            if (sum > 1m)
            {
                var factor = 1m / sum;
                result = result
                    .Select(a => new ComponentAllocation(a.CategoryId, a.Name, a.Weight * factor, a.Notes))
                    .ToList();
            }

            return result;
        }

        private string GetFallbackResponse(string userMessage)
        {
            var message = (userMessage ?? string.Empty).ToLower();

            if (message.Contains("xin chào") || message.Contains("hello") || message.Contains("chào"))
            {
                return "Xin chào! 👋 Tôi là trợ lý AI của PC STORE. Tôi có thể giúp bạn:\n\n" +
                       "• Tư vấn cấu hình PC theo ngân sách\n" +
                       "• Gợi ý linh kiện phù hợp nhu cầu\n" +
                       "• Hướng dẫn đặt hàng và bảo hành\n\n" +
                       "Bạn muốn hỏi về vấn đề nào?";
            }

            if (message.Contains("giá") || message.Contains("bao nhiêu") || message.Contains("price"))
            {
                return "Giá sản phẩm được hiển thị trực tiếp trên website PC STORE và cập nhật liên tục.\n\n" +
                       "Bạn hãy cho tôi biết tên sản phẩm hoặc khoảng ngân sách, tôi sẽ gợi ý cấu hình / sản phẩm phù hợp.";
            }

            if (message.Contains("cấu hình") || message.Contains("build") || message.Contains("xây dựng"))
            {
                return "Để xây dựng cấu hình PC:\n\n" +
                       "1️⃣ Vào mục \"Xây dựng cấu hình\" hoặc \"Cấu hình AI\" trên menu.\n" +
                       "2️⃣ Nhập ngân sách, mục đích sử dụng và yêu cầu đặc biệt.\n" +
                       "3️⃣ Hệ thống sẽ gợi ý cấu hình tối ưu từ kho linh kiện hiện có.\n\n" +
                       "Bạn cũng có thể gửi cho tôi: ngân sách + nhu cầu + yêu cầu đặc biệt, tôi sẽ gợi ý giúp bạn.";
            }

            if (message.Contains("liên hệ") || message.Contains("địa chỉ") || message.Contains("hotline"))
            {
                return "📞 Thông tin liên hệ PC STORE:\n\n" +
                       "- Hotline: 1900-xxxx\n" +
                       "- Email: support@pcstore.vn\n" +
                       "- Địa chỉ: 123 Đường ABC, Quận XYZ, TP.HCM\n" +
                       "- Giờ làm việc: 8:00 – 22:00 (tất cả các ngày).";
            }

            return "Tôi là trợ lý AI của PC STORE. Tôi có thể giúp bạn:\n\n" +
                   "• Tư vấn cấu hình PC theo ngân sách\n" +
                   "• Gợi ý linh kiện: CPU, Main, RAM, GPU, SSD, PSU, Case, tản nhiệt...\n" +
                   "• Hướng dẫn đặt hàng và bảo hành\n\n" +
                   "Hãy cho tôi biết ngân sách và mục đích sử dụng (vd: gaming, đồ họa, văn phòng...), tôi sẽ gợi ý cấu hình chi tiết.";
        }

        private List<ComponentAllocation> GetAllocationsForUsage(string usage)
        {
            var u = (usage ?? string.Empty).ToLower();

            bool highLoad = u.Contains("gaming") || u.Contains("đồ họa") || u.Contains("ai") || u.Contains("render");

            // Các trọng số được thiết kế sao cho tổng <= 1
            decimal wCpu = 0.18m;
            decimal wMain = 0.11m;
            decimal wRam = 0.10m;
            decimal wGpu = highLoad ? 0.28m : 0.22m;
            decimal wSsd = 0.09m;
            decimal wHdd = (u.Contains("đồ họa") || u.Contains("lưu trữ")) ? 0.05m : 0.03m;
            decimal wPsu = 0.07m;
            decimal wCase = 0.04m;
            decimal wCoolerWater = 0.05m;
            decimal wCoolerAir = 0.04m;

            var allocations = new List<ComponentAllocation>
            {
                new(1, "CPU", wCpu, "Nguồn sức mạnh xử lý"),
                new(2, "Mainboard", wMain, "Bo mạch chủ tương thích, dễ nâng cấp"),
                new(3, "RAM", wRam, "Đảm bảo đa nhiệm mượt mà"),
                new(4, "GPU", wGpu, "Xử lý đồ họa / render"),
                new(7, "SSD", wSsd, "Ổ cứng hệ điều hành & phần mềm"),
                new(8, "HDD", wHdd, "Lưu trữ dữ liệu, game, film"),
                new(5, "PSU", wPsu, "Nguồn chuẩn 80+ ổn định"),
                new(6, "Case", wCase, "Vỏ máy thoáng, dễ nâng cấp")
            };

            // Chỉ chọn 1 trong 2 loại tản nhiệt
            if (highLoad)
            {
                allocations.Add(new ComponentAllocation(11, "Tản nhiệt nước", wCoolerWater, "Giữ nhiệt độ CPU ổn định khi tải nặng"));
            }
            else
            {
                allocations.Add(new ComponentAllocation(12, "Tản nhiệt khí", wCoolerAir, "Hiệu quả, chi phí hợp lý, dễ lắp đặt"));
            }

            // Điều chỉnh trọng số an toàn nếu tổng > 1
            var sum = allocations.Sum(a => a.Weight);
            if (sum > 1m)
            {
                var factor = 1m / sum;
                allocations = allocations
                    .Select(a => new ComponentAllocation(a.CategoryId, a.Name, a.Weight * factor, a.Notes))
                    .ToList();
            }

            return allocations;
        }
    }

    public class ChatMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    internal record ComponentAllocation(int CategoryId, string Name, decimal Weight, string Notes);

    internal class ComponentSelection
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Notes { get; set; } = string.Empty;

        public List<Product>? Options { get; set; }
        public int SelectedIndex { get; set; }
    }
}


