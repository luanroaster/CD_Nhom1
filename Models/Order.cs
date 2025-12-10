namespace PCSTORE.Models
{
    public class Order
    {
        public int Id { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public string CustomerAddress { get; set; } = string.Empty;
        public List<OrderItem> Items { get; set; } = new List<OrderItem>();
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "Chờ duyệt"; // Chờ duyệt, Đang giao, Hoàn thành, Đã hủy
        public string PaymentStatus { get; set; } = "Chưa thanh toán"; // Chưa thanh toán, Đã thanh toán
        public string PaymentMethod { get; set; } = string.Empty; // COD, Bank Transfer, etc.
        public string Notes { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }
    }

    public class OrderItem
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public decimal SubTotal { get; set; }
    }
}

