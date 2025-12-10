// Quản lý cấu hình PC
let configs = {
    1: {},
    2: {},
    3: {},
    4: {},
    5: {}
};

let categories = [];
let allProducts = [];
let currentConfig = 1;

// Khởi tạo khi trang load
document.addEventListener('DOMContentLoaded', function() {
    loadCategories();
    loadProducts();
    initializeConfigTabs();
});

// Load danh mục
async function loadCategories() {
    try {
        const response = await fetch('/BuildPC/GetCategories');
        categories = await response.json();
        renderComponentSelectors();
        // Load sản phẩm sau khi đã có danh mục
        if (allProducts.length > 0) {
            loadProductsIntoSelectors();
        }
    } catch (error) {
        console.error('Lỗi khi load danh mục:', error);
    }
}

// Load tất cả sản phẩm
async function loadProducts() {
    try {
        const response = await fetch('/BuildPC/GetAllProducts');
        allProducts = await response.json();
        loadProductsIntoSelectors();
    } catch (error) {
        console.error('Lỗi khi load sản phẩm:', error);
    }
}

// Render các selector linh kiện
function renderComponentSelectors() {
    const componentTypes = [
        { id: 1, name: 'CPU - Bộ vi xử lý', icon: 'fas fa-microchip' },
        { id: 2, name: 'Main - Bo mạch chủ', icon: 'fas fa-server' },
        { id: 3, name: 'RAM - Bộ nhớ trong', icon: 'fas fa-memory' },
        { id: 4, name: 'VGA - Card Màn Hình', icon: 'fas fa-desktop' },
        { id: 5, name: 'PSU - Nguồn máy tính', icon: 'fas fa-plug' },
        { id: 6, name: 'Case - Vỏ máy tính', icon: 'fas fa-box' },
        { id: 7, name: 'SSD - Ổ cứng SSD', icon: 'fas fa-hdd' },
        { id: 8, name: 'HDD - Ổ cứng HDD', icon: 'fas fa-hdd' },
        { id: 9, name: 'Monitor - Màn hình', icon: 'fas fa-tv' },
        { id: 10, name: 'Fan - Fan tản nhiệt', icon: 'fas fa-fan' },
        { id: 11, name: 'Tản Nhiệt Nước', icon: 'fas fa-water' },
        { id: 12, name: 'Tản Nhiệt Khí', icon: 'fas fa-wind' },
        { id: 13, name: 'Keyboard - Bàn phím', icon: 'fas fa-keyboard' },
        { id: 14, name: 'Mouse - Chuột', icon: 'fas fa-mouse' },
        { id: 15, name: 'Speaker - Loa', icon: 'fas fa-volume-up' },
        { id: 16, name: 'Headphone - Tai nghe', icon: 'fas fa-headphones' }
    ];

    for (let i = 1; i <= 5; i++) {
        const body = document.getElementById(`config${i}-body`);
        if (!body) continue;

        body.innerHTML = '';
        componentTypes.forEach(component => {
            const componentDiv = document.createElement('div');
            componentDiv.className = 'mb-4';
            componentDiv.innerHTML = `
                <label class="form-label fw-bold">
                    <i class="${component.icon}"></i> ${component.name}
                </label>
                <select class="form-select component-select" 
                        data-component-id="${component.id}" 
                        data-config="${i}"
                        onchange="selectComponent(${component.id}, ${i}, this.value)">
                    <option value="">-- Chọn ${component.name} --</option>
                </select>
                <div id="selected-component-${component.id}-${i}" class="mt-2"></div>
            `;
            body.appendChild(componentDiv);
        });
    }

    // Load sản phẩm vào các select
    loadProductsIntoSelectors();
}

// Load sản phẩm vào các selector
function loadProductsIntoSelectors() {
    if (allProducts.length === 0 || categories.length === 0) {
        setTimeout(loadProductsIntoSelectors, 100);
        return;
    }

    categories.forEach(category => {
        const products = allProducts.filter(p => p.categoryId === category.id);
        const selects = document.querySelectorAll(`select[data-component-id="${category.id}"]`);
        
        selects.forEach(select => {
            // Xóa các option cũ (trừ option đầu tiên)
            while (select.options.length > 1) {
                select.remove(1);
            }
            
            products.forEach(product => {
                const option = document.createElement('option');
                option.value = product.id;
                option.textContent = `${product.name} - ${formatPrice(product.price)}`;
                option.dataset.product = JSON.stringify(product);
                select.appendChild(option);
            });
        });
    });
}

