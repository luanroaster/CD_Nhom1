using Microsoft.AspNetCore.Mvc;
using PCSTORE.Filters;
using PCSTORE.Models;
using System.Text.Json;

namespace PCSTORE.Controllers
{
    [AdminAuthorize]
    public class AdminOrderController : Controller
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<AdminOrderController> _logger;
        private readonly string _orderPath;
        private List<Order> _orders = new List<Order>();

        public AdminOrderController(IWebHostEnvironment environment, ILogger<AdminOrderController> logger)
        {
            _environment = environment;
            _logger = logger;
            var dataDir = Path.Combine(_environment.ContentRootPath, "Data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            _orderPath = Path.Combine(dataDir, "orders.json");
            LoadOrders();
        }

        private void LoadOrders()
        {
            try
            {
                if (System.IO.File.Exists(_orderPath))
                {
                    var json = System.IO.File.ReadAllText(_orderPath);
                    _orders = JsonSerializer.Deserialize<List<Order>>(json) ?? new List<Order>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi load orders");
                _orders = new List<Order>();
            }
        }

        private void SaveOrders()
        {
            try
            {
                var json = JsonSerializer.Serialize(_orders, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(_orderPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi save orders");
            }
        }

        public IActionResult Index(string status = "")
        {
            LoadOrders();
            var orders = _orders.OrderByDescending(o => o.CreatedAt).ToList();
            
            if (!string.IsNullOrEmpty(status))
            {
                orders = orders.Where(o => o.Status == status).ToList();
            }
            
            ViewBag.Orders = orders;
            ViewBag.Status = status;
            ViewBag.TotalOrders = _orders.Count;
            ViewBag.PendingOrders = _orders.Count(o => o.Status == "Chờ duyệt");
            ViewBag.DeliveringOrders = _orders.Count(o => o.Status == "Đang giao");
            ViewBag.CompletedOrders = _orders.Count(o => o.Status == "Hoàn thành");
            
            return View();
        }

        [HttpGet]
        public IActionResult Detail(int id)
        {
            LoadOrders();
            var order = _orders.FirstOrDefault(o => o.Id == id);
            if (order == null)
            {
                return RedirectToAction("Index");
            }
            return View(order);
        }

        [HttpPost]
        public IActionResult UpdateStatus(int id, string status)
        {
            try
            {
                LoadOrders();
                var order = _orders.FirstOrDefault(o => o.Id == id);
                if (order != null)
                {
                    order.Status = status;
                    order.UpdatedAt = DateTime.Now;
                    SaveOrders();
                    TempData["Success"] = "Đã cập nhật trạng thái đơn hàng thành công.";
                }
                return RedirectToAction("Detail", new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật trạng thái đơn hàng");
                TempData["Error"] = "Có lỗi xảy ra khi cập nhật trạng thái.";
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        public IActionResult UpdatePaymentStatus(int id, string paymentStatus)
        {
            try
            {
                LoadOrders();
                var order = _orders.FirstOrDefault(o => o.Id == id);
                if (order != null)
                {
                    order.PaymentStatus = paymentStatus;
                    order.UpdatedAt = DateTime.Now;
                    SaveOrders();
                    TempData["Success"] = "Đã cập nhật trạng thái thanh toán thành công.";
                }
                return RedirectToAction("Detail", new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật trạng thái thanh toán");
                TempData["Error"] = "Có lỗi xảy ra khi cập nhật trạng thái thanh toán.";
                return RedirectToAction("Index");
            }
        }
    }
}

