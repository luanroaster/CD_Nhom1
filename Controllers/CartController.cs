using Microsoft.AspNetCore.Mvc;
using PCSTORE.Models;
using PCSTORE.Services;
using System.Text.Json;

namespace PCSTORE.Controllers
{
    public class CartController : Controller
    {
        private readonly DataStoreService _dataStore;
        private readonly ILogger<CartController> _logger;

        public CartController(DataStoreService dataStore, ILogger<CartController> logger)
        {
            _dataStore = dataStore;
            _logger = logger;
        }

        // Hiển thị giỏ hàng
        public IActionResult Index()
        {
            try
            {
                // Đọc giỏ hàng từ session hoặc từ request (nếu dùng localStorage)
                var cartJson = HttpContext.Session.GetString("Cart");
                List<CartItem> cartItems = new List<CartItem>();

                if (!string.IsNullOrEmpty(cartJson))
                {
                    cartItems = JsonSerializer.Deserialize<List<CartItem>>(cartJson) ?? new List<CartItem>();
                }

                // Lấy thông tin chi tiết sản phẩm từ database
                var cartViewModel = new CartViewModel
                {
                    Items = new List<CartItemViewModel>()
                };

                foreach (var item in cartItems)
                {
                    var product = _dataStore.GetProductById(item.ProductId);
                    if (product != null)
                    {
                        cartViewModel.Items.Add(new CartItemViewModel
                        {
                            ProductId = product.Id,
                            ProductName = product.Name,
                            Price = product.Price,
                            Quantity = item.Quantity,
                            ImageUrl = product.ImageUrl,
                            Stock = product.Stock,
                            Total = product.Price * item.Quantity
                        });
                    }
                }

                cartViewModel.TotalAmount = cartViewModel.Items.Sum(i => i.Total);
                cartViewModel.TotalItems = cartViewModel.Items.Sum(i => i.Quantity);

                return View(cartViewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy giỏ hàng");
                return View(new CartViewModel { Items = new List<CartItemViewModel>() });
            }
        }

        // API: Thêm sản phẩm vào giỏ hàng
        [HttpPost]
        public IActionResult AddToCart([FromBody] AddToCartRequest request)
        {
            try
            {
                // Nếu khách chưa đăng nhập thì yêu cầu đăng nhập/đăng ký
                var customerId = HttpContext.Session.GetInt32("CustomerId");
                if (!customerId.HasValue)
                {
                    return Json(new { success = false, needLogin = true, redirectUrl = Url.Action("Login", "Account") });
                }

                var cartJson = HttpContext.Session.GetString("Cart");
                var cartItems = string.IsNullOrEmpty(cartJson) 
                    ? new List<CartItem>() 
                    : JsonSerializer.Deserialize<List<CartItem>>(cartJson) ?? new List<CartItem>();

                var existingItem = cartItems.FirstOrDefault(x => x.ProductId == request.ProductId);
                if (existingItem != null)
                {
                    existingItem.Quantity += request.Quantity;
                }
                else
                {
                    cartItems.Add(new CartItem
                    {
                        ProductId = request.ProductId,
                        Quantity = request.Quantity
                    });
                }

                HttpContext.Session.SetString("Cart", JsonSerializer.Serialize(cartItems));

                var totalItems = cartItems.Sum(x => x.Quantity);
                return Json(new { success = true, totalItems = totalItems });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi thêm vào giỏ hàng");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // API: Cập nhật số lượng sản phẩm
        [HttpPost]
        public IActionResult UpdateQuantity([FromBody] UpdateQuantityRequest request)
        {
            try
            {
                var cartJson = HttpContext.Session.GetString("Cart");
                if (string.IsNullOrEmpty(cartJson))
                {
                    return Json(new { success = false, message = "Giỏ hàng trống" });
                }

                var cartItems = JsonSerializer.Deserialize<List<CartItem>>(cartJson) ?? new List<CartItem>();
                var item = cartItems.FirstOrDefault(x => x.ProductId == request.ProductId);
                
                if (item == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy sản phẩm" });
                }

                if (request.Quantity <= 0)
                {
                    cartItems.Remove(item);
                }
                else
                {
                    item.Quantity = request.Quantity;
                }

                HttpContext.Session.SetString("Cart", JsonSerializer.Serialize(cartItems));

                // Tính lại tổng
                var product = _dataStore.GetProductById(request.ProductId);
                var totalItems = cartItems.Sum(x => x.Quantity);
                var itemTotal = product != null ? product.Price * request.Quantity : 0;
                var cartTotal = cartItems.Sum(x =>
                {
                    var p = _dataStore.GetProductById(x.ProductId);
                    return p != null ? p.Price * x.Quantity : 0;
                });

                return Json(new { 
                    success = true, 
                    totalItems = totalItems,
                    itemTotal = itemTotal,
                    cartTotal = cartTotal
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật số lượng");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // API: Xóa sản phẩm khỏi giỏ hàng
        [HttpPost]
        public IActionResult RemoveFromCart([FromBody] RemoveFromCartRequest request)
        {
            try
            {
                var cartJson = HttpContext.Session.GetString("Cart");
                if (string.IsNullOrEmpty(cartJson))
                {
                    return Json(new { success = false, message = "Giỏ hàng trống" });
                }

                var cartItems = JsonSerializer.Deserialize<List<CartItem>>(cartJson) ?? new List<CartItem>();
                cartItems.RemoveAll(x => x.ProductId == request.ProductId);

                HttpContext.Session.SetString("Cart", JsonSerializer.Serialize(cartItems));

                var totalItems = cartItems.Sum(x => x.Quantity);
                var cartTotal = cartItems.Sum(x =>
                {
                    var p = _dataStore.GetProductById(x.ProductId);
                    return p != null ? p.Price * x.Quantity : 0;
                });

                return Json(new { 
                    success = true, 
                    totalItems = totalItems,
                    cartTotal = cartTotal
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa khỏi giỏ hàng");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // API: Lấy số lượng sản phẩm trong giỏ hàng
        [HttpGet]
        public IActionResult GetCartCount()
        {
            try
            {
                var cartJson = HttpContext.Session.GetString("Cart");
                if (string.IsNullOrEmpty(cartJson))
                {
                    return Json(new { totalItems = 0 });
                }

                var cartItems = JsonSerializer.Deserialize<List<CartItem>>(cartJson) ?? new List<CartItem>();
                var totalItems = cartItems.Sum(x => x.Quantity);

                return Json(new { totalItems = totalItems });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy số lượng giỏ hàng");
                return Json(new { totalItems = 0 });
            }
        }

        // Xóa toàn bộ giỏ hàng
        [HttpPost]
        public IActionResult ClearCart()
        {
            try
            {
                HttpContext.Session.Remove("Cart");
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa giỏ hàng");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }

    // Models cho Cart
    public class CartItem
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }

    public class CartViewModel
    {
        public List<CartItemViewModel> Items { get; set; } = new List<CartItemViewModel>();
        public decimal TotalAmount { get; set; }
        public int TotalItems { get; set; }
    }

    public class CartItemViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public int Stock { get; set; }
        public decimal Total { get; set; }
    }

    public class AddToCartRequest
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; } = 1;
    }

    public class UpdateQuantityRequest
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }

    public class RemoveFromCartRequest
    {
        public int ProductId { get; set; }
    }

    public class SyncCartRequest
    {
        public List<CartItemSync> Items { get; set; } = new List<CartItemSync>();
    }

    public class CartItemSync
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }
}