// Chọn linh kiện
function selectComponent(componentId, configId, productId) {
    if (!productId) {
        delete configs[configId][componentId];
        updateConfigDisplay(configId);
        updateTotalCost();
        return;
    }

    const select = document.querySelector(`select[data-component-id="${componentId}"][data-config="${configId}"]`);
    const option = select.options[select.selectedIndex];
    const product = JSON.parse(option.dataset.product);

    configs[configId][componentId] = product;
    updateConfigDisplay(configId);
    updateTotalCost();
}

// Cập nhật hiển thị cấu hình
function updateConfigDisplay(configId) {
    const config = configs[configId];
    let html = '<div class="selected-components">';
    
    if (Object.keys(config).length === 0) {
        html += '<small class="text-muted">Chưa có linh kiện nào được chọn</small>';
    } else {
        Object.keys(config).forEach(componentId => {
            const product = config[componentId];
            const category = categories.find(c => c.id == componentId);
            html += `
                <div class="alert alert-info d-flex justify-content-between align-items-center mb-2">
                    <div>
                        <strong>${category?.name || 'Linh kiện'}:</strong> ${product.name}<br>
                        <small>Giá: ${formatPrice(product.price)}</small>
                    </div>
                    <button class="btn btn-sm btn-danger" onclick="removeComponent(${componentId}, ${configId})">
                        <i class="fas fa-times"></i>
                    </button>
                </div>
            `;
        });
    }
    
    html += '</div>';
    const summaryDiv = document.getElementById('configSummary');
    if (summaryDiv) {
        summaryDiv.innerHTML = html;
    }
}

// Xóa linh kiện
function removeComponent(componentId, configId) {
    delete configs[configId][componentId];
    const select = document.querySelector(`select[data-component-id="${componentId}"][data-config="${configId}"]`);
    if (select) {
        select.value = '';
    }
    updateConfigDisplay(configId);
    updateTotalCost();
}

// Tính tổng chi phí
function updateTotalCost() {
    const config = configs[currentConfig];
    let total = 0;
    
    Object.values(config).forEach(product => {
        total += product.price || 0;
    });
    
    const totalDiv = document.getElementById('totalCost');
    if (totalDiv) {
        totalDiv.innerHTML = `<h3 class="text-danger fw-bold">${formatPrice(total)}</h3>`;
    }
}

// Format giá tiền
function formatPrice(price) {
    return new Intl.NumberFormat('vi-VN').format(price) + ' đ';
}

// Khởi tạo tabs
function initializeConfigTabs() {
    const tabButtons = document.querySelectorAll('#configTabs button[data-bs-toggle="tab"]');
    tabButtons.forEach(button => {
        button.addEventListener('shown.bs.tab', function(event) {
            const targetId = event.target.getAttribute('data-bs-target');
            currentConfig = parseInt(targetId.replace('#config', ''));
            updateConfigDisplay(currentConfig);
            updateTotalCost();
        });
    });
    
    // Cập nhật hiển thị ban đầu cho cấu hình 1
    updateConfigDisplay(1);
    updateTotalCost();
}

// Làm mới cấu hình
function clearConfig() {
    const modal = new bootstrap.Modal(document.getElementById('clearModal'));
    modal.show();
}

function confirmClear() {
    configs[currentConfig] = {};
    renderComponentSelectors();
    updateConfigDisplay(currentConfig);
    updateTotalCost();
    bootstrap.Modal.getInstance(document.getElementById('clearModal')).hide();
}

// Export hình ảnh
function exportImage() {
    if (Object.keys(configs[currentConfig]).length === 0) {
        showAlert('OPPS...', 'Bạn chưa chọn sản phẩm nào');
        return;
    }

    // Tạo HTML để chuyển thành hình ảnh
    const config = configs[currentConfig];
    let html = `
        <div style="padding: 20px; font-family: Arial, sans-serif;">
            <h2 style="text-align: center; color: #0d6efd;">Cấu hình PC - Cấu hình ${currentConfig}</h2>
            <table style="width: 100%; border-collapse: collapse; margin-top: 20px;">
                <thead>
                    <tr style="background-color: #0d6efd; color: white;">
                        <th style="padding: 10px; border: 1px solid #ddd;">Linh kiện</th>
                        <th style="padding: 10px; border: 1px solid #ddd;">Tên sản phẩm</th>
                        <th style="padding: 10px; border: 1px solid #ddd;">Giá</th>
                    </tr>
                </thead>
                <tbody>
    `;

    let total = 0;
    Object.keys(config).forEach(componentId => {
        const product = config[componentId];
        const category = categories.find(c => c.id == componentId);
        total += product.price || 0;
        html += `
            <tr>
                <td style="padding: 10px; border: 1px solid #ddd;">${category?.name || 'Linh kiện'}</td>
                <td style="padding: 10px; border: 1px solid #ddd;">${product.name}</td>
                <td style="padding: 10px; border: 1px solid #ddd;">${formatPrice(product.price)}</td>
            </tr>
        `;
    });

    html += `
                </tbody>
                <tfoot>
                    <tr style="background-color: #f8f9fa; font-weight: bold;">
                        <td colspan="2" style="padding: 10px; border: 1px solid #ddd; text-align: right;">Tổng cộng:</td>
                        <td style="padding: 10px; border: 1px solid #ddd;">${formatPrice(total)}</td>
                    </tr>
                </tfoot>
            </table>
        </div>
    `;

    // Sử dụng html2canvas để chuyển HTML thành hình ảnh
    // Cần thêm thư viện html2canvas vào project
    showAlert('Thông báo', 'Chức năng này cần thư viện html2canvas. Vui lòng cài đặt thư viện này.');
}

