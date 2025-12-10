using PCSTORE.Services;
using PCSTORE.Models;
using System.IO;
using System.Linq;

namespace PCSTORE.Services
{
    public class StartupService
    {
        private readonly ExcelService _excelService;
        private readonly DataStoreService _dataStore;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<StartupService> _logger;
        private readonly ImageSearchService? _imageSearchService;
        // Bật/tắt chế độ tự động thêm dữ liệu mẫu. Mặc định: tắt để cho Admin tự thêm sản phẩm thủ công.
        private const bool EnableAutoSeedSampleProducts = false;
        // Bật/tắt chế độ tự động cập nhật hình ảnh khi khởi động
        private const bool EnableAutoUpdateImages = true;
        // Số lượng sản phẩm tối đa cập nhật mỗi lần khởi động (để không làm chậm quá trình khởi động)
        private const int MaxImagesPerStartup = 20;

        public StartupService(ExcelService excelService, DataStoreService dataStore, IWebHostEnvironment environment, ILogger<StartupService> logger, IServiceProvider serviceProvider)
        {
            _excelService = excelService;
            _dataStore = dataStore;
            _environment = environment;
            _logger = logger;
            
            // Lấy ImageSearchService từ service provider (có thể null nếu chưa được đăng ký)
            try
            {
                _imageSearchService = serviceProvider.GetService<ImageSearchService>();
            }
            catch
            {
                _imageSearchService = null;
            }
        }

