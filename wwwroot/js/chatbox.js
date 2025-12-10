// Chatbox functionality
(function() {
    'use strict';

    let chatboxOpen = false;
    let chatHistory = JSON.parse(localStorage.getItem('chatHistory') || '[]');

    // Initialize chatbox
    function initChatbox() {
        const chatboxToggle = document.getElementById('chatboxToggle');
        const chatboxContainer = document.getElementById('chatboxContainer');
        const chatboxClose = document.getElementById('chatboxClose');
        const chatboxSendBtn = document.getElementById('chatboxSendBtn');
        const chatboxInput = document.getElementById('chatboxInput');
        const chatboxMessages = document.getElementById('chatboxMessages');

        if (!chatboxToggle || !chatboxContainer) return;

        // Toggle chatbox
        chatboxToggle.addEventListener('click', function() {
            toggleChatbox();
        });

        if (chatboxClose) {
            chatboxClose.addEventListener('click', function() {
                toggleChatbox();
            });
        }

        // Send message
        if (chatboxSendBtn && chatboxInput) {
            chatboxSendBtn.addEventListener('click', function() {
                sendMessage();
            });

            chatboxInput.addEventListener('keypress', function(e) {
                if (e.key === 'Enter') {
                    sendMessage();
                }
            });
        }

        // Load chat history
        loadChatHistory();
    }

    // Toggle chatbox
    function toggleChatbox() {
        const chatboxContainer = document.getElementById('chatboxContainer');
        if (!chatboxContainer) return;

        chatboxOpen = !chatboxOpen;
        
        if (chatboxOpen) {
            chatboxContainer.classList.add('show');
            document.getElementById('chatboxInput')?.focus();
        } else {
            chatboxContainer.classList.remove('show');
        }
    }

    // Send message
    async function sendMessage() {
        const input = document.getElementById('chatboxInput');
        const messagesContainer = document.getElementById('chatboxMessages');
        
        if (!input || !messagesContainer) return;

        const message = input.value.trim();
        if (!message) return;

        // Add user message
        addMessage('user', message);
        input.value = '';
        input.disabled = true;
        
        // Show typing indicator
        showTypingIndicator();

        try {
            // Gọi AI API
            const response = await fetch('/api/Chat/message', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({
                    message: message,
                    history: chatHistory.slice(-10).map(item => ({
                        role: item.sender === 'user' ? 'user' : 'assistant',
                        content: item.text,
                        timestamp: item.timestamp
                    })) // Chỉ gửi 10 tin nhắn gần nhất làm context
                })
            });

            const data = await response.json();
            
            // Remove typing indicator
            removeTypingIndicator();
            
            if (data.success) {
                addMessage('bot', data.message);
            } else {
                addMessage('bot', 'Xin lỗi, có lỗi xảy ra. Vui lòng thử lại sau hoặc liên hệ hotline: 1900-xxxx');
            }
        } catch (error) {
            console.error('Error:', error);
            removeTypingIndicator();
            // Fallback to local response
            const botResponse = generateBotResponse(message);
            addMessage('bot', botResponse);
        } finally {
            input.disabled = false;
            input.focus();
        }
    }

    // Add message to chat
    function addMessage(sender, text) {
        const messagesContainer = document.getElementById('chatboxMessages');
        if (!messagesContainer) return;

        const messageDiv = document.createElement('div');
        messageDiv.className = `chat-message ${sender === 'user' ? 'user-message' : 'bot-message'}`;
        
        const messageContent = document.createElement('div');
        messageContent.className = 'message-content';
        // Support line breaks
        messageContent.innerHTML = text.replace(/\n/g, '<br>');
        
        const messageTime = document.createElement('div');
        messageTime.className = 'message-time';
        messageTime.textContent = new Date().toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
        
        messageDiv.appendChild(messageContent);
        messageDiv.appendChild(messageTime);
        messagesContainer.appendChild(messageDiv);

        // Scroll to bottom
        messagesContainer.scrollTop = messagesContainer.scrollHeight;

        // Save to history
        chatHistory.push({
            sender: sender,
            text: text,
            timestamp: new Date().toISOString()
        });
        // Keep only last 50 messages
        if (chatHistory.length > 50) {
            chatHistory = chatHistory.slice(-50);
        }
        localStorage.setItem('chatHistory', JSON.stringify(chatHistory));
    }

    // Show typing indicator
    function showTypingIndicator() {
        const messagesContainer = document.getElementById('chatboxMessages');
        if (!messagesContainer) return;

        const typingDiv = document.createElement('div');
        typingDiv.id = 'typingIndicator';
        typingDiv.className = 'chat-message bot-message';
        
        const typingContent = document.createElement('div');
        typingContent.className = 'message-content typing-indicator';
        typingContent.innerHTML = '<span></span><span></span><span></span>';
        
        typingDiv.appendChild(typingContent);
        messagesContainer.appendChild(typingDiv);
        messagesContainer.scrollTop = messagesContainer.scrollHeight;
    }

    // Remove typing indicator
    function removeTypingIndicator() {
        const typingIndicator = document.getElementById('typingIndicator');
        if (typingIndicator) {
            typingIndicator.remove();
        }
    }

    // Generate bot response (có thể tích hợp với Google Chat API hoặc AI sau)
    function generateBotResponse(userMessage) {
        const message = userMessage.toLowerCase();
        
        // Greetings
        if (message.includes('xin chào') || message.includes('hello') || message.includes('chào')) {
            return 'Xin chào! Tôi có thể giúp gì cho bạn? Bạn có thể hỏi về sản phẩm, giá cả, hoặc đặt hàng.';
        }
        
        // Product questions
        if (message.includes('sản phẩm') || message.includes('máy tính') || message.includes('pc')) {
            return 'Chúng tôi có đầy đủ các linh kiện PC như CPU, Mainboard, RAM, GPU, PSU, SSD, HDD, Case, Monitor và nhiều sản phẩm khác. Bạn muốn tìm hiểu về sản phẩm nào?';
        }
        
        // Price questions
        if (message.includes('giá') || message.includes('bao nhiêu') || message.includes('price')) {
            return 'Bạn có thể xem giá chi tiết của từng sản phẩm trên website. Hoặc bạn có thể cho tôi biết sản phẩm cụ thể bạn quan tâm, tôi sẽ cung cấp thông tin chi tiết.';
        }
        
        // Order questions
        if (message.includes('đặt hàng') || message.includes('mua') || message.includes('order')) {
            return 'Bạn có thể thêm sản phẩm vào giỏ hàng và tiến hành thanh toán. Nếu cần hỗ trợ, vui lòng liên hệ hotline: 1900-xxxx hoặc email: support@pcstore.vn';
        }
        
        // Contact questions
        if (message.includes('liên hệ') || message.includes('contact') || message.includes('hotline')) {
            return 'Bạn có thể liên hệ với chúng tôi qua:\n- Hotline: 1900-xxxx\n- Email: support@pcstore.vn\n- Địa chỉ: [Địa chỉ cửa hàng]\n- Giờ làm việc: 8:00 - 22:00 hàng ngày';
        }
        
        // Build PC questions
        if (message.includes('build') || message.includes('cấu hình') || message.includes('xây dựng')) {
            return 'Bạn có thể sử dụng tính năng "Xây Dựng Cấu Hình" trên website để tự chọn các linh kiện phù hợp. Hoặc bạn có thể mô tả nhu cầu của mình, tôi sẽ tư vấn cấu hình phù hợp.';
        }
        
        // Warranty questions
        if (message.includes('bảo hành') || message.includes('warranty') || message.includes('đổi trả')) {
            return 'Tất cả sản phẩm của chúng tôi đều có bảo hành chính hãng. Thời gian bảo hành tùy thuộc vào từng sản phẩm. Bạn có thể xem chi tiết trong thông tin sản phẩm hoặc liên hệ để được tư vấn cụ thể.';
        }
        
        // Default response
        return 'Cảm ơn bạn đã liên hệ! Tôi có thể giúp bạn về:\n- Thông tin sản phẩm\n- Giá cả\n- Đặt hàng\n- Tư vấn cấu hình PC\n- Bảo hành\n\nBạn muốn biết thêm thông tin gì?';
    }

    // Load chat history
    function loadChatHistory() {
        const messagesContainer = document.getElementById('chatboxMessages');
        if (!messagesContainer || chatHistory.length === 0) {
            // Show welcome message
            addMessage('bot', 'Xin chào! 👋 Tôi có thể giúp gì cho bạn? Bạn có thể hỏi về sản phẩm, giá cả, hoặc đặt hàng.');
            return;
        }

        chatHistory.forEach(item => {
            const messageDiv = document.createElement('div');
            messageDiv.className = `chat-message ${item.sender === 'user' ? 'user-message' : 'bot-message'}`;
            
            const messageContent = document.createElement('div');
            messageContent.className = 'message-content';
            messageContent.textContent = item.text;
            
            const messageTime = document.createElement('div');
            messageTime.className = 'message-time';
            const date = new Date(item.timestamp);
            messageTime.textContent = date.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
            
            messageDiv.appendChild(messageContent);
            messageDiv.appendChild(messageTime);
            messagesContainer.appendChild(messageDiv);
        });

        messagesContainer.scrollTop = messagesContainer.scrollHeight;
    }

    // Clear chat history
    function clearChatHistory() {
        chatHistory = [];
        localStorage.removeItem('chatHistory');
        const messagesContainer = document.getElementById('chatboxMessages');
        if (messagesContainer) {
            messagesContainer.innerHTML = '';
            addMessage('bot', 'Xin chào! 👋 Tôi có thể giúp gì cho bạn?');
        }
    }

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initChatbox);
    } else {
        initChatbox();
    }

    // Export functions for external use
    window.toggleChatbox = toggleChatbox;
    window.clearChatHistory = clearChatHistory;
})();

