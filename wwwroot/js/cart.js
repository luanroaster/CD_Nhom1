// Cart management functions
(function() {
    'use strict';

    // Load cart count on page load
    document.addEventListener('DOMContentLoaded', function() {
        updateCartBadge();
        
        // Sync localStorage cart to session (nếu có)
        syncLocalStorageToSession();
    });

    // Update cart badge
    function updateCartBadge() {
        fetch('/Cart/GetCartCount')
            .then(response => response.json())
            .then(data => {
                const badge = document.getElementById('cartBadge');
                if (badge) {
                    badge.textContent = data.totalItems || 0;
                    if (data.totalItems > 0) {
                        badge.style.display = 'inline-block';
                    } else {
                        badge.style.display = 'none';
                    }
                }
            })
            .catch(error => {
                console.error('Error updating cart badge:', error);
            });
    }

    // Sync localStorage cart to session (để tương thích với code cũ)
    function syncLocalStorageToSession() {
        const localCart = localStorage.getItem('cart');
        if (localCart) {
            try {
                const cartItems = JSON.parse(localCart);
                if (cartItems && cartItems.length > 0) {
                    // Chuyển đổi từ format localStorage sang format session
                    const sessionCart = cartItems.map(item => ({
                        ProductId: item.id,
                        Quantity: item.quantity || 1
                    }));

                    // Gửi lên server để lưu vào session
                    fetch('/Cart/SyncCart', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json',
                        },
                        body: JSON.stringify({ items: sessionCart })
                    })
                    .then(response => response.json())
                    .then(data => {
                        if (data.success) {
                            // Xóa localStorage sau khi sync
                            localStorage.removeItem('cart');
                            updateCartBadge();
                        }
                    })
                    .catch(error => {
                        console.error('Error syncing cart:', error);
                    });
                }
            } catch (e) {
                console.error('Error parsing localStorage cart:', e);
            }
        }
    }

    // Add to cart function (có thể gọi từ bất kỳ đâu)
    window.addToCart = function(productId, productName, price, quantity = 1) {
        fetch('/Cart/AddToCart', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify({
                productId: productId,
                quantity: quantity
            })
        })
        .then(response => response.json())
        .then(data => {
            if (data.needLogin) {
                // Chưa đăng nhập -> chuyển sang trang đăng nhập khách hàng
                const loginUrl = data.redirectUrl || '/Account/Login';
                showNotification('Vui lòng đăng nhập hoặc đăng ký tài khoản trước khi thêm vào giỏ hàng.', 'warning');
                setTimeout(() => {
                    window.location.href = loginUrl;
                }, 1000);
            } else if (data.success) {
                // Update badge
                updateCartBadge();
                
                // Show notification
                showNotification('Đã thêm "' + productName + '" vào giỏ hàng!', 'success');
            } else {
                showNotification('Lỗi: ' + (data.message || 'Không thể thêm vào giỏ hàng'), 'danger');
            }
        })
        .catch(error => {
            console.error('Error:', error);
            showNotification('Có lỗi xảy ra khi thêm vào giỏ hàng', 'danger');
        });
    };

    // Show notification
    function showNotification(message, type = 'success') {
        const notification = document.createElement('div');
        notification.className = `alert alert-${type} position-fixed top-0 start-50 translate-middle-x mt-3`;
        notification.style.zIndex = '9999';
        notification.style.minWidth = '300px';
        notification.style.textAlign = 'center';
        notification.innerHTML = '<i class="fas fa-' + (type === 'success' ? 'check-circle' : 'exclamation-circle') + ' me-2"></i>' + message;
        document.body.appendChild(notification);

        setTimeout(() => {
            notification.style.transition = 'opacity 0.5s';
            notification.style.opacity = '0';
            setTimeout(() => {
                notification.remove();
            }, 500);
        }, 3000);
    }

    // Export updateCartBadge for external use
    window.updateCartBadge = updateCartBadge;
})();