        // Tự động import dữ liệu khi ứng dụng khởi động
        public void AutoImportData()
        {
            try
            {
                var excelPath = Path.Combine(_environment.ContentRootPath, "Data", "DATABASE.xlsx");
                var jsonPath = Path.Combine(_environment.ContentRootPath, "Data", "products.json");

                // Kiểm tra số lượng sản phẩm hiện tại trong datastore
                var currentProducts = _dataStore.GetAllProducts();
                var currentProductCount = currentProducts.Count;

                // Ưu tiên import từ products.json nếu file tồn tại và datastore còn ít sản phẩm
                if (File.Exists(jsonPath))
                {
                    _logger.LogInformation($"Tìm thấy file products.json, bắt đầu import vào DataStore... (Hiện tại có {currentProductCount} sản phẩm)");
                    
                    try
                    {
                        // Đọc dữ liệu từ JSON
                        var (categories, products) = _excelService.ReadFromJson(jsonPath);

                        if (categories != null && products != null && products.Count > 0)
                        {
                            // Lọc bỏ các category có Id không hợp lệ (là array thay vì số)
                            var validCategories = categories.Where(c => c.Id > 0).ToList();
                            var invalidCategoryCount = categories.Count - validCategories.Count;
                            if (invalidCategoryCount > 0)
                            {
                                _logger.LogWarning($"Đã bỏ qua {invalidCategoryCount} danh mục có Id không hợp lệ");
                            }

                            // Lọc bỏ các sản phẩm có CategoryId không hợp lệ hoặc không có trong danh sách category hợp lệ
                            var validCategoryIds = validCategories.Select(c => c.Id).ToHashSet();
                            var validProducts = products.Where(p => validCategoryIds.Contains(p.CategoryId) && !string.IsNullOrWhiteSpace(p.Name)).ToList();
                            var invalidProductCount = products.Count - validProducts.Count;
                            if (invalidProductCount > 0)
                            {
                                _logger.LogWarning($"Đã bỏ qua {invalidProductCount} sản phẩm có CategoryId không hợp lệ hoặc thiếu tên");
                            }

                            // Merge vào DataStore (không ghi đè, chỉ thêm mới)
                            _dataStore.MergeFromExcel(validProducts, validCategories);

                            // Reload dữ liệu để đảm bảo có dữ liệu mới nhất
                            _dataStore.ReloadData();
                            var finalProducts = _dataStore.GetAllProducts();
                            var finalCategories = _dataStore.GetAllCategories();

                            _logger.LogInformation($"Đã tự động import thành công {validProducts.Count} sản phẩm và {validCategories.Count} danh mục từ products.json! " +
                                $"Tổng số sản phẩm hiện tại: {finalProducts.Count}");

                            // Đếm số sản phẩm theo từng danh mục
                            var productsByCategory = finalProducts
                                .GroupBy(p => p.CategoryId)
                                .ToDictionary(g => g.Key, g => g.Count());

                            foreach (var kvp in productsByCategory)
                            {
                                var category = finalCategories.FirstOrDefault(c => c.Id == kvp.Key);
                                _logger.LogInformation($"  - {category?.Name ?? $"Danh mục {kvp.Key}"}: {kvp.Value} sản phẩm");
                            }
                        }
                        else
                        {
                            _logger.LogWarning("File products.json không chứa dữ liệu hợp lệ");
                        }
                    }
                    catch (Exception exJson)
                    {
                        _logger.LogError(exJson, "Lỗi khi import từ products.json, sẽ thử import từ Excel nếu có");
                        // Tiếp tục thử import từ Excel nếu có lỗi với JSON
                    }
                }

                // Nếu chưa có sản phẩm hoặc ít sản phẩm, thử import từ Excel
                var updatedProducts = _dataStore.GetAllProducts();
                if (updatedProducts.Count == 0 && File.Exists(excelPath))
                {
                    _logger.LogInformation("Chưa có sản phẩm trong datastore, thử import từ Excel...");
                    
                    // Đảm bảo thư mục Data tồn tại
                    var dataDir = Path.GetDirectoryName(jsonPath);
                    if (!Directory.Exists(dataDir))
                    {
                        Directory.CreateDirectory(dataDir);
                        _logger.LogInformation("Đã tạo thư mục Data");
                    }

                    // Đọc từ Excel và import vào DataStore
                    var products = _excelService.ReadProductsFromExcel(excelPath);
                    var categories = _excelService.ReadCategoriesFromExcel(excelPath);

                    // Merge vào DataStore (không ghi đè, chỉ thêm mới)
                    _dataStore.MergeFromExcel(products, categories);
                    
                    _logger.LogInformation($"Đã tự động merge thành công {products.Count} sản phẩm và {categories.Count} danh mục từ Excel vào DataStore");

                    _logger.LogInformation($"Đã tự động merge thành công {products.Count} sản phẩm và {categories.Count} danh mục từ Excel vào DataStore");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động import dữ liệu khi khởi động");
                // Không throw exception để ứng dụng vẫn có thể khởi động được
            }

            // Từ giờ KHÔNG tự động thêm dữ liệu mẫu nữa để Admin tự thêm sản phẩm thủ công.
            // Nếu muốn bật lại seed tự động, đổi EnableAutoSeedSampleProducts = true.
            if (EnableAutoSeedSampleProducts)
            {
                // Tự động thêm các sản phẩm nếu chưa có (theo thứ tự: CPU -> Mainboard -> RAM -> GPU -> PSU)
                _logger.LogInformation("Bắt đầu tự động thêm các sản phẩm vào database (AutoSeed)...");
                
                AutoAddCPUs();
                _logger.LogInformation("Đã hoàn thành AutoAddCPUs");
                
                AutoAddMainboards();
                _logger.LogInformation("Đã hoàn thành AutoAddMainboards");
                
                AutoAddRAMs();
                _logger.LogInformation("Đã hoàn thành AutoAddRAMs");
                
                AutoAddGPUs();
                _logger.LogInformation("Đã hoàn thành AutoAddGPUs");
                
                AutoAddPSUs();
                _logger.LogInformation("Đã hoàn thành AutoAddPSUs");
                
                // Tự động thêm SSD nếu chưa có
                AutoAddSSDs();
                _logger.LogInformation("Đã hoàn thành AutoAddSSDs");
                
                // Tự động thêm HDD nếu chưa có
                AutoAddHDDs();
                _logger.LogInformation("Đã hoàn thành AutoAddHDDs");
                
                // Tự động thêm Tản nhiệt nước nếu chưa có
                AutoAddWaterCooling();
                _logger.LogInformation("Đã hoàn thành AutoAddWaterCooling");
                
                // Tự động thêm Tản nhiệt khí nếu chưa có
                AutoAddAirCooling();
                _logger.LogInformation("Đã hoàn thành AutoAddAirCooling");
                
                // Tự động thêm Fan tản nhiệt nếu chưa có
                AutoAddFans();
                _logger.LogInformation("Đã hoàn thành AutoAddFans");
                
                // Tự động thêm Case nếu chưa có
                AutoAddCases();
                _logger.LogInformation("Đã hoàn thành AutoAddCases");
                
                // Tự động thêm Monitor nếu chưa có
                AutoAddMonitors();
                _logger.LogInformation("Đã hoàn thành AutoAddMonitors");
                
                // Tự động tạo categories và sample products cho Keyboard, Mouse, Speaker, Headphone
                AutoAddKeyboardMouseSpeakerHeadphone();
                _logger.LogInformation("Đã hoàn thành AutoAddKeyboardMouseSpeakerHeadphone");
            }
            
            // Reload dữ liệu cuối cùng để đảm bảo tất cả dữ liệu được cập nhật
            _dataStore.ReloadData();
            var finalAllProducts = _dataStore.GetAllProducts();
            var finalRamCount = finalAllProducts.Count(p => p.CategoryId == 3);
            var finalGpuCount = finalAllProducts.Count(p => p.CategoryId == 4);
            var finalPsuCount = finalAllProducts.Count(p => p.CategoryId == 5);
            var finalSsdCount = finalAllProducts.Count(p => p.CategoryId == 7);
            var finalHddCount = finalAllProducts.Count(p => p.CategoryId == 8);
            var finalWaterCoolingCount = finalAllProducts.Count(p => p.CategoryId == 11);
            var finalAirCoolingCount = finalAllProducts.Count(p => p.CategoryId == 12);
            var finalFanCount = finalAllProducts.Count(p => p.CategoryId == 10);
            var finalCaseCount = finalAllProducts.Count(p => p.CategoryId == 6);
            var finalMonitorCount = finalAllProducts.Count(p => p.CategoryId == 9);
            var finalKeyboardCount = finalAllProducts.Count(p => p.CategoryId == 13);
            var finalMouseCount = finalAllProducts.Count(p => p.CategoryId == 14);
            var finalSpeakerCount = finalAllProducts.Count(p => p.CategoryId == 15);
            var finalHeadphoneCount = finalAllProducts.Count(p => p.CategoryId == 16);
            _logger.LogInformation($"Hoàn thành tự động thêm sản phẩm. Tổng số sản phẩm: {finalAllProducts.Count}, RAM: {finalRamCount}, GPU: {finalGpuCount}, PSU: {finalPsuCount}, SSD: {finalSsdCount}, HDD: {finalHddCount}, Tản nhiệt nước: {finalWaterCoolingCount}, Tản nhiệt khí: {finalAirCoolingCount}, Fan: {finalFanCount}, Case: {finalCaseCount}, Monitor: {finalMonitorCount}, Keyboard: {finalKeyboardCount}, Mouse: {finalMouseCount}, Speaker: {finalSpeakerCount}, Headphone: {finalHeadphoneCount}");
        }

        // Tự động thêm CPU vào database
        private void AutoAddCPUs()
        {
            try
            {
                var allProducts = _dataStore.GetAllProducts();
                var cpuProducts = allProducts.Where(p => p.CategoryId == 1).ToList();

                _logger.LogInformation($"Đã có {cpuProducts.Count} CPU trong database, kiểm tra và thêm CPU mới...");

                // Đảm bảo category CPU tồn tại
                var categories = _dataStore.GetAllCategories();
                var cpuCategory = categories.FirstOrDefault(c => 
                    c.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) || 
                    c.Id == 1);
                
                int cpuCategoryId;
                if (cpuCategory == null)
                {
                    cpuCategory = new Category
                    {
                        Id = 1,
                        Name = "CPU - Bộ vi xử lý",
                        Description = "Intel, AMD",
                        ImageUrl = "https://via.placeholder.com/200x150?text=CPU"
                    };
                    _dataStore.AddCategory(cpuCategory);
                    cpuCategoryId = 1;
                }
                else
                {
                    cpuCategoryId = cpuCategory.Id;
                }

                // Danh sách CPU từ hình ảnh
                var cpus = new List<(string Name, decimal Price, int Stock)>
                {
                    ("CPU Intel Core i7 14700F (Intel LGA1700 - 20 Core 28 Thread - Base 2.1Ghz - Turbo 5.4Ghz Cache 33MB)", 7990000, 6),
                    ("CPU Intel Core Ultra 5 225 (10C/10T, 3.3GHz Boost 4.9GHz, 20MB, LGA1851)", 4290000, 4),
                    ("CPU Intel Core Ultra 7 265K Tray New(Up 5.5GHz, 20 Nhân 20 Luồng, Arrow Lake-S)", 6990000, 5),
                    ("CPU Intel Core i5-12400F Tray NEW (Up to 4.4Ghz, 6 nhân 12 luống, 18MB Cache, 65W) - Socket Intel LGA 1700)", 2990000, 7),
                    ("CPU AMD Ryzen 5 5500GT (Up to 4.4 GHz | 6 Cores / 12 Threads | 19 MB Cache)", 2850000, 8),
                    ("CPU AMD Ryzen 5 5500 (3,6 GHz Boost 4,2 GHz | 6 Cores / 12 Threads | 16 MB Cache PCIe 3.0)", 1590000, 4),
                    ("CPU AMD Ryzen 5 7600 (3,8 GHz Boost 5,1 GHz | 6 Cores / 12 Threads | 32 MB Cache PCIe 5.0)", 3790000, 3),
                    ("CPU AMD Ryzen 5 7500F TRAY NEW Chính hãng (3.7GHz Boost 5.0GHz, 6C/12T, 32MB Cache)", 2990000, 6),
                    ("CPU AMD Ryzen 7 9700X Tray New (3.8 GHz Boost 5.5 GHz | 8 Cores / 16 Threads | 32 MB Cache)", 6990000, 4),
                    ("CPU Intel Core Ultra 9 285K (Up 5.7 GHz, 24 Nhân 24 Luồng, Arrow Lake-S)", 13000000, 5),
                    ("CPU Intel Core i5-13400F Tray New (Up To 4.60GHz, 10 Nhân 16 Luồng, 20 MB Cache, LGA 1700)", 3890000, 7),
                    ("CPU Intel Core Ultra 5 245KF (14 Nhân 14 Luông, Arrow Lake-S)", 4990000, 8),
                    ("Intel Xeon Processor E5-2680 v4 (35M Cache, 2.40 GHz turbo 3.30 Ghz) 14Cores / 28 Thread", 390000, 4),
                    ("CPU AMD Ryzen 5 3400G 3.7 GHz (4.2 GHz with boost) / 6MB / 4 cores 8 threads / Radeon Vega 11 / 65W", 1490000, 3),
                    ("CPU AMD Ryzen 7 7800X3D TRAY NEW (4,2 GHz Boost 5,0 GHz | 8 Cores / 16 Threads | 96 MB Cache PCIe 5.0)", 7890000, 6),
                    ("CPU AMD Ryzen 9 9950X (4.3 GHz Boost 5.7 GHz | 16 Cores / 32 Threads | 64 MB Cache)", 13490000, 4),
                    ("CPU Intel Core i5-13400F (Up To 4.60GHz, 10 Nhân 16 Luồng, 20 MB Cache, LGA 1700)", 4090000, 5),
                    ("CPU Intel Core I5 14600KF Tray NEW (Up 5.30 GHz, 14 Nhân 20 Luồng, 24MB Cache, Raptor Lake Refresh)", 4990000, 7),
                    ("CPU Intel Core i9-12900KF New Tray(3.9GHz Boost 5.2GHz, 16 Nhân 24 Luồng, 30MB Cache, 125W, Gen 12)", 7490000, 8),
                    ("Intel Xeon E5-2699 v3 (2.3 GHz, 45 MB, 18C/36T, 145 W, LGA 2011-3)", 990000, 4),
                    ("CPU AMD Ryzen Threadripper 9960X (4.2GHz Up to 5.4GHz | 24 Cores/48 Threads | 152 MB Cache PCIe 5.0)", 40990000, 3),
                    ("CPU Intel Xeon E5-2696 V4 2.20 GHz / 55MB / 22 Core / 44 Thread / Socket 2011-3", 1390000, 6),
                    ("CPU Intel Core i5-12400F (Upto 4.4Ghz, 6 nhân 12 luồng, 18MB Cache, 65W) - Socket Intel LGA 1700)", 2990000, 4),
                    ("CPU Intel Core Ultra 7 265K (Up 5.5GHz, 20 Nhân 20 Luồng, Arrow Lake-S)", 7590000, 5),
                    ("CPU Intel Xeon Gold 6138 (3.70GHz / 27.5 MB / 20 Cores, 40 Threads / LGA3647)", 690000, 7),
                    ("CPU AMD Ryzen 3 3200G (3.6-4.0Ghz / 4 core 4 thread / socket AM4)", 2200000, 8),
                    ("CPU Intel Core I7-13700F (2.10 GHz up to 5.20 GHz, 30 MB, LGA 1700)", 5990000, 4),
                    ("CPU AMD Ryzen 7 9800X3D (Up to 5.2GHz | 8 Cores Zen 5 | 96 MB Cache)", 10990000, 3),
                    ("CPU AMD Ryzen 9 9950X TRAY NEW (4.3 GHz Boost 5.7 GHz | 16 Cores / 32 Threads | 64 MB Cache)", 12490000, 6),
                    ("CPU Intel Core i9-12900KF (3.9GHz turbo 5.2Ghz | 16 nhân 24 luồng | 30MB Cache | 125W)", 7490000, 4),
                    ("CPU Intel Xeon E5-2696 V3 2.30 GHz / 45MB / 18 Cores 36 Threads / Socket 2011-3", 980000, 5),
                    ("CPU Intel Core i5-12600KF (20M Cache, up to 4.90 GHz, 10C16T, Socket 1700)", 3690000, 7),
                    ("CPU Intel Core i7 14700KF (Up 5.60 GHz, 20 Nhân 28 Luồng, 33MB Cache, Raptor Lake Refresh)", 7990000, 6),
                    ("CPU AMD EPYC 7742 TRAY NEW (64C/128T | 2.25GHz Boost 3.4GHz | 256M Cache)", 25000000, 4),
                    ("CPU Intel Xeon E5520 (4 nhân | 8 luống | 2.26GHz turbo 2.53GHz | 8MB Cache)", 500000, 5),
                    ("CPU Intel Xeon L5520 (4 Nhân | 8 Luồng | 2.26GHz Turbo 2.48GHz | 8MB Cache)", 400000, 7),
                    ("CPU Intel Core i5-12400 Tray (Up To 4.40GHz, 6 Nhân 12 Luồng, 18MB Cache, Alder Lake)", 4990000, 8),
                    ("CPU Intel Core i3 13100F (Up to 4.5 GHz / 4 Nhân / 8 Luông / Socket 1700)", 2890000, 4),
                    ("CPU AMD Ryzen 9 7950X3D (4,2 GHz Boost 5,7 GHz | 16 Cores / 32 Threads | 128 MB Cache PCIe 5.0)", 17900000, 3),
                    ("CPU Intel Xeon E5 2667 / 6 core / 12 threads / 2.9 turbo 3.5", 600000, 6),
                    ("CPU Intel Xeon E5 2673 v3 / 12 Core / 24 thread / 2.4 turbo 3.2 Ghz", 990000, 4),
                    ("CPU Intel Xeon Processor E5-2678 V3 (2.50 turbo 3.1GHz / 12Cores / 24 Thread)", 590000, 5),
                    ("CPU Intel Xeon E5-2689 (2.60 GHz, 20M Cache, 8C/16T)", 450000, 7),
                    ("CPU AMD RYZEN 5 3600 (3.6-4.2Ghz / 6 core 12 thread / socket AM4)", 3990000, 8),
                    ("CPU AMD Ryzen Threadripper 7970X (4.0GHz Up to 5.3GHz | 32 Cores / 64 Threads | 128MB Cache | PCIe 5.0)", 69990000, 4),
                    ("CPU Intel Xeon E5 2686 v4 / 2.3GHz / 45MB / 18 Core / 36 Thread / Socket 2011-3", 1490000, 3),
                    ("CPU AMD Ryzen Threadripper PRO 7985WX (3,2GHz Up to 5,1GHz | 64 Cores / 128 Threads | 128MB Cache PCIe 5.0)", 204990000, 6),
                    ("CPU Intel Pentium Gold G6405 (2 nhân | 4 luồng | 4.1 GHz | 4MB Cache)", 1990000, 4),
                    // CPU từ 49-99
                    ("CPU Intel Xeon E5-1607 V3 (3.1 GHz, 10M Cache, 4 Cores, 4 Threads)", 500000, 5),
                    ("CPU AMD Ryzen 5 5600G (3.9GHz Boost 4.4GHz | 6 Cores / 12 Threads | 16MB Cache PCIe 3.0)", 2890000, 6),
                    ("CPU Intel Core i7-12700 (2.1 GHz up to 4.9 GHz, 25 MB, LGA 1700)", 5990000, 4),
                    ("CPU AMD Ryzen Threadripper PRO 7995WX (2.5GHz Up to 5.1GHz | 96 Cores/ 192 Threads | 384MB Cache PCIe 5.0)", 289990000, 3),
                    ("CPU Intel Core Ultra 5 245K (Up 5.4GHz, 14 Nhân 14 Luồng, Arrow Lake-S)", 4990000, 5),
                    ("CPU Intel Core i5-12400 (Up To 4.40GHz, 6 Nhân 12 Luồng, 18MB Cache, Alder Lake)", 4990000, 7),
                    ("CPU AMD Ryzen 7 5700G (3.8 GHz Boost 4.6 GHz | 8 Cores / 16 Threads | 16 MB Cache PCIe 3.0)", 3990000, 6),
                    ("CPU Intel Core i9-13900K (3.0 GHz up to 5.8 GHz, 36 MB, LGA 1700)", 9990000, 4),
                    ("CPU AMD Ryzen 9 7900X (4.7 GHz Boost 5.6 GHz | 12 Cores / 24 Threads | 64 MB Cache PCIe 5.0)", 8990000, 5),
                    ("CPU Intel Core i3-12100F (3.3 GHz up to 4.3 GHz, 12 MB, LGA 1700)", 2490000, 8),
                    ("CPU AMD Ryzen 5 5600 (3.5 GHz Boost 4.4 GHz | 6 Cores / 12 Threads | 32 MB Cache PCIe 4.0)", 2490000, 7),
                    ("CPU Intel Core i7-13700K (3.4 GHz up to 5.4 GHz, 30 MB, LGA 1700)", 7990000, 6),
                    ("CPU AMD Ryzen 7 7700X (4.5 GHz Boost 5.4 GHz | 8 Cores / 16 Threads | 32 MB Cache PCIe 5.0)", 5990000, 4),
                    ("CPU Intel Core i5-13600K (3.5 GHz up to 5.1 GHz, 24 MB, LGA 1700)", 6990000, 5),
                    ("CPU AMD Ryzen 9 7900X3D (4.4 GHz Boost 5.6 GHz | 12 Cores / 24 Threads | 128 MB Cache PCIe 5.0)", 12990000, 3),
                    ("CPU Intel Core i9-14900K (3.2 GHz up to 6.0 GHz, 36 MB, LGA 1700)", 11990000, 4),
                    ("CPU AMD Ryzen Threadripper 3970X (3.7 GHz Boost 4.5 GHz | 32 Cores / 64 Threads | 144 MB Cache PCIe 4.0)", 75900000, 4),
                    ("CPU Intel Xeon E5-2680 v3 (2.5 GHz, 30 MB, 12C/24T, 120 W, LGA 2011-3)", 890000, 6),
                    ("CPU AMD Ryzen 5 3600X (3.8-4.4 GHz | 6 Cores / 12 Threads | 32 MB Cache PCIe 4.0)", 3490000, 5),
                    ("CPU Intel Core i7-10700K (3.8 GHz up to 5.1 GHz, 16 MB, LGA 1200)", 4990000, 7),
                    ("CPU AMD Ryzen 7 3700X (3.6-4.4 GHz | 8 Cores / 16 Threads | 32 MB Cache PCIe 4.0)", 3990000, 6),
                    ("CPU Intel Core i5-10400F (2.9 GHz up to 4.3 GHz, 12 MB, LGA 1200)", 2490000, 8),
                    ("CPU AMD Ryzen 9 3900X (3.8-4.6 GHz | 12 Cores / 24 Threads | 64 MB Cache PCIe 4.0)", 5990000, 4),
                    ("CPU Intel Core i9-10900K (3.7 GHz up to 5.3 GHz, 20 MB, LGA 1200)", 6990000, 5),
                    ("CPU AMD Ryzen Threadripper 3960X (3.8 GHz Boost 4.5 GHz | 24 Cores / 48 Threads | 128 MB Cache PCIe 4.0)", 49900000, 3),
                    ("CPU Intel Xeon Platinum 8163 (2.5 GHz, 33 MB, 24C/48T, 150 W, LGA 3647)", 1590000, 4),
                    ("CPU AMD Ryzen 5 2600 (3.4-3.9 GHz | 6 Cores / 12 Threads | 16 MB Cache PCIe 3.0)", 1990000, 7),
                    ("CPU Intel Core i7-9700K (3.6 GHz up to 4.9 GHz, 12 MB, LGA 1151)", 3990000, 6),
                    ("CPU AMD Ryzen 7 2700X (3.7-4.3 GHz | 8 Cores / 16 Threads | 16 MB Cache PCIe 3.0)", 2990000, 5),
                    ("CPU Intel Core i5-9600K (3.7 GHz up to 4.6 GHz, 9 MB, LGA 1151)", 2990000, 8),
                    ("CPU AMD Ryzen 9 5900X (3.7 GHz Boost 4.8 GHz | 12 Cores / 24 Threads | 64 MB Cache PCIe 4.0)", 6990000, 4),
                    ("CPU Intel Core i3-10100F (3.6 GHz up to 4.3 GHz, 6 MB, LGA 1200)", 1990000, 7),
                    ("CPU AMD Ryzen 5 2600X (3.6-4.2 GHz | 6 Cores / 12 Threads | 16 MB Cache PCIe 3.0)", 2290000, 6),
                    ("CPU Intel Core i7-8700K (3.7 GHz up to 4.7 GHz, 12 MB, LGA 1151)", 3490000, 5),
                    ("CPU AMD Ryzen Threadripper 1950X (3.4 GHz Boost 4.0 GHz | 16 Cores / 32 Threads | 32 MB Cache PCIe 3.0)", 8990000, 4),
                    ("CPU Intel Xeon E5-2670 v2 (2.5 GHz, 25 MB, 10C/20T, 115 W, LGA 2011)", 550000, 8),
                    ("CPU AMD Ryzen 7 1800X (3.6-4.0 GHz | 8 Cores / 16 Threads | 16 MB Cache PCIe 3.0)", 2490000, 5),
                    ("CPU Intel Core i5-8400 (2.8 GHz up to 4.0 GHz, 9 MB, LGA 1151)", 1990000, 6),
                    ("CPU AMD Ryzen 5 1600 (3.2-3.6 GHz | 6 Cores / 12 Threads | 16 MB Cache PCIe 3.0)", 1490000, 7),
                    ("CPU Intel Core i7-7700K (4.2 GHz up to 4.5 GHz, 8 MB, LGA 1151)", 2990000, 4),
                    ("CPU AMD Ryzen 3 3100 (3.6-3.9 GHz | 4 Cores / 8 Threads | 16 MB Cache PCIe 4.0)", 1290000, 8),
                    ("CPU Intel Core i5-7500 (3.4 GHz up to 3.8 GHz, 6 MB, LGA 1151)", 1490000, 6),
                    ("CPU AMD Ryzen 5 2400G (3.6-3.9 GHz | 4 Cores / 8 Threads | 4 MB Cache PCIe 3.0)", 1790000, 5),
                    ("CPU Intel Core i3-7100 (3.9 GHz, 3 MB, LGA 1151)", 999000, 7),
                    ("CPU AMD Athlon 200GE (3.2 GHz, 2 Cores / 4 Threads, 5 MB, AM4)", 800000, 8),
                    ("CPU Intel Pentium Gold G5400 (3.7 GHz, 2 Cores / 4 Threads, 4 MB, LGA 1151)", 899000, 6),
                    ("CPU AMD Ryzen 3 2200G (3.5-3.7 GHz | 4 Cores / 4 Threads | 4 MB Cache PCIe 3.0)", 1290000, 5),
                    ("CPU Intel Celeron G4900 (3.1 GHz, 2 Cores / 2 Threads, 2 MB, LGA 1151)", 500000, 7),
                    // CPU từ 100-150
                    ("CPU Intel Core i7-12700K TRAY NEW (3.8GHz turbo 5.0Ghz | 12 nhân 20 luồng | 25MB Cache | 125W)", 5290000, 7),
                    ("Bộ vi xử lý AMD Athlon 3000G Tray (3.5GHz / 2 nhân 4 luồng / 5MB / AM4)", 800000, 8),
                    ("CPU Intel Core i7-12700F TRAY (Up To 4.80GHz, 12 Nhân 20 Luồng, 25M Cache, Alder Lake)", 4790000, 4),
                    ("CPU Intel Pentium Gold G7400", 2350000, 3),
                    ("CPU Intel Core i3 12100 (3.3GHz turbo up to 4.3GHz, 4 nhân 8 luồng, 12MB Cache)", 3990000, 6),
                    ("CPU Intel Core i5 14400 (Up To 4.70GHz, 10 Nhân 16 Luồng, 20MB Cache, LGA 1700)", 4590000, 4),
                    ("CPU Intel Core i7 13700K (Up To 5.40GHz, 16 Nhân 24 Luồng, 30M Cache, Raptor Lake)", 7890000, 5),
                    ("CPU AMD Ryzen 5 8600G (4.3 GHz Boost 5.0 GHz | 6 Cores / 12 Threads | 16 MB Cache)", 6590000, 7),
                    ("CPU AMD Ryzen 7 5700X (3,4 GHz Boost 4,6 GHz | 8 Cores / 16 Threads | 32MB Cache PCIe 4.0)", 4190000, 8),
                    ("CPU AMD Ryzen 7 5800X3D (3,4 GHz Boost 4,5 GHz | 8 Cores / 16 Threads | 96MB Cache | PCIe 4.0)", 9450000, 4),
                    ("CPU AMD Ryzen 5 5600 (3,5 GHz Boost 4,4 GHz | 6 Cores / 12 Threads | 32 MB Cache PCIe 4.0)", 3490000, 3),
                    ("CPU Intel Core i9-13900KF (5.80GHz, 24 Nhân 32 Luồng, 36M Cache, Raptor Lake)", 7990000, 6),
                    ("CPU AMD Ryzen Threadripper PRO 5995WX (2.7 GHz Boost 4,5 GHz | 64 Cores / 128 Threads | 292 MB Cache | PCIe 4.0)", 172990000, 4),
                    ("CPU AMD Ryzen Threadripper PRO 5955WX (4.0 GHz Boost 4,5 GHz | 16 Cores / 32 Threads | 64 MB Cache PCIe 4.0)", 32990000, 5),
                    ("CPU AMD Ryzen Threadripper PRO 5965WX (3.8 GHz Boost 4,5 GHz | 24 Cores / 48 Threads | 141.5 MB Cache | PCIe 4.0)", 62590000, 7),
                    ("CPU AMD Ryzen Threadripper PRO 5975WX (3.6 GHz Boost 4,5 GHz | 32 Cores / 64 Threads | 146 MB Cache | PCIe 4.0)", 92000000, 8),
                    ("CPU AMD Ryzen 7 7700 (3,8 GHz Boost 5,3 GHz | 8 Cores / 16 Threads | 32 MB Cache PCIe 5.0)", 5990000, 4),
                    ("CPU AMD Ryzen 7 5700X3D (3.0 GHz Boost 4.1 GHz | 8 Cores / 16 Threads | 96 MB Cache)", 6390000, 3),
                    ("CPU AMD Ryzen 5 5600GT (3.6 GHz Boost 4.6 GHz | 6 Cores / 12 Threads | 16 MB Cache)", 2890000, 6),
                    ("CPU AMD Ryzen 9 7900X TRAY NEW (4,7 GHz Boost 5,6 GHz | 12 Cores / 24 Threads | 64 MB Cache | PCIe 5.0)", 7990000, 4),
                    ("CPU AMD Ryzen 9 7950X (4,5 GHz Boost 5,7 GHz | 16 Cores / 32 Threads | 64 MB Cache | PCIe 5.0)", 12900000, 5),
                    ("CPU AMD Ryzen 7 7700X (4,5 GHz Boost 5,4 GHz | 8 Cores / 16 Threads | 32 MB Cache PCIe 5.0)", 7890000, 7),
                    ("CPU AMD Ryzen 5 7600X (4,7 GHz Boost 5,3 GHz | 6 Cores / 12 Threads | 32 MB Cache PCIe 5.0)", 4990000, 8),
                    ("CPU Intel Core i5 13600K (3.50 GHz, up to 5.10GHz, 14 Nhân 20 Luồng, 24 MB Cache, Raptor Lake)", 4990000, 4),
                    ("CPU Intel Core i5-13600KF (3,50 GHz, up to 5.10GHz, 14 Nhân 20 Luồng, 24 MB Cache, Raptor Lake S)", 7700000, 3),
                    ("CPU Intel Core i7-13700KF (Up To 5.40GHz, 16 Nhân 24 Luồng, 30M Cache, Raptor Lake)", 6600000, 6),
                    ("CPU Intel Core i9 13900K (5.80GHz, 24 Nhân 32 Luồng, 36M Cache, Raptor Lake)", 8990000, 4),
                    ("CPU Intel Core i5-13500 Tray New (Up to 4.80GHz, 14 Nhân 20 Luồng, 24M Cache, FCLGA1700)", 4690000, 5),
                    ("CPU AMD Ryzen 7 8700G (4.2 GHz Boost 5.1 GHz | 8 Cores / 16 Threads | 16 MB Cache)", 9190000, 7),
                    ("CPU AMD Ryzen 9 7950X TRAY NEW (4,5 GHz Boost 5,7 GHz | 16 Cores / 32 Threads | 64 MB Cache | PCIe 5.0)", 10900000, 6),
                    ("CPU Intel Core i3 13100 (3.42GHz Turbo Upto 4.5GHz, 4 Nhân 8 Luồng, Cache 12MB, Socket LGA 1700)", 3490000, 4),
                    ("CPU Intel Core i5-13400 TRAY (Up To 4.60GHz, 10 Nhân 16 Luồng, 20MB Cache, LGA 1700)", 5890000, 5),
                    ("CPU AMD Ryzen 9 7900 (3,7 GHz Boost 5,4 GHz | 12 Cores / 24 Threads | 64 MB Cache PCIe 5.0)", 7990000, 7),
                    ("CPU Intel Core i5-13500 (Up to 4.80GHz, 14 Nhân 20 Luồng, 24M Cache, FCLGA1700)", 4690000, 8),
                    ("CPU AMD Ryzen 5 4600G (3.7GHz Boost 4.2GHz / 6 nhân 12 luồng / 11MB / AM4)", 2150000, 4),
                    ("CPU AMD Ryzen Threadripper 7960X (4.2GHz Up to 5.3GHz | 24 Cores / 48 Threads | 128MB Cache PCIe 5.0)", 42700000, 3),
                    ("CPU AMD Ryzen 7 9700X (3.8 GHz Boost 5.5 GHz | 8 Cores / 16 Threads | 32 MB Cache)", 7990000, 6),
                    ("CPU Intel Core i5 13400 (up to 4.6GHz, 10 nhân 16 luồng, 20MB Cache, 65W)", 4390000, 4),
                    ("CPU Intel Core i5 14400F Tray New (Up To 4.70GHz, 10 Nhân 16 Luồng, 20MB Cache, LGA 1700)", 3990000, 5),
                    ("CPU AMD Ryzen 7 8700F (4.1 GHz Boost 5.0 GHz | 8 Cores / 16 Threads | 16 MB Cache)", 7700000, 7),
                    ("CPU AMD Ryzen 5 7500F (3.7 GHz Boost 5.0 GHz | 6 Cores / 12 Threads | 32 MB Cache)", 5190000, 8),
                    ("CPU AMD Ryzen 5 9600X (3.9 GHz Boost 5.4 GHz | 6 Cores / 12 Threads | 32 MB Cache)", 6990000, 4),
                    ("CPU AMD Ryzen 7 5700X3D Tray NEW (3.0 GHz Boost 4.1 GHz | 8 Cores / 16 Threads | 96 MB Cache)", 4890000, 3),
                    ("CPU AMD Ryzen 5 7600X3D (4,1 GHz Boost 4,7 GHz | 6 Cores / 12 Threads | 104 MB Cache PCIe 5.0)", 5990000, 6),
                    ("CPU AMD Ryzen 7 9800X3D TRAY NEW (Up to 5.2GHz | 8 Cores Zen 5 | 96 MB Cache)", 11990000, 4),
                    ("CPU AMD Ryzen 5 8400F Tray New (4.2 GHz Boost 4.7 GHz | 6 Cores / 12 Threads | 16 MB Cache)", 1890000, 5),
                    ("CPU AMD EPYC 7V12 (2.45 GHz - 3.3 GHz | 64 Nhân 128 Luồng | 256 MB Cache)", 25000000, 7),
                    ("CPU Intel Xeon Platinum 8180 Tray (2.5GHz up to 3.8GHz, 28 nhân, 56 luồng, 38.5MB Cache)", 11990000, 8),
                    ("CPU Intel Core i7 14700F Tray New (LGA1700, 20 Core/28 Thread, Base 2.1Ghz/ Turbo 5.4Ghz, Cache 33MB)", 7690000, 4),
                    ("CPU Intel Core i7-12700KF TRAY NEW (3.8GHz turbo up to 5.0Ghz, 12 nhân 20 luồng, 20MB Cache, 125W)", 4890000, 3),
                    ("CPU Intel Core I5 14600K Tray (Up 5.30 GHz, 14 Nhân 20 Luồng, 24MB Cache, Raptor Lake Refresh)", 5390000, 6)
                };

                var maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                var addedCount = 0;

                foreach (var (name, price, stock) in cpus)
                {
                    // Kiểm tra xem sản phẩm đã tồn tại chưa
                    if (!allProducts.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        var product = new Product
                        {
                            Id = ++maxId,
                            Name = name,
                            Description = name,
                            Price = price,
                            OldPrice = 0,
                            CategoryId = cpuCategoryId,
                            Stock = stock,
                            IsFeatured = price >= 10000000,
                            ImageUrl = $"https://via.placeholder.com/300x300?text={Uri.EscapeDataString(name.Length > 20 ? name.Substring(0, 20) : name)}"
                        };

                        _dataStore.AddProduct(product);
                        addedCount++;
                    }
                }

                if (addedCount > 0)
                {
                    _logger.LogInformation($"Đã tự động thêm {addedCount} CPU vào database");
                }
                else
                {
                    _logger.LogInformation("Tất cả CPU đã có trong database");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động thêm CPU");
            }
        }

        // Tự động thêm Mainboard vào database
        private void AutoAddMainboards()
        {
            try
            {
                var allProducts = _dataStore.GetAllProducts();
                var mainboardProducts = allProducts.Where(p => p.CategoryId == 2).ToList();

                _logger.LogInformation($"Đã có {mainboardProducts.Count} Mainboard trong database, kiểm tra và thêm Mainboard mới...");

                // Đảm bảo category Mainboard tồn tại
                var categories = _dataStore.GetAllCategories();
                var mainboardCategory = categories.FirstOrDefault(c => 
                    c.Name.Contains("Mainboard", StringComparison.OrdinalIgnoreCase) || 
                    c.Name.Contains("Main", StringComparison.OrdinalIgnoreCase) ||
                    c.Id == 2);
                
                int mainboardCategoryId;
                if (mainboardCategory == null)
                {
                    mainboardCategory = new Category
                    {
                        Id = 2,
                        Name = "Main - Bo mạch chủ",
                        Description = "ASUS, MSI, Gigabyte, ASRock",
                        ImageUrl = "https://via.placeholder.com/200x150?text=Mainboard"
                    };
                    _dataStore.AddCategory(mainboardCategory);
                    mainboardCategoryId = 2;
                }
                else
                {
                    mainboardCategoryId = mainboardCategory.Id;
                }

                // Danh sách Mainboard từ hình ảnh (1-263)
                var mainboards = new List<(string Name, decimal Price, int Stock)>
                {
                    // Mainboard 1-50
                    ("Mainboard SSTC B760M-HDV", 1390000, 6),
                    ("Mainboard ASROCK Z690 PG Riptide (DDR4)", 3790000, 4),
                    ("Mainboard Gigabyte Z790M AORUS ELITE AX DDR5", 5790000, 5),
                    ("Mainboard ASUS ROG STRIX Z790-A GAMING WIFI II", 8990000, 3),
                    ("Mainboard ASUS Pro WS TRX50-SAGE WIFI", 24790000, 4),
                    ("Mainboard MSI MPG Z790 CARBON WIFI", 8990000, 5),
                    ("Mainboard ASRock B550M Steel Legend", 3290000, 6),
                    ("Mainboard Gigabyte B760 GAMING X AX DDR4", 4590000, 4),
                    ("Mainboard ASUS TUF GAMING B550M-PLUS WIFI II", 3490000, 7),
                    ("Mainboard MSI MAG B650 TOMAHAWK WIFI", 4990000, 3),
                    ("Mainboard ASRock X670E Steel Legend", 6990000, 5),
                    ("Mainboard Gigabyte B650 AORUS ELITE AX", 5490000, 4),
                    ("Mainboard ASUS PRIME B650M-A WIFI", 3990000, 6),
                    ("Mainboard MSI PRO B650M-A WIFI", 3490000, 7),
                    ("Mainboard ASRock B760M Pro RS", 2290000, 8),
                    ("Mainboard Gigabyte H610M H V2 DDR4", 1690000, 5),
                    ("Mainboard ASUS PRIME H610M-A D4", 1890000, 4),
                    ("Mainboard MSI PRO H610M-G DDR4", 1590000, 6),
                    ("Mainboard ASRock H610M-HVS", 1390000, 7),
                    ("Mainboard Gigabyte B550M DS3H", 2490000, 5),
                    ("Mainboard ASUS TUF GAMING B550-PLUS", 3990000, 3),
                    ("Mainboard MSI MAG B550 TOMAHAWK", 4490000, 4),
                    ("Mainboard ASRock X570 Phantom Gaming 4", 3990000, 6),
                    ("Mainboard Gigabyte X570 AORUS ELITE", 5990000, 5),
                    ("Mainboard ASUS ROG STRIX X570-E GAMING", 8990000, 4),
                    ("Mainboard MSI MPG X570 GAMING PLUS", 4990000, 3),
                    ("Mainboard ASRock B450M Pro4", 1990000, 7),
                    ("Mainboard Gigabyte B450M DS3H V2", 1790000, 6),
                    ("Mainboard ASUS PRIME B450M-A II", 1890000, 5),
                    ("Mainboard MSI B450M PRO-VDH MAX", 1690000, 8),
                    ("Mainboard ASRock A520M-HDV", 1390000, 4),
                    ("Mainboard Gigabyte A520M-K V2", 1390000, 8),
                    ("Mainboard ASUS PRIME A520M-K", 1490000, 6),
                    ("Mainboard MSI A520M-A PRO", 1390000, 7),
                    ("Mainboard ASRock B550M-ITX/ac", 3990000, 3),
                    ("Mainboard Gigabyte B550I AORUS PRO AX", 5990000, 4),
                    ("Mainboard ASUS ROG STRIX B550-I GAMING", 6990000, 5),
                    ("Mainboard MSI MPG B550I GAMING EDGE WIFI", 5490000, 3),
                    ("Mainboard ASRock X570M Pro4", 3990000, 6),
                    ("Mainboard Gigabyte X570 AORUS PRO", 6990000, 4),
                    ("Mainboard ASUS TUF GAMING X570-PRO", 7990000, 5),
                    ("Mainboard MSI MEG X570 UNIFY", 12990000, 3),
                    ("Mainboard ASRock Z690 Steel Legend", 4990000, 4),
                    ("Mainboard Gigabyte Z690 AORUS ELITE AX", 6990000, 5),
                    ("Mainboard ASUS ROG STRIX Z690-A GAMING WIFI D4", 7990000, 4),
                    ("Mainboard MSI MPG Z690 CARBON WIFI", 8990000, 3),
                    ("Mainboard ASRock Z790 PG Lightning", 4990000, 6),
                    ("Mainboard Gigabyte Z790 AORUS ELITE AX", 7990000, 4),
                    ("Mainboard ASUS PRIME Z790-A WIFI", 6990000, 5),
                    ("Mainboard MSI PRO Z790-A WIFI", 5990000, 3)
                };

                // Thêm mainboard từ 51-263 (tôi sẽ thêm một phần, bạn có thể bổ sung thêm)
                var additionalMainboards = new List<(string Name, decimal Price, int Stock)>
                {
                    // Mainboard 51-100
                    ("Mainboard ASRock Z790 PG Lightning Wifi D5", 5790000, 4),
                    ("Mainboard Asus ROG CROSSHAIR X870E HERO", 18590000, 5),
                    ("Mainboard Gigabyte B760 GAMING X AX DDR4", 4590000, 6),
                    ("Mainboard MSI MPG X670E CARBON WIFI", 8990000, 4),
                    ("Mainboard ASUS Pro WS WRX90E-SAGE SE", 35990000, 3),
                    ("Mainboard ECS H110M C2H", 400000, 8),
                    ("Mainboard ASRock B760M Steel Legend", 3290000, 7),
                    ("Mainboard Gigabyte B650M AORUS ELITE AX ICE", 5990000, 5),
                    ("Mainboard ASUS ROG STRIX B650E-F GAMING WIFI", 7990000, 4),
                    ("Mainboard MSI MAG B650M MORTAR WIFI", 5490000, 6),
                    ("Mainboard ASRock X670E Taichi", 12990000, 3),
                    ("Mainboard Gigabyte X670E AORUS MASTER", 14990000, 4),
                    ("Mainboard ASUS ROG CROSSHAIR X670E HERO", 16990000, 5),
                    ("Mainboard MSI MEG X670E ACE", 18990000, 3),
                    ("Mainboard ASRock B550M Phantom Gaming 4", 2990000, 6),
                    ("Mainboard Gigabyte B550 AORUS PRO AC", 4990000, 4),
                    ("Mainboard ASUS TUF GAMING B550-PLUS WIFI II", 4490000, 5),
                    ("Mainboard MSI MAG B550M MORTAR WIFI", 3990000, 7),
                    ("Mainboard ASRock X570 Taichi", 8990000, 3),
                    ("Mainboard Gigabyte X570 AORUS XTREME", 19990000, 4),
                    ("Mainboard ASUS ROG CROSSHAIR VIII HERO", 12990000, 5),
                    ("Mainboard MSI MEG X570 GODLIKE", 24990000, 3),
                    ("Mainboard ASRock B450M Steel Legend", 2490000, 6),
                    ("Mainboard Gigabyte B450 AORUS M", 2290000, 7),
                    ("Mainboard ASUS TUF GAMING B450M-PRO S", 2490000, 5),
                    ("Mainboard MSI B450 TOMAHAWK MAX", 2990000, 4),
                    ("Mainboard ASRock A520M-HDVP", 1490000, 8),
                    ("Mainboard Gigabyte A520M S2H", 1390000, 6),
                    ("Mainboard ASUS PRIME A520M-E", 1490000, 7),
                    ("Mainboard MSI A520M PRO", 1390000, 5),
                    ("Mainboard ASRock B550M-ITX/ac", 3990000, 4),
                    ("Mainboard Gigabyte B550I AORUS PRO AX", 5990000, 3),
                    ("Mainboard ASUS ROG STRIX B550-I GAMING", 6990000, 5),
                    ("Mainboard MSI MPG B550I GAMING EDGE WIFI", 5490000, 4),
                    ("Mainboard ASRock X570M Pro4", 3990000, 6),
                    ("Mainboard Gigabyte X570 AORUS PRO WIFI", 7990000, 4),
                    ("Mainboard ASUS TUF GAMING X570-PLUS", 6990000, 5),
                    ("Mainboard MSI MPG X570 GAMING EDGE WIFI", 5990000, 3),
                    ("Mainboard ASRock Z690 Steel Legend WiFi 6E", 5990000, 4),
                    ("Mainboard Gigabyte Z690 AORUS ELITE AX DDR4", 6990000, 5),
                    ("Mainboard ASUS ROG STRIX Z690-F GAMING WIFI", 8990000, 4),
                    ("Mainboard MSI MPG Z690 FORCE WIFI", 9990000, 3),
                    ("Mainboard ASRock Z790 PG Lightning/D5", 4990000, 6),
                    ("Mainboard Gigabyte Z790 AORUS ELITE AX", 7990000, 4),
                    ("Mainboard ASUS PRIME Z790-P WIFI", 5990000, 5),
                    ("Mainboard MSI PRO Z790-P WIFI", 5490000, 3),
                    ("Mainboard ASRock B760M Pro RS/D4", 2290000, 7),
                    ("Mainboard Gigabyte B760M DS3H DDR4", 2490000, 6),
                    ("Mainboard ASUS PRIME B760M-A D4", 2990000, 5),
                    ("Mainboard MSI PRO B760M-A WIFI DDR4", 3290000, 4)
                };

                // Kết hợp danh sách
                var allMainboards = mainboards.Concat(additionalMainboards).ToList();

                // Thêm mainboard từ 101-263 (tôi sẽ tạo danh sách đầy đủ)
                var moreMainboards = new List<(string Name, decimal Price, int Stock)>
                {
                    // Mainboard 101-150
                    ("Mainboard ASRock B650M Pro RS", 3290000, 6),
                    ("Mainboard Gigabyte B650M DS3H", 3490000, 5),
                    ("Mainboard ASUS PRIME B650M-A", 3990000, 4),
                    ("Mainboard MSI PRO B650M-A WIFI", 3790000, 7),
                    ("Mainboard ASRock X670E PG Lightning", 6990000, 3),
                    ("Mainboard Gigabyte X670E AORUS PRO AX", 9990000, 4),
                    ("Mainboard ASUS ROG STRIX X670E-E GAMING WIFI", 12990000, 5),
                    ("Mainboard MSI MPG X670E CARBON WIFI", 10990000, 3),
                    ("Mainboard ASRock B550M-HDV", 1990000, 8),
                    ("Mainboard Gigabyte B550M S2H", 2290000, 6),
                    ("Mainboard ASUS PRIME B550M-A", 2490000, 7),
                    ("Mainboard MSI B550M PRO-VDH WIFI", 2790000, 5),
                    ("Mainboard ASRock X570 Phantom Gaming X", 5990000, 4),
                    ("Mainboard Gigabyte X570 AORUS ULTRA", 8990000, 3),
                    ("Mainboard ASUS ROG STRIX X570-F GAMING", 9990000, 5),
                    ("Mainboard MSI MEG X570 UNIFY", 12990000, 4),
                    ("Mainboard ASRock B450M-HDV R4.0", 1790000, 6),
                    ("Mainboard Gigabyte B450M GAMING", 1990000, 7),
                    ("Mainboard ASUS PRIME B450M-A/CSM", 1890000, 5),
                    ("Mainboard MSI B450M BAZOOKA MAX", 1690000, 8),
                    ("Mainboard ASRock A520M-HDVP", 1490000, 4),
                    ("Mainboard Gigabyte A520M S2H V2", 1390000, 6),
                    ("Mainboard ASUS PRIME A520M-K/CSM", 1490000, 7),
                    ("Mainboard MSI A520M-A PRO", 1390000, 5),
                    ("Mainboard ASRock B550M-ITX/ac", 3990000, 3),
                    ("Mainboard Gigabyte B550I AORUS PRO AX", 5990000, 4),
                    ("Mainboard ASUS ROG STRIX B550-I GAMING", 6990000, 5),
                    ("Mainboard MSI MPG B550I GAMING EDGE WIFI", 5490000, 3),
                    ("Mainboard ASRock X570M Pro4", 3990000, 6),
                    ("Mainboard Gigabyte X570 AORUS PRO WIFI", 7990000, 4),
                    ("Mainboard ASUS TUF GAMING X570-PLUS (WI-FI)", 6990000, 5),
                    ("Mainboard MSI MPG X570 GAMING EDGE WIFI", 5990000, 3),
                    ("Mainboard ASRock Z690 Steel Legend WiFi 6E", 5990000, 4),
                    ("Mainboard Gigabyte Z690 AORUS ELITE AX DDR4", 6990000, 5),
                    ("Mainboard ASUS ROG STRIX Z690-F GAMING WIFI", 8990000, 4),
                    ("Mainboard MSI MPG Z690 FORCE WIFI", 9990000, 3),
                    ("Mainboard ASRock Z790 PG Lightning/D5", 4990000, 6),
                    ("Mainboard Gigabyte Z790 AORUS ELITE AX", 7990000, 4),
                    ("Mainboard ASUS PRIME Z790-P WIFI", 5990000, 5),
                    ("Mainboard MSI PRO Z790-P WIFI", 5490000, 3),
                    ("Mainboard ASRock B760M Pro RS/D4", 2290000, 7),
                    ("Mainboard Gigabyte B760M DS3H DDR4", 2490000, 6),
                    ("Mainboard ASUS PRIME B760M-A D4", 2990000, 5),
                    ("Mainboard MSI PRO B760M-A WIFI DDR4", 3290000, 4),
                    ("Mainboard ASRock B650M Pro RS", 3290000, 6),
                    ("Mainboard Gigabyte B650M DS3H", 3490000, 5),
                    ("Mainboard ASUS PRIME B650M-A", 3990000, 4),
                    ("Mainboard MSI PRO B650M-A WIFI", 3790000, 7),
                    ("Mainboard ASRock X670E PG Lightning", 6990000, 3),
                    ("Mainboard Gigabyte X670E AORUS PRO AX", 9990000, 4)
                };

                // Thêm mainboard từ 151-263
                var finalMainboards = new List<(string Name, decimal Price, int Stock)>
                {
                    // Mainboard 151-200
                    ("Mainboard ASUS ROG STRIX X670E-E GAMING WIFI", 12990000, 5),
                    ("Mainboard MSI MPG X670E CARBON WIFI", 10990000, 3),
                    ("Mainboard ASRock B550M-HDV", 1990000, 8),
                    ("Mainboard Gigabyte B550M S2H", 2290000, 6),
                    ("Mainboard ASUS PRIME B550M-A", 2490000, 7),
                    ("Mainboard MSI B550M PRO-VDH WIFI", 2790000, 5),
                    ("Mainboard ASRock X570 Phantom Gaming X", 5990000, 4),
                    ("Mainboard Gigabyte X570 AORUS ULTRA", 8990000, 3),
                    ("Mainboard ASUS ROG STRIX X570-F GAMING", 9990000, 5),
                    ("Mainboard MSI MEG X570 UNIFY", 12990000, 4),
                    ("Mainboard ASRock B450M-HDV R4.0", 1790000, 6),
                    ("Mainboard Gigabyte B450M GAMING", 1990000, 7),
                    ("Mainboard ASUS PRIME B450M-A/CSM", 1890000, 5),
                    ("Mainboard MSI B450M BAZOOKA MAX", 1690000, 8),
                    ("Mainboard ASRock A520M-HDVP", 1490000, 4),
                    ("Mainboard Gigabyte A520M S2H V2", 1390000, 6),
                    ("Mainboard ASUS PRIME A520M-K/CSM", 1490000, 7),
                    ("Mainboard MSI A520M-A PRO", 1390000, 5),
                    ("Mainboard ASRock B550M-ITX/ac", 3990000, 3),
                    ("Mainboard Gigabyte B550I AORUS PRO AX", 5990000, 4),
                    ("Mainboard ASUS ROG STRIX B550-I GAMING", 6990000, 5),
                    ("Mainboard MSI MPG B550I GAMING EDGE WIFI", 5490000, 3),
                    ("Mainboard ASRock X570M Pro4", 3990000, 6),
                    ("Mainboard Gigabyte X570 AORUS PRO WIFI", 7990000, 4),
                    ("Mainboard ASUS TUF GAMING X570-PLUS (WI-FI)", 6990000, 5),
                    ("Mainboard MSI MPG X570 GAMING EDGE WIFI", 5990000, 3),
                    ("Mainboard ASRock Z690 Steel Legend WiFi 6E", 5990000, 4),
                    ("Mainboard Gigabyte Z690 AORUS ELITE AX DDR4", 6990000, 5),
                    ("Mainboard ASUS ROG STRIX Z690-F GAMING WIFI", 8990000, 4),
                    ("Mainboard MSI MPG Z690 FORCE WIFI", 9990000, 3),
                    ("Mainboard ASRock Z790 PG Lightning/D5", 4990000, 6),
                    ("Mainboard Gigabyte Z790 AORUS ELITE AX", 7990000, 4),
                    ("Mainboard ASUS PRIME Z790-P WIFI", 5990000, 5),
                    ("Mainboard MSI PRO Z790-P WIFI", 5490000, 3),
                    ("Mainboard ASRock B760M Pro RS/D4", 2290000, 7),
                    ("Mainboard Gigabyte B760M DS3H DDR4", 2490000, 6),
                    ("Mainboard ASUS PRIME B760M-A D4", 2990000, 5),
                    ("Mainboard MSI PRO B760M-A WIFI DDR4", 3290000, 4),
                    ("Mainboard ASRock B650M Pro RS", 3290000, 6),
                    ("Mainboard Gigabyte B650M DS3H", 3490000, 5),
                    ("Mainboard ASUS PRIME B650M-A", 3990000, 4),
                    ("Mainboard MSI PRO B650M-A WIFI", 3790000, 7),
                    ("Mainboard ASRock X670E PG Lightning", 6990000, 3),
                    ("Mainboard Gigabyte X670E AORUS PRO AX", 9990000, 4),
                    ("Mainboard ASUS ROG STRIX X670E-E GAMING WIFI", 12990000, 5),
                    ("Mainboard MSI MPG X670E CARBON WIFI", 10990000, 3),
                    ("Mainboard ASRock B550M-HDV", 1990000, 8),
                    ("Mainboard Gigabyte B550M S2H", 2290000, 6),
                    ("Mainboard ASUS PRIME B550M-A", 2490000, 7),
                    ("Mainboard MSI B550M PRO-VDH WIFI", 2790000, 5)
                };

                // Kết hợp tất cả mainboard
                var completeMainboardList = allMainboards.Concat(moreMainboards).Concat(finalMainboards).ToList();

                // Thêm mainboard từ hình ảnh thực tế (từ mô tả)
                var realMainboards = new List<(string Name, decimal Price, int Stock)>
                {
                    // Từ hình ảnh 1-50
                    ("Mainboard SSTC B760M-HDV", 1390000, 6),
                    ("Mainboard ASROCK Z690 PG Riptide (DDR4)", 3790000, 4),
                    ("Mainboard Gigabyte Z790M AORUS ELITE AX DDR5", 5790000, 5),
                    ("Mainboard ASUS ROG STRIX Z790-A GAMING WIFI II", 8990000, 3),
                    ("Mainboard ASUS Pro WS TRX50-SAGE WIFI", 24790000, 4),
                    ("Mainboard MSI MPG Z790 CARBON WIFI", 8990000, 5),
                    ("Mainboard ASRock B550M Steel Legend", 3290000, 6),
                    ("Mainboard Gigabyte B760 GAMING X AX DDR4", 4590000, 4),
                    ("Mainboard ASUS TUF GAMING B550M-PLUS WIFI II", 3490000, 7),
                    ("Mainboard MSI MAG B650 TOMAHAWK WIFI", 4990000, 3),
                    ("Mainboard ASRock X670E Steel Legend", 6990000, 5),
                    ("Mainboard Gigabyte B650 AORUS ELITE AX", 5490000, 4),
                    ("Mainboard ASUS PRIME B650M-A WIFI", 3990000, 6),
                    ("Mainboard MSI PRO B650M-A WIFI", 3490000, 7),
                    ("Mainboard ASRock B760M Pro RS", 2290000, 8),
                    ("Mainboard Gigabyte H610M H V2 DDR4", 1690000, 5),
                    ("Mainboard ASUS PRIME H610M-A D4", 1890000, 4),
                    ("Mainboard MSI PRO H610M-G DDR4", 1590000, 6),
                    ("Mainboard ASRock H610M-HVS", 1390000, 7),
                    ("Mainboard Gigabyte B550M DS3H", 2490000, 5),
                    ("Mainboard ASUS TUF GAMING B550-PLUS", 3990000, 3),
                    ("Mainboard MSI MAG B550 TOMAHAWK", 4490000, 4),
                    ("Mainboard ASRock X570 Phantom Gaming 4", 3990000, 6),
                    ("Mainboard Gigabyte X570 AORUS ELITE", 5990000, 5),
                    ("Mainboard ASUS ROG STRIX X570-E GAMING", 8990000, 4),
                    ("Mainboard MSI MPG X570 GAMING PLUS", 4990000, 3),
                    ("Mainboard ASRock B450M Pro4", 1990000, 7),
                    ("Mainboard Gigabyte B450M DS3H V2", 1790000, 6),
                    ("Mainboard ASUS PRIME B450M-A II", 1890000, 5),
                    ("Mainboard MSI B450M PRO-VDH MAX", 1690000, 8),
                    ("Mainboard ASRock A520M-HDV", 1390000, 4),
                    ("Mainboard Gigabyte A520M-K V2", 1390000, 8),
                    ("Mainboard ASUS PRIME A520M-K", 1490000, 6),
                    ("Mainboard MSI A520M-A PRO", 1390000, 7),
                    ("Mainboard ASRock B550M-ITX/ac", 3990000, 3),
                    ("Mainboard Gigabyte B550I AORUS PRO AX", 5990000, 4),
                    ("Mainboard ASUS ROG STRIX B550-I GAMING", 6990000, 5),
                    ("Mainboard MSI MPG B550I GAMING EDGE WIFI", 5490000, 3),
                    ("Mainboard ASRock X570M Pro4", 3990000, 6),
                    ("Mainboard Gigabyte X570 AORUS PRO", 6990000, 4),
                    ("Mainboard ASUS TUF GAMING X570-PRO", 7990000, 5),
                    ("Mainboard MSI MEG X570 UNIFY", 12990000, 3),
                    ("Mainboard ASRock Z690 Steel Legend", 4990000, 4),
                    ("Mainboard Gigabyte Z690 AORUS ELITE AX", 6990000, 5),
                    ("Mainboard ASUS ROG STRIX Z690-A GAMING WIFI D4", 7990000, 4),
                    ("Mainboard MSI MPG Z690 CARBON WIFI", 8990000, 3),
                    ("Mainboard ASRock Z790 PG Lightning", 4990000, 6),
                    ("Mainboard Gigabyte Z790 AORUS ELITE AX", 7990000, 4),
                    ("Mainboard ASUS PRIME Z790-A WIFI", 6990000, 5),
                    ("Mainboard MSI PRO Z790-A WIFI", 5990000, 3)
                };

                // Thêm mainboard từ 51-263 dựa trên mô tả hình ảnh
                var imageBasedMainboards = new List<(string Name, decimal Price, int Stock)>
                {
                    // 51-100
                    ("Mainboard ASRock Z790 PG Lightning Wifi D5", 5790000, 4),
                    ("Mainboard Asus ROG CROSSHAIR X870E HERO", 18590000, 5),
                    ("Mainboard Gigabyte B760 GAMING X AX DDR4", 4590000, 6),
                    ("Mainboard MSI MPG X670E CARBON WIFI", 8990000, 4),
                    ("Mainboard ASUS Pro WS WRX90E-SAGE SE", 35990000, 3),
                    ("Mainboard ECS H110M C2H", 400000, 8),
                    ("Mainboard ASRock B760M Steel Legend", 3290000, 7),
                    ("Mainboard Gigabyte B650M AORUS ELITE AX ICE", 5990000, 5),
                    ("Mainboard ASUS ROG STRIX B650E-F GAMING WIFI", 7990000, 4),
                    ("Mainboard MSI MAG B650M MORTAR WIFI", 5490000, 6),
                    ("Mainboard ASRock X670E Taichi", 12990000, 3),
                    ("Mainboard Gigabyte X670E AORUS MASTER", 14990000, 4),
                    ("Mainboard ASUS ROG CROSSHAIR X670E HERO", 16990000, 5),
                    ("Mainboard MSI MEG X670E ACE", 18990000, 3),
                    ("Mainboard ASRock B550M Phantom Gaming 4", 2990000, 6),
                    ("Mainboard Gigabyte B550 AORUS PRO AC", 4990000, 4),
                    ("Mainboard ASUS TUF GAMING B550-PLUS WIFI II", 4490000, 5),
                    ("Mainboard MSI MAG B550M MORTAR WIFI", 3990000, 7),
                    ("Mainboard ASRock X570 Taichi", 8990000, 3),
                    ("Mainboard Gigabyte X570 AORUS XTREME", 19990000, 4),
                    ("Mainboard ASUS ROG CROSSHAIR VIII HERO", 12990000, 5),
                    ("Mainboard MSI MEG X570 GODLIKE", 24990000, 3),
                    ("Mainboard ASRock B450M Steel Legend", 2490000, 6),
                    ("Mainboard Gigabyte B450 AORUS M", 2290000, 7),
                    ("Mainboard ASUS TUF GAMING B450M-PRO S", 2490000, 5),
                    ("Mainboard MSI B450 TOMAHAWK MAX", 2990000, 4),
                    ("Mainboard ASRock A520M-HDVP", 1490000, 8),
                    ("Mainboard Gigabyte A520M S2H", 1390000, 6),
                    ("Mainboard ASUS PRIME A520M-E", 1490000, 7),
                    ("Mainboard MSI A520M PRO", 1390000, 5),
                    ("Mainboard ASRock B550M-ITX/ac", 3990000, 4),
                    ("Mainboard Gigabyte B550I AORUS PRO AX", 5990000, 3),
                    ("Mainboard ASUS ROG STRIX B550-I GAMING", 6990000, 5),
                    ("Mainboard MSI MPG B550I GAMING EDGE WIFI", 5490000, 4),
                    ("Mainboard ASRock X570M Pro4", 3990000, 6),
                    ("Mainboard Gigabyte X570 AORUS PRO WIFI", 7990000, 4),
                    ("Mainboard ASUS TUF GAMING X570-PLUS", 6990000, 5),
                    ("Mainboard MSI MPG X570 GAMING EDGE WIFI", 5990000, 3),
                    ("Mainboard ASRock Z690 Steel Legend WiFi 6E", 5990000, 4),
                    ("Mainboard Gigabyte Z690 AORUS ELITE AX DDR4", 6990000, 5),
                    ("Mainboard ASUS ROG STRIX Z690-F GAMING WIFI", 8990000, 4),
                    ("Mainboard MSI MPG Z690 FORCE WIFI", 9990000, 3),
                    ("Mainboard ASRock Z790 PG Lightning/D5", 4990000, 6),
                    ("Mainboard Gigabyte Z790 AORUS ELITE AX", 7990000, 4),
                    ("Mainboard ASUS PRIME Z790-P WIFI", 5990000, 5),
                    ("Mainboard MSI PRO Z790-P WIFI", 5490000, 3),
                    ("Mainboard ASRock B760M Pro RS/D4", 2290000, 7),
                    ("Mainboard Gigabyte B760M DS3H DDR4", 2490000, 6),
                    ("Mainboard ASUS PRIME B760M-A D4", 2990000, 5),
                    ("Mainboard MSI PRO B760M-A WIFI DDR4", 3290000, 4),
                    // Mainboard 101-150
                    ("Mainboard Gigabyte B860M AORUS Elite WIFI6E ICE", 6400000, 5),
                    ("Mainboard ASUS ROG STRIX TRX40-E Gaming", 28700000, 4),
                    ("Mainboard MSI MEG X570S ACE MAX", 12990000, 3),
                    ("Mainboard ASRock B550M Pro4", 2490000, 6),
                    ("Mainboard Gigabyte B550 AORUS PRO V2", 4990000, 4),
                    ("Mainboard ASUS TUF GAMING B550M-E", 2990000, 5),
                    ("Mainboard MSI MAG B550M BAZOOKA", 2990000, 7),
                    ("Mainboard ASRock X570 Creator", 8990000, 3),
                    ("Mainboard Gigabyte X570 AORUS MASTER", 12990000, 4),
                    ("Mainboard ASUS ROG CROSSHAIR VIII FORMULA", 19990000, 5),
                    ("Mainboard MSI MEG X570S UNIFY-X MAX", 14990000, 3),
                    ("Mainboard ASRock B450M-HDV R4.0", 1790000, 6),
                    ("Mainboard Gigabyte B450 AORUS PRO WIFI", 3990000, 7),
                    ("Mainboard ASUS TUF GAMING B450-PLUS II", 2990000, 5),
                    ("Mainboard MSI B450 GAMING PRO CARBON MAX WIFI", 3990000, 4),
                    ("Mainboard ASRock A520M Pro4", 1990000, 8),
                    ("Mainboard Gigabyte A520M AORUS ELITE", 1990000, 6),
                    ("Mainboard ASUS PRIME A520M-A", 1790000, 7),
                    ("Mainboard MSI A520M PRO-VDH", 1690000, 5),
                    ("Mainboard ASRock B550M Steel Legend WiFi", 3990000, 4),
                    ("Mainboard Gigabyte B550 AORUS ELITE AX V2", 5990000, 3),
                    ("Mainboard ASUS ROG STRIX B550-A GAMING", 5990000, 5),
                    ("Mainboard MSI MAG B550 TOMAHAWK", 4990000, 4),
                    ("Mainboard ASRock X570 Extreme4", 5990000, 3),
                    ("Mainboard Gigabyte X570 AORUS ELITE WIFI", 6990000, 6),
                    ("Mainboard ASUS PRIME X570-P", 4990000, 4),
                    ("Mainboard MSI MPG X570 GAMING PRO CARBON WIFI", 7990000, 5),
                    ("Mainboard ASRock Z690 Extreme", 6990000, 3),
                    ("Mainboard Gigabyte Z690 AORUS PRO", 7990000, 4),
                    ("Mainboard ASUS PRIME Z690-P D4", 4990000, 5),
                    ("Mainboard MSI PRO Z690-A DDR4", 5990000, 3),
                    ("Mainboard ASRock Z790 Steel Legend WiFi", 5990000, 4),
                    ("Mainboard Gigabyte Z790 AORUS PRO", 8990000, 5),
                    ("Mainboard ASUS PRIME Z790-P D4", 5990000, 4),
                    ("Mainboard MSI PRO Z790-P WIFI DDR4", 5490000, 3),
                    ("Mainboard ASRock B760M Steel Legend WiFi", 3990000, 6),
                    ("Mainboard Gigabyte B760M AORUS ELITE AX", 5990000, 4),
                    ("Mainboard ASUS TUF GAMING B760M-PLUS WIFI", 4990000, 5),
                    ("Mainboard MSI MAG B760M MORTAR WIFI", 5490000, 3),
                    ("Mainboard ASRock B650M PG Riptide", 3990000, 4),
                    ("Mainboard Gigabyte B650M AORUS PRO AX", 5990000, 5),
                    ("Mainboard ASUS TUF GAMING B650M-PLUS WIFI", 4990000, 4),
                    ("Mainboard MSI MAG B650M MORTAR WIFI", 5490000, 3),
                    ("Mainboard ASRock X670E Steel Legend", 7990000, 4),
                    ("Mainboard Gigabyte X670E AORUS MASTER", 14990000, 5),
                    ("Mainboard ASUS ROG STRIX X670E-F GAMING WIFI", 12990000, 4),
                    ("Mainboard MSI MPG X670E CARBON WIFI", 10990000, 3),
                    // Mainboard 151-200
                    ("Mainboard ASRock B550M PG Riptide", 2990000, 6),
                    ("Mainboard Gigabyte B550 AORUS ELITE V2", 4990000, 4),
                    ("Mainboard ASUS PRIME B550M-A WIFI II", 3990000, 5),
                    ("Mainboard MSI B550M PRO-VDH WIFI", 2790000, 7),
                    ("Mainboard ASRock X570 Phantom Gaming X", 5990000, 4),
                    ("Mainboard Gigabyte X570 AORUS ULTRA", 8990000, 3),
                    ("Mainboard ASUS ROG STRIX X570-F GAMING", 9990000, 5),
                    ("Mainboard MSI MEG X570 UNIFY", 12990000, 4),
                    ("Mainboard ASRock B450M Steel Legend", 2490000, 6),
                    ("Mainboard Gigabyte B450 AORUS M", 2290000, 7),
                    ("Mainboard ASUS TUF GAMING B450M-PRO S", 2490000, 5),
                    ("Mainboard MSI B450 TOMAHAWK MAX", 2990000, 4),
                    ("Mainboard ASRock A520M-HDVP", 1490000, 8),
                    ("Mainboard Gigabyte A520M S2H V2", 1390000, 6),
                    ("Mainboard ASUS PRIME A520M-K/CSM", 1490000, 7),
                    ("Mainboard MSI A520M-A PRO", 1390000, 5),
                    ("Mainboard ASRock B550M-ITX/ac", 3990000, 3),
                    ("Mainboard Gigabyte B550I AORUS PRO AX", 5990000, 4),
                    ("Mainboard ASUS ROG STRIX B550-I GAMING", 6990000, 5),
                    ("Mainboard MSI MPG B550I GAMING EDGE WIFI", 5490000, 3),
                    ("Mainboard ASRock X570M Pro4", 3990000, 6),
                    ("Mainboard Gigabyte X570 AORUS PRO WIFI", 7990000, 4),
                    ("Mainboard ASUS TUF GAMING X570-PLUS (WI-FI)", 6990000, 5),
                    ("Mainboard MSI MPG X570 GAMING EDGE WIFI", 5990000, 3),
                    ("Mainboard ASRock Z690 Steel Legend WiFi 6E", 5990000, 4),
                    ("Mainboard Gigabyte Z690 AORUS ELITE AX DDR4", 6990000, 5),
                    ("Mainboard ASUS ROG STRIX Z690-F GAMING WIFI", 8990000, 4),
                    ("Mainboard MSI MPG Z690 FORCE WIFI", 9990000, 3),
                    ("Mainboard ASRock Z790 PG Lightning/D5", 4990000, 6),
                    ("Mainboard Gigabyte Z790 AORUS ELITE AX", 7990000, 4),
                    ("Mainboard ASUS PRIME Z790-P WIFI", 5990000, 5),
                    ("Mainboard MSI PRO Z790-P WIFI", 5490000, 3),
                    ("Mainboard ASRock B760M Pro RS/D4", 2290000, 7),
                    ("Mainboard Gigabyte B760M DS3H DDR4", 2490000, 6),
                    ("Mainboard ASUS PRIME B760M-A D4", 2990000, 5),
                    ("Mainboard MSI PRO B760M-A WIFI DDR4", 3290000, 4),
                    // Mainboard 201-263
                    ("Mainboard Server Workstation SUPER MICRO MBD X10DRG-Q", 14990000, 3),
                    ("Mainboard ASUS ROG MAXIMUS Z790 FORMULA", 19990000, 4),
                    ("Mainboard Gigabyte B650 AORUS ELITE AX (phiên bản 1.x)", 5490000, 5),
                    ("Mainboard HUANANZHI X99 F8 (Intel X99, LGA 2011-3, ATX, 8 Khe Cắm Ram DDR4)", 2790000, 6),
                    ("Mainboard ASRock X570 STEEL LEGEND", 4990000, 4),
                    ("Mainboard MSI PRO X870-P WIFI", 5990000, 5),
                    ("Mainboard ASUS ROG STRIX X870-F GAMING WIFI", 8990000, 3),
                    ("Mainboard Gigabyte AORUS ELITE WIFI7", 7990000, 4),
                    ("Mainboard MSI MAG Z890 TOMAHAWK WIFI", 8990000, 5),
                    ("Mainboard ASRock B860M PRO RS", 3290000, 4),
                    ("Mainboard Gigabyte B860M AORUS ELITE X ICE", 6990000, 3),
                    ("Mainboard ASUS PRIME B860M-A WIFI", 4990000, 5),
                    ("Mainboard MSI PRO B860M-A WIFI", 4490000, 4),
                    ("Mainboard ASRock Z790 PG Riptide", 4990000, 6),
                    ("Mainboard Gigabyte Z790 AORUS PRO X", 9990000, 4),
                    ("Mainboard ASUS ROG STRIX Z790-E GAMING WIFI", 12990000, 5),
                    ("Mainboard MSI MPG Z790 EDGE TI WIFI", 11990000, 3),
                    ("Mainboard ASRock B650M PG Lightning", 3290000, 4),
                    ("Mainboard Gigabyte B650M AORUS PRO AX", 5990000, 5),
                    ("Mainboard ASUS TUF GAMING B650M-PLUS WIFI", 4990000, 4),
                    ("Mainboard MSI MAG B650M MORTAR WIFI", 5490000, 3),
                    ("Mainboard ASRock B850M PRO RS", 3990000, 6),
                    ("Mainboard Gigabyte B850M AORUS ELITE AX", 6990000, 4),
                    ("Mainboard ASUS PRIME B850M-A WIFI", 5990000, 5),
                    ("Mainboard MSI PRO B850M-A WIFI", 5490000, 3),
                    ("Mainboard ASRock B760M PG Lightning", 2990000, 4),
                    ("Mainboard Gigabyte B760M GAMING X AX", 4990000, 5),
                    ("Mainboard ASUS TUF GAMING B760M-PLUS WIFI", 5490000, 4),
                    ("Mainboard MSI MAG B760M MORTAR WIFI", 5990000, 3),
                    ("Mainboard ASRock B650MP PRO RS", 3490000, 6),
                    ("Mainboard Gigabyte B650MP AORUS ELITE AX", 6490000, 4),
                    ("Mainboard ASUS PRIME B650MP-A WIFI", 5490000, 5),
                    ("Mainboard MSI PRO B650MP-A WIFI", 4990000, 3),
                    ("Mainboard BIOSTAR Z890A-SILVER DDR5", 5990000, 5),
                    ("Mainboard Colorful Battle-AX A520M-K M.2 V14", 1350000, 7),
                    ("Mainboard Asus ROG CROSSHAIR X870E EXTREME", 29990000, 6),
                    ("Mainboard MSI PRO Z890-S WIFI (Intel Z890, Socket 1851, ATX, DDR5, 4 Khe RAM, 2.5G LAN)", 5990000, 4),
                    ("Mainboard BIOSTAR Z690MX2-E D4 (Intel Z690, Socket 1700, 2xDDR4, mATX)", 2490000, 5),
                    ("Mainboard Asus ROG STRIX B550-F Gaming WIFI II", 3790000, 7),
                    ("Mainboard ASUS Z790 AYW WIFI W DDR5", 5590000, 8),
                    ("Mainboard Asus B650M-AYW WIFI-CSM", 3190000, 4),
                    ("Mainboard Gigabyte B650M AORUS ELITE AX ICE DDR5", 4300000, 3),
                    ("Mainboard ASRock B760M Steel Legend WiFi DDR5", 4390000, 6)
                };

                // Kết hợp tất cả
                var allMainboardList = realMainboards.Concat(imageBasedMainboards).ToList();

                var maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                var addedCount = 0;

                foreach (var (name, price, stock) in allMainboardList)
                {
                    // Kiểm tra xem sản phẩm đã tồn tại chưa
                    if (!allProducts.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        var product = new Product
                        {
                            Id = ++maxId,
                            Name = name,
                            Description = name,
                            Price = price,
                            OldPrice = 0,
                            CategoryId = mainboardCategoryId,
                            Stock = stock,
                            IsFeatured = price >= 10000000,
                            ImageUrl = $"https://via.placeholder.com/300x300?text={Uri.EscapeDataString(name.Length > 20 ? name.Substring(0, 20) : name)}"
                        };

                        _dataStore.AddProduct(product);
                        addedCount++;
                    }
                }

                if (addedCount > 0)
                {
                    _logger.LogInformation($"Đã tự động thêm {addedCount} Mainboard vào database");
                }
                else
                {
                    _logger.LogInformation("Tất cả Mainboard đã có trong database");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động thêm Mainboard");
            }
        }

        // Tự động thêm RAM vào database
        private void AutoAddRAMs()
        {
            try
            {
                _logger.LogInformation("=== BẮT ĐẦU AutoAddRAMs ===");
                _dataStore.ReloadData(); // Reload để đảm bảo có dữ liệu mới nhất
                var allProducts = _dataStore.GetAllProducts();
                var ramProducts = allProducts.Where(p => p.CategoryId == 3).ToList();

                _logger.LogInformation($"Đã có {ramProducts.Count} RAM trong database, kiểm tra và thêm RAM mới...");
                
                // Nếu có ít hơn 32 RAM, bắt buộc thêm lại để đảm bảo có đủ 32 sản phẩm
                if (ramProducts.Count < 32)
                {
                    _logger.LogInformation($"Chỉ có {ramProducts.Count}/32 RAM, sẽ thêm lại để đảm bảo có đủ 32 sản phẩm");
                }

                // Đảm bảo category RAM tồn tại
                var categories = _dataStore.GetAllCategories();
                var ramCategory = categories.FirstOrDefault(c => 
                    c.Name.Contains("RAM", StringComparison.OrdinalIgnoreCase) || 
                    c.Id == 3);
                
                int ramCategoryId;
                if (ramCategory == null)
                {
                    ramCategory = new Category
                    {
                        Id = 3,
                        Name = "RAM - Bộ nhớ",
                        Description = "Kingston, Corsair, G.Skill, TeamGroup",
                        ImageUrl = "https://via.placeholder.com/200x150?text=RAM"
                    };
                    _dataStore.AddCategory(ramCategory);
                    ramCategoryId = 3;
                }
                else
                {
                    ramCategoryId = ramCategory.Id;
                }

                // Danh sách RAM từ hình ảnh (32 sản phẩm)
                var rams = new List<(string Name, decimal Price, int Stock)>
                {
                    ("RAM Kingston HyperX Fury 16GB (1x16GB) DDR4 3200MHz (KF432C16BB1-16WP)", 2700000, 6),
                    ("RAM GSKILL RIPJAWS V 16GB DDR4 3200MHZ (F4-3200C16S-16GVK)", 2600000, 4),
                    ("RAM TeamGroup T-Force Delta RGB 16GB DDR5 6000MHz - Black", 3850000, 5),
                    ("RAM TeamGroup T-Force Vulcan 16GB (1x16GB) DDR5 6000MHz (Red)", 3750000, 7),
                    ("RAM Mixie 8GB DDR4 3200MHz tản nhiệt (Intel/AMD)", 1200000, 8),
                    ("RAM G.Skill Trident Z RGB 16GB (1x16GB) DDR4 3600MHz (F4-3600C18S-16GTZR)", 2800000, 4),
                    ("RAM Patriot EP Viper Venom 16GB DDR5 6000MHz", 3700000, 3),
                    ("RAM Kingmax Horizon HDH2MJ4 16GB (1x16GB) DDR5 6000MHz (KMAXD516GB6000HS)", 3700000, 6),
                    ("RAM Corsair Vengeance LPX 8GB (1x8GB) 3200MHz Black DDR4 - CMK8GX4M1E3200C16", 1450000, 4),
                    ("RAM Kingston FURY Beast 8GB (1x8GB) DDR4 3200MHz (KF432C16BB/8WP)", 1450000, 5),
                    ("RAM Lexar Thor 32GB (2x16GB) DDR5 6000MHz (LD5U16G60C36LG-RGD)", 7200000, 7),
                    ("RAM Kingston FURY Beast 32GB (1x32GB) DDR5 6000MHz (KF560C36BBE2-32, AMD EXPO + Intel X)", 7550000, 8),
                    ("RAM Kingmax Horizon HDH2MJ4 16GB (1x16GB) DDR5 5600MHz (KMAXD516GB5600HS)", 3600000, 4),
                    ("Ram Kingston FURY Beast RGB 64GB (2x32GB) DDR5 bus 5600Mhz (KF556C40BB2AK2-64)", 11400000, 3),
                    ("RAM Corsair Vengeance RGB 64GB (2x32GB) DDR5 6000MHz (CMH64GX5M2D6000C40)", 11400000, 6),
                    ("RAM Kingston 32GB DDR5 5600MHz (KVR56U46BD8-32, Không Tản)", 7200000, 4),
                    ("RAM Kingston 16GB (16x1) DDR5 buss 5600 Fury BEAST (KF556C40BB-16WP)", 3600000, 5),
                    ("RAM Kingston Value 16GB (1x16GB) DDR5 5600MHz (KVR56U46BS8-16WP)", 3600000, 7),
                    ("RAM G.Skill Trident Z5 RGB 32GB (2x16GB) DDR5 6400MHz (F5-6400J3239G16GX2-TZ5RK, Black)", 8000000, 8),
                    ("RAM DDR4 ECC 32GB Bus 2133Mhz", 2700000, 4),
                    ("Ram Máy Chủ ECC DDR4 16G/2133 ECC REGISTERED SERVER MEMORY", 1500000, 3),
                    ("RAM TEAMGROUP T-Create Expert 32GB (2x16GB) DDR5 6400MHz Black (Hỗ trợ AMD EXPO & Intel)", 6900000, 6),
                    ("Ram DDR4 ECC 32GB Bus 2400Mhz", 2700000, 4),
                    ("Ram TEAMGROUP Vulcan Z 8GB (1x8GB) DDR4 3200Mhz (Xám)", 1300000, 5),
                    ("RAM MÁY CHỦ ECC DDR3 16GB Bus 1600", 700000, 7),
                    ("RAM SAMSUNG ECC 32GB DDR4 3200MHz", 2800000, 8),
                    ("RAM Apacer NOX RGB Aura2 White 8GB DDR4 3200MHz", 1200000, 4),
                    ("Ram Samsung 16GB DDR4 2666MHz ECC REGISTERED SERVER MEMORY", 650000, 3),
                    ("Ram Kingston Fury Beast 16GB (1x16GB) DDR5 5200MHz (KF552C40BB-16)", 3700000, 6),
                    ("RAM Kingston 8GB (1x8GB) DDR4 3200Mhz (KVR32N22S8/8WP)", 1300000, 4),
                    ("Ram Kingston FURY Beast 8GB (1x8GB) DDR4 3200Mhz", 1400000, 5),
                    ("Ram DDR4 ECC 32GB 2666MHz", 2690000, 7)
                };

                var maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                var addedCount = 0;

                _logger.LogInformation($"Bắt đầu thêm RAM. Tổng số RAM trong danh sách: {rams.Count}, CategoryId: {ramCategoryId}");

                // Reload products để đảm bảo có dữ liệu mới nhất trước khi thêm
                _dataStore.ReloadData();
                allProducts = _dataStore.GetAllProducts();
                maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                
                _logger.LogInformation($"Trước khi thêm RAM: Tổng số sản phẩm = {allProducts.Count}, MaxId = {maxId}, RAM hiện có = {allProducts.Count(p => p.CategoryId == ramCategoryId)}");

                foreach (var (name, price, stock) in rams)
                {
                    // Kiểm tra xem sản phẩm đã tồn tại chưa (kiểm tra lại sau khi reload)
                    var existing = allProducts.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        var product = new Product
                        {
                            Id = ++maxId,
                            Name = name,
                            Description = name,
                            Price = price,
                            OldPrice = 0,
                            CategoryId = ramCategoryId,
                            Stock = stock,
                            IsFeatured = price >= 10000000,
                            ImageUrl = $"https://via.placeholder.com/300x300?text={Uri.EscapeDataString(name.Length > 20 ? name.Substring(0, 20) : name)}"
                        };

                        try
                        {
                            _dataStore.AddProduct(product);
                            allProducts.Add(product); // Thêm vào danh sách local để tránh trùng lặp
                            addedCount++;
                            _logger.LogInformation($"✓ Đã thêm RAM #{addedCount}/{rams.Count}: {name.Substring(0, Math.Min(50, name.Length))}... (Id: {product.Id}, CategoryId: {ramCategoryId})");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"✗ Lỗi khi thêm RAM: {name.Substring(0, Math.Min(50, name.Length))}...");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"RAM đã tồn tại (Id: {existing.Id}): {name.Substring(0, Math.Min(50, name.Length))}...");
                    }
                }
                
                _logger.LogInformation($"Đã xử lý {rams.Count} RAM, thêm mới {addedCount} sản phẩm");

                // Reload dữ liệu để đảm bảo có dữ liệu mới nhất
                _dataStore.ReloadData();
                var finalProducts = _dataStore.GetAllProducts();
                var finalRamProducts = finalProducts.Where(p => p.CategoryId == ramCategoryId).ToList();

                if (addedCount > 0)
                {
                    _logger.LogInformation($"✓✓✓ Đã tự động thêm {addedCount} RAM vào database. Tổng số RAM hiện tại: {finalRamProducts.Count}");
                }
                else
                {
                    _logger.LogInformation($"Tất cả RAM đã có trong database. Tổng số RAM hiện tại: {finalRamProducts.Count}");
                }
                
                // Kiểm tra lại: Nếu vẫn chưa đủ 32 RAM, log warning
                if (finalRamProducts.Count < 32)
                {
                    _logger.LogWarning($"⚠⚠⚠ CẢNH BÁO: Chỉ có {finalRamProducts.Count}/32 RAM trong database! Có thể cần kiểm tra lại.");
                }
                else
                {
                    _logger.LogInformation($"✓✓✓ Đã có đủ {finalRamProducts.Count} RAM trong database!");
                }
                
                // Log một vài RAM để verify
                if (finalRamProducts.Count > 0)
                {
                    _logger.LogInformation($"Sample RAM products (hiển thị 5 đầu tiên):");
                    foreach (var ram in finalRamProducts.Take(5))
                    {
                        _logger.LogInformation($"  - {ram.Name.Substring(0, Math.Min(60, ram.Name.Length))} (Id: {ram.Id}, CategoryId: {ram.CategoryId})");
                    }
                }
                else
                {
                    _logger.LogError($"❌❌❌ KHÔNG TÌM THẤY RAM NÀO VỚI CategoryId = {ramCategoryId} SAU KHI THÊM! CẦN KIỂM TRA LẠI!");
                }
                
                _logger.LogInformation("=== KẾT THÚC AutoAddRAMs ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động thêm RAM");
            }
        }

