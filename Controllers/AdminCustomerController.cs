using Microsoft.AspNetCore.Mvc;
using PCSTORE.Filters;
using PCSTORE.Services;
using PCSTORE.Models;

namespace PCSTORE.Controllers
{
    [AdminAuthorize]
    public class AdminCustomerController : Controller
    {
        private readonly CustomerService _customerService;
        private readonly ILogger<AdminCustomerController> _logger;

        public AdminCustomerController(CustomerService customerService, ILogger<AdminCustomerController> logger)
        {
            _customerService = customerService;
            _logger = logger;
        }

        public IActionResult Index()
        {
            try
            {
                var customers = _customerService.GetAllCustomers();
                ViewBag.Customers = customers;
                ViewBag.TotalCustomers = customers.Count;
                ViewBag.ActiveCustomers = customers.Count(c => c.IsActive);
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi load danh sách khách hàng");
                return View();
            }
        }

        [HttpGet]
        public IActionResult Detail(int id)
        {
            try
            {
                var customers = _customerService.GetAllCustomers();
                var customer = customers.FirstOrDefault(c => c.Id == id);
                if (customer == null)
                {
                    return RedirectToAction("Index");
                }
                return View(customer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi load chi tiết khách hàng");
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        public IActionResult ToggleActive(int id)
        {
            try
            {
                var customers = _customerService.GetAllCustomers();
                var customer = customers.FirstOrDefault(c => c.Id == id);
                if (customer != null)
                {
                    customer.IsActive = !customer.IsActive;
                    _customerService.SaveCustomers(customers);
                    TempData["Success"] = customer.IsActive ? "Đã kích hoạt tài khoản khách hàng." : "Đã khóa tài khoản khách hàng.";
                }
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật trạng thái khách hàng");
                TempData["Error"] = "Có lỗi xảy ra khi cập nhật trạng thái.";
                return RedirectToAction("Index");
            }
        }
    }
}

