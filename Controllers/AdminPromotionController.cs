using Microsoft.AspNetCore.Mvc;
using PCSTORE.Filters;
using PCSTORE.Models;
using System.Text.Json;

namespace PCSTORE.Controllers
{
    [AdminAuthorize]
    public class AdminPromotionController : Controller
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<AdminPromotionController> _logger;
        private readonly string _promotionPath;
        private readonly string _bannerPath;
        private List<Promotion> _promotions = new List<Promotion>();
        private List<Banner> _banners = new List<Banner>();

        public AdminPromotionController(
            IWebHostEnvironment environment,
            ILogger<AdminPromotionController> logger)
        {
            _environment = environment;
            _logger = logger;
            var dataDir = Path.Combine(_environment.ContentRootPath, "Data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            _promotionPath = Path.Combine(dataDir, "promotions.json");
            _bannerPath = Path.Combine(dataDir, "banners.json");
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                if (System.IO.File.Exists(_promotionPath))
                {
                    var json = System.IO.File.ReadAllText(_promotionPath);
                    _promotions = JsonSerializer.Deserialize<List<Promotion>>(json) ?? new List<Promotion>();
                }
                if (System.IO.File.Exists(_bannerPath))
                {
                    var json = System.IO.File.ReadAllText(_bannerPath);
                    _banners = JsonSerializer.Deserialize<List<Banner>>(json) ?? new List<Banner>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi load promotion data");
            }
        }

        private void SavePromotions()
        {
            try
            {
                var json = JsonSerializer.Serialize(_promotions, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(_promotionPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi save promotions");
            }
        }

        private void SaveBanners()
        {
            try
            {
                var json = JsonSerializer.Serialize(_banners, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(_bannerPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi save banners");
            }
        }

        // Promotion Management
        public IActionResult Promotions()
        {
            LoadData();
            ViewBag.Promotions = _promotions.OrderByDescending(p => p.CreatedAt).ToList();
            ViewBag.ActivePromotions = _promotions.Count(p => p.IsActive && p.EndDate >= DateTime.Now);
            return View();
        }

        [HttpGet]
        public IActionResult PromotionForm(int? id)
        {
            LoadData();
            if (id.HasValue && id.Value > 0)
            {
                var promotion = _promotions.FirstOrDefault(p => p.Id == id.Value);
                if (promotion == null)
                {
                    return RedirectToAction("Promotions");
                }
                return View(promotion);
            }
            return View(new Promotion { StartDate = DateTime.Now, EndDate = DateTime.Now.AddDays(30) });
        }

        [HttpPost]
        public IActionResult PromotionForm(Promotion promotion)
        {
            if (string.IsNullOrWhiteSpace(promotion.Code) || string.IsNullOrWhiteSpace(promotion.Name))
            {
                TempData["Error"] = "Vui lòng nhập đầy đủ thông tin.";
                return View(promotion);
            }

            try
            {
                LoadData();
                if (promotion.Id > 0)
                {
                    var existing = _promotions.FirstOrDefault(p => p.Id == promotion.Id);
                    if (existing != null)
                    {
                        existing.Code = promotion.Code;
                        existing.Name = promotion.Name;
                        existing.Description = promotion.Description;
                        existing.Type = promotion.Type;
                        existing.Value = promotion.Value;
                        existing.MinOrderAmount = promotion.MinOrderAmount;
                        existing.MaxUsage = promotion.MaxUsage;
                        existing.StartDate = promotion.StartDate;
                        existing.EndDate = promotion.EndDate;
                        existing.IsActive = promotion.IsActive;
                    }
                }
                else
                {
                    promotion.Id = _promotions.Count > 0 ? _promotions.Max(p => p.Id) + 1 : 1;
                    promotion.CreatedAt = DateTime.Now;
                    promotion.UsedCount = 0;
                    _promotions.Add(promotion);
                }
                SavePromotions();
                TempData["Success"] = promotion.Id > 0 ? "Đã cập nhật mã khuyến mãi thành công." : "Đã thêm mã khuyến mãi mới thành công.";
                return RedirectToAction("Promotions");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu mã khuyến mãi");
                TempData["Error"] = "Có lỗi xảy ra khi lưu mã khuyến mãi.";
                return View(promotion);
            }
        }

        [HttpPost]
        public IActionResult DeletePromotion(int id)
        {
            try
            {
                LoadData();
                var promotion = _promotions.FirstOrDefault(p => p.Id == id);
                if (promotion != null)
                {
                    _promotions.Remove(promotion);
                    SavePromotions();
                    TempData["Success"] = "Đã xóa mã khuyến mãi thành công.";
                }
                return RedirectToAction("Promotions");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa mã khuyến mãi");
                TempData["Error"] = "Có lỗi xảy ra khi xóa mã khuyến mãi.";
                return RedirectToAction("Promotions");
            }
        }

        // Banner Management
        public IActionResult Banners()
        {
            LoadData();
            ViewBag.Banners = _banners.OrderBy(b => b.DisplayOrder).ToList();
            return View();
        }

        [HttpGet]
        public IActionResult BannerForm(int? id)
        {
            LoadData();
            if (id.HasValue && id.Value > 0)
            {
                var banner = _banners.FirstOrDefault(b => b.Id == id.Value);
                if (banner == null)
                {
                    return RedirectToAction("Banners");
                }
                return View(banner);
            }
            return View(new Banner { DisplayOrder = _banners.Count > 0 ? _banners.Max(b => b.DisplayOrder) + 1 : 1 });
        }

        [HttpPost]
        public IActionResult BannerForm(Banner banner)
        {
            if (string.IsNullOrWhiteSpace(banner.Title) || string.IsNullOrWhiteSpace(banner.ImageUrl))
            {
                TempData["Error"] = "Vui lòng nhập đầy đủ thông tin.";
                return View(banner);
            }

            try
            {
                LoadData();
                if (banner.Id > 0)
                {
                    var existing = _banners.FirstOrDefault(b => b.Id == banner.Id);
                    if (existing != null)
                    {
                        existing.Title = banner.Title;
                        existing.Description = banner.Description;
                        existing.ImageUrl = banner.ImageUrl;
                        existing.LinkUrl = banner.LinkUrl;
                        existing.DisplayOrder = banner.DisplayOrder;
                        existing.IsActive = banner.IsActive;
                    }
                }
                else
                {
                    banner.Id = _banners.Count > 0 ? _banners.Max(b => b.Id) + 1 : 1;
                    banner.CreatedAt = DateTime.Now;
                    _banners.Add(banner);
                }
                SaveBanners();
                TempData["Success"] = banner.Id > 0 ? "Đã cập nhật banner thành công." : "Đã thêm banner mới thành công.";
                return RedirectToAction("Banners");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu banner");
                TempData["Error"] = "Có lỗi xảy ra khi lưu banner.";
                return View(banner);
            }
        }

        [HttpPost]
        public IActionResult DeleteBanner(int id)
        {
            try
            {
                LoadData();
                var banner = _banners.FirstOrDefault(b => b.Id == id);
                if (banner != null)
                {
                    _banners.Remove(banner);
                    SaveBanners();
                    TempData["Success"] = "Đã xóa banner thành công.";
                }
                return RedirectToAction("Banners");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa banner");
                TempData["Error"] = "Có lỗi xảy ra khi xóa banner.";
                return RedirectToAction("Banners");
            }
        }
    }
}