// Export Excel
function exportExcel() {
    if (Object.keys(configs[currentConfig]).length === 0) {
        showAlert('OPPS...', 'Bạn chưa chọn sản phẩm nào');
        return;
    }

    const config = configs[currentConfig];
    let csv = 'Linh kiện,Tên sản phẩm,Giá\n';
    let total = 0;

    Object.keys(config).forEach(componentId => {
        const product = config[componentId];
        const category = categories.find(c => c.id == componentId);
        total += product.price || 0;
        csv += `"${category?.name || 'Linh kiện'}","${product.name}",${product.price}\n`;
    });

    csv += `"Tổng cộng","",${total}\n`;

    const blob = new Blob(['\ufeff' + csv], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    const url = URL.createObjectURL(blob);
    link.setAttribute('href', url);
    link.setAttribute('download', `Cau-hinh-PC-${currentConfig}.csv`);
    link.style.visibility = 'hidden';
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}

// In cấu hình
function printConfig() {
    if (Object.keys(configs[currentConfig]).length === 0) {
        showAlert('OPPS...', 'Bạn chưa chọn sản phẩm nào');
        return;
    }

    const printWindow = window.open('', '_blank');
    const config = configs[currentConfig];
    let html = `
        <!DOCTYPE html>
        <html>
        <head>
            <title>Cấu hình PC - Cấu hình ${currentConfig}</title>
            <style>
                body { font-family: Arial, sans-serif; padding: 20px; }
                table { width: 100%; border-collapse: collapse; margin-top: 20px; }
                th, td { padding: 10px; border: 1px solid #ddd; text-align: left; }
                th { background-color: #0d6efd; color: white; }
                tfoot { font-weight: bold; background-color: #f8f9fa; }
            </style>
        </head>
        <body>
            <h2 style="text-align: center;">Cấu hình PC - Cấu hình ${currentConfig}</h2>
            <table>
                <thead>
                    <tr>
                        <th>Linh kiện</th>
                        <th>Tên sản phẩm</th>
                        <th>Giá</th>
                    </tr>
                </thead>
                <tbody>
    `;

    let total = 0;
    Object.keys(config).forEach(componentId => {
        const product = config[componentId];
        const category = categories.find(c => c.id == componentId);
        total += product.price || 0;
        html += `
            <tr>
                <td>${category?.name || 'Linh kiện'}</td>
                <td>${product.name}</td>
                <td>${formatPrice(product.price)}</td>
            </tr>
        `;
    });

    html += `
                </tbody>
                <tfoot>
                    <tr>
                        <td colspan="2" style="text-align: right;">Tổng cộng:</td>
                        <td>${formatPrice(total)}</td>
                    </tr>
                </tfoot>
            </table>
        </body>
        </html>
    `;

    printWindow.document.write(html);
    printWindow.document.close();
    printWindow.print();
}

// Thêm vào giỏ hàng
function addToCart() {
    if (Object.keys(configs[currentConfig]).length === 0) {
        showAlert('OPPS...', 'Bạn chưa chọn sản phẩm nào');
        return;
    }

    // Lưu vào localStorage hoặc gửi lên server
    const cart = JSON.parse(localStorage.getItem('cart') || '[]');
    const config = configs[currentConfig];
    
    Object.values(config).forEach(product => {
        cart.push({
            id: product.id,
            name: product.name,
            price: product.price,
            quantity: 1
        });
    });

    localStorage.setItem('cart', JSON.stringify(cart));
    showAlert('Thành công', 'Đã thêm cấu hình vào giỏ hàng!');
}

// Hiển thị thông báo
function showAlert(title, message) {
    document.getElementById('alertMessage').innerHTML = `
        <h5>${title}</h5>
        <p>${message}</p>
    `;
    const modal = new bootstrap.Modal(document.getElementById('alertModal'));
    modal.show();
}

