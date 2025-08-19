using Azure.Communication.Identity;
using CrossTenantChat.Configuration;
using CrossTenantChat.Models;
using Microsoft.Extensions.Options;

namespace CrossTenantChat.Services
{
    public interface IAzureCommunicationService
    {
        Task<TokenExchangeResult> ExchangeEntraIdTokenForAcsTokenAsync(ChatUser user);
        Task<ChatThread> CreateChatThreadAsync(string topic, ChatUser creator);
        Task<bool> AddParticipantToChatAsync(string threadId, ChatUser participant);
        Task<bool> SendMessageAsync(string threadId, string message, ChatUser sender);
        Task<List<Models.ChatMessage>> GetMessagesAsync(string threadId);
        Task<List<ChatThread>> GetUserChatThreadsAsync(ChatUser user);
    }

    public class AzureCommunicationService : IAzureCommunicationService
    {
        private readonly AzureConfiguration _azureConfig;
        private readonly ILogger<AzureCommunicationService> _logger;
        private readonly CommunicationIdentityClient? _identityClient;
        
        // In-memory storage for demo purposes
        private readonly Dictionary<string, ChatThread> _chatThreads;
        private readonly Dictionary<string, List<Models.ChatMessage>> _threadMessages;
        private readonly Dictionary<string, List<string>> _userThreads;

        public AzureCommunicationService(
            IOptions<AzureConfiguration> azureConfig,
            ILogger<AzureCommunicationService> logger)
        {
            _azureConfig = azureConfig.Value;
            _logger = logger;
            _chatThreads = new Dictionary<string, ChatThread>();
            _threadMessages = new Dictionary<string, List<Models.ChatMessage>>();
            _userThreads = new Dictionary<string, List<string>>();

            // Initialize ACS client if connection string is provided
            if (!string.IsNullOrEmpty(_azureConfig.AzureCommunicationServices.ConnectionString))
            {
                try
                {
                    _identityClient = new CommunicationIdentityClient(_azureConfig.AzureCommunicationServices.ConnectionString);
                    _logger.LogInformation("Azure Communication Services client initialized successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not initialize ACS client, running in demo mode");
                }
            }
            else
            {
                _logger.LogInformation("No ACS connection string provided, running in demo mode");
            }
        }

