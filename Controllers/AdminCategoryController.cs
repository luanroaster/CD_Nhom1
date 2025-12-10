using Microsoft.AspNetCore.Mvc;
using PCSTORE.Filters;
using PCSTORE.Services;
using PCSTORE.Models;

namespace PCSTORE.Controllers
{
    [AdminAuthorize]
    public class AdminCategoryController : Controller
    {
        private readonly DataStoreService _dataStore;
        private readonly ILogger<AdminCategoryController> _logger;

        public AdminCategoryController(DataStoreService dataStore, ILogger<AdminCategoryController> logger)
        {
            _dataStore = dataStore;
            _logger = logger;
        }

        public IActionResult Index()
        {
            var categories = _dataStore.GetAllCategories();
            ViewBag.Categories = categories;
            return View();
        }

        [HttpGet]
        public IActionResult Form(int? id)
        {
            if (id.HasValue && id.Value > 0)
            {
                var category = _dataStore.GetAllCategories().FirstOrDefault(c => c.Id == id.Value);
                if (category == null)
                {
                    return RedirectToAction("Index");
                }
                return View(category);
            }
            return View(new Category());
        }

        [HttpPost]
        public IActionResult Form(Category category)
        {
            if (string.IsNullOrWhiteSpace(category.Name))
            {
                TempData["Error"] = "Vui lòng nhập tên danh mục.";
                return View(category);
            }

            try
            {
                if (category.Id > 0)
                {
                    // Update category
                    _dataStore.UpdateCategory(category);
                    TempData["Success"] = "Đã cập nhật danh mục thành công.";
                }
                else
                {
                    // Add new category
                    _dataStore.AddCategory(category);
                    TempData["Success"] = "Đã thêm danh mục mới thành công.";
                }

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu danh mục");
                TempData["Error"] = "Có lỗi xảy ra khi lưu danh mục.";
                return View(category);
            }
        }

        [HttpPost]
        public IActionResult Delete(int id)
        {
            try
            {
                _dataStore.DeleteCategory(id);
                TempData["Success"] = "Đã xóa danh mục thành công.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa danh mục");
                TempData["Error"] = "Có lỗi xảy ra khi xóa danh mục.";
                return RedirectToAction("Index");
            }
        }
    }
}

