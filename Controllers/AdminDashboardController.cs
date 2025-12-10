using Microsoft.AspNetCore.Mvc;
using PCSTORE.Filters;
using PCSTORE.Services;
using PCSTORE.Models;

namespace PCSTORE.Controllers
{
    [AdminAuthorize]
    public class AdminDashboardController : Controller
    {
        private readonly DataStoreService _dataStore;
        private readonly CustomerService _customerService;
        private readonly ILogger<AdminDashboardController> _logger;

        public AdminDashboardController(
            DataStoreService dataStore,
            CustomerService customerService,
            ILogger<AdminDashboardController> logger)
        {
            _dataStore = dataStore;
            _customerService = customerService;
            _logger = logger;
        }

        public IActionResult Index()
        {
            try
            {
                var products = _dataStore.GetAllProducts();
                var customers = _customerService.GetAllCustomers();
                
                // Doanh thu hôm nay (giả lập - cần tích hợp với Order service)
                var todayRevenue = 0m;
                var todayOrders = 0;
                
                // Doanh thu tháng này
                var monthRevenue = 0m;
                var monthOrders = 0;
                
                // Đơn hàng mới (giả lập)
                var newOrders = 0;
                
                // Sản phẩm bán chạy (giả lập - cần tích hợp với Order service)
                var bestSellingProducts = products
                    .OrderByDescending(p => p.Stock) // Tạm thời dùng Stock làm tiêu chí
                    .Take(5)
                    .ToList();
                
                // Tồn kho sắp hết (Stock < 10)
                var lowStockProducts = products
                    .Where(p => p.Stock > 0 && p.Stock < 10)
                    .OrderBy(p => p.Stock)
                    .ToList();
                
                ViewBag.TodayRevenue = todayRevenue;
                ViewBag.TodayOrders = todayOrders;
                ViewBag.MonthRevenue = monthRevenue;
                ViewBag.MonthOrders = monthOrders;
                ViewBag.NewOrders = newOrders;
                ViewBag.BestSellingProducts = bestSellingProducts;
                ViewBag.LowStockProducts = lowStockProducts;
                ViewBag.TotalProducts = products.Count;
                ViewBag.TotalCustomers = customers.Count;
                
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi load dashboard");
                return View();
            }
        }
    }
}

