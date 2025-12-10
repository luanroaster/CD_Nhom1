namespace PCSTORE.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        // Thông tin chung
        public string Brand { get; set; } = string.Empty;
        public string ModelCode { get; set; } = string.Empty;
        public string Warranty { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal? OldPrice { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        // Lưu danh sách đường dẫn ảnh phụ, phân cách bằng dấu ';'
        public string ExtraImages { get; set; } = string.Empty;
        // Chuỗi thông số kỹ thuật chi tiết (tùy theo từng loại linh kiện)
        public string Specs { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public bool IsFeatured { get; set; }
        public int Stock { get; set; }
    }
}

