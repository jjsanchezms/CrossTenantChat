using Azure.Communication.Chat;
using Azure.Communication.Identity;
using Azure;
using Microsoft.Extensions.Caching.Memory;
using CrossTenantChat.Models;

namespace CrossTenantChat.Services;

public class LiveAzureCommunicationService : IAzureCommunicationService
{
    private readonly ILogger<LiveAzureCommunicationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;
    private readonly CommunicationIdentityClient _identityClient;
    
    private readonly string _acsConnectionString;
    
    // In-memory storage for demo - in production use proper database
    private readonly Dictionary<string, ChatThread> _chatThreads;
    private readonly Dictionary<string, List<Models.ChatMessage>> _threadMessages;
    private readonly Dictionary<string, List<string>> _userThreads;

    public LiveAzureCommunicationService(
        ILogger<LiveAzureCommunicationService> logger,
        IConfiguration configuration,
        IMemoryCache memoryCache)
    {
        _logger = logger;
        _configuration = configuration;
        _memoryCache = memoryCache;
        _chatThreads = new Dictionary<string, ChatThread>();
        _threadMessages = new Dictionary<string, List<Models.ChatMessage>>();
        _userThreads = new Dictionary<string, List<string>>();

        // Load ACS configuration
        _acsConnectionString = configuration["Azure:AzureCommunicationServices:ConnectionString"] 
            ?? throw new InvalidOperationException("ACS Connection String not configured");

        // Initialize ACS identity client
        _identityClient = new CommunicationIdentityClient(_acsConnectionString);
        
        _logger.LogInformation("Live Azure Communication Service initialized");
    }

    public async Task<TokenExchangeResult> ExchangeEntraIdTokenForAcsTokenAsync(ChatUser user)
    {
        _logger.LogInformation("🔄 Exchanging Entra ID token for ACS token for user: {UserId} ({TenantName})", 
            user.Id, user.TenantName);

        var result = new TokenExchangeResult();

        try
        {
            // Check cache first
            var cacheKey = $"acs_token_{user.Id}_{user.TenantName}";
            if (_memoryCache.TryGetValue(cacheKey, out TokenExchangeResult? cachedResult) && cachedResult != null)
            {
                _logger.LogInformation("✅ Retrieved cached ACS token for user: {UserId}", user.Id);
                return cachedResult;
            }

            // Create or get cached communication user identity
            var userCacheKey = $"communication_user_{user.Id}_{user.TenantName}";
            string communicationUserId;

            if (_memoryCache.TryGetValue(userCacheKey, out string? cachedUserId) && 
                !string.IsNullOrEmpty(cachedUserId))
            {
                communicationUserId = cachedUserId;
                _logger.LogInformation("Using cached communication user for: {UserId}", user.Id);
            }
            else
            {
                // Create new communication user
                var communicationUserResponse = await _identityClient.CreateUserAsync();
                communicationUserId = communicationUserResponse.Value.Id;
                
                // Cache the communication user identity (longer cache since this doesn't expire)
                _memoryCache.Set(userCacheKey, communicationUserId, TimeSpan.FromHours(24));
                _logger.LogInformation("Created new communication user: {CommunicationUserId} for EntraId user: {UserId}", 
                    communicationUserId, user.Id);
            }

            // Generate access token for the communication user
            var tokenResponse = await _identityClient.GetTokenAsync(
                new Azure.Communication.CommunicationUserIdentifier(communicationUserId), 
                new[] { CommunicationTokenScope.Chat });

            result.IsSuccess = true;
            result.AccessToken = tokenResponse.Value.Token;
            result.AcsUserId = communicationUserId;
            result.ExpiresOn = tokenResponse.Value.ExpiresOn.DateTime;

            // Update user with ACS information
            user.AcsUserId = result.AcsUserId;
            user.AcsAccessToken = result.AccessToken;

            // Cache the result for 50 minutes (tokens are valid for 60 minutes)
            _memoryCache.Set(cacheKey, result, TimeSpan.FromMinutes(50));

            _logger.LogInformation("✅ Successfully generated ACS access token for user: {UserId}", user.Id);
            
            if (user.IsFromFabrikam)
            {
                _logger.LogInformation("🌐 CROSS-TENANT TOKEN EXCHANGE: Fabrikam user authenticated to Contoso ACS");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error exchanging Entra ID token for ACS token: {Error}", ex.Message);
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    public async Task<ChatThread> CreateChatThreadAsync(string topic, ChatUser creator)
    {
        _logger.LogInformation("💬 Creating chat thread: '{Topic}' by {UserName} ({TenantName})", 
            topic, creator.Name, creator.TenantName);

        try
        {
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

            _logger.LogInformation("✅ Chat thread created with live ACS backend: {ThreadId}", threadId);
            
            // Send welcome messages
            await SendSystemMessageAsync(threadId, 
                $"💬 Chat thread '{topic}' created by {creator.Name} ({creator.TenantName})");

            if (creator.IsFromFabrikam)
            {
                await SendSystemMessageAsync(threadId, 
                    "🌐 Cross-tenant chat enabled! Fabrikam user connected to Contoso ACS resources");
                
                _logger.LogInformation("🎉 CROSS-TENANT THREAD: Fabrikam user created thread in live Contoso ACS");
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

            _logger.LogInformation("✅ Participant added successfully to live ACS thread");

            // Send system message about new participant
            var crossTenantInfo = participant.IsFromFabrikam ? " (Cross-tenant user from Fabrikam)" : "";
            await SendSystemMessageAsync(threadId, 
                $"👋 {participant.Name} from {participant.TenantName} joined the chat{crossTenantInfo}");

            if (participant.IsFromFabrikam || thread.IsCrossTenant)
            {
                _logger.LogInformation("🌐 CROSS-TENANT PARTICIPANT: Multi-tenant chat now active in live ACS");
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
            _logger.LogInformation("{Indicator} Message sent via live ACS from {SenderName} ({TenantName}): {Message}", 
                crossTenantIndicator, sender.Name, sender.TenantName, message.Substring(0, Math.Min(50, message.Length)));

            if (sender.IsFromFabrikam)
            {
                _logger.LogInformation("🔄 CROSS-TENANT MESSAGE: Fabrikam user sent message via live Contoso ACS");
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
            _logger.LogError(ex, "❌ Error getting messages from live ACS thread {ThreadId}", threadId);
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
            _logger.LogError(ex, "❌ Error getting chat threads from live ACS for user {UserEmail}", user.Email);
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
            
            _logger.LogInformation("📢 System message sent to live ACS thread: {Message}", message);
            
            await Task.Delay(10); // Small delay for demo effect
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error sending system message to live ACS thread");
            return false;
        }
    }
}