        public async Task<TokenExchangeResult> ExchangeEntraIdTokenForAcsTokenAsync(ChatUser user)
        {
            var result = new TokenExchangeResult();

            try
            {
                _logger.LogInformation("🔄 Starting Entra ID to ACS token exchange");
                _logger.LogInformation("📧 User: {UserEmail} from {TenantName} tenant", user.Email, user.TenantName);
                _logger.LogInformation("🆔 Entra ID: {UserId} | Tenant ID: {TenantId}", user.Id, user.TenantId);

                if (_identityClient != null)
                {
                    // Real ACS integration
                    var identityResponse = await _identityClient.CreateUserAsync();
                    var tokenResponse = await _identityClient.GetTokenAsync(identityResponse.Value, new[] { CommunicationTokenScope.Chat });

                    result.IsSuccess = true;
                    result.AccessToken = tokenResponse.Value.Token;
                    result.AcsUserId = identityResponse.Value.Id;
                    result.ExpiresOn = tokenResponse.Value.ExpiresOn.DateTime;

                    _logger.LogInformation("✅ Real ACS token generated");
                    _logger.LogInformation("🎯 ACS User ID: {AcsUserId}", result.AcsUserId);
                }
                else
                {
                    // Demo mode - simulate token exchange
                    result.IsSuccess = true;
                    result.AccessToken = $"demo_acs_token_{Guid.NewGuid():N}";
                    result.AcsUserId = $"8:acs:demo-{user.Id.Substring(0, 8)}";
                    result.ExpiresOn = DateTime.UtcNow.AddHours(24);

                    _logger.LogInformation("✅ Demo ACS token generated");
                    _logger.LogInformation("🎯 Demo ACS User ID: {AcsUserId}", result.AcsUserId);
                }

                // Update user with ACS information
                user.AcsUserId = result.AcsUserId;
                user.AcsAccessToken = result.AccessToken;

                // Log cross-tenant flow details
                if (user.IsFromFabrikam)
                {
                    _logger.LogInformation("🌐 CROSS-TENANT SUCCESS: Fabrikam user authenticated to Contoso ACS");
                    _logger.LogInformation("📊 Flow: {FabrikamTenant} → {ContosoTenant}", 
                        "Fabrikam Corp", "Contoso Ltd (ACS Host)");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during token exchange for user: {UserEmail}", user.Email);
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public async Task<ChatThread> CreateChatThreadAsync(string topic, ChatUser creator)
        {
            try
            {
                _logger.LogInformation("💬 Creating chat thread: '{Topic}' by {UserName}", topic, creator.Name);

                var threadId = $"thread_{Guid.NewGuid():N}";
                var chatThread = new ChatThread
                {
                    Id = threadId,
                    Topic = topic,
                    CreatedBy = creator.Id,
                    CreatedOn = DateTime.UtcNow,
                    Participants = new List<ChatUser> { creator },
                    IsCrossTenant = creator.IsFromFabrikam
                };

                _chatThreads[threadId] = chatThread;
                _threadMessages[threadId] = new List<Models.ChatMessage>();
                
                // Track user threads
                if (!_userThreads.ContainsKey(creator.Id))
                {
                    _userThreads[creator.Id] = new List<string>();
                }
                _userThreads[creator.Id].Add(threadId);

                _logger.LogInformation("✅ Chat thread created: {ThreadId}", threadId);
                
                // Send welcome messages
                await SendSystemMessageAsync(threadId, 
                    $"💬 Chat thread '{topic}' created by {creator.Name} ({creator.TenantName})");

                if (creator.IsFromFabrikam)
                {
                    await SendSystemMessageAsync(threadId, 
                        "🌐 Cross-tenant chat enabled! Fabrikam user connected to Contoso ACS resources");
                    
                    _logger.LogInformation("🎉 CROSS-TENANT THREAD: Fabrikam user created thread in Contoso ACS");
                }

                await Task.Delay(50); // Small delay for demo effect

                return chatThread;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating chat thread: {Topic}", topic);
                throw;
            }
        }

        public async Task<bool> AddParticipantToChatAsync(string threadId, ChatUser participant)
        {
            try
            {
                _logger.LogInformation("➕ Adding participant {UserName} ({TenantName}) to thread {ThreadId}", 
                    participant.Name, participant.TenantName, threadId);

                if (!_chatThreads.ContainsKey(threadId))
                {
                    _logger.LogWarning("⚠️ Chat thread not found: {ThreadId}", threadId);
                    return false;
                }

                var thread = _chatThreads[threadId];
                thread.Participants.Add(participant);

                // Track user threads
                if (!_userThreads.ContainsKey(participant.Id))
                {
                    _userThreads[participant.Id] = new List<string>();
                }
                _userThreads[participant.Id].Add(threadId);

                _logger.LogInformation("✅ Participant added successfully");

                // Send system message about new participant
                var crossTenantInfo = participant.IsFromFabrikam ? " (Cross-tenant user from Fabrikam)" : "";
                await SendSystemMessageAsync(threadId, 
                    $"👋 {participant.Name} from {participant.TenantName} joined the chat{crossTenantInfo}");

                if (participant.IsFromFabrikam || thread.IsCrossTenant)
                {
                    _logger.LogInformation("🌐 CROSS-TENANT PARTICIPANT: Multi-tenant chat now active");
                    thread.IsCrossTenant = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error adding participant to thread {ThreadId}", threadId);
                return false;
            }
        }

        public async Task<bool> SendMessageAsync(string threadId, string message, ChatUser sender)
        {
            try
            {
                if (!_threadMessages.ContainsKey(threadId))
                {
                    _logger.LogWarning("⚠️ Thread not found: {ThreadId}", threadId);
                    return false;
                }

                var chatMessage = new Models.ChatMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    ThreadId = threadId,
                    Content = message,
                    SenderId = sender.AcsUserId,
                    SenderName = sender.Name,
                    SenderTenant = sender.TenantName,
                    Timestamp = DateTime.UtcNow,
                    Type = MessageType.Text
                };

                _threadMessages[threadId].Add(chatMessage);

                var crossTenantIndicator = sender.IsFromFabrikam ? "🌐" : "🏢";
                _logger.LogInformation("{Indicator} Message from {SenderName} ({TenantName}): {Message}", 
                    crossTenantIndicator, sender.Name, sender.TenantName, message.Substring(0, Math.Min(50, message.Length)));

                if (sender.IsFromFabrikam)
                {
                    _logger.LogInformation("🔄 CROSS-TENANT MESSAGE: Fabrikam user sent message via Contoso ACS");
                }

                await Task.Delay(20); // Small delay for demo effect
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error sending message from {UserEmail}", sender.Email);
                return false;
            }
        }

        public async Task<List<Models.ChatMessage>> GetMessagesAsync(string threadId)
        {
            try
            {
                if (!_threadMessages.ContainsKey(threadId))
                {
                    return new List<Models.ChatMessage>();
                }

                var messages = _threadMessages[threadId].OrderBy(m => m.Timestamp).ToList();
                
                // Add cross-tenant indicators to messages
                foreach (var message in messages)
                {
                    if (message.Type == MessageType.Text && message.SenderTenant == "Fabrikam")
                    {
                        message.Content = $"🌐 {message.Content}";
                    }
                }

                await Task.Delay(10); // Small delay for demo effect
                return messages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting messages from thread {ThreadId}", threadId);
                return new List<Models.ChatMessage>();
            }
        }

        public async Task<List<ChatThread>> GetUserChatThreadsAsync(ChatUser user)
        {
            try
            {
                var userThreads = new List<ChatThread>();

                if (_userThreads.ContainsKey(user.Id))
                {
                    var threadIds = _userThreads[user.Id];
                    foreach (var threadId in threadIds)
                    {
                        if (_chatThreads.ContainsKey(threadId))
                        {
                            userThreads.Add(_chatThreads[threadId]);
                        }
                    }
                }

                await Task.Delay(10); // Small delay for demo effect
                return userThreads.OrderByDescending(t => t.CreatedOn).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting chat threads for user {UserEmail}", user.Email);
                return new List<ChatThread>();
            }
        }

        private async Task<bool> SendSystemMessageAsync(string threadId, string message)
        {
            try
            {
                if (!_threadMessages.ContainsKey(threadId))
                {
                    return false;
                }

                var systemMessage = new Models.ChatMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    ThreadId = threadId,
                    Content = message,
                    SenderId = "system",
                    SenderName = "System",
                    SenderTenant = "System",
                    Timestamp = DateTime.UtcNow,
                    Type = MessageType.System
                };

                _threadMessages[threadId].Add(systemMessage);
                
                _logger.LogInformation("📢 System message: {Message}", message);
                
                await Task.Delay(10); // Small delay for demo effect
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error sending system message");
                return false;
            }
        }
    }
}