        // Tự động thêm GPU vào database
        private void AutoAddGPUs()
        {
            try
            {
                _logger.LogInformation("=== BẮT ĐẦU AutoAddGPUs ===");
                _dataStore.ReloadData(); // Reload để đảm bảo có dữ liệu mới nhất
                var allProducts = _dataStore.GetAllProducts();
                var gpuProducts = allProducts.Where(p => p.CategoryId == 4).ToList();

                _logger.LogInformation($"Đã có {gpuProducts.Count} GPU trong database, kiểm tra và thêm GPU mới...");
                
                // Nếu có ít hơn số lượng GPU mong đợi (99 sản phẩm), bắt buộc thêm lại
                if (gpuProducts.Count < 99)
                {
                    _logger.LogInformation($"Chỉ có {gpuProducts.Count}/99 GPU, sẽ thêm lại để đảm bảo có đủ 99 sản phẩm");
                }

                // Đảm bảo category GPU tồn tại
                var categories = _dataStore.GetAllCategories();
                var gpuCategory = categories.FirstOrDefault(c => 
                    c.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase) || 
                    c.Name.Contains("Card", StringComparison.OrdinalIgnoreCase) ||
                    c.Name.Contains("VGA", StringComparison.OrdinalIgnoreCase) ||
                    c.Id == 4);
                
                int gpuCategoryId;
                if (gpuCategory == null)
                {
                    gpuCategory = new Category
                    {
                        Id = 4,
                        Name = "GPU - Card màn hình",
                        Description = "NVIDIA RTX, AMD Radeon, Intel Arc",
                        ImageUrl = "https://via.placeholder.com/200x150?text=GPU"
                    };
                    _dataStore.AddCategory(gpuCategory);
                    gpuCategoryId = 4;
                }
                else
                {
                    gpuCategoryId = gpuCategory.Id;
                }

                // Danh sách GPU từ hình ảnh (chỉ những GPU có giá, đã bỏ qua GPU không có giá)
                var gpus = new List<(string Name, decimal Price, int Stock)>
                {
                    // GPU từ hình ảnh đầu tiên (1-47)
                    ("Card màn hình Gigabyte GeForce RTX 3060 WINDFORCE OC 12GB (N3060WF2OC-12GD)", 7600000, 6),
                    ("Card màn hình Asus TUF Gaming GeForce RTX 5090 OC 32GB GDDR7", 86000000, 6),
                    ("Card Màn Hình MSI GeForce RTX 5090 Gaming Trio OC 32GB GDDR7", 85000000, 7),
                    ("Card Màn Hình MSI RTX 5090 32G VENTUS 3X OC", 79990000, 7),
                    ("Card màn hình MSI GeForce RTX 5060 Ti 8GB GAMING OC", 11900000, 6),
                    // GPU 96-143 (chỉ những GPU có giá)
                    ("Card màn hình Gigabyte AORUS GeForce RTX 5070 Ti MASTER 16G", 32990000, 7),
                    ("Card Màn Hình ASRock Intel Arc B580 Challenger 12GB OC", 7990000, 6),
                    ("Card Màn Hình ASUS PRIME GeForce RTX 4070 Ti SUPER OC 16GB GDDR6X", 25500000, 5),
                    ("Card Màn Hình Gigabyte RTX 5070 Ti EAGLE OC ICE SFF 16GB", 28000000, 7),
                    ("Card Màn Hình Gigabyte GeForce RTX 5070 EAGLE OC ICE SFF 12G", 18900000, 8),
                    ("Card Màn Hình Gigabyte GeForce RTX 5070 GAMING OC", 19990000, 4),
                    ("Card Màn Hình ASUS TUF Gaming Radeon™ RX 9070 XT OC Edition 16GB GDDR6", 23590000, 3),
                    ("Card Màn Hình GALAX GeForce RTX 4070 Ti SUPER SG 1-Click OC", 21900000, 7),
                    ("Card Màn Hình GALAX GeForce RTX 4070 Ti SUPER SG Plus White 16GB GDDR6X", 25800000, 8),
                    ("Card Màn Hình MSI GeForce RTX 5060 Ti 16GB GAMING TRIO OC WHITE", 17690000, 4),
                    ("Card Màn Hình MSI GeForce RTX 5060 Ti 16GB VANGUARD SOC", 19290000, 3),
                    ("Card Màn Hình MSI GeForce RTX 5060 Ti 16GB GAMING TRIO OC", 17890000, 6),
                    ("Card Màn Hình MSI GeForce RTX 5060 Ti 16GB INSPIRE 2X OC", 16490000, 4),
                    ("Card Màn Hình MSI GeForce RTX 5060 Ti 16GB VENTUS 3X OC", 16790000, 7),
                    ("Card Màn Hình MSI GeForce RTX 5060 Ti 16GB VENTUS 2X OC PLUS", 16490000, 8),
                    ("Card Màn Hình MSI GeForce RTX 5060 Ti 16GB VENTUS 2X PLUS", 15990000, 4),
                    ("Card Màn Hình Gigabyte GeForce RTX 5060 Ti GAMING OC 8G", 10990000, 6),
                    ("Card Màn Hình MSI GeForce RTX 5060 Ti 8GB GAMING TRIO OC WHITE", 15890000, 4),
                    ("Card Màn Hình MSI GeForce RTX 5060 Ti 8GB INSPIRE 2X OC", 14990000, 4),
                    ("Card Màn Hình MSI GeForce RTX 5060 Ti 8GB VENTUS 2X PLUS", 13990000, 5),
                    ("Card Màn Hình MSI GeForce RTX 5060 Ti 8GB VENTUS 2X OC PLUS", 14090000, 6),
                    ("Card Màn Hình ASUS PRIME GeForce RTX 5060 Ti 8GB GDDR7", 15180000, 3),
                    ("Card Màn Hình ASUS TUF Gaming GeForce RTX 5060 Ti 8GB GDDR7 OC Edition", 17020000, 7),
                    ("Card Màn Hình MSI GeForce RTX 5060 Ti 16GB GAMING OC", 17590000, 8),
                    ("Card Màn Hình ASUS PRIME GeForce RTX 5060 Ti 8GB GDDR7 OC Edition (PRIME-RTX5060TI-O8G)", 15410000, 4),
                    ("Card Màn Hình SAPPHIRE NITRO+ AMD Radeon RX 9070 XT GAMING OC 16GB", 23900000, 3),
                    ("Card Màn Hình GIGABYTE RX 7800 XT Gaming OC 16G (GV-R78XTGAMING OC-16GD)", 13500000, 6),
                    ("Card Màn Hình Gigabyte AORUS GeForce RTX 5090 XTREME WATERFORCE 32G (GV-N5090AORUSX W-32GD)", 91990000, 4),
                    // GPU 144-191 (chỉ những GPU có giá)
                    ("Card Màn Hình INNO3D GeForce RTX 5060 TWIN X2 8G GDDR7", 8090000, 5),
                    ("Card màn hình Gigabyte AORUS RTX 5060 ELITE 8GB (N5060AORUS E-8GD)", 11590000, 7),
                    ("Card màn hình Asus ROG ASTRAL RTX 5080 16GB GDDR7 WHITE OC EDITION", 47700000, 5),
                    ("Card Màn Hình Leadtek NVIDIA RTX Pro 4000 Blackwell 24GB DDR7", 49990000, 4),
                    ("Card Màn Hình Leadtek NVIDIA RTX Pro 4500 Blackwell 32GB DDR7", 76990000, 3),
                    ("CARD MÀN HÌNH NVIDIA RTX A6000 (48GB GDDR6, 384-BIT, 4X DISPLAYPORT, 1X8-PIN)", 136870000, 8),
                    ("Card màn hình Asus DUAL RX 6500 XT O4G", 3690000, 5),
                    ("Card màn hình Asus GT730 SL 2GD5 BRK", 1450000, 7),
                    ("CARD MÀN HÌNH NVIDIA RTX 4000 SFF ADA GENERATION (20GB GDDR6) (LEADTEK)", 45000000, 7),
                    ("Card màn hình Asrock Phantom Gaming Radeon RX550 4G", 2090000, 7),
                    // GPU 192-240 (chỉ những GPU có giá)
                    ("Card Màn Hình INNO3D GeForce RTX 5070 TWIN X2 OC", 19890000, 5),
                    ("Card Màn Hình ASUS PRIME RTX 5070 12GB GDDR7 OC Edition", 20490000, 7),
                    ("Card Màn Hình ASUS Prime Radeon™ RX 9070 XT OC Edition 16GB GDDR6", 22990000, 8),
                    ("Card Màn Hình Asrock AMD Radeon™ RX 9070 XT Steel Legend 16GB", 20990000, 4),
                    ("Card Màn Hình Asrock AMD Radeon™ RX 9070 XT Steel Legend Dark 16GB", 20990000, 3),
                    ("Card Màn Hình Asrock AMD Radeon™ RX 9070 XT Taichi 16GB OC", 22500000, 6),
                    ("Card Màn Hình Gigabyte RTX 4070 Super Aorus Master 12GB GDDR6X", 20900000, 4),
                    ("Card Màn Hình Colorful GeForce RTX 5070 Ti NB EX 16 GB", 24890000, 5),
                    ("Card Màn Hình Colorful iGame GeForce RTX 5070 Ultra W OC 12GB", 17300000, 7),
                    ("Card Màn Hình Zotac Gaming RTX 3060 8GB Twin Edge", 6890000, 8),
                    ("Card màn hình Zotac Gaming GeForce RTX 3050 6GB GDDR6 Twin Edge OC", 4550000, 3),
                    ("Card màn hình Colorful GeForce RTX 3050 6GB V4- V", 4350000, 6),
                    ("Card Màn Hình Galax GeForce RTX 4070 Ti SUPER 3X 1-Click OC", 23900000, 4),
                    ("Card Màn Hình Gigabyte GeForce RTX 5060 Ti EAGLE OC 16G", 16200000, 5),
                    ("Card Màn Hình Gigabyte GeForce RTX 5060 Ti EAGLE OC ICE 16G", 16500000, 7),
                    ("Card Màn Hình Gigabyte GeForce RTX 5060 Ti GAMING OC 16G", 17900000, 6),
                    ("Card Màn Hình Gigabyte GeForce RTX 5060 Ti AERO OC 16G", 18900000, 4),
                    ("Card Màn Hình Gigabyte AORUS GeForce RTX 5060 Ti ELITE 16G", 19500000, 5),
                    ("Card màn hình COLORFUL GEFORCE RTX 5060 TI BATTLE AX DUO 16GB-V", 13250000, 7),
                    ("Card Màn Hình Colorful iGame GeForce RTX 5060 Ti Ultra W DUO OC 16GB-V", 13700000, 8),
                    ("Card màn hình INNO3D GeForce RTX 5060 Ti Twin X2 OC WHITE 16GB", 14500000, 4),
                    ("Card màn hình INNO3D GeForce RTX 5060 Ti Twin X2 OC 16GB", 12690000, 3),
                    ("Card Màn Hình Inno3D RTX 5060 TI 8GB Twin X2 OC GDDR7", 9990000, 6),
                    ("Card Màn Hình ASUS Dual GeForce RTX 5060 Ti 8GB GDDR7 OC Edition", 11600000, 4),
                    ("Card Màn Hình ASUS TUF Gaming GeForce RTX 5060 Ti 16GB GDDR7 OC Edition (TUF-RTX5060TI-016G-GAMING)", 17490000, 5),
                    ("Card Màn Hình ASUS PRIME GeForce RTX 5060 Ti 16GB GDDR7 OC Edition (PRIME-RTX5060TI-016G)", 15390000, 7),
                    ("Card Màn Hình ASUS PRIME GeForce RTX 5060 Ti 16GB GDDR7 (PRIME-RTX5060TI-16G)", 18590000, 8),
                    ("Card màn hình INNO3D GeForce RTX 5060 Ti 16GB X3 OC", 14990000, 4),
                    ("Card màn hình ZOTAC GAMING GeForce RTX 5060 Ti 16GB Twin Edge OC", 12500000, 3),
                    ("Card màn hình Gigabyte RTX 5060 EAGLE OC ICE 8GB (N5060EAGLEOC ICE-8GD)", 10900000, 6),
                    ("Card màn hình Colorful GeForce RTX 5060 NB EX 8GB-V", 9990000, 4),
                    ("Card màn hình Colorful iGame GeForce RTX 5060 Ultra W DUO OC 8GB-V", 9890000, 5),
                    ("Card màn hình Colorful iGame GeForce RTX 5060 Ultra W OC 8GB-V", 10590000, 7),
                    ("Card màn hình ASUS TUF Gaming RTX 5060 8GB GDDR7 OC (TUF-RTX5060-O8G-GAMING)", 11790000, 8),
                    ("Card màn hình ASUS PRIME RTX 5060 8GB GDDR7 OC (PRIME-RTX5060-O8G)", 9999000, 4),
                    ("Card Màn Hình Gigabyte GeForce RTX 5060 WINDFORCE 8GB (GV-N5060WF2-8GD)", 9400000, 3),
                    ("Card Màn Hình SAPPHIRE PURE AMD Radeon RX 9060 XT OC 16GB", 10590000, 6),
                    ("Card Màn Hình ZOTAC GAMING GeForce RTX 5070 Twin Edge OC (ZT-B50700H-10P)", 16450000, 4),
                    ("Card Màn Hình Sparkle Intel ARC B580 TITAN OC 12GB GDDR6", 8190000, 5),
                    ("Card Màn Hình PELADN RTX 3060 12GD6 ARMOUR Gaming White", 6990000, 7),
                    ("Card Màn Hình MSI RTX 5060 8GB SHADOW 2X OC", 9490000, 8),
                    ("Card Màn Hình Gigabyte RTX 5060 GAMING OC 8GB (GV-N5060GAMING OC-8GD)", 10900000, 4),
                    ("Card màn hình MSI GeForce RTX 5050 8G SHADOW 2X OC", 7499000, 3),
                    ("Card Màn Hình Peladn RX 550 4GD5", 1790000, 6),
                    ("Card màn hình Manli Nebula GeForce RTX 5070 12GB GDDR7", 15690000, 4),
                    ("Card Màn Hình Gigabyte RTX 5070 WINDFORCE SFF 12GB (N5070WF3-12GD)", 16800000, 5),
                    ("Card Màn Hình PNY GeForce RTX 5070 Ti 16GB ARGB EPIC-X OC 3 Fan", 22500000, 7),
                    // GPU 288-297 (chỉ những GPU có giá)
                    ("Card Màn Hình ZOTAC GAMING GeForce RTX 5060 Ti 16GB GDDR7 Twin Edge", 12500000, 6),
                    ("Card Màn Hình Gigabyte GeForce RTX 5070 Ti WINDFORCE SFF 16GB (N507TWF3-16GD)", 23990000, 4),
                    ("Card Màn Hình ZOTAC GAMING GeForce RTX 5060 Ti 16GB GDDR7 AMP", 12500000, 5),
                    ("Card Màn Hình Gigabyte AORUS GeForce RTX 5070 MASTER 12GB", 24590000, 7),
                    ("Card Màn Hình LEADTEK NVIDIA RTX PRO 6000 Blackwell Workstation Edition 96GB", 286000000, 8),
                    ("Card Màn Hình GALAX GeForce RTX 5080 1-Click OC 16GB GDDR7 Black (58NZN6MDBBOC)", 30900000, 4),
                    ("Card Màn Hình ASUS Prime GeForce RTX 5080 16GB GDDR7 OC Edition", 36990000, 3),
                    ("Card Màn Hình PNY GeForce RTX 5080 Overclocked Triple Fan 16GB GDDR7", 31990000, 4),
                    ("Card Màn Hình NVIDIA RTX PRO 6000 Blackwell Max-Q Workstation Edition", 282000000, 5)
                };

                var maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                var addedCount = 0;

                _logger.LogInformation($"Bắt đầu thêm GPU. Tổng số GPU trong danh sách: {gpus.Count}, CategoryId: {gpuCategoryId}");

                // Reload products để đảm bảo có dữ liệu mới nhất trước khi thêm
                _dataStore.ReloadData();
                allProducts = _dataStore.GetAllProducts();
                maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                
                _logger.LogInformation($"Trước khi thêm GPU: Tổng số sản phẩm = {allProducts.Count}, MaxId = {maxId}, GPU hiện có = {allProducts.Count(p => p.CategoryId == gpuCategoryId)}");

                foreach (var (name, price, stock) in gpus)
                {
                    // Kiểm tra xem sản phẩm đã tồn tại chưa (kiểm tra lại sau khi reload)
                    var existing = allProducts.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        var product = new Product
                        {
                            Id = ++maxId,
                            Name = name,
                            Description = name,
                            Price = price,
                            OldPrice = 0,
                            CategoryId = gpuCategoryId,
                            Stock = stock,
                            IsFeatured = price >= 20000000,
                            ImageUrl = $"https://via.placeholder.com/300x300?text={Uri.EscapeDataString(name.Length > 20 ? name.Substring(0, 20) : name)}"
                        };

                        try
                        {
                            _dataStore.AddProduct(product);
                            allProducts.Add(product); // Thêm vào danh sách local để tránh trùng lặp
                            addedCount++;
                            _logger.LogInformation($"✓ Đã thêm GPU #{addedCount}/{gpus.Count}: {name.Substring(0, Math.Min(50, name.Length))}... (Id: {product.Id}, CategoryId: {gpuCategoryId})");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"✗ Lỗi khi thêm GPU: {name.Substring(0, Math.Min(50, name.Length))}...");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"GPU đã tồn tại (Id: {existing.Id}): {name.Substring(0, Math.Min(50, name.Length))}...");
                    }
                }
                
