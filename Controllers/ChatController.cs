using Microsoft.AspNetCore.Mvc;
using PCSTORE.Services;
using System.Text.Json;

namespace PCSTORE.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly AIChatService _aiChatService;
        private readonly ILogger<ChatController> _logger;

        public ChatController(AIChatService aiChatService, ILogger<ChatController> logger)
        {
            _aiChatService = aiChatService;
            _logger = logger;
        }

        [HttpPost("message")]
        public async Task<IActionResult> SendMessage([FromBody] ChatRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Message))
                {
                    return BadRequest(new { error = "Tin nhắn không được để trống" });
                }

                // Parse chat history từ request - hỗ trợ cả hai format (Role/Content và sender/text)
                var chatHistory = request.History?.Select(h =>
                {
                    // Hỗ trợ cả format mới (Role/Content) và format cũ (sender/text)
                    string role;
                    string content;
                    DateTime timestamp;
                    
                    if (!string.IsNullOrWhiteSpace(h.Role))
                    {
                        // Format mới: Role/Content
                        role = h.Role;
                        content = h.Content ?? string.Empty;
                    }
                    else if (!string.IsNullOrWhiteSpace(h.Content))
                    {
                        // Có thể là format khác, thử parse
                        role = "user"; // mặc định
                        content = h.Content;
                    }
                    else
                    {
                        // Không có dữ liệu hợp lệ
                        return null;
                    }
                    
                    // Parse timestamp
                    if (!string.IsNullOrWhiteSpace(h.Timestamp))
                    {
                        if (DateTime.TryParse(h.Timestamp, out var parsedTime))
                        {
                            timestamp = parsedTime;
                        }
                        else
                        {
                            timestamp = DateTime.Now;
                        }
                    }
                    else
                    {
                        timestamp = DateTime.Now;
                    }
                    
                    return new ChatMessage
                    {
                        Role = role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user",
                        Content = content,
                        Timestamp = timestamp
                    };
                })
                .Where(h => h != null)
                .ToList() ?? new List<ChatMessage>();

                // Gọi AI service
                var aiResponse = await _aiChatService.GetAIResponseAsync(request.Message, chatHistory);

                return Ok(new ChatResponse
                {
                    Success = true,
                    Message = aiResponse,
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý tin nhắn chat");
                return StatusCode(500, new { error = "Có lỗi xảy ra khi xử lý tin nhắn" });
            }
        }
    }

    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public List<ChatHistoryItem>? History { get; set; }
    }

    public class ChatHistoryItem
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
    }

    public class ChatResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
    }
}

