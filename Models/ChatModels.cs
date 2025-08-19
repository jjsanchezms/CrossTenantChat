namespace CrossTenantChat.Models
{
    public class ChatUser
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string TenantName { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public DateTime TokenExpiry { get; set; }
        public bool IsFromFabrikam { get; set; }
        public string AcsUserId { get; set; } = string.Empty;
        public string AcsAccessToken { get; set; } = string.Empty;
    }

    public class ChatMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ThreadId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string SenderId { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string SenderTenant { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public MessageType Type { get; set; } = MessageType.Text;
    }

    public enum MessageType
    {
        Text,
        System,
        CrossTenantInfo
    }

    public class ChatThread
    {
        public string Id { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public List<ChatUser> Participants { get; set; } = new();
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = string.Empty;
        public bool IsCrossTenant { get; set; }
    }

    public class TokenExchangeResult
    {
        public bool IsSuccess { get; set; }
        public string AccessToken { get; set; } = string.Empty;
        public string AcsUserId { get; set; } = string.Empty;
        public DateTime ExpiresOn { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public Dictionary<string, object> Claims { get; set; } = new();
    }

    public class CrossTenantAuthenticationFlow
    {
        public string FlowId { get; set; } = Guid.NewGuid().ToString();
        public string SourceTenant { get; set; } = string.Empty;
        public string TargetTenant { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public List<AuthenticationStep> Steps { get; set; } = new();
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsSuccessful { get; set; }
    }

    public class AuthenticationStep
    {
        public string StepName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool IsSuccessful { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