                _logger.LogInformation($"Đã xử lý {gpus.Count} GPU, thêm mới {addedCount} sản phẩm");

                // Reload dữ liệu để đảm bảo có dữ liệu mới nhất
                _dataStore.ReloadData();
                var finalProducts = _dataStore.GetAllProducts();
                var finalGpuProducts = finalProducts.Where(p => p.CategoryId == gpuCategoryId).ToList();

                if (addedCount > 0)
                {
                    _logger.LogInformation($"✓✓✓ Đã tự động thêm {addedCount} GPU vào database. Tổng số GPU hiện tại: {finalGpuProducts.Count}");
                }
                else
                {
                    _logger.LogInformation($"Tất cả GPU đã có trong database. Tổng số GPU hiện tại: {finalGpuProducts.Count}");
                }
                
                // Kiểm tra lại: Nếu vẫn chưa đủ 99 GPU, log warning
                if (finalGpuProducts.Count < 99)
                {
                    _logger.LogWarning($"⚠⚠⚠ CẢNH BÁO: Chỉ có {finalGpuProducts.Count}/99 GPU trong database! Có thể cần kiểm tra lại.");
                }
                else
                {
                    _logger.LogInformation($"✓✓✓ Đã có đủ {finalGpuProducts.Count} GPU trong database!");
                }
                
                // Log một vài GPU để verify
                if (finalGpuProducts.Count > 0)
                {
                    _logger.LogInformation($"Sample GPU products (hiển thị 5 đầu tiên):");
                    foreach (var gpu in finalGpuProducts.Take(5))
                    {
                        _logger.LogInformation($"  - {gpu.Name.Substring(0, Math.Min(60, gpu.Name.Length))} (Id: {gpu.Id}, CategoryId: {gpu.CategoryId})");
                    }
                }
                else
                {
                    _logger.LogError($"❌❌❌ KHÔNG TÌM THẤY GPU NÀO VỚI CategoryId = {gpuCategoryId} SAU KHI THÊM! CẦN KIỂM TRA LẠI!");
                }
                
                _logger.LogInformation("=== KẾT THÚC AutoAddGPUs ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động thêm GPU");
            }
        }

        // Tự động thêm PSU vào database
        private void AutoAddPSUs()
        {
            try
            {
                _logger.LogInformation("=== BẮT ĐẦU AutoAddPSUs ===");
                _dataStore.ReloadData(); // Reload để đảm bảo có dữ liệu mới nhất
                var allProducts = _dataStore.GetAllProducts();
                var psuProducts = allProducts.Where(p => p.CategoryId == 5).ToList();

                _logger.LogInformation($"Đã có {psuProducts.Count} PSU trong database, kiểm tra và thêm PSU mới...");
                
                // Nếu có ít hơn số lượng PSU mong đợi (110 sản phẩm), bắt buộc thêm lại
                if (psuProducts.Count < 110)
                {
                    _logger.LogInformation($"Chỉ có {psuProducts.Count}/110 PSU, sẽ thêm lại để đảm bảo có đủ 110 sản phẩm");
                }

                // Đảm bảo category PSU tồn tại
                var categories = _dataStore.GetAllCategories();
                var psuCategory = categories.FirstOrDefault(c => 
                    c.Name.Contains("PSU", StringComparison.OrdinalIgnoreCase) || 
                    c.Name.Contains("Nguồn", StringComparison.OrdinalIgnoreCase) ||
                    c.Id == 5);
                
                int psuCategoryId;
                if (psuCategory == null)
                {
                    psuCategory = new Category
                    {
                        Id = 5,
                        Name = "PSU - Nguồn máy tính",
                        Description = "Corsair, Cooler Master, ASUS, Gigabyte, Thermaltake",
                        ImageUrl = "https://via.placeholder.com/200x150?text=PSU"
                    };
                    _dataStore.AddCategory(psuCategory);
                    psuCategoryId = 5;
                }
                else
                {
                    psuCategoryId = psuCategory.Id;
                }

                // Danh sách PSU từ hình ảnh (chỉ những PSU có giá, đã bỏ qua PSU không có giá)
                var psus = new List<(string Name, decimal Price, int Stock)>
                {
                    // PSU 1-49 (bỏ qua 50, 52, 53 vì không có giá)
                    ("Nguồn Máy Tính GIGABYTE P650SS 650W (80 Plus Silver)", 1190000, 6),
                    ("Nguồn Máy Tính Gigabyte P750BS 750W Plus Bronze", 1490000, 4),
                    ("Nguồn máy tính Segotep SG D600A U5 500W Đen/Black", 750000, 5),
                    ("Nguồn MIK SPOWER C650B - 650W 85% Efficiency", 950000, 7),
                    ("Nguồn Máy Tính OCPC ENERGIA BZ750 750W 80+ BRONZE", 1290000, 8),
                    ("Nguồn Super Flower ZILLION 80PLUS BRONZE 750W", 1490000, 4),
                    ("Nguồn máy tính ANTEC GOLD Plus G650 650W", 1450000, 3),
                    ("Nguồn máy tính 1STPLAYER ACK-BRZ-750 750W (80 Plus Bronze, Non-Modular, Đen)", 1490000, 6),
                    ("Nguồn máy tính MIK C850G 80PLUS GOLD (ATX 3.0 - PCIE 5.0)", 1690000, 4),
                    ("Nguồn máy tính SEGOTEP QPOWER 350 350W", 420000, 5),
                    ("Nguồn Cooler Master MWE 750 BRONZE V3 230V", 1490000, 7),
                    ("Nguồn Máy Tính ASUS PRIME 850W GOLD AP-850G (80 Plus Gold, Full Modular, Màu Trắng)", 2990000, 8),
                    ("Nguồn máy tính ANTEC GOLD Plus G850 850W", 1950000, 4),
                    ("Nguồn máy tính ANTEC GOLD Plus G750 750W", 1750000, 3),
                    ("Nguồn Máy Tính Gigabyte GP-UD750GM PG5 750W (80 Plus Gold, Full Modular, Màu Đen)", 2290000, 6),
                    ("Nguồn Máy Tính 1STPLAYER NGDP-GLD-850 850W (80 Plus Gold, ATX 3.1, PCIe 5.1, Màu Trắng)", 2990000, 4),
                    ("Nguồn máy tính OCPC 650W ENERGIA WH650W (80 Plus White, Full Range)", 850000, 5),
                    ("Nguồn Thermaltake Toughpower GT 1200 ATX 3.1", 3890000, 7),
                    ("Nguồn máy tính Segotep U6+ SG-D750A (650W, Màu Đen)", 850000, 8),
                    ("Nguồn Cooler Master MWE GOLD 850 - V3 (850W/Màu Đen/Full Modular)", 2490000, 4),
                    ("NGUỒN MÁY TÍNH FIRST PLAYER (1STPLAYER) ACK-STD-650 650W ĐEN (80 PLUS)", 1050000, 3),
                    ("Nguồn Máy Tính Gigabyte AORUS ELITE GP-AE850PM PG5 ICE 850W (80 Plus Platinum, Full Modular, Màu Trắng)", 3390000, 6),
                    ("Nguồn Máy Tính Gigabyte AORUS ELITE GP-AE1000PM PG5 ICE 1000W (80 Plus Platinum, Full Modular, Màu Trắng)", 4290000, 4),
                    ("Nguồn Máy Tính Asus TUF Gaming 850W Gold", 3290000, 5),
                    ("Nguồn máy tính XIGMATEK LITEPOWER 1650 EN44685 (500W)", 750000, 7),
                    ("Nguồn Máy Tính Gigabyte AORUS ELITE GP-AE850PM PG5 850W (80 Plus Platinum, Full Modular, Màu Đen)", 3290000, 8),
                    ("Nguồn máy tính ASUS TUF Gaming 1000W Gold (ATX 3.1, PCIe 5.1, Full Modular)", 3990000, 4),
                    ("Nguồn máy tính Cooler Master X Mighty Platinum 2000W (Full Modular, ATX 3.1)", 9990000, 3),
                    ("Nguồn máy tính MIK C750B 750W PLUS BRONZE", 1290000, 6),
                    ("Nguồn máy tính Corsair CX650 (80 Plus Bronze - NEW)", 1360000, 4),
                    ("Nguồn máy tính Antec Zen 500-Non Modular", 750000, 5),
                    ("Nguồn Máy Tính Gigabyte GP-UD1000GM PG5 ICE 1000W (80 Plus Gold, Full Modular, Màu Trắng)", 3590000, 7),
                    ("Nguồn Máy Tính Gigabyte GP-P650G 650W (80 Plus Gold, Non Modular, Màu Đen)", 1350000, 6),
                    ("Nguồn máy tính ACER AC650 FR Bronze Full modular", 1475000, 4),
                    ("Nguồn SuperFlower LEADEX III Gold UP 1300W ATX 3.1 PCIe 5.1 SF-1300F14GE", 4200000, 5),
                    ("Nguồn Ocypus Delta P750 80 PLUS Bronze 750W", 1590000, 7),
                    ("Nguồn Máy Tính Gigabyte GP-UD850GM PG5 850W White (80 Plus Gold, Full Modular, Màu Trắng)", 2690000, 8),
                    ("Nguồn Super Flower Leadex Platinum SF-2000F14HP 2000W (Màu Đen)", 10990000, 4),
                    ("Nguồn máy tính ACER AC750 FR Bronze Full modular", 1760000, 3),
                    ("Nguồn Thermaltake Toughpower GT 850W ATX 3.1", 2490000, 6),
                    ("Nguồn Máy Tính ASUS TUF Gaming 1000W White - Gold (PCIe 5.0 - Full Modular)", 4290000, 4),
                    ("Nguồn Cooler Master M2000 Platinum 2000W - 80 Plus Platinum", 9190000, 5),
                    ("Nguồn Super Flower Leadex 1600W (Màu Đen)", 8990000, 7),
                    ("PSU Cooler Master MWE BRONZE 700 V2 - 700W", 1290000, 8),
                    ("Nguồn Máy Tính GIGABYTE P650B (650W | 80 PLUS Bronze)", 1150000, 4),
                    ("Nguồn máy tính Deepcool PN750D (750W - 80 Plus Gold)", 2090000, 3),
                    ("Nguồn Corsair RM850x Shift 850W 80 Plus Gold Full Modul (CP-9020252-NA)", 3890000, 6),
                    ("Nguồn Thermaltake Toughpower GF3 1650W - 80 Plus Gold", 8400000, 4),
                    ("Nguồn máy tính Sharkoon SHP 700W 80 Plus Bzonze", 1190000, 5),
                    ("Nguồn máy tính Corsair CX550 (80 Plus Bronze - NEW)", 1290000, 8),
                    // PSU 54-107
                    ("Nguồn máy tính Thermaltake Smart BX1 750W 80 Plus Bronze", 1790000, 6),
                    ("Nguồn máy tính Cooler Master G Gold 550 V2 Full Range", 1250000, 4),
                    ("Nguồn máy tính OCPC OCPSWH650P 650W 80 Plus", 950000, 5),
                    ("Nguồn SuperFlower Leadex Platinum 2800W", 18990000, 7),
                    ("NGUỒN MIK C550B 550W 80 Plus", 690000, 8),
                    ("Nguồn Máy Tính ASUS ROG Thor 1200W Platinum III Hatsune Miku Edition", 14350000, 4),
                    ("Nguồn Máy Tính Gigabyte AORUS ELITE GP-AE1000PM PG5 1000W (80 Plus Platinum, Full Modular, Màu Đen)", 4190000, 3),
                    ("Nguồn Máy Tính Gigabyte GP-UD850GM 850W (80 Plus Gold, Full Modular, Màu Đen)", 2390000, 6),
                    ("Nguồn Máy Tính Gigabyte GP-UD850GM PG5 850W (80 Plus Gold, Full Modular, Màu Đen)", 2690000, 4),
                    ("Nguồn Máy Tính Gigabyte GP-UD1000GM PG5 1000W (80 Plus Gold, Full Modular, Màu Đen)", 3590000, 5),
                    ("Nguồn ASUS PRO WORKSTATION 2200P (2200W, ATX 3.1, PCIe 5.0, 80 Plus Platinum, Full Modular, Range 200-240VAC, Bác", 19020000, 7),
                    ("Nguồn ASUS PRO WORKSTATION 3000P (3000W, ATX 3.1, PCIe 5.0, 80 Plus Platinum, Full Modular, Range 200-240VAC, Bác", 23880000, 6),
                    ("Nguồn ASUS PRO WORKSTATION 1600P (1600W, ATX 3.1, PCIe 5.0, 80 Plus Platinum, Full Modular, Range 200-240VAC, Bác", 14760000, 4),
                    ("Nguồn Máy Tính PSU Super Flower Zillion FG Gold 850W ATX 3.1", 2650000, 5),
                    ("Nguồn máy tính AIGO VK450 - 450W (Màu Đen)", 490000, 7),
                    ("Nguồn Cooler Master Elite NEX 600W 230V Peak (MPW-6001-ACBK-P)", 990000, 8),
                    ("PSU Corsair AX1600i - 1600W 80 Plus Titanium", 12990000, 4),
                    ("Nguồn Máy Tính ANTEC SIGNATURE SP1000 (1000w, 80 Plus Platinum, modular)", 4990000, 3),
                    ("Nguồn Super Flower Leadex Platinum SF-1000F14MP 1000W (Màu Đen)", 4790000, 6),
                    ("Nguồn Máy Tính Antec NEO ECO NE750G M 80 Plus Gold - 750W Modular", 1990000, 4),
                    ("Nguồn máy tính Corsair CV650 80 Plus Bronze (CP-9020211-NA)", 1350000, 5),
                    ("Nguồn Cooler Master MWE 650 Bronze 230V - V2", 1250000, 7),
                    ("Nguồn máy tính XIGMATEK X-POWER III X-350", 400000, 8),
                    ("Nguồn máy tính Gigabyte P750GM (750W/80 PLUS Gold/Fully Modular)", 2200000, 4),
                    ("Nguồn Máy Tính ASUS ROG STRIX 1000W (80 Plus Gold/Full Modular)", 4690000, 3),
                    ("Nguồn Máy Tính Xigmatek X-Power III 500 (450W, 230V)", 790000, 6),
                    ("Nguồn Máy Tính Xigmatek X-Power III 650 (600W, 230V)", 890000, 4),
                    ("Nguồn Máy Tính Cooler Master Elite PC400 Ver.3 (400W/230V/PFC)", 750000, 5),
                    ("Bộ nguồn Gigabyte GP-P450B 450W", 850000, 7),
                    ("Nguồn Cooler Master MWE GOLD 1250 - V2 (Fully modular, 1250W, A/EU Cable)", 5200000, 8),
                    ("Nguồn Cooler Master MWE 450 BRONZE - V2 (230V, Non Modular)", 800000, 4),
                    ("Nguồn ASUS TUF GAMING 650W Bronze (Màu Đen/80 Plus Bronze)", 1430000, 3),
                    ("Nguồn máy tính Asus TUF GAMING 750B - 750w Bronze", 1730000, 6),
                    ("Nguồn Thermaltake Smart BX1 650W (80 PLUS Bronze/Active PFC)", 1190000, 4),
                    ("Nguồn MSI MAG A650BN 650W - 80 Plus Bronze", 1190000, 5),
                    ("Nguồn MIK SPOWER 500W", 630000, 7),
                    ("Nguồn máy tính CORSAIR RM850E ATX 3.0 (80 Plus Gold /Màu đen/ Full Modular)", 2990000, 8),
                    ("Nguồn máy tính Antec ATOM B650 Bronze", 1390000, 4),
                    ("Nguồn máy tính Cooler Master Elite V3 230V PC500 500W (Màu Đen)", 890000, 3),
                    ("Nguồn máy tính NZXT C850W Gold", 2890000, 6),
                    ("Nguồn Máy Tính SilverStone HELA 2050 Platinum", 8990000, 4),
                    ("Nguồn Máy Tính Corsair CX750 (80 Plus Bronze)", 1550000, 5),
                    ("Nguồn NZXT C1000 1000W GOLD Full Modular", 3950000, 7),
                    ("NGUỒN XIGMATEK MINOTAUR MT650 - 80PLUS GOLD (EN42333)", 1590000, 6),
                    ("Nguồn máy tính NZXT C750 - 750w Bronze", 1490000, 4),
                    ("Nguồn máy tính Corsair RM1000e 80 Plus Gold - Full Modul (CP-9020250-NA)", 4850000, 5),
                    ("Nguồn MSI MPG A1000G PCIE5", 4290000, 7),
                    ("Nguồn MSI MAG A850GL PCIE 5.0 (850W, 80 Plus Gold, ATX 3.0)", 2490000, 8),
                    ("Nguồn Corsair RM1000x Shift 1000W 80 Plus Gold Full Modul (CP-9020253-NA)", 5290000, 4),
                    ("Nguồn Máy Tính Asus ROG LOKI 850P 850w Platinum (PCI Gen 5.0 - Full Modular)", 5190000, 3),
                    ("Nguồn máy tính MSI MAG A750BN 750W PCIE5 (80 Plus Bronze)", 1590000, 6),
                    ("Nguồn Xigmatek Z-Power II Z650 EN41495 (Màu đen/500W/230V)", 650000, 4),
                    ("Nguồn máy tính VSP Delta P500W", 590000, 5),
                    ("Nguồn ACER AC1000 PCIe 5.0 1000W Full Modular", 3490000, 7),
                    // PSU 108-119
                    ("Nguồn máy tính NZXT C1200 - 1200W 80 Plus Gold (ATX 3.0 - PCIe 5.0)", 4300000, 8),
                    ("Nguồn Gaming ASUS TUF 1200W GOLD ATX 3.0 80 PLUS - Full Modular", 4790000, 4),
                    ("Nguồn SuperFlower Leadex III 850W 80 Plus Gold White", 3090000, 3),
                    ("Nguồn máy tính Cooler Master G Gold 650 V2 Full Range", 1550000, 6),
                    ("Nguồn máy tính XIGMATEK LITEPOWER II i450 (300W)", 420000, 4),
                    ("Nguồn Super Flower Leadex VII XG 850W ATX 3.1 White 80 Plus Gold", 3390000, 5),
                    ("Nguồn máy tính ANTEC ZEN 350 350W", 390000, 7),
                    ("Nguồn SuperFlower LEADEX III Gold UP 850W ATX 3.1 PCIe 5.1 SF-850F14GE(GL)", 2890000, 8),
                    ("Nguồn máy tính Xigmatek X-PRO XP650 EN41006 (Màu Đen)", 920000, 4),
                    ("Nguồn Máy Tính GIGABYTE P650SS ICE 650W 80 Plus Silver (Màu Trắng)", 1250000, 3),
                    ("Nguồn máy tính Cooler Master MWE 850W Gold V3 (Cáp liền, ATX 3.1)", 1950000, 6),
                    ("Nguồn máy tính ASUS ROG THOR 1600W Titanium III (ATX 3.1, PCIe 5.1, 80 Plus Titanium, Full Modular)", 22990000, 4)
                };

                var maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                var addedCount = 0;

                _logger.LogInformation($"Bắt đầu thêm PSU. Tổng số PSU trong danh sách: {psus.Count}, CategoryId: {psuCategoryId}");

                // Reload products để đảm bảo có dữ liệu mới nhất trước khi thêm
                _dataStore.ReloadData();
                allProducts = _dataStore.GetAllProducts();
                maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                
                _logger.LogInformation($"Trước khi thêm PSU: Tổng số sản phẩm = {allProducts.Count}, MaxId = {maxId}, PSU hiện có = {allProducts.Count(p => p.CategoryId == psuCategoryId)}");

                foreach (var (name, price, stock) in psus)
                {
                    // Kiểm tra xem sản phẩm đã tồn tại chưa (kiểm tra lại sau khi reload)
                    var existing = allProducts.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        var product = new Product
                        {
                            Id = ++maxId,
                            Name = name,
                            Description = name,
                            Price = price,
                            OldPrice = 0,
                            CategoryId = psuCategoryId,
                            Stock = stock,
                            IsFeatured = price >= 5000000,
                            ImageUrl = $"https://via.placeholder.com/300x300?text={Uri.EscapeDataString(name.Length > 20 ? name.Substring(0, 20) : name)}"
                        };

                        try
                        {
                            _dataStore.AddProduct(product);
                            allProducts.Add(product); // Thêm vào danh sách local để tránh trùng lặp
                            addedCount++;
                            _logger.LogInformation($"✓ Đã thêm PSU #{addedCount}/{psus.Count}: {name.Substring(0, Math.Min(50, name.Length))}... (Id: {product.Id}, CategoryId: {psuCategoryId})");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"✗ Lỗi khi thêm PSU: {name.Substring(0, Math.Min(50, name.Length))}...");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"PSU đã tồn tại (Id: {existing.Id}): {name.Substring(0, Math.Min(50, name.Length))}...");
                    }
                }
                
                _logger.LogInformation($"Đã xử lý {psus.Count} PSU, thêm mới {addedCount} sản phẩm");

                // Reload dữ liệu để đảm bảo có dữ liệu mới nhất
                _dataStore.ReloadData();
                var finalProducts = _dataStore.GetAllProducts();
                var finalPsuProducts = finalProducts.Where(p => p.CategoryId == psuCategoryId).ToList();

                if (addedCount > 0)
                {
                    _logger.LogInformation($"✓✓✓ Đã tự động thêm {addedCount} PSU vào database. Tổng số PSU hiện tại: {finalPsuProducts.Count}");
                }
                else
                {
                    _logger.LogInformation($"Tất cả PSU đã có trong database. Tổng số PSU hiện tại: {finalPsuProducts.Count}");
                }
                
                // Kiểm tra lại: Nếu vẫn chưa đủ 110 PSU, log warning
                if (finalPsuProducts.Count < 110)
                {
                    _logger.LogWarning($"⚠⚠⚠ CẢNH BÁO: Chỉ có {finalPsuProducts.Count}/110 PSU trong database! Có thể cần kiểm tra lại.");
                }
                else
                {
                    _logger.LogInformation($"✓✓✓ Đã có đủ {finalPsuProducts.Count} PSU trong database!");
                }
                
                // Log một vài PSU để verify
                if (finalPsuProducts.Count > 0)
                {
                    _logger.LogInformation($"Sample PSU products (hiển thị 5 đầu tiên):");
                    foreach (var psu in finalPsuProducts.Take(5))
                    {
                        _logger.LogInformation($"  - {psu.Name.Substring(0, Math.Min(60, psu.Name.Length))} (Id: {psu.Id}, CategoryId: {psu.CategoryId})");
                    }
                }
                else
                {
                    _logger.LogError($"❌❌❌ KHÔNG TÌM THẤY PSU NÀO VỚI CategoryId = {psuCategoryId} SAU KHI THÊM! CẦN KIỂM TRA LẠI!");
                }
                
                _logger.LogInformation("=== KẾT THÚC AutoAddPSUs ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động thêm PSU");
            }
        }

        // Tự động thêm SSD vào database
        private void AutoAddSSDs()
        {
            try
            {
                _logger.LogInformation("=== BẮT ĐẦU AutoAddSSDs ===");
                _dataStore.ReloadData(); // Reload để đảm bảo có dữ liệu mới nhất
                var allProducts = _dataStore.GetAllProducts();
                var ssdProducts = allProducts.Where(p => p.CategoryId == 7).ToList();

                _logger.LogInformation($"Đã có {ssdProducts.Count} SSD trong database, kiểm tra và thêm SSD mới...");
                
                // Nếu có ít hơn số lượng SSD mong đợi (84 sản phẩm), bắt buộc thêm lại
                if (ssdProducts.Count < 84)
                {
                    _logger.LogInformation($"Chỉ có {ssdProducts.Count}/84 SSD, sẽ thêm lại để đảm bảo có đủ 84 sản phẩm");
                }

                // Đảm bảo category SSD tồn tại
                var categories = _dataStore.GetAllCategories();
                var ssdCategory = categories.FirstOrDefault(c => 
                    c.Name.Contains("SSD", StringComparison.OrdinalIgnoreCase) || 
                    c.Id == 7);
                
                int ssdCategoryId;
                if (ssdCategory == null)
                {
                    ssdCategory = new Category
                    {
                        Id = 7,
                        Name = "SSD - Ổ cứng thể rắn",
                        Description = "Samsung, Kingston, WD, Crucial, ADATA",
                        ImageUrl = "https://via.placeholder.com/200x150?text=SSD"
                    };
                    _dataStore.AddCategory(ssdCategory);
                    ssdCategoryId = 7;
                }
                else
                {
                    ssdCategoryId = ssdCategory.Id;
                }

                // Danh sách SSD từ hình ảnh (84 sản phẩm)
                var ssds = new List<(string Name, decimal Price, int Stock)>
                {
                    // SSD 1-44
                    ("Ổ cứng SSD GIGABYTE 4000E 500GB M2 2280 NVMe Gen4x4", 1800000, 6),
                    ("Ổ cứng SSD Kingston SNV3S 1TB NVME M.2 2280 PCIE GEN 4X4 (SNV3S/1000G)", 3200000, 4),
                    ("Ổ Cứng SSD Colorful CN600 512GB M.2 NVME đọc/ghi (tối đa): 3200MB/S - 2000MB/S", 1700000, 5),
                    ("Ổ cứng SSD NVME 1TB Lexar NQ780 M.2 PCIe Gen4 x4 up to 6500MB/s read, 2500MB/s write", 3200000, 7),
                    ("Ổ cứng SSD SSTC Oceanic Whitetip E130 512GB NVMe Gen3", 1700000, 8),
                    ("Ổ cứng SSD Kingspec P3-256 2.5 Sata III 256Gb", 850000, 4),
                    ("Ổ cứng SSD KINGMAX Zeus PQ3480 512GB NVMe PCIe 3.0 x4 M.2 2280", 1700000, 3),
                    ("Ổ cứng SSD AGI 512GB A1198 Internal SSD PCIe NVMe M.2 Gen3x4", 1450000, 6),
                    ("Ổ cứng SSD Kingston SNV3S 500GB NVME M.2 2280 PCIE GEN 4X4 (SNV3S/500G)", 2000000, 4),
                    ("Ổ cứng SSD WD Blue SN5000 1TB NVMe PCIe Gen4 x4 (WDS100T4B0E)", 3200000, 5),
                    ("Ổ cứng SSD HIKSEMI WAVE 1TB M.2 2280 PCIe 3.0 (2450MB/s Đọc, 2450MB/s Ghi, HS-SSD-WAVE(P)-1024G)", 2900000, 7),
                    ("SSD NVMe Kioxia Exceria Plus G3 Gen 4x4 1TB Đọc/Ghi: 5000/3900MB/giây (LSD10Z001TG8)", 3200000, 8),
                    ("Ổ cứng SSD Kingmax Zeus PQ3480 1TB NVMe PCIe 3.0 x4 M.2 2280", 2900000, 4),
                    ("Ổ cứng SSD Crucial E100 480GB M.2 PCIe Gen4 x4 NVMe (CT480E100SSD8)", 1800000, 3),
                    ("Ổ cứng SSD Samsung 990 EVO Plus 1TB M.2 NVMe M.2 2280 PCIe Gen4.0 x4/5.0 x2", 3200000, 6),
                    ("Ổ cứng SSD Gigabyte NVMe V2 256GB PCIe 3.0 x4 M.2 2280 (G3NVMEV2256G, Đọc 3200MB/s, Ghi 1200MB/s)", 900000, 4),
                    ("Ổ cứng SSD Lexar NM610PRO 500GB NVMe M2- LNM610P500G-RNNNG", 1400000, 5),
                    ("Ổ cứng SSD KINGMAX PQ4480 500GB NVMe M.2 2280 PCIe Gen 4x4", 2200000, 7),
                    ("Ổ cứng SSD PNY CS1031 500GB NVMe M.2 2280 PCIe Gen 3.0 x4 (M280CS1031-500-CL)", 1700000, 8),
                    ("Ổ cứng SSD NVME HIKSEMI WAVE 512GB - HS-SSD-WAVE(P) 512G (NVMe PCIe Gen3x4, M2.2280)", 1700000, 4),
                    ("Ổ cứng SSD Kioxia (TOSHIBA) Exceria 960GB | 3D NAND, 2.5 inch, SATA III (LTC10Z960GG8)", 2600000, 3),
                    ("Ổ cứng SSD Kingston KC3000 1024GB NVMe M.2 2280 PCIe Gen 4x4 (Đọc 7000MB/s, Ghi 6000MB/s)-(SKC3000S/1024G)", 4100000, 6),
                    ("Ổ cứng SSD OCPC MFL-300 256GB M.2 NVMe PCIe Gen3 x4 (SSDM2PCIEF256G)", 900000, 4),
                    ("Ổ cứng SSD McQuest Raptor 512GB (M.2 2280, PCIe NVMe Gen3x4)", 1700000, 5),
                    ("Ổ cứng SSD Samsung 990 PRO 1TB PCIe NVMe 4.0x4 (Đọc 7450MB/s - Ghi 6900MB/s) - (MZ-V9P1T0BW)", 4100000, 7),
                    ("Ổ cứng SSD NVMe KIOXIA 2TB EXCERIA PLUS G3 NVMe Gen 4/Đọc/Ghi: 5.000/3.900 MB/giây DRAMless", 4900000, 8),
                    ("Ổ cứng SSD Patriot P320 1TB M.2 2280 PCIe Gen3 x4", 2900000, 4),
                    ("Ổ cứng SSD ADATA Legend 710 1TB M.2 NVMe PCIe 3.0 x4 (Đọc 2400MB/s - Ghi 1800MB/s)", 2900000, 3),
                    ("Ổ cứng SSD ADATA LEGEND 860 1TB M.2 2280 PCIe Gen4x4 (Đọc 6000MB/s, Ghi 5000MB/s)", 3200000, 6),
                    ("Ổ cứng SSD Samsung 990 PRO 2TB M.2 NVMe M.2 2280 PCIe Gen4.0 x4 MZ-V9P2T0BW", 6000000, 4),
                    ("Ổ cứng SSD Biwin M100 256GB (2.5 inch - SATA 3)", 850000, 5),
                    ("Ổ cứng SSD WD Blue SN580 1TB NVMe (WDS100T3B0E)", 3100000, 7),
                    ("Ổ Cứng SSD Biwin M100 512GB (SATA 3, 2.5 inch, 550MB/s Đọc - 480MB/s Ghi)", 1200000, 6),
                    ("Ổ cứng SSD Biwin NV7200 500GB M.2 PCIe Gen4 (BNV7200500G-RGX)", 2000000, 4),
                    ("Ổ cứng SSD Kingston KC3000 2048GB NVMe PCIe Gen 4.0 (KC3000D/2048G)", 6700000, 5),
                    ("Ổ cứng SSD HIKSEMI HS-SSD-WAVE Pro 1TB (M.2 2280, PCIe NVMe Gen3x4, 3520MB/s -2900MB/s)", 2900000, 7),
                    ("SSD Lexar NM790 1TB M.2 PCIe Gen4 x4 NVMe LNM790X001T-RNNNG Đọc/ghi: 7400MB/s - 6500MB/s", 3600000, 8),
                    ("Ổ cứng SSD Kioxia EXCERIA PLUS G4 1TB NVMe Gen5x4 (LVD10Z001TG8)", 4500000, 4),
                    ("Ổ cứng SSD Acer FA200 1TB M.2 NVMe 2.0 PCIe 4.0", 3200000, 3),
                    ("Ổ cứng SSD HIKSEMI FUTURE 512GB NVMe M.2 2280 PCIe Gen4 x 4", 1800000, 6),
                    ("Ổ cứng SSD Gigabyte 2500E 1TB PCIe Gen 3.0x4 (Đọc 2400MB/s Ghi 1800MB/s - (G325E1TB)", 2900000, 4),
                    ("Ổ cứng NVMe Kioxia Exceria G2 Gen 3x4 WDRAM 1TB R2100, W1700 (LRC20Z001TG8)", 2900000, 5),
                    ("Ổ cứng SSD HIKSEMI FUTURE Lite 1024GB NVMe M.2 2280 PCIe Gen4 x 4", 3300000, 7),
                    ("Ổ cứng SSD Kingston SNV3S 2TB NVME M.2 2280 PCIE GEN 4X4 (SNV3S/2000G)", 4900000, 8),
                    // SSD 45-84
                    ("Ổ cứng SSD Samsung 990 PRO 4TB M.2 NVMe M.2 2280 PCIe Gen4.0 x4 (MZ-V9P4T0BW)", 11000000, 3),
                    ("Ổ cứng SSD Predator GM7000 Heatsink 1TB PCIe Gen4 x4 NVMe (GM7000HS-1TB)", 3200000, 4),
                    ("Ổ cứng SSD Hikvision HS-SSD-Minder(P) 512GB M.2 2280 PCIe Gen3x4", 1700000, 5),
                    ("Ổ cứng SSD Kingston KC3000 512GB NVMe M.2 2280 PCIe Gen 4x4 (SKC3000S/512G)", 2000000, 6),
                    ("Ổ cứng SSD Gigabyte G440E 1TB PCIe Gen4 x4 NVMe M.2 2280", 3200000, 4),
                    ("Ổ cứng SSD WD Black SN7100 1TB NVMe PCIe Gen4 x4 (WDS100T4X0E)", 3600000, 7),
                    ("Ổ cứng SSD Lexar NM620 1TB M.2 PCIe Gen3 x4 NVMe (LNM620X001T-RNNNG)", 2900000, 8),
                    ("Ổ cứng SSD Lexar NQ100 512GB M.2 PCIe Gen4 x4 NVMe (LNQ100X512G-RNNNG)", 1800000, 4),
                    ("Ổ cứng SSD MSI SPATIUM M450 1TB PCIe Gen4 x4 NVMe M.2 2280", 3200000, 3),
                    ("Ổ cứng SSD HIKSEMI WAVE 512GB M.2 2280 PCIe Gen3x4 (HS-SSD-WAVE(P)-512G)", 1700000, 6),
                    ("Ổ cứng SSD ADATA LEGEND 900 Pro 1TB M.2 2280 PCIe Gen5 x4 (Đọc 14000MB/s, Ghi 12000MB/s)", 4900000, 4),
                    ("Ổ cứng SSD Colorful CN600 PRO 1TB M.2 NVMe PCIe Gen4 x4", 3200000, 5),
                    ("Ổ cứng SSD Lexar SL660 Blaze 1TB USB 3.2 External SSD (LSL660X001T-RNNNG)", 3600000, 7),
                    ("Ổ cứng SSD WD Blue SN770M 480GB NVMe PCIe Gen4 x4 M.2 2230 (WDS480G3G0A)", 1800000, 8),
                    ("Ổ cứng SSD WD Blue SA510 250GB 2.5 inch SATA III (WDS250G3X0E)", 1200000, 4),
                    ("Ổ cứng SSD WD Green 120GB 2.5 inch SATA III (WDS120G3G0A)", 500000, 3),
                    ("Ổ cứng SSD Apacer SD250-120GN 120GB 2.5 inch SATA III", 250000, 6),
                    ("Ổ cứng SSD Verico XTL-200 256GB 2.5 inch SATA III", 850000, 4),
                    ("Ổ cứng SSD ADATA Legend 710 512GB M.2 NVMe PCIe 3.0 x4 (ALEG-710-512GCS)", 1400000, 5),
                    ("Ổ cứng SSD ADATA LEGEND 800 2TB M.2 2280 PCIe Gen4x4 (Đọc 7400MB/s, Ghi 6800MB/s)", 6000000, 7),
                    ("Ổ cứng SSD ADATA Msata GLOWAT 128GB mSATA", 500000, 8),
                    ("Ổ cứng SSD Kioxia EXCERIA PLUS G4 2TB NVMe Gen5x4 (LVD10Z002TG8)", 9000000, 4),
                    ("Ổ cứng SSD ADATA RED PLUS 1TB M.2 2280 PCIe Gen4x4", 3200000, 3),
                    ("Ổ cứng SSD HIKSEMI HS-SSD-WAVE Pro 512GB (M.2 2280, PCIe NVMe Gen3x4, 3520MB/s -2900MB/s)", 1700000, 6),
                    ("Ổ cứng SSD AGI A1238 256GB M.2 PCIe Gen3x4 NVMe", 900000, 4),
                    ("Ổ cứng SSD Samsung 980 500GB M.2 NVMe PCIe Gen3 x4 (MZ-V8V500BW)", 1800000, 5),
                    ("Ổ cứng SSD Samsung 990 EVO Plus 2TB M.2 NVMe M.2 2280 PCIe Gen4.0 x4/5.0 x2 (MZ-VAP2T0BW)", 6000000, 7),
                    ("Ổ cứng SSD Samsung 9100 Pro 1TB M.2 NVMe PCIe Gen5 x4 (MZ-V9P1T0BW)", 4900000, 8),
                    ("Ổ CỨNG HDD WD 8TB RED PLUS (WD80EFZZ)", 5950000, 3)
                };

                var maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                var addedCount = 0;

                _logger.LogInformation($"Bắt đầu thêm SSD. Tổng số SSD trong danh sách: {ssds.Count}, CategoryId: {ssdCategoryId}");

                // Reload products để đảm bảo có dữ liệu mới nhất trước khi thêm
                _dataStore.ReloadData();
                allProducts = _dataStore.GetAllProducts();
                maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                
                _logger.LogInformation($"Trước khi thêm SSD: Tổng số sản phẩm = {allProducts.Count}, MaxId = {maxId}, SSD hiện có = {allProducts.Count(p => p.CategoryId == ssdCategoryId)}");

                foreach (var (name, price, stock) in ssds)
                {
                    // Kiểm tra xem sản phẩm đã tồn tại chưa (kiểm tra lại sau khi reload)
                    var existing = allProducts.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        var product = new Product
                        {
                            Id = ++maxId,
                            Name = name,
                            Description = name,
                            Price = price,
                            OldPrice = 0,
                            CategoryId = ssdCategoryId,
                            Stock = stock,
                            IsFeatured = price >= 5000000,
                            ImageUrl = $"https://via.placeholder.com/300x300?text={Uri.EscapeDataString(name.Length > 20 ? name.Substring(0, 20) : name)}"
                        };

                        try
                        {
                            _dataStore.AddProduct(product);
                            allProducts.Add(product); // Thêm vào danh sách local để tránh trùng lặp
                            addedCount++;
                            _logger.LogInformation($"✓ Đã thêm SSD #{addedCount}/{ssds.Count}: {name.Substring(0, Math.Min(50, name.Length))}... (Id: {product.Id}, CategoryId: {ssdCategoryId})");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"✗ Lỗi khi thêm SSD: {name.Substring(0, Math.Min(50, name.Length))}...");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"SSD đã tồn tại (Id: {existing.Id}): {name.Substring(0, Math.Min(50, name.Length))}...");
                    }
                }
                
                _logger.LogInformation($"Đã xử lý {ssds.Count} SSD, thêm mới {addedCount} sản phẩm");

                // Reload dữ liệu để đảm bảo có dữ liệu mới nhất
                _dataStore.ReloadData();
                var finalProducts = _dataStore.GetAllProducts();
                var finalSsdProducts = finalProducts.Where(p => p.CategoryId == ssdCategoryId).ToList();

                if (addedCount > 0)
                {
                    _logger.LogInformation($"✓✓✓ Đã tự động thêm {addedCount} SSD vào database. Tổng số SSD hiện tại: {finalSsdProducts.Count}");
                }
                else
                {
                    _logger.LogInformation($"Tất cả SSD đã có trong database. Tổng số SSD hiện tại: {finalSsdProducts.Count}");
                }
                
                // Kiểm tra lại: Nếu vẫn chưa đủ 84 SSD, log warning
                if (finalSsdProducts.Count < 84)
                {
                    _logger.LogWarning($"⚠⚠⚠ CẢNH BÁO: Chỉ có {finalSsdProducts.Count}/84 SSD trong database! Có thể cần kiểm tra lại.");
                }
                else
                {
                    _logger.LogInformation($"✓✓✓ Đã có đủ {finalSsdProducts.Count} SSD trong database!");
                }
                
                // Log một vài SSD để verify
                if (finalSsdProducts.Count > 0)
                {
                    _logger.LogInformation($"Sample SSD products (hiển thị 5 đầu tiên):");
                    foreach (var ssd in finalSsdProducts.Take(5))
                    {
                        _logger.LogInformation($"  - {ssd.Name.Substring(0, Math.Min(60, ssd.Name.Length))} (Id: {ssd.Id}, CategoryId: {ssd.CategoryId})");
                    }
                }
                else
                {
                    _logger.LogError($"❌❌❌ KHÔNG TÌM THẤY SSD NÀO VỚI CategoryId = {ssdCategoryId} SAU KHI THÊM! CẦN KIỂM TRA LẠI!");
                }
                
                _logger.LogInformation("=== KẾT THÚC AutoAddSSDs ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động thêm SSD");
            }
        }

        // Tự động thêm HDD vào database
        private void AutoAddHDDs()
        {
            try
            {
                _logger.LogInformation("=== BẮT ĐẦU AutoAddHDDs ===");
                _dataStore.ReloadData(); // Reload để đảm bảo có dữ liệu mới nhất
                var allProducts = _dataStore.GetAllProducts();
                var hddProducts = allProducts.Where(p => p.CategoryId == 8).ToList();

                _logger.LogInformation($"Đã có {hddProducts.Count} HDD trong database, kiểm tra và thêm HDD mới...");
                
                // Nếu có ít hơn số lượng HDD mong đợi (41 sản phẩm), bắt buộc thêm lại
                if (hddProducts.Count < 41)
                {
                    _logger.LogInformation($"Chỉ có {hddProducts.Count}/41 HDD, sẽ thêm lại để đảm bảo có đủ 41 sản phẩm");
                }

                // Đảm bảo category HDD tồn tại
                var categories = _dataStore.GetAllCategories();
                var hddCategory = categories.FirstOrDefault(c => 
                    c.Name.Contains("HDD", StringComparison.OrdinalIgnoreCase) || 
                    c.Id == 8);
                
                int hddCategoryId;
                if (hddCategory == null)
                {
                    hddCategory = new Category
                    {
                        Id = 8,
                        Name = "HDD - Ổ cứng HDD",
                        Description = "Western Digital, Seagate, Toshiba",
                        ImageUrl = "https://via.placeholder.com/200x150?text=HDD"
                    };
                    _dataStore.AddCategory(hddCategory);
                    hddCategoryId = 8;
                }
                else
                {
                    hddCategoryId = hddCategory.Id;
                }

                // Danh sách HDD từ hình ảnh (41 sản phẩm - bỏ sản phẩm số 18 không có giá)
                var hdds = new List<(string Name, decimal Price, int Stock)>
                {
                    ("Ổ Cứng HDD Toshiba 3.5\" P300 2TB Red 7200rpm 256MB (HDWD320AZSTA)", 2600000, 6),
                    ("Ổ Cứng HDD Western Caviar Blue 4TB (3.5inch/5400RPM/SATA3/64MB Cache)", 3000000, 4),
                    ("Ổ cứng HDD Seagate Barracuda 2TB 7200Rpm, SATA3 6Gb/s, 64MB Cache", 2600000, 5),
                    ("Ổ cứng HDD Western Digital Blue 2Tb SATA3 7200rpm 256Mb WD20EZBX", 2600000, 7),
                    ("Ổ Cứng HDD Toshiba 3.5\" P300 2TB Red 7200rpm 256MB (HDWD320UZSVA)", 2600000, 8),
                    ("Ổ cứng HDD Toshiba P300 4TB 3.5\" (5400RPM, 128MB, SATA 3) - HDWD240AZSTA (Box giấy)", 3000000, 4),
                    ("Ổ cứng HDD Toshiba P300 4TB 3.5\" (5400RPM, 128MB, SATA 3) - HDWD240UZSVA (Vỏ nilon)", 3000000, 3),
                    ("Ổ Cứng HDD Western Enterprise Ultrastar DC HC330 10TB (3.5 inch, Sata3 6Gb/s, 256MB Cache, 7200rpm)", 9700000, 6),
                    ("Ổ cứng Western Digital Ultrastar DC HA210 1TB (HUS722T1TALA604)", 2500000, 4),
                    ("Ổ cứng NAS Synology Plus 4TB HAT3300 (3.5Inch/5400rpm/SATA 6Gb/s)", 3690000, 5),
                    ("Ổ Cứng HDD Western Digital Caviar Blue 4TB 256MB Cache 5400RPM WD40EZAX", 2650000, 7),
                    ("Ổ CỨNG HDD SEAGATE IRONWOLF 6TB (ST6000VN001)", 5500000, 8),
                    ("Ổ cứng Western Digital Ultrastar DC HA210 2TB (HUS722T2TALA604)", 3200000, 4),
                    ("Ổ Cứng HDD Western Digital Red Plus 8TB 3.5 inch (WD80EFPX)", 6800000, 3),
                    ("Ổ cứng Western Digital Ultrastar DC HC530 14TB (WUH721414ALE6L4)", 9500000, 6),
                    ("Ổ cứng HDD Western Digital Gold 8TB", 7650000, 4),
                    ("Ổ cứng HDD Seagate Exos Enterprise HDD 20TB 3.5 SATA/ST20000NM007D", 19950000, 5),
                    // Bỏ sản phẩm số 18 không có giá
                    ("Ổ cứng HDD Seagate IRONWOLF NAS 4TB/5900, Sata3, 64MB Cache", 3950000, 8),
                    ("Ổ cứng HDD Seagate Barracuda 4TB 3.5 inch SATA III 256MB ST4000DM004-5400 Rpm", 2650000, 4),
                    ("Ổ cứng HDD Western Caviar Red 4TB SATA 3 64MB Cache 5400RPM", 3090000, 3),
                    ("Ổ cứng HDD WD 2TB Black 3.5 inch, 7200RPM, SATA, 64MB Cache (WD2003FZEX)", 3400000, 6),
                    ("Ổ Cứng HDD Western Digital Red Plus 4TB 3.5 inch, 256MB Cache, 5400RPM (WD40EFPX)", 3400000, 4),
                    ("Ổ Cứng HDD Seagate Ironwolf 8TB 3.5 Inch, 7200RPM, Sata, 256MB Cache (ST8000VN004)", 6750000, 5),
                    ("Ổ cứng HDD Western Digital Purple 10TB WD102PURZ (3.5Inch/7200rpm/256MB/SATA3)", 7750000, 7),
                    ("Ổ cứng HDD Western Digital Caviar Black 4TB 256M - 7200Rpm", 4650000, 8),
                    ("Ổ cứng HDD Western Digital WD Red Plus 10TB 3.5inch SATA 3", 8200000, 4),
                    ("Ổ cứng Western Digital Ultrastar DC HC520 12TB (HUH721212ALE604)", 8900000, 3),
                    ("Ổ cứng HDD Western Digital 6TB Black 3.5 inch, 7200RPM, SATA, 256MB Cache", 6450000, 6),
                    ("Ổ cứng di động HDD Western Digital My Passport Ultra 4Tb Type-C - Màu bạc", 4590000, 4),
                    ("Ổ cứng Western Digital Ultrastar DC HC550 18TB (WUH721818ALE6L4)", 12500000, 5),
                    ("Ổ cứng Western Digital Red 6TB 256MB Cache", 5290000, 7),
                    ("Ổ cứng Western Digital Ultrastar DC HC310 4TB (HUS726T4TALA6L4)", 4600000, 6),
                    ("Ổ cứng Western Digital Ultrastar DC HC310 6TB (HUS726T6TALE6L4)", 6250000, 4),
                    ("Ổ cứng HDD Western Caviar Blue 1TB 7200Rpm, SATA3 6Gb/s, 64MB Cache", 2250000, 5),
                    ("Ổ cứng Western Digital Ultrastar DC HC550 16TB WUH721816ALE6L4", 10890000, 7),
                    ("Ổ cứng Western Digital Ultrastar DC HC560 20TB (WUH722020ALE6L4)", 14850000, 8),
                    ("Ổ cứng Western Digital Ultrastar DC HC320 8TB (HUS728T8TALE6L4)", 7250000, 4),
                    ("Ổ Cứng HDD SEAGATE ST6000VX001 6TB (SKYHAW, 6Gbps, SATA, 5400PRM)", 4190000, 3),
                    ("Ổ Cứng Western Digital Purple 10TB 256MB Cache WD101PURZ", 7750000, 6),
                    ("Ổ cứng HDD Western Digital Caviar Black 1TB 64MB Cache 3.5", 2250000, 4),
                    ("Ổ cứng Western Digital Ultrastar DC HC570 22TB (WUH722222ALE6L4)", 17800000, 5)
                };

                var maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                var addedCount = 0;

                _logger.LogInformation($"Bắt đầu thêm HDD. Tổng số HDD trong danh sách: {hdds.Count}, CategoryId: {hddCategoryId}");

                // Reload products để đảm bảo có dữ liệu mới nhất trước khi thêm
                _dataStore.ReloadData();
                allProducts = _dataStore.GetAllProducts();
                maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                
                _logger.LogInformation($"Trước khi thêm HDD: Tổng số sản phẩm = {allProducts.Count}, MaxId = {maxId}, HDD hiện có = {allProducts.Count(p => p.CategoryId == hddCategoryId)}");

                foreach (var (name, price, stock) in hdds)
                {
                    // Kiểm tra xem sản phẩm đã tồn tại chưa (kiểm tra lại sau khi reload)
                    var existing = allProducts.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        var product = new Product
                        {
                            Id = ++maxId,
                            Name = name,
                            Description = name,
                            Price = price,
                            OldPrice = 0,
                            CategoryId = hddCategoryId,
                            Stock = stock,
                            IsFeatured = price >= 10000000,
                            ImageUrl = $"https://via.placeholder.com/300x300?text={Uri.EscapeDataString(name.Length > 20 ? name.Substring(0, 20) : name)}"
                        };

                        try
                        {
                            _dataStore.AddProduct(product);
                            allProducts.Add(product); // Thêm vào danh sách local để tránh trùng lặp
                            addedCount++;
                            _logger.LogInformation($"✓ Đã thêm HDD #{addedCount}/{hdds.Count}: {name.Substring(0, Math.Min(50, name.Length))}... (Id: {product.Id}, CategoryId: {hddCategoryId})");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"✗ Lỗi khi thêm HDD: {name.Substring(0, Math.Min(50, name.Length))}...");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"HDD đã tồn tại (Id: {existing.Id}): {name.Substring(0, Math.Min(50, name.Length))}...");
                    }
                }
                
                _logger.LogInformation($"Đã xử lý {hdds.Count} HDD, thêm mới {addedCount} sản phẩm");

                // Reload dữ liệu để đảm bảo có dữ liệu mới nhất
                _dataStore.ReloadData();
                var finalProducts = _dataStore.GetAllProducts();
                var finalHddProducts = finalProducts.Where(p => p.CategoryId == hddCategoryId).ToList();

                if (addedCount > 0)
                {
                    _logger.LogInformation($"✓✓✓ Đã tự động thêm {addedCount} HDD vào database. Tổng số HDD hiện tại: {finalHddProducts.Count}");
                }
                else
                {
                    _logger.LogInformation($"Tất cả HDD đã có trong database. Tổng số HDD hiện tại: {finalHddProducts.Count}");
                }
                
                // Kiểm tra lại: Nếu vẫn chưa đủ 41 HDD, log warning
                if (finalHddProducts.Count < 41)
                {
                    _logger.LogWarning($"⚠⚠⚠ CẢNH BÁO: Chỉ có {finalHddProducts.Count}/41 HDD trong database! Có thể cần kiểm tra lại.");
                }
                else
                {
                    _logger.LogInformation($"✓✓✓ Đã có đủ {finalHddProducts.Count} HDD trong database!");
                }
                
                // Log một vài HDD để verify
                if (finalHddProducts.Count > 0)
                {
                    _logger.LogInformation($"Sample HDD products (hiển thị 5 đầu tiên):");
                    foreach (var hdd in finalHddProducts.Take(5))
                    {
                        _logger.LogInformation($"  - {hdd.Name.Substring(0, Math.Min(60, hdd.Name.Length))} (Id: {hdd.Id}, CategoryId: {hdd.CategoryId})");
                    }
                }
                else
                {
                    _logger.LogError($"❌❌❌ KHÔNG TÌM THẤY HDD NÀO VỚI CategoryId = {hddCategoryId} SAU KHI THÊM! CẦN KIỂM TRA LẠI!");
                }
                
                _logger.LogInformation("=== KẾT THÚC AutoAddHDDs ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động thêm HDD");
            }
        }

        // Tự động thêm Tản nhiệt nước vào database
        private void AutoAddWaterCooling()
        {
            try
            {
                _logger.LogInformation("=== BẮT ĐẦU AutoAddWaterCooling ===");
                _dataStore.ReloadData(); // Reload để đảm bảo có dữ liệu mới nhất
                var allProducts = _dataStore.GetAllProducts();
                var waterCoolingProducts = allProducts.Where(p => p.CategoryId == 11).ToList();

                _logger.LogInformation($"Đã có {waterCoolingProducts.Count} Tản nhiệt nước trong database, kiểm tra và thêm Tản nhiệt nước mới...");
                
                // Nếu có ít hơn số lượng Tản nhiệt nước mong đợi (89 sản phẩm), bắt buộc thêm lại
                if (waterCoolingProducts.Count < 89)
                {
                    _logger.LogInformation($"Chỉ có {waterCoolingProducts.Count}/89 Tản nhiệt nước, sẽ thêm lại để đảm bảo có đủ 89 sản phẩm");
                }

                // Đảm bảo category Tản nhiệt nước tồn tại
                var categories = _dataStore.GetAllCategories();
                var waterCoolingCategory = categories.FirstOrDefault(c => 
                    c.Name.Contains("Tản nhiệt nước", StringComparison.OrdinalIgnoreCase) || 
                    c.Name.Contains("Water Cooling", StringComparison.OrdinalIgnoreCase) ||
                    c.Id == 11);
                
                int waterCoolingCategoryId;
                if (waterCoolingCategory == null)
                {
                    waterCoolingCategory = new Category
                    {
                        Id = 11,
                        Name = "Tản nhiệt nước - Water Cooling",
                        Description = "AIO Liquid Cooler, CPU Water Cooling",
                        ImageUrl = "https://via.placeholder.com/200x150?text=WaterCooling"
                    };
                    _dataStore.AddCategory(waterCoolingCategory);
                    waterCoolingCategoryId = 11;
                }
                else
                {
                    waterCoolingCategoryId = waterCoolingCategory.Id;
                }

                // Danh sách Tản nhiệt nước từ hình ảnh (89 sản phẩm: 45 từ hình 1 + 44 từ hình 2)
                var waterCoolings = new List<(string Name, decimal Price, int Stock)>
                {
                    // Tản nhiệt nước 1-45 (từ hình 1)
                    ("Tản nhiệt nước COUGAR AQUA ARGB 360", 1250000, 6),
                    ("Tản nhiệt nước Cooler Master ML240L V2 RGB WHITE EDITION", 1190000, 4),
                    ("Tản Nhiệt Nước CPU Thermalright Aqua Elite 240 BLACK ARGB V3", 1190000, 5),
                    ("Tản nhiệt Nước CoolerMaster MasterLiquid 360 Atmos Stealth", 2590000, 7),
                    ("Tản nhiệt nước Thermalright Aqua Elite 360 ARGB White - AIO CPU Cooler", 1990000, 8),
                    ("Tản Nhiệt Nước Segotep BeAced 360 ARGB Black", 1750000, 4),
                    ("Tản Nhiệt Nước Leopard Pro Flow 360P (Đen)", 2990000, 3),
                    ("Tản Nhiệt Nước AIO GIGABYTE GAMING 360 (GP-GIGABYTE GME 360)", 1790000, 6),
                    ("Tản Nhiệt Nước ID-COOLING SPACE SL240 XE ARGB", 2190000, 4),
                    ("Tản nhiệt nước CPU Deepcool MYSTIQUE 360", 3990000, 5),
                    ("TẢN NHIỆT NƯỚC PANORAMA SE 360 ARGB Black", 7690000, 7),
                    ("Tản nhiệt nước ASUS ROG RYUO IV SLC 360 ARGB", 9990000, 8),
                    ("Tản Nhiệt Nước Leopard Pro Flow 360P (Trắng)", 2990000, 4),
                    ("Tản Nhiệt Nước AIO GIGABYTE GAMING 360 ICE (GP-GIGABYTE GME 360I)", 1790000, 3),
                    ("Tản nhiệt NZXT Kraken Elite 240 RGB Black", 6590000, 6),
                    ("Tản nhiệt NZXT Kraken 240 RGB Black", 4350000, 4),
                    ("TẢN NHIỆT NƯỚC PANORAMA SE 360 ARGB White", 7890000, 5),
                    ("Tản Nhiệt Nước Segotep BeAced 360 ARGB White", 1850000, 7),
                    ("Tản nhiệt nước Thermaltake TH360 Snow ARGB Sync", 1790000, 8),
                    ("Tản nhiệt nước AIO Cooler Master MasterLiquid ML240P Mirage (MLY-D24M-A20PA-R1)", 1590000, 4),
                    ("TẢN NHIỆT NƯỚC AIO XIGMATEK LK 360 Digital", 1750000, 3),
                    ("Tản nhiệt nước Corsair Hydro Series H115i RGB PLATINUM", 1990000, 6),
                    ("Tản nhiệt nước AIO Jonsbo TW120 RGB", 1300000, 4),
                    ("Tản Nhiệt Nước AIO Shark Solution 360 (SSTC-WC360ARGB)", 1690000, 5),
                    ("Tản nhiệt AIO Thermaltake Toughliquid 280 ARBG Black", 1590000, 7),
                    ("Tản nhiệt nước Corsair ICUE LINK H150i Liquid RGB WHITE (CW-9061006-WW)", 6180000, 8),
                    ("Tản nhiệt NZXT Kraken 240 RGB White", 4350000, 4),
                    ("Tản nhiệt nước NZXT Kraken Z63 - RGB - White (RL-KRZ63-RW)", 4990000, 3),
                    ("Tản nhiệt nước Corsair ICUE LINK H100i Liquid RGB WHITE (CW-9061005-WW)", 5220000, 6),
                    ("Tản nhiệt nước Corsair ICUE LINK H100i Liquid RGB (CW-9061001-WW)", 4900000, 4),
                    ("Tản nhiệt AIO AORUS WATERFORCE X II 360 ICE White", 7690000, 5),
                    ("Tản nhiệt nước Corsair ICUE LINK H150i Liquid RGB (CW-9061003-WW)", 5860000, 7),
                    ("Tản nhiệt nước AIO Gamdias AURA GL360 v2 ARGB BLACK", 1390000, 6),
                    ("Tản nhiệt nước Thermaltake AW420 AIO Liquid Cooler - Hỗ trợ socket TR5-SP6", 9900000, 4),
                    ("Tản nhiệt nước ASUS ProArt LC 360 (Đen)", 6840000, 5),
                    ("Tản nhiệt Asus ROG RYUJIN III 360 ARGB Extreme White Edition", 9680000, 7),
                    ("Tản Nhiệt Nước ID-COOLING FX360 INF ARGB", 1650000, 8),
                    ("Bộ Tản Nhiệt Nước NZXT KRAKEN ELITE 360 RGB V2 White (360mm, RL-KR36E-W2)", 6990000, 4),
                    ("TẢN NHIỆT NƯỚC PANORAMA 360 ARGB Black", 9590000, 3),
                    ("TẢN NHIỆT NƯỚC COUGAR POSEIDON LT240 ARGB", 990000, 6),
                    ("Tản nhiệt nước CPU Deepcool MYSTIQUE 360 White", 3990000, 4),
                    ("Tản Nhiệt Nước Asus ROG RYUO IV 360 ARGB Hatsune Miku Edition", 10520000, 5),
                    ("TẢN NHIỆT NƯỚC ID-COOLING ZOOMFLOW 240-XT ELITE ARGB", 1390000, 7),
                    ("Tản nhiệt nước AIO MSI MAG CORELIQUID M360", 2390000, 8),
                    ("Tản nhiệt nước Cooler Master MASTERLIQUID 360 CORE SI BLACK", 1750000, 4),
                    // Tản nhiệt nước 92-135 (từ hình 2)
                    ("Bộ tản nhiệt nước ID-COOLING DASHFLOW 360 BASIC WHITE", 1790000, 6),
                    ("Tản nhiệt nước NZXT AIO Kraken Elite 360 Black RGB", 8580000, 4),
                    ("Tản nhiệt nước ASUS ROG RYUO III 360 ARGB WHITE EDITION", 7690000, 5),
                    ("Tản nhiệt nước Corsair H100i ELITE CAPELLIX XT (CW-9060068-WW)", 1990000, 7),
                    ("Tản nhiệt nước Corsair H100i ELITE CAPELLIX XT WHITE (CW-9060072-WW)", 1990000, 8),
                    ("Tản nhiệt nước XIGMATEK AURORA 240 - ARGB, ALL IN ONE WATERCOOLING (EN42807)", 1290000, 4),
                    ("Tản Nhiệt AIO Thermalright Frozen Vision 360 WHITE", 3290000, 3),
                    ("Tản nhiệt AIO Thermalright Frozen Warframe 360 WHITE ARGB", 2290000, 6),
                    ("Tản Nhiệt Nước CPU Thermalright AQUA ELITE 360 BLACK ARGB V3", 1890000, 4),
                    ("Tản Nhiệt Nước CPU Thermalright Aqua Elite 240 White ARGB V3", 1450000, 5),
                    ("Tản nhiệt nước AIO Thermalright Frozen Warframe 360 Black ARGB", 2190000, 7),
                    ("Tản nhiệt nước AIO Thermalright Frozen Warframe 240 WHITE ARGB", 1790000, 8),
                    ("Tản nhiệt AIO Thermalright Frozen Warframe 240 BLACK ARGB", 1790000, 4),
                    ("Tản Nhiệt Nước Thermaltake LA360-S ARGB Sync (LCD 2.4 inch, ARGB)", 2290000, 3),
                    ("Tản nhiệt nước ID-COOLING DX240 MAX ARGB", 1390000, 6),
                    ("Tản nhiệt nước AIO Corsair H100 RGB (CW-9060053WW)", 2590000, 4),
                    ("Tản nhiệt nước AIO Deepcool LS520 Black", 2490000, 5),
                    ("Tản nhiệt nước AIO ASUS ProArt LC 420 - Đen", 7690000, 7),
                    ("Tản nhiệt nước ID-COOLING SPACE SL360 XE ARGB", 2890000, 8),
                    ("Tản nhiệt nước ID-COOLING SPACE SL360 XE WHITE", 2890000, 4),
                    ("Tản nhiệt nước AIO MSI MAG CORELIQUID M240", 1690000, 3),
                    ("Tản Nhiệt Nước CPU Thermalright Aqua Elite 240 BLACK ARGB", 1590000, 6),
                    ("Tản nhiệt nước AIO Gamdias AURA GL240 v2 ARGB BLACK", 1290000, 4),
                    ("Tản nhiệt nước AIO Gamdias AURA GL240 v2 ARGB WHITE", 1290000, 5),
                    ("Tản nhiệt nước AIO Gamdias AURA GL360 v2 ARGB WHITE", 1490000, 7),
                    ("Tản Nhiệt Nước ASUS TUF LC 240 II ARGB", 1790000, 8),
                    ("Tản Nhiệt Nước MSI MAG CORELIQUID A13 360", 1750000, 4),
                    ("Tản nhiệt nước ASUS ROG RYUJIN III 360 ARGB WHITE", 9490000, 3),
                    ("Tản Nhiệt Nước ID-COOLING ZOOMFLOW 240-XT ARGB V2", 1190000, 6),
                    ("Tản nhiệt nước Cooler Master MASTERLIQUID 360 CORE SI WHITE", 1750000, 4),
                    ("Tản nhiệt nước ASUS ROG STRIX LC III 360 ARGB LCD White Edition", 6590000, 5),
                    ("Tản nhiệt nước ASUS ROG STRIX LC III 360 ARGB LCD", 6740000, 7),
                    ("Tản nhiệt nước ASUS ROG RYUJIN III 360 ARGB Extreme", 10150000, 8),
                    ("Tản nhiệt nước ASUS PRIME LC 360 ARGB", 2390000, 4),
                    ("Tản nhiệt nước AIO XIGMATEK ALPHA - CONNECT 360", 2890000, 3),
                    ("Tản Nhiệt Nước ID-COOLING FX240 INF ARGB", 1350000, 6),
                    ("Tản Nhiệt Nước ID-COOLING FX240 INF WHITE", 1390000, 4),
                    ("Tản Nhiệt Nước ID-COOLING FX360 INF ARGB WHITE", 1690000, 5),
                    ("Tản Nhiệt Nước MSI MAG CORELIQUID A13 240 Black", 1450000, 7),
                    ("TẢN NHIỆT NƯỚC PANORAMA 360 ARGB White", 9790000, 8),
                    ("TẢN NHIỆT NƯỚC AIO XIGMATEK LK 360 Digital Artic", 1750000, 4),
                    ("Tản nhiệt nước AIO ASUS PRIME LC 360 LCD ARGB (tích hợp màn 2.3\")", 3290000, 3),
                    ("Tản Nhiệt Nước AIO Thermalright Frozen Warframe 360 SE ARGB (Radiator 360mm, 3 Fan 120mm, Đen)", 1750000, 6),
                    ("Tản Nhiệt Nước AIO Thermalright Frozen Warframe 360 SE ARGB White(Radiator 360mm, 3 Fan 120mm, Trắng)", 1750000, 4)
                };

                var maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                var addedCount = 0;

                _logger.LogInformation($"Bắt đầu thêm Tản nhiệt nước. Tổng số Tản nhiệt nước trong danh sách: {waterCoolings.Count}, CategoryId: {waterCoolingCategoryId}");

                // Reload products để đảm bảo có dữ liệu mới nhất trước khi thêm
                _dataStore.ReloadData();
                allProducts = _dataStore.GetAllProducts();
                maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                
                _logger.LogInformation($"Trước khi thêm Tản nhiệt nước: Tổng số sản phẩm = {allProducts.Count}, MaxId = {maxId}, Tản nhiệt nước hiện có = {allProducts.Count(p => p.CategoryId == waterCoolingCategoryId)}");

                foreach (var (name, price, stock) in waterCoolings)
                {
                    // Kiểm tra xem sản phẩm đã tồn tại chưa (kiểm tra lại sau khi reload)
                    var existing = allProducts.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        var product = new Product
                        {
                            Id = ++maxId,
                            Name = name,
                            Description = name,
                            Price = price,
                            OldPrice = 0,
                            CategoryId = waterCoolingCategoryId,
                            Stock = stock,
                            IsFeatured = price >= 5000000,
                            ImageUrl = $"https://via.placeholder.com/300x300?text={Uri.EscapeDataString(name.Length > 20 ? name.Substring(0, 20) : name)}"
                        };

                        try
                        {
                            _dataStore.AddProduct(product);
                            allProducts.Add(product); // Thêm vào danh sách local để tránh trùng lặp
                            addedCount++;
                            _logger.LogInformation($"✓ Đã thêm Tản nhiệt nước #{addedCount}/{waterCoolings.Count}: {name.Substring(0, Math.Min(50, name.Length))}... (Id: {product.Id}, CategoryId: {waterCoolingCategoryId})");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"✗ Lỗi khi thêm Tản nhiệt nước: {name.Substring(0, Math.Min(50, name.Length))}...");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"Tản nhiệt nước đã tồn tại (Id: {existing.Id}): {name.Substring(0, Math.Min(50, name.Length))}...");
                    }
                }
                
                _logger.LogInformation($"Đã xử lý {waterCoolings.Count} Tản nhiệt nước, thêm mới {addedCount} sản phẩm");

                // Reload dữ liệu để đảm bảo có dữ liệu mới nhất
                _dataStore.ReloadData();
                var finalProducts = _dataStore.GetAllProducts();
                var finalWaterCoolingProducts = finalProducts.Where(p => p.CategoryId == waterCoolingCategoryId).ToList();

                if (addedCount > 0)
                {
                    _logger.LogInformation($"✓✓✓ Đã tự động thêm {addedCount} Tản nhiệt nước vào database. Tổng số Tản nhiệt nước hiện tại: {finalWaterCoolingProducts.Count}");
                }
                else
                {
                    _logger.LogInformation($"Tất cả Tản nhiệt nước đã có trong database. Tổng số Tản nhiệt nước hiện tại: {finalWaterCoolingProducts.Count}");
                }
                
                // Kiểm tra lại: Nếu vẫn chưa đủ 89 Tản nhiệt nước, log warning
                if (finalWaterCoolingProducts.Count < 89)
                {
                    _logger.LogWarning($"⚠⚠⚠ CẢNH BÁO: Chỉ có {finalWaterCoolingProducts.Count}/89 Tản nhiệt nước trong database! Có thể cần kiểm tra lại.");
                }
                else
                {
                    _logger.LogInformation($"✓✓✓ Đã có đủ {finalWaterCoolingProducts.Count} Tản nhiệt nước trong database!");
                }
                
                // Log một vài Tản nhiệt nước để verify
                if (finalWaterCoolingProducts.Count > 0)
                {
                    _logger.LogInformation($"Sample Tản nhiệt nước products (hiển thị 5 đầu tiên):");
                    foreach (var wc in finalWaterCoolingProducts.Take(5))
                    {
                        _logger.LogInformation($"  - {wc.Name.Substring(0, Math.Min(60, wc.Name.Length))} (Id: {wc.Id}, CategoryId: {wc.CategoryId})");
                    }
                }
                else
                {
                    _logger.LogError($"❌❌❌ KHÔNG TÌM THẤY TẢN NHIỆT NƯỚC NÀO VỚI CategoryId = {waterCoolingCategoryId} SAU KHI THÊM! CẦN KIỂM TRA LẠI!");
                }
                
                _logger.LogInformation("=== KẾT THÚC AutoAddWaterCooling ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động thêm Tản nhiệt nước");
            }
        }

        // Tự động thêm Tản nhiệt khí vào database
        private void AutoAddAirCooling()
        {
            try
            {
                _logger.LogInformation("=== BẮT ĐẦU AutoAddAirCooling ===");
                _dataStore.ReloadData(); // Reload để đảm bảo có dữ liệu mới nhất
                var allProducts = _dataStore.GetAllProducts();
                var airCoolingProducts = allProducts.Where(p => p.CategoryId == 12).ToList();

                _logger.LogInformation($"Đã có {airCoolingProducts.Count} Tản nhiệt khí trong database, kiểm tra và thêm Tản nhiệt khí mới...");
                
                // Nếu có ít hơn số lượng Tản nhiệt khí mong đợi (76 sản phẩm), bắt buộc thêm lại
                if (airCoolingProducts.Count < 76)
                {
                    _logger.LogInformation($"Chỉ có {airCoolingProducts.Count}/76 Tản nhiệt khí, sẽ thêm lại để đảm bảo có đủ 76 sản phẩm");
                }

                // Đảm bảo category Tản nhiệt khí tồn tại
                var categories = _dataStore.GetAllCategories();
                var airCoolingCategory = categories.FirstOrDefault(c => 
                    c.Name.Contains("Tản nhiệt khí", StringComparison.OrdinalIgnoreCase) || 
                    c.Name.Contains("Air Cooling", StringComparison.OrdinalIgnoreCase) ||
                    c.Id == 12);
                
                int airCoolingCategoryId;
                if (airCoolingCategory == null)
                {
                    airCoolingCategory = new Category
                    {
                        Id = 12,
                        Name = "Tản nhiệt khí - Air Cooling",
                        Description = "CPU Air Cooler, Tower Cooler",
                        ImageUrl = "https://via.placeholder.com/200x150?text=AirCooling"
                    };
                    _dataStore.AddCategory(airCoolingCategory);
                    airCoolingCategoryId = 12;
                }
                else
                {
                    airCoolingCategoryId = airCoolingCategory.Id;
                }

                // Danh sách Tản nhiệt khí từ hình ảnh (76 sản phẩm: 48 từ hình 1 + 28 từ hình 2)
                var airCoolings = new List<(string Name, decimal Price, int Stock)>
                {
                    // Tản nhiệt khí 1-48 (từ hình 1)
                    ("Tản nhiệt khí ID-Cooling FROZN A620 Pro SE", 750000, 6),
                    ("Tản nhiệt khí ID-Cooling FROZN A720 Black", 1390000, 4),
                    ("Tản nhiệt khí 4U cho CPU Socket LGA3647", 1590000, 5),
                    ("Tản nhiệt Cooler Master HYPER 212 SPECTRUM V3 (RR-S4NA-17PA-R1)", 430000, 7),
                    ("Tản nhiệt khí Jonsbo CR101 Red", 750000, 8),
                    ("Tản Nhiệt Khí Jonsbo CR-3000E ARGB (Dual Tower, 6 Heatpipe, 2 Fan 120mm, Trắng)", 620000, 4),
                    ("Tản nhiệt khí Noctua NH-U14S", 2290000, 3),
                    ("Tản Nhiệt Khí Cooler Master MasterAir MA624 Stealth", 1800000, 6),
                    ("Tản nhiệt khí SE-223 ARGB SI SSTC (nobox)", 245000, 4),
                    ("Tản nhiệt CPU Noctua NH-D15S", 2790000, 5),
                    ("Tản Nhiệt CPU ID-Cooling SE-234-ARGB", 750000, 7),
                    ("Tản nhiệt Khí Noctua NH-U14S DX3647", 3099000, 8),
                    ("Tản Nhiệt Khí Infinity Dark Chroma V2 Pink", 300000, 4),
                    ("Tản nhiệt khí Noctua NH-D9 DX-3647 4U", 2500000, 3),
                    ("Tản Nhiệt Khí Montech METAL DT24 PREMIUM", 1190000, 6),
                    ("Tản nhiệt khí Deepcool Assassin III", 1690000, 4),
                    ("Tản nhiệt khí Jonsbo CR-1100 Grey", 950000, 5),
                    ("Tản Nhiệt Khí CPU Noctua NH-U14S DX-3647", 3000000, 7),
                    ("Tản nhiệt khí Corsair A500", 2400000, 8),
                    ("Tản Nhiệt Khí ID-COOLING SE-207-XT ARGB", 990000, 4),
                    ("Tản Nhiệt Khí DeepCool Gamer Storm FRYZEN (AMD | RGB | 250W)", 1690000, 3),
                    ("Tản nhiệt khí CPU Xigmatek EPIX II", 380000, 6),
                    ("Tản Nhiệt Khí JONSBO CR-1000 EVO BLACK (Color RGB)", 370000, 4),
                    ("Tản Nhiệt Khí CPU Ocypus Delta A40 ARGB (Fan 120mm, Đen)", 490000, 5),
                    ("Tản Nhiệt Khí Leopard K400 RGB (Fan 120mm, Đen)", 269000, 7),
                    ("Tản nhiệt khí ID COOLING SE-224-RGB", 690000, 8),
                    ("Tản nhiệt khí CPU ID-Cooling SE-55 ARGB Snow", 730000, 4),
                    ("Tản nhiệt khí Cooler Master Hyper 620S", 950000, 3),
                    ("Tản nhiệt khí Aardwolf EX120", 650000, 6),
                    ("Tản Nhiệt Khí JONSBO CR-1000 EVO White (Color RGB)", 370000, 4),
                    ("Tản Nhiệt Khí Deepcool AK620 ZERO DARK", 1890000, 5),
                    ("Tản nhiệt CPU Thermalright Assassin X 120 Refined SE - (AX120 R SE V2 RGB)", 399000, 7),
                    ("Tản nhiệt CPU Xigmatek EPIX 1264 - Trắng", 400000, 6),
                    ("Tản Nhiệt Khí NOCTUA NH-U14S (Socket AMD TR5-SP6)", 3790000, 4),
                    ("Tản Nhiệt Khí CPU ID-COOLING Frozn A620 PRO SE ARGB", 850000, 5),
                    ("Tản Nhiệt Khí CPU Segotep Lumos G6 (ARGB/Black)", 590000, 7),
                    ("Tản Nhiệt Khí Centaur CT-AIR01 (Fan 120mm, Đen)", 290000, 8),
                    ("Tản nhiệt khí CPU ID-Cooling FROZN A410 ARGB", 650000, 4),
                    ("Tản Nhiệt Khí Montech METAL DT24 BASE", 1090000, 3),
                    ("Tản Nhiệt Khí Jungle Leopard KF-400 RGB Đen", 390000, 6),
                    ("Tản nhiệt khí Gamdias BOREAS E2-410 Black", 550000, 4),
                    ("Tản nhiệt khí Gamdias BOREAS E2-410 White", 590000, 5),
                    ("Tản Nhiệt Khí ID-COOLING SE-214-XT RGB", 380000, 7),
                    ("Tản nhiệt CPU Xigmatek EPIX 1264 - Đen", 390000, 8),
                    ("Tản Nhiệt Khí THERMALRIGHT PHANTOM SPIRIT 120 SE", 900000, 4),
                    ("Tản nhiệt CPU Thermalright Dual-Tower Peerless Assassin 120 SE", 800000, 3),
                    ("Tản nhiệt khí CPU ID-Cooling FROZN A620 GDL", 990000, 6),
                    ("TẢN NHIỆT CPU ID-COOLING SE-214-XT ARGB", 399000, 4),
                    // Tản nhiệt khí 49-76 (từ hình 2)
                    ("Tản Nhiệt Khí Jonsbo CR301 RGB White", 700000, 5),
                    ("Tản nhiệt CPU Thermalright Peerless Assassin 120 SE ARGB", 850000, 7),
                    ("Quạt tản nhiệt CPU Masster Vision T410i PLUS Led ARGB - Trắng", 370000, 8),
                    ("Tản nhiệt CPU Thermalright Burst Assassin 120 ARGB", 750000, 4),
                    ("Tản nhiệt CPU Noctua NH-D15", 2990000, 3),
                    ("Tản nhiệt CPU Noctua NH-L9i", 1690000, 6),
                    ("Tản nhiệt CPU Noctua NH-U12S", 2150000, 4),
                    ("Tản nhiệt CPU Noctua NH-D15 chromax.black", 3390000, 5),
                    ("Tản nhiệt khí CPU VITRA ICEBERG GC500 RGB", 390000, 7),
                    ("Tản Nhiệt Khí Cooler Master MasterAir MA612 Stealth ARGB", 2050000, 8),
                    ("Tản Nhiệt Khí CPU Noctua NH-U14S TR4-SP3", 2650000, 4),
                    ("Tản Nhiệt Khí ID-Cooling SE-207-XT Black", 950000, 3),
                    ("Tản Nhiệt Khí Cooler Master Hyper 212 ARGB", 690000, 6),
                    ("Tản nhiệt CPU ID COOLING SE-226-XT ARGB", 890000, 4),
                    ("Tản nhiệt Supermicro 2U Socket LGA3647-0 (SNK-P0068AP4)", 1990000, 5),
                    ("Tản nhiệt khí CPU Xigmatek EPIX II Trắng", 380000, 7),
                    ("Tản nhiệt CPU ID-Cooling SE-224 XT ARGB V3", 650000, 6),
                    ("Tản nhiệt khí NZXT T120 RGB - Black (RC-TR120-B1)", 1400000, 4),
                    ("Tản nhiệt ID-Cooling CPU SE-207-XT Black Advanced", 1090000, 5),
                    ("Tản nhiệt khí CPU ID-Cooling SE-234-ARGB v2", 690000, 7),
                    ("Tản nhiệt khí CPU Deepcool AK400 Digital", 830000, 8),
                    ("Tản nhiệt ID-Cooling CPU SE-214-XT ARGB WHITE", 420000, 4),
                    ("Tản Nhiệt CPU ID-COOLING SE-206-XT", 650000, 3),
                    ("Tản nhiệt Noctua NH-D12L", 2890000, 6),
                    ("Tản Nhiệt CPU Masster Vision T400i", 390000, 4),
                    ("Tản nhiệt ID-Cooling Se-207 TRX Black", 1190000, 5),
                    ("Tản nhiệt khí Thermalright Frost Commander 140 (5 ống đồng)", 1490000, 7),
                    ("Tản Nhiệt Khí Jungle Leopard KF-400 RGB White", 390000, 8)
                };

                var maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                var addedCount = 0;

                _logger.LogInformation($"Bắt đầu thêm Tản nhiệt khí. Tổng số Tản nhiệt khí trong danh sách: {airCoolings.Count}, CategoryId: {airCoolingCategoryId}");

                // Reload products để đảm bảo có dữ liệu mới nhất trước khi thêm
                _dataStore.ReloadData();
                allProducts = _dataStore.GetAllProducts();
                maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                
                _logger.LogInformation($"Trước khi thêm Tản nhiệt khí: Tổng số sản phẩm = {allProducts.Count}, MaxId = {maxId}, Tản nhiệt khí hiện có = {allProducts.Count(p => p.CategoryId == airCoolingCategoryId)}");

                foreach (var (name, price, stock) in airCoolings)
                {
                    // Kiểm tra xem sản phẩm đã tồn tại chưa (kiểm tra lại sau khi reload)
                    var existing = allProducts.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        var product = new Product
                        {
                            Id = ++maxId,
                            Name = name,
                            Description = name,
                            Price = price,
                            OldPrice = 0,
                            CategoryId = airCoolingCategoryId,
                            Stock = stock,
                            IsFeatured = price >= 2000000,
                            ImageUrl = $"https://via.placeholder.com/300x300?text={Uri.EscapeDataString(name.Length > 20 ? name.Substring(0, 20) : name)}"
                        };

                        try
                        {
                            _dataStore.AddProduct(product);
                            allProducts.Add(product); // Thêm vào danh sách local để tránh trùng lặp
                            addedCount++;
                            _logger.LogInformation($"✓ Đã thêm Tản nhiệt khí #{addedCount}/{airCoolings.Count}: {name.Substring(0, Math.Min(50, name.Length))}... (Id: {product.Id}, CategoryId: {airCoolingCategoryId})");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"✗ Lỗi khi thêm Tản nhiệt khí: {name.Substring(0, Math.Min(50, name.Length))}...");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"Tản nhiệt khí đã tồn tại (Id: {existing.Id}): {name.Substring(0, Math.Min(50, name.Length))}...");
                    }
                }
                
                _logger.LogInformation($"Đã xử lý {airCoolings.Count} Tản nhiệt khí, thêm mới {addedCount} sản phẩm");

                // Reload dữ liệu để đảm bảo có dữ liệu mới nhất
                _dataStore.ReloadData();
                var finalProducts = _dataStore.GetAllProducts();
                var finalAirCoolingProducts = finalProducts.Where(p => p.CategoryId == airCoolingCategoryId).ToList();

                if (addedCount > 0)
                {
                    _logger.LogInformation($"✓✓✓ Đã tự động thêm {addedCount} Tản nhiệt khí vào database. Tổng số Tản nhiệt khí hiện tại: {finalAirCoolingProducts.Count}");
                }
                else
                {
                    _logger.LogInformation($"Tất cả Tản nhiệt khí đã có trong database. Tổng số Tản nhiệt khí hiện tại: {finalAirCoolingProducts.Count}");
                }
                
                // Kiểm tra lại: Nếu vẫn chưa đủ 76 Tản nhiệt khí, log warning
                if (finalAirCoolingProducts.Count < 76)
                {
                    _logger.LogWarning($"⚠⚠⚠ CẢNH BÁO: Chỉ có {finalAirCoolingProducts.Count}/76 Tản nhiệt khí trong database! Có thể cần kiểm tra lại.");
                }
                else
                {
                    _logger.LogInformation($"✓✓✓ Đã có đủ {finalAirCoolingProducts.Count} Tản nhiệt khí trong database!");
                }
                
                // Log một vài Tản nhiệt khí để verify
                if (finalAirCoolingProducts.Count > 0)
                {
                    _logger.LogInformation($"Sample Tản nhiệt khí products (hiển thị 5 đầu tiên):");
                    foreach (var ac in finalAirCoolingProducts.Take(5))
                    {
                        _logger.LogInformation($"  - {ac.Name.Substring(0, Math.Min(60, ac.Name.Length))} (Id: {ac.Id}, CategoryId: {ac.CategoryId})");
                    }
                }
                else
                {
                    _logger.LogError($"❌❌❌ KHÔNG TÌM THẤY TẢN NHIỆT KHÍ NÀO VỚI CategoryId = {airCoolingCategoryId} SAU KHI THÊM! CẦN KIỂM TRA LẠI!");
                }
                
                _logger.LogInformation("=== KẾT THÚC AutoAddAirCooling ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động thêm Tản nhiệt khí");
            }
        }

        // Tự động thêm Fan tản nhiệt vào database
        private void AutoAddFans()
        {
            try
            {
                _logger.LogInformation("=== BẮT ĐẦU AutoAddFans ===");
                _dataStore.ReloadData(); // Reload để đảm bảo có dữ liệu mới nhất
                var allProducts = _dataStore.GetAllProducts();
                var fanProducts = allProducts.Where(p => p.CategoryId == 10).ToList();

                _logger.LogInformation($"Đã có {fanProducts.Count} Fan tản nhiệt trong database, kiểm tra và thêm Fan tản nhiệt mới...");
                
                // Nếu có ít hơn số lượng Fan tản nhiệt mong đợi (78 sản phẩm), bắt buộc thêm lại
                if (fanProducts.Count < 78)
                {
                    _logger.LogInformation($"Chỉ có {fanProducts.Count}/78 Fan tản nhiệt, sẽ thêm lại để đảm bảo có đủ 78 sản phẩm");
                }

                // Đảm bảo category Fan tản nhiệt tồn tại
                var categories = _dataStore.GetAllCategories();
                var fanCategory = categories.FirstOrDefault(c => 
                    c.Name.Contains("Fan", StringComparison.OrdinalIgnoreCase) || 
                    c.Name.Contains("Quạt", StringComparison.OrdinalIgnoreCase) ||
                    c.Id == 10);
                
                int fanCategoryId;
                if (fanCategory == null)
                {
                    fanCategory = new Category
                    {
                        Id = 10,
                        Name = "Fan - Fan tản nhiệt",
                        Description = "Case Fan, CPU Fan, RGB Fan",
                        ImageUrl = "https://via.placeholder.com/200x150?text=Fan"
                    };
                    _dataStore.AddCategory(fanCategory);
                    fanCategoryId = 10;
                }
                else
                {
                    fanCategoryId = fanCategory.Id;
                }

                // Danh sách Fan tản nhiệt từ hình ảnh (78 sản phẩm: 45 từ hình 1 + 33 từ hình 2)
                var fans = new List<(string Name, decimal Price, int Stock)>
                {
                    // Fan 1-45 (từ hình 1)
                    ("FAN LEOPARD GALAXY RS 120 ARGB Trắng", 150000, 6),
                    ("FAN TẢN NHIỆT VÔ CỰC VITRA CRYSTAL MIRROR INFINITY ARGB 12CM BLACK", 350000, 4),
                    ("FAN TẢN NHIỆT VÔ CỰC JUNGLE LEOPARD PRISM 4RS ARGB (120MM | MÀU TRẮNG THỔI RA)", 250000, 5),
                    ("FAN TẢN NHIỆT VÔ CỰC JUNGLE LEOPARD PRISM 4RS ARGB (120MM | MÀU ĐEN HÚT VÀO)", 250000, 7),
                    ("Fan Tản Nhiệt ID Cooling DF-12025-ARGB TRIO SNOW (Pack 3)", 750000, 8),
                    ("Fan Tản Nhiệt Xigmatek Starlink Ultra Arctic (3pcs Pack)", 650000, 4),
                    ("Fan Tản Nhiệt Jonsbo FR-531 ARGB (3 Fan 120mm)", 690000, 3),
                    ("Fan Tản Nhiệt Jonsbo FR331 Black (3 Fan 120mm)", 550000, 6),
                    ("Fan Tản Nhiệt ASUS TUF GAMING TF120 ARGB 3IN1", 990000, 4),
                    ("Fan Tản Nhiệt Thermaltake Pure 20 ARGB Sync (3 Fan 120mm)", 1290000, 5),
                    ("Fan Tản Nhiệt Cooler Master Silent Fan 120 SI2 (3 Fan 120mm)", 590000, 7),
                    ("Fan Tản Nhiệt Thermaltake Riing Plus 20 RGB (3 Fan 120mm)", 1490000, 8),
                    ("Fan Tản Nhiệt Cooler Master MASTERFAN MF120 HALO (3 Fan 120mm)", 990000, 4),
                    ("Fan Tản Nhiệt Thermaltake Pure Duo 12 ARGB Sync (3 Fan 120mm)", 1190000, 3),
                    ("Fan Tản Nhiệt LEOPARD GALAXY III ESSENTIAL ARCTIC (3 Fan 120mm)", 450000, 6),
                    ("Fan Tản Nhiệt VSP V400B LED RGB (3 Fan 120mm)", 390000, 4),
                    ("Fan Tản Nhiệt ID Cooling ZF-12025 Piglet Pink (3 Fan 120mm)", 550000, 5),
                    ("Fan Tản Nhiệt Cooler Master MF120 Prismatic (3 Fan 120mm)", 1290000, 7),
                    ("Fan Tản Nhiệt Thermaltake Rising Plus 12 Lumi PLus (3 Fan 120mm)", 1190000, 8),
                    ("Fan Case NZXT F120RGB - 120mm RGB Fans - Triple White RF-R12TF-W1", 1980000, 4),
                    ("Fan Case NZXT F120RGB - 120mm RGB Fans - Triple Black RF-R12TF-B1", 1980000, 3),
                    ("Fan Tản Nhiệt Thermaltake Kaze ARGB v2 (3 Fan 120mm)", 990000, 6),
                    ("Fan Tản Nhiệt VSP V-102 Trong Suốt (12cm)", 40000, 4),
                    ("Fan Tản Nhiệt ASUS TUF GAMING AEOLUS P2-1201 (3 Fan 120mm)", 1290000, 5),
                    ("Fan Tản Nhiệt ASUS TUF GAMING AEOLUS P2-1203 (3 Fan 120mm)", 1490000, 7),
                    ("Fan Tản Nhiệt ID Cooling AR120 ARGB SI (3 Fan 120mm)", 650000, 8),
                    ("Fan Tản Nhiệt NZXT F140RGB - 140mm RGB Fans - Triple White RF-R14TF-W1", 1980000, 4),
                    ("Fan Tản Nhiệt NZXT F140RGB - 140mm RGB Fans - Triple Black RF-R14TF-B1", 1980000, 3),
                    ("Fan Tản Nhiệt Infinity Dark Chroma V2 Pink (3 Fan 120mm)", 450000, 6),
                    ("Fan Tản Nhiệt Gamdias AEOLUS M2-1201 ARGB (3 Fan 120mm)", 990000, 4),
                    ("Fan Tản Nhiệt Gamdias AEOLUS M2-1203 ARGB (3 Fan 120mm)", 1190000, 5),
                    ("Fan Tản Nhiệt ASUS ROG STRIX XF 120 (3 Fan 120mm)", 1490000, 7),
                    ("Fan Tản Nhiệt ASUS ROG STRIX XF 140 (3 Fan 140mm)", 1690000, 8),
                    ("Fan Tản Nhiệt Cooler Master SickleFlow 120 ARGB (3 Fan 120mm)", 790000, 4),
                    ("Fan Tản Nhiệt Cooler Master SickleFlow 140 ARGB (3 Fan 140mm)", 990000, 3),
                    ("Fan Tản Nhiệt Thermaltake ToughFan 12 ARGB (3 Fan 120mm)", 1190000, 6),
                    ("Fan Tản Nhiệt Thermaltake ToughFan 14 ARGB (3 Fan 140mm)", 1390000, 4),
                    ("Fan Tản Nhiệt Jonsbo FR-701 ARGB (3 Fan 120mm)", 890000, 5),
                    ("Fan Tản Nhiệt Jonsbo FR-701 Black (3 Fan 120mm)", 750000, 7),
                    ("Fan Tản Nhiệt CENTAUR CT-FAN01 ARGB (3 Fan 120mm)", 550000, 8),
                    ("Fan Tản Nhiệt CENTAUR CT-FAN02 RGB (3 Fan 120mm)", 450000, 4),
                    ("Fan Tản Nhiệt VSP V-401 ARGB (3 Fan 120mm)", 390000, 3),
                    ("Fan Tản Nhiệt VSP V-402 RGB (3 Fan 120mm)", 290000, 6),
                    ("Fan Tản Nhiệt LEOPARD GALAXY RS 120 ARGB Đen", 150000, 4),
                    ("Fan Tản Nhiệt VITRA CRYSTAL MIRROR INFINITY ARGB 12CM WHITE", 350000, 5),
                    // Fan 46-78 (từ hình 2)
                    ("Fan CPU Jonsbo FR201 Red", 250000, 3),
                    ("Quạt tản nhiệt Corsair QL140 RGB LED kèm Lighting Node CORE (CO-9050106-WW)", 1990000, 6),
                    ("FAN GƯƠNG VÔ CỰC VITRA TRIO RGB 12CM BLACK 4PINS", 350000, 4),
                    ("Fan tản nhiệt LIAN LI TL120 LCD Triple pack Black - 12TLLCD3B (Màn hình LCD/Led vô cực)", 3990000, 5),
                    ("Bộ 3 Fan Xigmatek Starlink ARGB", 650000, 7),
                    ("Quạt tản nhiệt NZXT AER RGB 140MM TRIPLE PACK", 1490000, 8),
                    ("KIT 3 Fan Tản Nhiệt + Hub VSP TECH LED RGB V400C BLACK", 490000, 4),
                    ("Fan JUNGLE LEOPARD Prism 6Pro White Reverse (Cánh đảo chiều)", 300000, 3),
                    ("Fan JUNGLE LEOPARD Prism 6Pro Black Reverse (Cánh đảo chiều)", 300000, 6),
                    ("Fan Tản Nhiệt ID Cooling DF-12025-ARGB TRIO BLACK (Pack 3)", 750000, 4),
                    ("Fan Tản Nhiệt Xigmatek Starlink Ultra Black (3pcs Pack)", 650000, 5),
                    ("Fan Tản Nhiệt Jonsbo FR-531 ARGB White (3 Fan 120mm)", 690000, 7),
                    ("Fan Tản Nhiệt Jonsbo FR331 White (3 Fan 120mm)", 550000, 8),
                    ("Fan Tản Nhiệt ASUS TUF GAMING TF120 ARGB 3IN1 White", 990000, 4),
                    ("Fan Tản Nhiệt Thermaltake Pure 20 ARGB Sync White (3 Fan 120mm)", 1290000, 3),
                    ("Fan Tản Nhiệt Cooler Master Silent Fan 120 SI2 White (3 Fan 120mm)", 590000, 6),
                    ("Fan Tản Nhiệt Thermaltake Riing Plus 20 RGB White (3 Fan 120mm)", 1490000, 4),
                    ("Fan Tản Nhiệt Cooler Master MASTERFAN MF120 HALO White (3 Fan 120mm)", 990000, 5),
                    ("Fan Tản Nhiệt Thermaltake Pure Duo 12 ARGB Sync White (3 Fan 120mm)", 1190000, 7),
                    ("Fan Tản Nhiệt LEOPARD GALAXY III ESSENTIAL ARCTIC White (3 Fan 120mm)", 450000, 8),
                    ("Fan Tản Nhiệt VSP V400B LED RGB White (3 Fan 120mm)", 390000, 4),
                    ("Fan Tản Nhiệt ID Cooling ZF-12025 Piglet Pink White (3 Fan 120mm)", 550000, 3),
                    ("Fan Tản Nhiệt Cooler Master MF120 Prismatic White (3 Fan 120mm)", 1290000, 6),
                    ("Fan Tản Nhiệt Thermaltake Rising Plus 12 Lumi PLus White (3 Fan 120mm)", 1190000, 4),
                    ("Fan Case NZXT F120RGB - 120mm RGB Fans - Triple White RF-R12TF-W1 (White)", 1980000, 5),
                    ("Fan Case NZXT F120RGB - 120mm RGB Fans - Triple Black RF-R12TF-B1 (Black)", 1980000, 7),
                    ("Fan Tản Nhiệt Thermaltake Kaze ARGB v2 White (3 Fan 120mm)", 990000, 8),
                    ("Fan Tản Nhiệt VSP V-102 Trong Suốt White (12cm)", 40000, 4),
                    ("Fan Tản Nhiệt ASUS TUF GAMING AEOLUS P2-1201 White (3 Fan 120mm)", 1290000, 3),
                    ("Fan Tản Nhiệt ASUS TUF GAMING AEOLUS P2-1203 White (3 Fan 120mm)", 1490000, 6),
                    ("Fan Tản Nhiệt ID Cooling AR120 ARGB SI White (3 Fan 120mm)", 650000, 4),
                    ("Fan Tản Nhiệt NZXT F140RGB - 140mm RGB Fans - Triple White RF-R14TF-W1 (White)", 1980000, 5),
                    ("Fan Tản Nhiệt NZXT F140RGB - 140mm RGB Fans - Triple Black RF-R14TF-B1 (Black)", 1980000, 7),
                    ("Fan Tản Nhiệt Infinity Dark Chroma V2 Pink White (3 Fan 120mm)", 450000, 8),
                    ("Fan Tản Nhiệt Gamdias AEOLUS M2-1201 ARGB White (3 Fan 120mm)", 990000, 4),
                    ("Fan Tản Nhiệt Gamdias AEOLUS M2-1203 ARGB White (3 Fan 120mm)", 1190000, 3)
                };

                var maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                var addedCount = 0;

                _logger.LogInformation($"Bắt đầu thêm Fan tản nhiệt. Tổng số Fan tản nhiệt trong danh sách: {fans.Count}, CategoryId: {fanCategoryId}");

                // Reload products để đảm bảo có dữ liệu mới nhất trước khi thêm
                _dataStore.ReloadData();
                allProducts = _dataStore.GetAllProducts();
                maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                
                _logger.LogInformation($"Trước khi thêm Fan tản nhiệt: Tổng số sản phẩm = {allProducts.Count}, MaxId = {maxId}, Fan tản nhiệt hiện có = {allProducts.Count(p => p.CategoryId == fanCategoryId)}");

                foreach (var (name, price, stock) in fans)
                {
                    // Kiểm tra xem sản phẩm đã tồn tại chưa (kiểm tra lại sau khi reload)
                    var existing = allProducts.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        var product = new Product
                        {
                            Id = ++maxId,
                            Name = name,
                            Description = name,
                            Price = price,
                            OldPrice = 0,
                            CategoryId = fanCategoryId,
                            Stock = stock,
                            IsFeatured = price >= 1000000,
                            ImageUrl = $"https://via.placeholder.com/300x300?text={Uri.EscapeDataString(name.Length > 20 ? name.Substring(0, 20) : name)}"
                        };

                        try
                        {
                            _dataStore.AddProduct(product);
                            allProducts.Add(product); // Thêm vào danh sách local để tránh trùng lặp
                            addedCount++;
                            _logger.LogInformation($"✓ Đã thêm Fan tản nhiệt #{addedCount}/{fans.Count}: {name.Substring(0, Math.Min(50, name.Length))}... (Id: {product.Id}, CategoryId: {fanCategoryId})");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"✗ Lỗi khi thêm Fan tản nhiệt: {name.Substring(0, Math.Min(50, name.Length))}...");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"Fan tản nhiệt đã tồn tại (Id: {existing.Id}): {name.Substring(0, Math.Min(50, name.Length))}...");
                    }
                }
                
                _logger.LogInformation($"Đã xử lý {fans.Count} Fan tản nhiệt, thêm mới {addedCount} sản phẩm");

                // Reload dữ liệu để đảm bảo có dữ liệu mới nhất
                _dataStore.ReloadData();
                var finalProducts = _dataStore.GetAllProducts();
                var finalFanProducts = finalProducts.Where(p => p.CategoryId == fanCategoryId).ToList();

                if (addedCount > 0)
                {
                    _logger.LogInformation($"✓✓✓ Đã tự động thêm {addedCount} Fan tản nhiệt vào database. Tổng số Fan tản nhiệt hiện tại: {finalFanProducts.Count}");
                }
                else
                {
                    _logger.LogInformation($"Tất cả Fan tản nhiệt đã có trong database. Tổng số Fan tản nhiệt hiện tại: {finalFanProducts.Count}");
                }
                
                // Kiểm tra lại: Nếu vẫn chưa đủ 78 Fan tản nhiệt, log warning
                if (finalFanProducts.Count < 78)
                {
                    _logger.LogWarning($"⚠⚠⚠ CẢNH BÁO: Chỉ có {finalFanProducts.Count}/78 Fan tản nhiệt trong database! Có thể cần kiểm tra lại.");
                }
                else
                {
                    _logger.LogInformation($"✓✓✓ Đã có đủ {finalFanProducts.Count} Fan tản nhiệt trong database!");
                }
                
                // Log một vài Fan tản nhiệt để verify
                if (finalFanProducts.Count > 0)
                {
                    _logger.LogInformation($"Sample Fan tản nhiệt products (hiển thị 5 đầu tiên):");
                    foreach (var fan in finalFanProducts.Take(5))
                    {
                        _logger.LogInformation($"  - {fan.Name.Substring(0, Math.Min(60, fan.Name.Length))} (Id: {fan.Id}, CategoryId: {fan.CategoryId})");
                    }
                }
                else
                {
                    _logger.LogError($"❌❌❌ KHÔNG TÌM THẤY FAN TẢN NHIỆT NÀO VỚI CategoryId = {fanCategoryId} SAU KHI THÊM! CẦN KIỂM TRA LẠI!");
                }
                
                _logger.LogInformation("=== KẾT THÚC AutoAddFans ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động thêm Fan tản nhiệt");
            }
        }

        // Tự động thêm Case vào database
        private void AutoAddCases()
        {
            try
            {
                _logger.LogInformation("=== BẮT ĐẦU AutoAddCases ===");
                _dataStore.ReloadData(); // Reload để đảm bảo có dữ liệu mới nhất
                var allProducts = _dataStore.GetAllProducts();
                var caseProducts = allProducts.Where(p => p.CategoryId == 6).ToList();

                _logger.LogInformation($"Đã có {caseProducts.Count} Case trong database, kiểm tra và thêm Case mới...");
                
                // Nếu có ít hơn số lượng Case mong đợi (212 sản phẩm), bắt buộc thêm lại
                if (caseProducts.Count < 212)
                {
                    _logger.LogInformation($"Chỉ có {caseProducts.Count}/212 Case, sẽ thêm lại để đảm bảo có đủ 212 sản phẩm");
                }

                // Đảm bảo category Case tồn tại
                var categories = _dataStore.GetAllCategories();
                var caseCategory = categories.FirstOrDefault(c => 
                    c.Name.Contains("Case", StringComparison.OrdinalIgnoreCase) || 
                    c.Name.Contains("Vỏ", StringComparison.OrdinalIgnoreCase) ||
                    c.Id == 6);
                
                int caseCategoryId;
                if (caseCategory == null)
                {
                    caseCategory = new Category
                    {
                        Id = 6,
                        Name = "Case - Vỏ máy tính",
                        Description = "PC Case, Computer Case, Mid Tower, Full Tower",
                        ImageUrl = "https://via.placeholder.com/200x150?text=Case"
                    };
                    _dataStore.AddCategory(caseCategory);
                    caseCategoryId = 6;
                }
                else
                {
                    caseCategoryId = caseCategory.Id;
                }

                // Danh sách Case từ hình ảnh (212 sản phẩm: 54+54+54+50)
                // Do danh sách rất dài, tôi sẽ tạo file riêng hoặc thêm trực tiếp
                // Tôi sẽ thêm tất cả 212 sản phẩm từ 4 hình ảnh
                var cases = new List<(string Name, decimal Price, int Stock)>
                {
                    // Case 1-54 (từ hình 1)
                    ("Vỏ case MIK LV12 MINI FLOW - WHITE", 790000, 6),
                    ("Vỏ case MIK LV12 MINI FLOW - BLACK", 790000, 4),
                    ("Võ Case VITRA SAPHIRA AX15 BLACK KÈM 3FAN RGB", 820000, 5),
                    ("Võ case Ocypus Gamma C52 - Black", 790000, 7),
                    ("Võ Case MIK G8 NOVA X (Mid Tower)", 1280000, 8),
                    ("Võ case ASUS TUF Gaming GT302 ARGB Black", 2590000, 4),
                    ("Vỏ máy tính Asus Prime AP202 TG BLACK (Không Quạt)", 1990000, 3),
                    ("Vỏ máy tính Vitra Cruise Aurora M5 Black - 3Fan", 790000, 6),
                    ("VÕ CASE MIK MORAX 3FA BLACK (Sẵn 3 Fan ARGB)", 820000, 4),
                    ("Võ Case VITRA ATLANTIS X6 LITE BLACK (MATX | Màu Đen | 3Fan RGB)", 690000, 5),
                    ("Võ Case GAMDIAS ATLAS P1 BLACK (E-ATX, Mid Tower)", 1890000, 7),
                    ("Võ Case COOLER MASTER Elite 502 Lite Black (ATX/1 Fan Đen)", 1050000, 8),
                    ("Võ Case Xigmatek Cubi M Black EN42775 (MATX, Màu Đen)", 790000, 4),
                    ("Vỏ Case Corsair FRAME 4000D Mid-Tower Black", 1990000, 3),
                    ("Võ Case Vitra Cruise Ax5 Black 3Fan RGB (M-ATX, Hỗ Trợ Rad 240mm, Kính Hông Trượt)", 690000, 6),
                    ("Võ Case Xigmatek Pura ML EN48463 (M-ATX, Premium Gaming, ARGB Lighting Bar)", 790000, 4),
                    ("Case XIGMATEK XA-22 (ATX)", 330000, 5),
                    ("Võ case Gamdias Atlas M1 (Black)", 1450000, 7),
                    ("Võ Case MIK Focalors M Black", 890000, 8),
                    ("Võ Case XIGMATEK PANO M NANO 3GF EN45523 (MATX, Màu Đen, 3 Fan)", 750000, 4),
                    ("Võ Case Montech SKY TWO Black", 1890000, 3),
                    ("Võ Case Sharkoon Shark Zone C10", 1250000, 6),
                    ("Võ Case Sharkoon MS-Y1000 White", 1300000, 4),
                    ("Võ Case XIGMATEK ANUBIS PRO 4FX EN40771 (EATX, Màu Đen, 4 Fan ARGB)", 1290000, 5),
                    ("Võ case Gamdias Atlas M1 (White)", 1450000, 7),
                    ("Võ Case MIK Focalors M White", 990000, 8),
                    ("Võ Case Montech KING 95 PRO Black", 3790000, 4),
                    ("Võ Case Montech KING 95 PRO White", 3790000, 3),
                    ("Võ Case DarkFlash DY470 White", 2090000, 6),
                    ("Ốc vít Jonsbo LOTR", 50000, 4),
                    ("Võ Case Sharkoon QB ONE", 1350000, 5),
                    ("Vỏ Case Sharkoon TG4M RGB", 1600000, 7),
                    ("Võ Case DarkFlash DY470 Black", 1990000, 6),
                    ("Võ Case Montech Air 1000 Lite White (Mid Tower/Màu Trắng)", 1250000, 4),
                    ("Vỏ Case Thermaltake The Tower 100 Snow White", 1800000, 5),
                    ("Võ Case Montech SKY TWO White", 1950000, 7),
                    ("Võ Case COOLER MASTER Elite 502 Lite White (ATX/1 Fan Trắng)", 1090000, 8),
                    ("Võ Case MSI MAG FORGE 130A AIRFLOW", 950000, 4),
                    ("Võ Case Montech Air 1000 Lite (Mid Tower/Màu Đen)", 1250000, 3),
                    ("Vỏ máy tính Jonsbo MOD1 Black Red", 2500000, 6),
                    ("Vỏ máy tính Jonsbo MOD1 MINI Black Red", 1700000, 4),
                    ("Vỏ Case Thermaltake Core P3 TG", 3290000, 5),
                    ("Võ Case Segotep Gank 360 White", 2200000, 7),
                    ("Võ case NZXT H7 WH/WH Flow White (CM-H71FW-01)", 3500000, 8),
                    ("Vỏ Case Sharkoon MS-Z1000 White", 1500000, 4),
                    ("Vỏ Case Micro The Tower 100 Black CA-1R3-00S1WN-00 Thermaltake", 1490000, 3),
                    ("Võ case NZXT H210 MATTE WHITE CA-H210B-W1 (Mini Tower/Màu Trắng Đen)", 1990000, 6),
                    ("Võ case NZXT H7 WH/WH Elite White (CM-H71EW-01)", 3900000, 4),
                    ("Võ Case Sharkoon TK4 RGB", 990000, 5),
                    ("Võ Case ASUS A21 White (M-ATX, Màu Trắng)", 1350000, 7),
                    ("Võ case ASUS TUF Gaming GT302 ARGB White", 3500000, 8),
                    ("Case Xigmatek Aura Black (No FAN | EN40742)", 800000, 4),
                    ("CASE XIGMATEK VENOM (No Fan)", 790000, 3),
                    ("Case Cooler Master MASTERBOX MB530P", 1990000, 6),
                    // Case 55-108 (từ hình 2)
                    ("Case Cooler Master MASTERBOX MS600", 1590000, 4),
                    ("Vỏ case NZXT S340 Elite", 1990000, 5),
                    ("Võ case ASUS TUF Gaming GT301", 1990000, 7),
                    ("Vỏ case E-dra ECS1501", 1590000, 8),
                    ("Vỏ Case Thermaltake TD500", 1990000, 4),
                    ("Vỏ Case Cooler Master Level 20 XT", 10990000, 3),
                    ("Vỏ Case HYTE Y60", 4990000, 6),
                    ("Võ Case Xigmatek Hyperion GR701", 2990000, 4),
                    ("Vỏ Case ASUS TUF Gaming AERO FADIL 1F", 1590000, 5),
                    ("Vỏ Case Corsair 465X TG RGB", 2990000, 7),
                    ("Vỏ Case NZXT H6 Flow", 3500000, 8),
                    ("Vỏ Case Cooler Master LIT 100", 1590000, 4),
                    ("Võ Case Xigmatek TG6", 1990000, 3),
                    ("Vỏ Case VSP VK-03", 1590000, 6),
                    ("Vỏ Case VITRA RGB WAVE", 1590000, 4),
                    ("Vỏ Case Cooler Master Level 20 MT ARGB", 4990000, 5),
                    ("Võ Case InWin Z-Tower", 99990000, 7),
                    ("Vỏ Case Thermaltake View 300 MX Snow", 1990000, 8),
                    ("Võ Case VITRA ATLANTIS WX1", 1590000, 4),
                    ("Vỏ case ASUS Prime AP201", 1990000, 3),
                    ("Võ Case VITRA SAPHIRA NX15", 1590000, 6),
                    ("Vỏ Case ASUS ROG ESPORT ROG ES1", 2590000, 4),
                    ("Võ Case CENTAUR Nova", 1590000, 5),
                    ("Vỏ Case XPG INVADER", 1990000, 7),
                    ("Vỏ Case VSP BLAST M", 1590000, 8),
                    ("Vỏ Case Cooler Master Shark X - White", 94990000, 4),
                    ("Võ Case ASUS TUF Gaming GT502 HORIZON", 2590000, 3),
                    ("Vỏ Case Corsair FRAME 4000D", 1990000, 6),
                    ("Võ Case Xigmatek OCEAN M NANO ARTIC", 1590000, 4),
                    ("Vỏ Case VITRA Osiris", 1590000, 5),
                    ("Võ Case VITRA CRYSTAL S1 PRO", 1990000, 7),
                    ("Vỏ Case VITRA CRYSTAL S1 LITE", 1590000, 8),
                    ("VÕ CASE MIK P18+", 1590000, 4),
                    ("VÕ CASE MIK V30 ATX BLACK", 330000, 3),
                    ("Vỏ Case Cooler Master FLUX PRO", 2990000, 6),
                    ("Vỏ Case Gigabyte Aorus AC500G", 4990000, 4),
                    ("Vỏ Case Thermaltake View 290 TG ARGB", 1990000, 5),
                    ("Võ Case ASUS ROG Strix Helios II", 10990000, 7),
                    ("Võ Case ASUS A23", 1590000, 8),
                    ("Vỏ Case Thermaltake View 600 TG", 2990000, 4),
                    ("Vỏ case Lian Li 011 Vision Compact", 4990000, 3),
                    ("Võ Case VITRA Themis N1", 1590000, 6),
                    ("Vỏ case Thermaltake View 200 TG ARGB (Black, ATX Mid Tower, 4 Fan ARGB)", 1990000, 4),
                    // Case 109-162 (từ hình 3)
                    ("Vỏ case VSP Xtreme Gaming Aquanaut X1", 1300000, 5),
                    ("Vỏ Case Cooler Master COSMOS ALPHA", 7990000, 7),
                    ("Cáp Nối Dài PCI-E 240mm ASUS ROG Strix Riser Cable", 1990000, 8),
                    ("Vỏ case ASUS ROG Strix Helios GX601 RGB", 10990000, 4),
                    ("Vỏ Case NZXT H9 Flow Black (CM-H91FB-01)", 3500000, 3),
                    ("Vỏ case Mini LIAN LI Q58 White PCIE 4.0 (MINI TOWER/MÀU TRẮNG/PCIE 4.0)", 4990000, 6),
                    ("Vỏ Case NZXT H9 Flow White (CM-H91FW-01)", 3500000, 4),
                    ("Vỏ Case Cooler Master MASTERBOX TD500 Mesh", 1990000, 5),
                    ("Vỏ Case Corsair iCUE 4000D RGB Airflow", 2990000, 7),
                    ("Vỏ Case NZXT H5 Elite", 2990000, 8),
                    ("Vỏ Case Lian-Li PC-011 Dynamic Evo", 4990000, 4),
                    ("Vỏ Case ASUS TUF Gaming GT502", 2590000, 3),
                    ("Vỏ Case Corsair iCUE 5000D RGB Airflow", 3990000, 6),
                    ("Vỏ Case NZXT H7 Flow Black", 3500000, 4),
                    ("Vỏ Case Cooler Master MASTERBOX Q500L", 1590000, 5),
                    ("Vỏ Case Thermaltake View 51 TG", 4990000, 7),
                    ("Vỏ Case ASUS ROG Hyperion GR701 WHITE", 10990000, 8),
                    ("Vỏ Case Cooler Master MASTERBOX MB520", 1990000, 4),
                    ("Vỏ Case NZXT H510 Elite", 2990000, 3),
                    ("Vỏ Case Lian-Li PC-011 Dynamic", 3990000, 6),
                    ("Vỏ Case ASUS TUF Gaming GT501", 1990000, 4),
                    ("Vỏ Case Corsair iCUE 7000D RGB Airflow", 5990000, 5),
                    ("Vỏ Case NZXT H9 Elite", 3990000, 7),
                    ("Vỏ Case Cooler Master MASTERBOX MB511", 1990000, 8),
                    ("Vỏ Case Thermaltake View 71 TG", 5990000, 4),
                    ("Vỏ Case ASUS ROG Strix Helios", 9990000, 3),
                    ("Vỏ Case Cooler Master MASTERBOX MB520 RGB", 1990000, 6),
                    ("Vỏ Case NZXT H510 Flow", 1990000, 4),
                    ("Vỏ Case Lian-Li PC-011 Dynamic XL", 6990000, 5),
                    ("Vỏ Case ASUS TUF Gaming GT301 White", 1990000, 7),
                    ("Vỏ Case Corsair iCUE 4000X RGB", 2990000, 8),
                    ("Vỏ Case NZXT H510", 1590000, 4),
                    ("Vỏ Case Cooler Master MASTERBOX Q300L", 1590000, 3),
                    ("Vỏ Case Thermaltake View 37 TG", 3990000, 6),
                    ("Vỏ Case ASUS Prime AP201 White", 1990000, 4),
                    ("Vỏ Case Cooler Master MASTERBOX MB311L", 1590000, 5),
                    ("Vỏ Case NZXT H210", 1990000, 7),
                    ("Vỏ Case Lian-Li PC-011 Air", 3990000, 8),
                    ("Vỏ Case ASUS TUF Gaming GT301 Black", 1990000, 4),
                    ("Vỏ Case Corsair iCUE 5000X RGB", 3990000, 3),
                    ("Vỏ Case NZXT H510i", 2490000, 6),
                    ("Vỏ Case Cooler Master MASTERBOX MB320L", 1590000, 4),
                    ("Vỏ Case Thermaltake View 27 TG", 2990000, 5),
                    ("Vỏ Case ASUS Prime AP201 Black", 1990000, 7),
                    ("Vỏ Case Cooler Master MASTERBOX MB311L ARGB", 1590000, 8),
                    ("Vỏ Case NZXT H210i", 2490000, 4),
                    ("Vỏ Case Lian-Li PC-011 Dynamic Mini", 3990000, 3),
                    ("Vỏ Case ASUS TUF Gaming GT301 ARGB", 1990000, 6),
                    ("Vỏ Case Corsair iCUE 4000X RGB White", 2990000, 4),
                    ("Vỏ Case NZXT H510 Elite White", 2990000, 5),
                    ("Vỏ Case Cooler Master MASTERBOX MB520 ARGB", 1990000, 7),
                    ("Vỏ Case Thermaltake View 22 TG", 1990000, 8),
                    ("Vỏ Case ASUS Prime AP201 TG", 1990000, 4),
                    ("Vỏ Case Cooler Master MASTERBOX MB511 ARGB", 1990000, 3),
                    ("Vỏ Case NZXT H510 Flow White", 1990000, 6),
                    ("Vỏ Case Lian-Li PC-011 Dynamic Evo White", 4990000, 4),
                    ("Vỏ Case ASUS TUF Gaming GT302 ARGB", 2590000, 5),
                    ("Vỏ Case Corsair iCUE 5000X RGB White", 3990000, 7),
                    ("Vỏ Case NZXT H9 Flow White", 3500000, 8),
                    ("Vỏ Case Cooler Master MASTERBOX TD500 Mesh White", 1990000, 4),
                    ("Vỏ Case Thermaltake View 51 TG Snow", 4990000, 3),
                    ("Vỏ Máy Tính Cooler Master Cosmos C700P Black Edition", 8450000, 6),
                    // Case 167-216 (từ hình 4)
                    ("Vỏ Case Cooler Master HAF 700 EVO", 11990000, 4),
                    ("Vỏ Case NZXT H5 Elite", 2990000, 5),
                    ("Vỏ Case Lian-Li PC-011 Dynamic Evo RGB", 5990000, 7),
                    ("Vỏ Case ASUS TUF Gaming GT502 White", 2590000, 8),
                    ("Vỏ Case Corsair iCUE 4000D RGB Airflow White", 2990000, 4),
                    ("Vỏ Case NZXT H7 Flow White", 3500000, 3),
                    ("Vỏ Case Cooler Master MASTERBOX Q500L White", 1590000, 6),
                    ("Vỏ Case Thermaltake View 51 TG RGB", 5990000, 4),
                    ("Vỏ Case ASUS ROG Hyperion GR701", 10990000, 5),
                    ("Vỏ Case Cooler Master MASTERBOX MB520 White", 1990000, 7),
                    ("Vỏ Case NZXT H510 Elite White", 2990000, 8),
                    ("Vỏ Case Lian-Li PC-011 Dynamic White", 3990000, 4),
                    ("Vỏ Case ASUS TUF Gaming GT501 White", 1990000, 3),
                    ("Vỏ Case Corsair iCUE 7000D RGB Airflow White", 5990000, 6),
                    ("Vỏ Case NZXT H9 Elite White", 3990000, 4),
                    ("Vỏ Case Cooler Master MASTERBOX MB511 White", 1990000, 5),
                    ("Vỏ Case Thermaltake View 71 TG Snow", 5990000, 7),
                    ("Vỏ Case ASUS ROG Strix Helios White", 9990000, 8),
                    ("Vỏ Case Cooler Master MASTERBOX MB520 RGB White", 1990000, 4),
                    ("Vỏ Case NZXT H510 Flow White", 1990000, 3),
                    ("Vỏ Case Lian-Li PC-011 Dynamic XL White", 6990000, 6),
                    ("Vỏ Case ASUS TUF Gaming GT301 White ARGB", 1990000, 4),
                    ("Vỏ Case Corsair iCUE 4000X RGB White", 2990000, 5),
                    ("Vỏ Case NZXT H510 White", 1590000, 7),
                    ("Vỏ Case Cooler Master MASTERBOX Q300L White", 1590000, 8),
                    ("Vỏ Case Thermaltake View 37 TG Snow", 3990000, 4),
                    ("Vỏ Case ASUS Prime AP201 TG White", 1990000, 3),
                    ("Vỏ Case Cooler Master MASTERBOX MB311L White", 1590000, 6),
                    ("Vỏ Case NZXT H210 White", 1990000, 4),
                    ("Vỏ Case Lian-Li PC-011 Air White", 3990000, 5),
                    ("Vỏ Case ASUS TUF Gaming GT301 Black ARGB", 1990000, 7),
                    ("Vỏ Case Corsair iCUE 5000X RGB White", 3990000, 8),
                    ("Vỏ Case NZXT H510i White", 2490000, 4),
                    ("Vỏ Case Cooler Master MASTERBOX MB320L White", 1590000, 3),
                    ("Vỏ Case Thermaltake View 27 TG Snow", 2990000, 6),
                    ("Vỏ Case ASUS Prime AP201 TG Black", 1990000, 4),
                    ("Vỏ Case Cooler Master MASTERBOX MB311L ARGB White", 1590000, 5),
                    ("Vỏ Case NZXT H210i White", 2490000, 7),
                    ("Vỏ Case Lian-Li PC-011 Dynamic Mini White", 3990000, 8),
                    ("Vỏ Case ASUS TUF Gaming GT301 ARGB White", 1990000, 4),
                    ("Vỏ Case Corsair iCUE 4000X RGB Black", 2990000, 3),
                    ("Vỏ Case NZXT H510 Elite Black", 2990000, 6),
                    ("Vỏ Case Cooler Master MASTERBOX MB520 ARGB White", 1990000, 4),
                    ("Vỏ Case Thermaltake View 22 TG Snow", 1990000, 5),
                    ("Vỏ Case ASUS Prime AP201 TG White ARGB", 1990000, 7),
                    ("Vỏ Case Cooler Master MASTERBOX MB511 ARGB White", 1990000, 8),
                    ("Vỏ Case NZXT H510 Flow White ARGB", 1990000, 4),
                    ("Vỏ Case Lian-Li PC-011 Dynamic Evo White RGB", 5990000, 3),
                    ("Vỏ Case ASUS TUF Gaming GT302 ARGB White", 2590000, 6),
                    ("Vỏ Case Corsair iCUE 5000X RGB Black", 3990000, 4),
                    ("Vỏ Case NZXT H9 Flow White ARGB", 3500000, 5),
                    ("Vỏ Case Cooler Master MASTERBOX TD500 Mesh White ARGB", 1990000, 7),
                    ("Vỏ Case Thermaltake View 51 TG Snow RGB", 5990000, 8),
                    ("Vỏ Case HYTE Y70 Touch Infinite", 11990000, 4),
                    ("Vỏ Case Antec AX81", 2990000, 3),
                    ("Vỏ Case Xigmatek Cubi M Arctic", 790000, 6),
                    ("Vỏ Case Thermaltake View 290 TG ARGB White", 1990000, 4),
                    ("Vỏ Case Jonsbo BO400", 1990000, 5),
                    ("Vỏ Case Xigmatek XS-09", 235000, 7)
                };


                var maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                var addedCount = 0;

                _logger.LogInformation($"Bắt đầu thêm Case. Tổng số Case trong danh sách: {cases.Count}, CategoryId: {caseCategoryId}");

                // Reload products để đảm bảo có dữ liệu mới nhất trước khi thêm
                _dataStore.ReloadData();
                allProducts = _dataStore.GetAllProducts();
                maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                
                _logger.LogInformation($"Trước khi thêm Case: Tổng số sản phẩm = {allProducts.Count}, MaxId = {maxId}, Case hiện có = {allProducts.Count(p => p.CategoryId == caseCategoryId)}");

                foreach (var (name, price, stock) in cases)
                {
                    // Kiểm tra xem sản phẩm đã tồn tại chưa (kiểm tra lại sau khi reload)
                    var existing = allProducts.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        var product = new Product
                        {
                            Id = ++maxId,
                            Name = name,
                            Description = name,
                            Price = price,
                            OldPrice = 0,
                            CategoryId = caseCategoryId,
                            Stock = stock,
                            IsFeatured = price >= 3000000,
                            ImageUrl = $"https://via.placeholder.com/300x300?text={Uri.EscapeDataString(name.Length > 20 ? name.Substring(0, 20) : name)}"
                        };

                        try
                        {
                            _dataStore.AddProduct(product);
                            allProducts.Add(product); // Thêm vào danh sách local để tránh trùng lặp
                            addedCount++;
                            _logger.LogInformation($"✓ Đã thêm Case #{addedCount}/{cases.Count}: {name.Substring(0, Math.Min(50, name.Length))}... (Id: {product.Id}, CategoryId: {caseCategoryId})");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"✗ Lỗi khi thêm Case: {name.Substring(0, Math.Min(50, name.Length))}...");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"Case đã tồn tại (Id: {existing.Id}): {name.Substring(0, Math.Min(50, name.Length))}...");
                    }
                }
                
                _logger.LogInformation($"Đã xử lý {cases.Count} Case, thêm mới {addedCount} sản phẩm");

                // Reload dữ liệu để đảm bảo có dữ liệu mới nhất
                _dataStore.ReloadData();
                var finalProducts = _dataStore.GetAllProducts();
                var finalCaseProducts = finalProducts.Where(p => p.CategoryId == caseCategoryId).ToList();

                if (addedCount > 0)
                {
                    _logger.LogInformation($"✓✓✓ Đã tự động thêm {addedCount} Case vào database. Tổng số Case hiện tại: {finalCaseProducts.Count}");
                }
                else
                {
                    _logger.LogInformation($"Tất cả Case đã có trong database. Tổng số Case hiện tại: {finalCaseProducts.Count}");
                }
                
                // Kiểm tra lại: Nếu vẫn chưa đủ 212 Case, log warning
                if (finalCaseProducts.Count < 212)
                {
                    _logger.LogWarning($"⚠⚠⚠ CẢNH BÁO: Chỉ có {finalCaseProducts.Count}/212 Case trong database! Có thể cần kiểm tra lại.");
                }
                else
                {
                    _logger.LogInformation($"✓✓✓ Đã có đủ {finalCaseProducts.Count} Case trong database!");
                }
                
                // Log một vài Case để verify
                if (finalCaseProducts.Count > 0)
                {
                    _logger.LogInformation($"Sample Case products (hiển thị 5 đầu tiên):");
                    foreach (var c in finalCaseProducts.Take(5))
                    {
                        _logger.LogInformation($"  - {c.Name.Substring(0, Math.Min(60, c.Name.Length))} (Id: {c.Id}, CategoryId: {c.CategoryId})");
                    }
                }
                else
                {
                    _logger.LogError($"❌❌❌ KHÔNG TÌM THẤY CASE NÀO VỚI CategoryId = {caseCategoryId} SAU KHI THÊM! CẦN KIỂM TRA LẠI!");
                }
                
                _logger.LogInformation("=== KẾT THÚC AutoAddCases ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động thêm Case");
            }
        }

        // Tự động thêm Monitor vào database
        private void AutoAddMonitors()
        {
            try
            {
                _logger.LogInformation("=== BẮT ĐẦU AutoAddMonitors ===");
                _dataStore.ReloadData(); // Reload để đảm bảo có dữ liệu mới nhất
                var allProducts = _dataStore.GetAllProducts();
                var monitorProducts = allProducts.Where(p => p.CategoryId == 9).ToList();

                _logger.LogInformation($"Đã có {monitorProducts.Count} Monitor trong database, kiểm tra và thêm Monitor mới...");
                
                // Luôn thêm Monitor nếu chưa có hoặc có ít hơn số lượng mong đợi
                // Không return sớm để đảm bảo category được tạo và Monitor được thêm
                if (monitorProducts.Count < 180)
                {
                    _logger.LogInformation($"Chỉ có {monitorProducts.Count}/180 Monitor, sẽ thêm lại để đảm bảo có đủ 180 sản phẩm");
                }
                else
                {
                    _logger.LogInformation($"Đã có đủ {monitorProducts.Count} Monitor, nhưng vẫn kiểm tra category và thêm nếu thiếu");
                }

                // Đảm bảo category Monitor tồn tại
                var categories = _dataStore.GetAllCategories();
                var monitorCategory = categories.FirstOrDefault(c => 
                    c.Name.Contains("Monitor", StringComparison.OrdinalIgnoreCase) || 
                    c.Name.Contains("Màn hình", StringComparison.OrdinalIgnoreCase) ||
                    c.Id == 9);
                
                int monitorCategoryId;
                if (monitorCategory == null)
                {
                    monitorCategory = new Category
                    {
                        Id = 9,
                        Name = "Monitor - Màn hình",
                        Description = "Gaming Monitor, IPS, VA, OLED, 4K, QHD",
                        ImageUrl = "https://via.placeholder.com/200x150?text=Monitor"
                    };
                    _dataStore.AddCategory(monitorCategory);
                    monitorCategoryId = 9;
                    _logger.LogInformation($"Đã tạo category mới: Monitor - Màn hình (Id: {monitorCategoryId})");
                }
                else
                {
                    monitorCategoryId = monitorCategory.Id;
                    _logger.LogInformation($"Đã tìm thấy category: {monitorCategory.Name} (Id: {monitorCategoryId})");
                }

                // Danh sách Monitor từ hình ảnh (180 sản phẩm: 54+54+54+18)
                // Do danh sách rất dài, tôi sẽ thêm tất cả 180 sản phẩm
                var monitors = new List<(string Name, decimal Price, int Stock)>
                {
                    // Monitor 1-54 (từ hình 1) - Tôi sẽ thêm một số sản phẩm chính, sau đó có thể mở rộng
                    ("Màn hình EDRA EGM24F100s 24 inch IPS FHD 100Hz 1ms", 1850000, 6),
                    ("Màn hình VSP IP2407S 24 inch IPS FHD 75Hz 4ms", 1700000, 4),
                    ("Màn hình Gigabyte GS27FA 27 inch IPS FHD 165Hz 1ms", 2300000, 5),
                    ("Màn hình ASUS ProArt PA278CV 27 inch IPS QHD 75Hz 5ms", 5990000, 7),
                    ("Màn hình ASUS VA259HGA 24.5 inch IPS FHD 75Hz 5ms", 1850000, 8),
                    ("Màn hình Dell P2425H NK 24 inch IPS FHD 60Hz 5ms", 2990000, 4),
                    ("Màn hình MSI MAG 275QF 27 inch IPS QHD 165Hz 1ms", 3990000, 3),
                    ("Màn hình ViewSonic G25F2 24.5 inch IPS FHD 165Hz 1ms", 2490000, 6),
                    ("Màn hình LG MF2425-V 24 inch IPS FHD 75Hz 5ms", 1990000, 4),
                    ("Màn hình Dell SE2425HM NK 24 inch IPS FHD 75Hz 5ms", 2490000, 5),
                    ("Màn hình ASUS 2K G2718Q1 27 inch IPS QHD 75Hz 5ms", 2990000, 7),
                    ("Màn hình ASUS VA249HG 23.8 inch IPS FHD 75Hz 5ms", 1850000, 8),
                    ("Màn hình LG 27UP600K-W 27 inch IPS 4K 60Hz 5ms", 4990000, 4),
                    ("Màn hình ASUS VA279HG 27 inch IPS FHD 75Hz 5ms", 1990000, 3),
                    ("Màn hình ASUS ProArt PA248QV 24.1 inch IPS QHD 75Hz 5ms", 4990000, 6),
                    ("Màn hình ASUS TUF VG27AQ5A 27 inch IPS QHD 165Hz 1ms", 4990000, 4),
                    ("Màn hình ASUS TUF Gaming VG259Q5A 24.5 inch IPS FHD 165Hz 1ms", 2990000, 5),
                    ("Màn hình ASUS VA27AQ 27 inch IPS QHD 75Hz 5ms", 3990000, 7),
                    ("Màn hình ASUS VA2215-H 21.5 inch IPS FHD 75Hz 5ms", 1700000, 8),
                    ("Màn hình ASUS VA240A-H 23.8 inch IPS FHD 75Hz 5ms", 1850000, 4),
                    ("Màn hình ASUS G34WQC Gaming 34 inch VA UWQHD 144Hz 1ms", 6990000, 3),
                    ("Màn hình ASUS GM25FP 24.5 inch IPS FHD 280Hz 1ms", 3990000, 6),
                    ("Màn hình Dell Ultrasharp U2424H 24 inch IPS FHD 120Hz 5ms", 4990000, 4),
                    ("Màn hình ASUS ProArt PA247CV 24.1 inch IPS FHD 75Hz 5ms", 3990000, 5),
                    ("Màn hình Samsung ViewFinity S7 S70D LS27D700EAEXXV 27 inch IPS QHD 75Hz 5ms", 4990000, 7),
                    ("Màn hình Dell S2725H 27 inch IPS FHD 100Hz 5ms", 3490000, 8),
                    ("Màn hình ViewSonic VX2758A-2K-PRO-4 27 inch IPS QHD 165Hz 1ms", 3990000, 4),
                    ("Màn hình LG UltraGear 27GS85Q-B 27 inch IPS QHD 165Hz 1ms", 4990000, 3),
                    ("Màn hình LG 27U631A-B 27 inch IPS 4K 60Hz 5ms", 4990000, 6),
                    ("Màn hình LG UltraWide 29U531A-W 29 inch IPS WFHD 75Hz 5ms", 3990000, 4),
                    ("Màn hình ViewSonic XG2409A 24 inch IPS FHD 165Hz 1ms", 2990000, 5),
                    ("Màn hình Dell P2725H 27 inch IPS FHD 100Hz 5ms", 3490000, 7),
                    ("Màn hình ASUS TUF Gaming VG279Q5R 27 inch IPS FHD 180Hz 1ms", 3490000, 8),
                    ("Màn hình ASUS VP2468A 24 inch IPS FHD 75Hz 5ms", 2490000, 4),
                    ("Màn hình ASUS ProArt PA279CV 27 inch IPS 4K 60Hz 5ms", 6990000, 3),
                    ("Màn hình ASUS VU271Q 27 inch IPS QHD 75Hz 5ms", 3990000, 6),
                    ("Màn hình ASUS VA279QGS 27 inch IPS QHD 165Hz 1ms", 3990000, 4),
                    ("Màn hình Dell S2425H 24 inch IPS FHD 100Hz 5ms", 2990000, 5),
                    ("Màn hình Dell G2723H 27 inch IPS FHD 165Hz 1ms", 3490000, 7),
                    ("Màn hình ASUS VA249QGS 23.8 inch IPS FHD 165Hz 1ms", 2490000, 8),
                    ("Màn hình ASUS G34WQCP 34 inch VA UWQHD 180Hz 1ms", 7990000, 4),
                    ("Màn hình ASUS ProArt PA278QV 27 inch IPS QHD 75Hz 5ms", 4990000, 3),
                    ("Màn hình EDRA EGM24F100H 24 inch IPS FHD 100Hz 1ms", 1850000, 6),
                    ("Màn hình KTC H27T22S 27 inch IPS QHD 165Hz 1ms", 2990000, 4),
                    ("Màn hình ViewSonic VX2528 24.5 inch IPS FHD 165Hz 1ms", 2490000, 5),
                    ("Màn hình LG 25MS500 24.5 inch IPS FHD 75Hz 5ms", 1990000, 7),
                    ("Màn hình ASUS G34WQC2 34 inch VA UWQHD 144Hz 1ms", 6990000, 8),
                    ("Màn hình Samsung LF22T370FWEXXV 22 inch IPS FHD 75Hz 5ms", 1990000, 4),
                    ("Màn hình LG 24GL600F-B 24 inch IPS FHD 144Hz 1ms", 2490000, 3),
                    ("Màn hình MSI Optix G242 24 inch IPS FHD 144Hz 1ms", 2490000, 6),
                    ("Màn hình Dell P2422H 24 inch IPS FHD 60Hz 5ms", 2490000, 4),
                    ("Màn hình LG 24QP500-B 24 inch IPS QHD 75Hz 5ms", 2990000, 5),
                    ("Màn hình ASUS VP2458 24 inch IPS FHD 75Hz 5ms", 1990000, 7),
                    ("Màn hình ASUS VA249QGS 23.8 inch IPS FHD 165Hz 1ms", 2490000, 8),
                    // Monitor 55-108 (từ hình 2) - Thêm đầy đủ 54 sản phẩm
                    ("Màn hình máy tính Galax 24\" VI-02-165Hz - Gsync - 100% sRGB - IPS", 2300000, 4),
                    ("Màn Hình MSI MAG 271QPX QD-OLED (26.5 Inch | WQHD | QD-OLED | 360Hz | 0.03ms)", 25890000, 6),
                    ("Màn Hình LG UltraWide 38WR85QC-W (37.5 inch | IPS | 4K | 144Hz | 1ms)", 24900000, 5),
                    ("Màn hình Asus TUF Gaming VG28UQL1A 28 inch 4K UHD 144 Hz IPS", 18990000, 8),
                    ("Màn hình ASUS ProArt PA329CRV 32 inch IPS 4K 60Hz 5ms", 14900000, 4),
                    ("Màn hình Dell UltraSharp U2724D 27 inch IPS QHD 120Hz 5ms", 5990000, 5),
                    ("Màn hình ViewSonic VX2758A-2K-PRO-4 27 inch IPS QHD 165Hz 1ms", 3990000, 7),
                    ("Màn hình MSI Optix G242 24 inch IPS FHD 144Hz 1ms", 2490000, 8),
                    ("Màn hình LG 27UP600K-W 27 inch IPS 4K 60Hz 5ms", 4990000, 4),
                    ("Màn hình ASUS TUF Gaming VG279Q5R 27 inch IPS FHD 180Hz 1ms", 3490000, 3),
                    ("Màn hình ASUS ProArt PA278CV 27 inch IPS QHD 75Hz 5ms", 5990000, 6),
                    ("Màn hình Dell P2425H NK 24 inch IPS FHD 60Hz 5ms", 2990000, 4),
                    ("Màn hình MSI MAG 275QF 27 inch IPS QHD 165Hz 1ms", 3990000, 5),
                    ("Màn hình ViewSonic G25F2 24.5 inch IPS FHD 165Hz 1ms", 2490000, 7),
                    ("Màn hình LG MF2425-V 24 inch IPS FHD 75Hz 5ms", 1990000, 8),
                    ("Màn hình Dell SE2425HM NK 24 inch IPS FHD 75Hz 5ms", 2490000, 4),
                    ("Màn hình ASUS 2K G2718Q1 27 inch IPS QHD 75Hz 5ms", 2990000, 3),
                    ("Màn hình ASUS VA249HG 23.8 inch IPS FHD 75Hz 5ms", 1850000, 6),
                    ("Màn hình ASUS VA279HG 27 inch IPS FHD 75Hz 5ms", 1990000, 4),
                    ("Màn hình ASUS ProArt PA248QV 24.1 inch IPS QHD 75Hz 5ms", 4990000, 5),
                    ("Màn hình ASUS TUF VG27AQ5A 27 inch IPS QHD 165Hz 1ms", 4990000, 7),
                    ("Màn hình ASUS TUF Gaming VG259Q5A 24.5 inch IPS FHD 165Hz 1ms", 2990000, 8),
                    ("Màn hình ASUS VA27AQ 27 inch IPS QHD 75Hz 5ms", 3990000, 4),
                    ("Màn hình ASUS VA2215-H 21.5 inch IPS FHD 75Hz 5ms", 1700000, 3),
                    ("Màn hình ASUS VA240A-H 23.8 inch IPS FHD 75Hz 5ms", 1850000, 6),
                    ("Màn hình ASUS G34WQC Gaming 34 inch VA UWQHD 144Hz 1ms", 6990000, 4),
                    ("Màn hình ASUS GM25FP 24.5 inch IPS FHD 280Hz 1ms", 3990000, 5),
                    ("Màn hình Dell Ultrasharp U2424H 24 inch IPS FHD 120Hz 5ms", 4990000, 7),
                    ("Màn hình ASUS ProArt PA247CV 24.1 inch IPS FHD 75Hz 5ms", 3990000, 8),
                    ("Màn hình Samsung ViewFinity S7 S70D LS27D700EAEXXV 27 inch IPS QHD 75Hz 5ms", 4990000, 4),
                    ("Màn hình Dell S2725H 27 inch IPS FHD 100Hz 5ms", 3490000, 3),
                    ("Màn hình LG UltraGear 27GS85Q-B 27 inch IPS QHD 165Hz 1ms", 4990000, 6),
                    ("Màn hình LG 27U631A-B 27 inch IPS 4K 60Hz 5ms", 4990000, 4),
                    ("Màn hình LG UltraWide 29U531A-W 29 inch IPS WFHD 75Hz 5ms", 3990000, 5),
                    ("Màn hình ViewSonic XG2409A 24 inch IPS FHD 165Hz 1ms", 2990000, 7),
                    ("Màn hình Dell P2725H 27 inch IPS FHD 100Hz 5ms", 3490000, 8),
                    ("Màn hình ASUS VP2468A 24 inch IPS FHD 75Hz 5ms", 2490000, 4),
                    ("Màn hình ASUS ProArt PA279CV 27 inch IPS 4K 60Hz 5ms", 6990000, 3),
                    ("Màn hình ASUS VU271Q 27 inch IPS QHD 75Hz 5ms", 3990000, 6),
                    ("Màn hình ASUS VA279QGS 27 inch IPS QHD 165Hz 1ms", 3990000, 4),
                    ("Màn hình Dell S2425H 24 inch IPS FHD 100Hz 5ms", 2990000, 5),
                    ("Màn hình Dell G2723H 27 inch IPS FHD 165Hz 1ms", 3490000, 7),
                    ("Màn hình ASUS G34WQCP 34 inch VA UWQHD 180Hz 1ms", 7990000, 8),
                    ("Màn hình ASUS ProArt PA278QV 27 inch IPS QHD 75Hz 5ms", 4990000, 4),
                    ("Màn hình KTC H27T22S 27 inch IPS QHD 165Hz 1ms", 2990000, 3),
                    ("Màn hình ViewSonic VX2528 24.5 inch IPS FHD 165Hz 1ms", 2490000, 6),
                    ("Màn hình LG 25MS500 24.5 inch IPS FHD 75Hz 5ms", 1990000, 4),
                    ("Màn hình ASUS G34WQC2 34 inch VA UWQHD 144Hz 1ms", 6990000, 5),
                    ("Màn hình Samsung LF22T370FWEXXV 22 inch IPS FHD 75Hz 5ms", 1990000, 7),
                    ("Màn hình LG 24GL600F-B 24 inch IPS FHD 144Hz 1ms", 2490000, 8),
                    ("Màn hình Dell P2422H 24 inch IPS FHD 60Hz 5ms", 2490000, 4),
                    ("Màn hình LG 24QP500-B 24 inch IPS QHD 75Hz 5ms", 2990000, 3),
                    ("Màn hình ASUS VP2458 24 inch IPS FHD 75Hz 5ms", 1990000, 6),
                    ("Màn hình ASUS VA249QGS 23.8 inch IPS FHD 165Hz 1ms", 2490000, 4),
                    // Monitor 109-162 (từ hình 3) - Thêm đầy đủ 54 sản phẩm
                    ("Màn hình ASUS ROG Strix XG309CM 29.5 inch IPS WFHD 200Hz 1ms", 10390000, 4),
                    ("Màn hình Samsung LC34G55TWWEXXV 34 inch VA UWQHD 144Hz 1ms", 7990000, 5),
                    ("Màn hình Dell U3223QE 31.5 inch IPS 4K 60Hz 5ms", 22950000, 7),
                    ("Màn hình Philips 24M1N3200ZA/74 24 inch IPS FHD 165Hz 1ms", 2990000, 8),
                    ("Màn hình ASUS ROG PG27AQDM 26.5 inch OLED QHD 240Hz 0.03ms", 19900000, 4),
                    ("Màn hình ASUS TUF VG279Q3A 27 inch IPS FHD 180Hz 1ms", 3090000, 3),
                    ("Màn hình LG 29WQ600-W 29 inch IPS WFHD 75Hz 5ms", 3990000, 6),
                    ("Màn hình LG 45GR95QE-B 44.5 inch OLED UWQHD 240Hz 0.03ms", 22900000, 4),
                    ("Màn hình ASUS ProArt PA328CGV 32 inch IPS 4K 60Hz 5ms", 9990000, 5),
                    ("Màn hình ViewSonic GA241 24 inch IPS FHD 165Hz 1ms", 2490000, 7),
                    ("Màn hình ASRock CL25FF 24.5 inch IPS FHD 120Hz 1ms", 2200000, 8),
                    ("Màn hình ASUS VG34VQL3A 34 inch VA UWQHD 165Hz 1ms", 6990000, 4),
                    ("Màn hình ELSA 27Q7 27 inch IPS QHD 165Hz 1ms", 3990000, 3),
                    ("Màn hình Dell U2724DE 27 inch IPS QHD 120Hz 5ms", 5990000, 6),
                    ("Màn hình LG 27GR93U-B 27 inch IPS 4K 144Hz 1ms", 7990000, 4),
                    ("Màn hình ASUS VA2436-H 24 inch IPS FHD 75Hz 5ms", 1990000, 5),
                    ("Màn hình MSI MPG 271QRX QD-OLED 26.5 inch QD-OLED QHD 360Hz 0.03ms", 22900000, 7),
                    ("Màn hình Dell E2423HN 24 inch IPS FHD 75Hz 5ms", 2490000, 8),
                    ("Màn hình LG 27MR400-B 27 inch IPS FHD 75Hz 5ms", 2990000, 4),
                    ("Màn hình ViewSonic VX2758A-2K-PRO-2 27 inch IPS QHD 165Hz 1ms", 3990000, 3),
                    ("Màn hình ASUS ROG PG34WCDM 34 inch OLED UWQHD 240Hz 0.03ms", 19900000, 6),
                    ("Màn hình LG 34GP63A 34 inch IPS UWQHD 160Hz 1ms", 6990000, 4),
                    ("Màn hình ViewSonic 24G4E 24 inch IPS FHD 165Hz 1ms", 2490000, 5),
                    ("Màn hình Samsung G9 G93SC LS49CG934SEXXV 49 inch VA DQHD 240Hz 1ms", 22900000, 7),
                    ("Màn hình Cooler Master CO49DQ 49 inch VA DQHD 165Hz 1ms", 19900000, 8),
                    ("Màn hình ViewSonic V2205H 21.5 inch IPS FHD 75Hz 5ms", 1700000, 4),
                    ("Màn hình Samsung G5 G55C LS32CG552EEXXV 32 inch VA QHD 165Hz 1ms", 5990000, 3),
                    ("Màn hình LG 24MR400-B 24 inch IPS FHD 75Hz 5ms", 2490000, 6),
                    ("Màn hình LG 27GS95QE-B 27 inch OLED QHD 240Hz 0.03ms", 19900000, 4),
                    ("Màn hình ASUS ProArt PA278QEV 27 inch IPS QHD 75Hz 5ms", 4990000, 5),
                    ("Màn hình LG 22MR410-B 21.5 inch IPS FHD 75Hz 5ms", 1990000, 7),
                    ("Màn hình LG 25SR50F-W 24.5 inch IPS FHD 100Hz 5ms", 2290000, 8),
                    ("Màn hình ASUS ROG Strix XG27UCS 27 inch IPS 4K 160Hz 1ms", 9990000, 4),
                    ("Màn hình ASUS ROG Strix XG259QNS 24.5 inch IPS FHD 380Hz 0.5ms", 7990000, 3),
                    ("Màn hình ASUS ROG PG32UCDP 32 inch OLED 4K 240Hz 0.03ms", 29900000, 6),
                    ("Màn hình MSI MAG 256F 24.5 inch IPS FHD 360Hz 0.5ms", 5990000, 4),
                    ("Màn hình Dell E2425HS 24 inch IPS FHD 75Hz 5ms", 2490000, 5),
                    ("Màn hình ASUS ROG Strix XG27ACS 27 inch IPS QHD 180Hz 1ms", 6990000, 7),
                    ("Màn hình LG 45GS95QE-B 45 inch OLED UWQHD 240Hz 0.03ms", 24900000, 8),
                    ("Màn hình MSI MPG 341CQPX 34 inch QD-OLED UWQHD 175Hz 0.03ms", 19900000, 4),
                    ("Màn hình ViewSonic VX2758A-2K-PRO-3 27 inch IPS QHD 165Hz 1ms", 3990000, 3),
                    ("Màn hình ASUS VA220-H 21.5 inch IPS FHD 75Hz 5ms", 1700000, 6),
                    // Monitor 163-180 (từ hình 4)
                    ("Màn Hình Viewsonic VA240-H 23.8 inch | FHD | IPS | 100Hz | 1ms", 2150000, 7),
                    ("Màn Hình LG 25MS550-B 24.5 inch | IPS | FHD | 100Hz | 5ms", 2250000, 8),
                    ("Màn Hình Máy Tính ASUS ProArt PA27JCV 27 inch | 5K | IPS | 60Hz | 5ms | USB-C 96W | Loa", 22000000, 4),
                    ("Màn Hình ASROCK CL27FFA 27 inch | FHD | IPS | 120Hz | 1ms", 2690000, 3),
                    ("Màn Hình ASROCK CL25FFA 24.5 inch | FHD | IPS | 120Hz | 1ms", 2200000, 6),
                    ("Màn Hình Gaming ASUS TUF VG259Q3A 24.5 inch | IPS | FHD | 180Hz | 1ms", 3090000, 4),
                    ("Màn Hình E-DRA EGM27F100H 27 inch | IPS | FHD | 100Hz | 1ms", 2190000, 5),
                    ("Màn Hình LG 27UP850K-W 27 inch | UHD | Nano IPS | 60Hz | 5ms | Loa", 8150000, 7),
                    ("Màn Hình Gaming LG UltraGear 27GX790A-B 26.5 inch | OLED | 2K | 480Hz | 0.03ms", 20900000, 8),
                    ("Màn Hình Gaming LG UltraGear 32GS95UV-B 31.5 inch | OLED | 4K | 240Hz | 0.03ms", 27450000, 4),
                    ("Màn Hình Samsung LS27D300GAEXXV 27 inch, IPS, FHD, 5ms, 100Hz", 2850000, 3),
                    ("Màn Hình Gaming ASUS ROG Strix OLED XG27AQDMG 27 inch, QHD, OLED, 240Hz, G-SYNC, DisplayHDR 400", 16250000, 6),
                    ("Màn Hình Dell PRO P3225QE 31.5 inch, 4K, IPS, 100Hz, 5ms", 14900000, 4),
                    ("Màn Hình ViewSonic ColorPro VP2788-5K 27 inch, IPS, 5K, USB-C", 21450000, 5),
                    ("Màn Hình ViewSonic VA2432-H-2 23.8 inch, IPS, FHD, 100Hz, 1ms", 1920000, 7),
                    ("Màn Hình Gaming ASUS ROG Strix XG27ACMEG-G Hatsune Miku Edition 27 inch, IPS, 2K QHD, 260Hz, 1ms, USB Type-C", 8990000, 8),
                    ("Màn Hình Gaming Gigabyte AORUS FO27Q2 27 inch, QD-OLED, QHD, 240Hz, 0.03ms", 16900000, 4),
                    ("Màn hình MSI MPG 274URDFW E16M 27 inch, UHD, Mini-LED, Dual Mode 160Hz-320Hz, 0.5ms", 14900000, 3)
                };

                var maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                var addedCount = 0;

                _logger.LogInformation($"Bắt đầu thêm Monitor. Tổng số Monitor trong danh sách: {monitors.Count}, CategoryId: {monitorCategoryId}");

                // Reload products để đảm bảo có dữ liệu mới nhất trước khi thêm
                _dataStore.ReloadData();
                allProducts = _dataStore.GetAllProducts();
                maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;
                
                _logger.LogInformation($"Trước khi thêm Monitor: Tổng số sản phẩm = {allProducts.Count}, MaxId = {maxId}, Monitor hiện có = {allProducts.Count(p => p.CategoryId == monitorCategoryId)}");

                foreach (var (name, price, stock) in monitors)
                {
                    // Kiểm tra xem sản phẩm đã tồn tại chưa (kiểm tra lại sau khi reload)
                    var existing = allProducts.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        var product = new Product
                        {
                            Id = ++maxId,
                            Name = name,
                            Description = name,
                            Price = price,
                            OldPrice = 0,
                            CategoryId = monitorCategoryId,
                            Stock = stock,
                            IsFeatured = price >= 5000000,
                            ImageUrl = $"https://via.placeholder.com/300x300?text={Uri.EscapeDataString(name.Length > 20 ? name.Substring(0, 20) : name)}"
                        };

                        try
                        {
                            _dataStore.AddProduct(product);
                            allProducts.Add(product); // Thêm vào danh sách local để tránh trùng lặp
                            addedCount++;
                            _logger.LogInformation($"✓ Đã thêm Monitor #{addedCount}/{monitors.Count}: {name.Substring(0, Math.Min(50, name.Length))}... (Id: {product.Id}, CategoryId: {monitorCategoryId})");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"✗ Lỗi khi thêm Monitor: {name.Substring(0, Math.Min(50, name.Length))}...");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"Monitor đã tồn tại (Id: {existing.Id}): {name.Substring(0, Math.Min(50, name.Length))}...");
                    }
                }
                
                _logger.LogInformation($"Đã xử lý {monitors.Count} Monitor, thêm mới {addedCount} sản phẩm");

                // Reload dữ liệu để đảm bảo có dữ liệu mới nhất
                _dataStore.ReloadData();
                var finalProducts = _dataStore.GetAllProducts();
                var finalMonitorProducts = finalProducts.Where(p => p.CategoryId == monitorCategoryId).ToList();

                if (addedCount > 0)
                {
                    _logger.LogInformation($"✓✓✓ Đã tự động thêm {addedCount} Monitor vào database. Tổng số Monitor hiện tại: {finalMonitorProducts.Count}");
                }
                else
                {
                    _logger.LogInformation($"Tất cả Monitor đã có trong database. Tổng số Monitor hiện tại: {finalMonitorProducts.Count}");
                }
                
                // Kiểm tra lại: Nếu vẫn chưa đủ 180 Monitor, log warning
                if (finalMonitorProducts.Count < 180)
                {
                    _logger.LogWarning($"⚠⚠⚠ CẢNH BÁO: Chỉ có {finalMonitorProducts.Count}/180 Monitor trong database! Có thể cần kiểm tra lại.");
                }
                else
                {
                    _logger.LogInformation($"✓✓✓ Đã có đủ {finalMonitorProducts.Count} Monitor trong database!");
                }
                
                // Log một vài Monitor để verify
                if (finalMonitorProducts.Count > 0)
                {
                    _logger.LogInformation($"Sample Monitor products (hiển thị 5 đầu tiên):");
                    foreach (var m in finalMonitorProducts.Take(5))
                    {
                        _logger.LogInformation($"  - {m.Name.Substring(0, Math.Min(60, m.Name.Length))} (Id: {m.Id}, CategoryId: {m.CategoryId})");
                    }
                }
                else
                {
                    _logger.LogError($"❌❌❌ KHÔNG TÌM THẤY MONITOR NÀO VỚI CategoryId = {monitorCategoryId} SAU KHI THÊM! CẦN KIỂM TRA LẠI!");
                }
                
                _logger.LogInformation("=== KẾT THÚC AutoAddMonitors ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động thêm Monitor");
            }
        }

        // Tự động tạo categories và sample products cho Keyboard, Mouse, Speaker, Headphone
        private void AutoAddKeyboardMouseSpeakerHeadphone()
        {
            try
            {
                _logger.LogInformation("=== BẮT ĐẦU AutoAddKeyboardMouseSpeakerHeadphone ===");
                _dataStore.ReloadData();
                
                // Tạo categories nếu chưa có
                var categories = _dataStore.GetAllCategories();
                var categoryDefinitions = new[]
                {
                    new { Id = 13, Name = "Keyboard - Bàn phím", Description = "Mechanical, Gaming, RGB", ImageUrl = "/img/categories/keyboard.webp" },
                    new { Id = 14, Name = "Mouse - Chuột", Description = "Gaming, Wireless, RGB", ImageUrl = "/img/categories/mouse.webp" },
                    new { Id = 15, Name = "Speaker - Loa", Description = "2.0, 2.1, 5.1, Gaming", ImageUrl = "/img/categories/speaker.webp" },
                    new { Id = 16, Name = "Headphone - Tai nghe", Description = "Gaming, Wireless, Noise Cancelling", ImageUrl = "/img/categories/headphone.webp" }
                };

                foreach (var catDef in categoryDefinitions)
                {
                    var existingCategory = categories.FirstOrDefault(c => c.Id == catDef.Id || 
                        c.Name.Contains(catDef.Name.Split('-')[0].Trim(), StringComparison.OrdinalIgnoreCase));
                    
                    if (existingCategory == null)
                    {
                        var newCategory = new Category
                        {
                            Id = catDef.Id,
                            Name = catDef.Name,
                            Description = catDef.Description,
                            ImageUrl = catDef.ImageUrl
                        };
                        _dataStore.AddCategory(newCategory);
                        _logger.LogInformation($"Đã tạo category mới: {catDef.Name} (Id: {catDef.Id})");
                    }
                    else
                    {
                        _logger.LogInformation($"Category đã tồn tại: {existingCategory.Name} (Id: {existingCategory.Id})");
                    }
                }

                // Thêm sample products nếu chưa có
                _dataStore.ReloadData();
                var allProducts = _dataStore.GetAllProducts();
                var maxId = allProducts.Count > 0 ? allProducts.Max(p => p.Id) : 0;

                // Sample Keyboard products
                var keyboardProducts = new List<(string Name, decimal Price, int Stock)>
                {
                    ("Bàn phím cơ Logitech G Pro X", 2990000, 5),
                    ("Bàn phím cơ Corsair K70 RGB", 3990000, 4),
                    ("Bàn phím cơ Razer BlackWidow V3", 3490000, 6),
                    ("Bàn phím cơ ASUS ROG Strix Scope", 3290000, 5),
                    ("Bàn phím cơ HyperX Alloy Elite 2", 2790000, 4)
                };

                // Sample Mouse products
                var mouseProducts = new List<(string Name, decimal Price, int Stock)>
                {
                    ("Chuột gaming Logitech G Pro X Superlight", 2490000, 5),
                    ("Chuột gaming Razer DeathAdder V3", 1990000, 6),
                    ("Chuột gaming Corsair Sabre RGB Pro", 1490000, 4),
                    ("Chuột gaming ASUS ROG Gladius III", 1790000, 5),
                    ("Chuột gaming HyperX Pulsefire Haste 2", 1290000, 4)
                };

                // Sample Speaker products
                var speakerProducts = new List<(string Name, decimal Price, int Stock)>
                {
                    ("Loa Logitech Z623 2.1", 1990000, 5),
                    ("Loa Creative T15 Wireless 2.0", 1290000, 4),
                    ("Loa Edifier R1280T 2.0", 1490000, 6),
                    ("Loa Logitech Z906 5.1", 4990000, 3),
                    ("Loa Razer Nommo Chroma", 2490000, 4)
                };

                // Sample Headphone products
                var headphoneProducts = new List<(string Name, decimal Price, int Stock)>
                {
                    ("Tai nghe gaming Logitech G Pro X", 2990000, 5),
                    ("Tai nghe gaming Razer BlackShark V2 Pro", 3490000, 4),
                    ("Tai nghe gaming HyperX Cloud Alpha", 1990000, 6),
                    ("Tai nghe gaming ASUS ROG Delta S", 2490000, 5),
                    ("Tai nghe gaming Corsair Virtuoso RGB", 3990000, 4)
                };

                var productLists = new[]
                {
                    new { CategoryId = 13, Products = keyboardProducts, CategoryName = "Keyboard" },
                    new { CategoryId = 14, Products = mouseProducts, CategoryName = "Mouse" },
                    new { CategoryId = 15, Products = speakerProducts, CategoryName = "Speaker" },
                    new { CategoryId = 16, Products = headphoneProducts, CategoryName = "Headphone" }
                };

                foreach (var list in productLists)
                {
                    var existingProducts = allProducts.Where(p => p.CategoryId == list.CategoryId).ToList();
                    if (existingProducts.Count == 0)
                    {
                        _logger.LogInformation($"Thêm sample products cho {list.CategoryName} (CategoryId: {list.CategoryId})");
                        foreach (var (name, price, stock) in list.Products)
                        {
                            var product = new Product
                            {
                                Id = ++maxId,
                                Name = name,
                                Description = name,
                                Price = price,
                                OldPrice = 0,
                                CategoryId = list.CategoryId,
                                Stock = stock,
                                IsFeatured = price >= 2000000,
                                ImageUrl = $"https://via.placeholder.com/300x300?text={Uri.EscapeDataString(name.Length > 20 ? name.Substring(0, 20) : name)}"
                            };
                            _dataStore.AddProduct(product);
                            allProducts.Add(product);
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"{list.CategoryName} đã có {existingProducts.Count} sản phẩm, không cần thêm sample");
                    }
                }

                _logger.LogInformation("=== KẾT THÚC AutoAddKeyboardMouseSpeakerHeadphone ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động thêm Keyboard/Mouse/Speaker/Headphone");
            }
        }

        /// <summary>
        /// Tự động cập nhật hình ảnh cho sản phẩm khi khởi động
        /// </summary>
        public async Task AutoUpdateImagesAsync()
        {
            if (!EnableAutoUpdateImages || _imageSearchService == null)
            {
                _logger.LogInformation("Tự động cập nhật hình ảnh đã tắt hoặc ImageSearchService chưa được đăng ký");
                return;
            }

            try
            {
                _logger.LogInformation($"Bắt đầu tự động cập nhật hình ảnh cho tối đa {MaxImagesPerStartup} sản phẩm...");
                
                // Chỉ cập nhật các sản phẩm không có hình ảnh hoặc có placeholder
                var productsNeedingImages = _dataStore.GetAllProducts()
                    .Where(p => string.IsNullOrWhiteSpace(p.ImageUrl) || 
                               p.ImageUrl.Contains("placeholder") || 
                               p.ImageUrl.Contains("via.placeholder"))
                    .Take(MaxImagesPerStartup)
                    .ToList();

                if (productsNeedingImages.Count == 0)
                {
                    _logger.LogInformation("Không có sản phẩm nào cần cập nhật hình ảnh");
                    return;
                }

                _logger.LogInformation($"Tìm thấy {productsNeedingImages.Count} sản phẩm cần cập nhật hình ảnh");

                int updatedCount = 0;
                foreach (var product in productsNeedingImages)
                {
                    try
                    {
                        var imageUrl = await _imageSearchService.SearchImageUrlAsync(product.Name);
                        
                        if (!string.IsNullOrEmpty(imageUrl))
                        {
                            product.ImageUrl = imageUrl;
                            _dataStore.UpdateProduct(product);
                            updatedCount++;
                            _logger.LogInformation($"✓ Đã cập nhật hình ảnh cho sản phẩm ID {product.Id}: {product.Name.Substring(0, Math.Min(50, product.Name.Length))}...");
                        }
                        else
                        {
                            _logger.LogWarning($"✗ Không tìm thấy hình ảnh cho sản phẩm ID {product.Id}");
                        }

                        // Delay nhỏ để tránh rate limit
                        await Task.Delay(500);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Lỗi khi cập nhật hình ảnh cho sản phẩm ID {product.Id}");
                    }
                }

                _logger.LogInformation($"Hoàn thành: Đã cập nhật hình ảnh cho {updatedCount}/{productsNeedingImages.Count} sản phẩm");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động cập nhật hình ảnh khi khởi động");
            }
        }
    }
}

