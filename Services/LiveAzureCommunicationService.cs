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

    // Demo: auto-add these participants to any newly created thread
    private static readonly string ContosoAutoEmail = "contoso@juanjosesshotmail.onmicrosoft.com";
    private static readonly string FabrikamAutoEmail = "fabrikam@juanjosesanchezsanchezoutlo.onmicrosoft.com";

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

    public async Task<(string Token, string UserId)> GetCommunicationUserTokenAsync(string userId)
    {
        try
        {
            _logger.LogInformation("🔄 Getting live ACS token for user: {UserId}", userId);

            // Check cache first
            var cacheKey = $"acs_simple_token_{userId}";
            if (_memoryCache.TryGetValue(cacheKey, out (string, string)? cachedResult) && cachedResult.HasValue)
            {
                _logger.LogInformation("✅ Retrieved cached ACS token for user: {UserId}", userId);
                return cachedResult.Value;
            }

            // Create or get cached communication user identity
            var userCacheKey = $"communication_user_simple_{userId}";
            string communicationUserId;

            if (_memoryCache.TryGetValue(userCacheKey, out string? cachedUserId) && 
                !string.IsNullOrEmpty(cachedUserId))
            {
                communicationUserId = cachedUserId;
                _logger.LogInformation("Using cached communication user for: {UserId}", userId);
            }
            else
            {
                // Create new communication user
                var communicationUserResponse = await _identityClient.CreateUserAsync();
                communicationUserId = communicationUserResponse.Value.Id;
                
                // Cache the communication user identity
                _memoryCache.Set(userCacheKey, communicationUserId, TimeSpan.FromHours(24));
                _logger.LogInformation("Created new communication user: {CommunicationUserId} for user: {UserId}", 
                    communicationUserId, userId);
            }

            // Generate access token for the communication user
            var tokenResponse = await _identityClient.GetTokenAsync(
                new Azure.Communication.CommunicationUserIdentifier(communicationUserId), 
                new[] { CommunicationTokenScope.Chat });

            var result = (tokenResponse.Value.Token, communicationUserId);

            // Cache the result for 50 minutes (tokens are valid for 60 minutes)
            _memoryCache.Set(cacheKey, result, TimeSpan.FromMinutes(50));

            _logger.LogInformation("✅ Successfully generated live ACS access token for user: {UserId}", userId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting live ACS token for user: {UserId}", userId);
            throw;
        }
    }

    public async Task<ChatThread> CreateChatThreadAsync(string topic, string[] userIds)
    {
        try
        {
            _logger.LogInformation("💬 Creating chat thread: '{Topic}' with {UserCount} users", topic, userIds.Length);
            // Always create a brand-new thread for explicit UI requests (do not reuse cached thread here)
            // Cached thread reuse is handled by GetOrCreateUserThreadAsync via the ChatUser overload.

            var threadId = $"thread_{Guid.NewGuid():N}";
            var chatThread = new ChatThread
            {
                Id = threadId,
                Topic = topic,
                CreatedBy = userIds.FirstOrDefault() ?? "unknown",
                CreatedOn = DateTime.UtcNow,
                Participants = new List<ChatUser>(), // Will be populated when users join
                IsCrossTenant = false
            };

            _chatThreads[threadId] = chatThread;
            _threadMessages[threadId] = new List<Models.ChatMessage>();

            // Auto-add well-known demo participants by email so the other user sees/joins later
            foreach (var p in GetDefaultParticipants())
            {
                TryAddParticipantInternal(chatThread, p);
            }
            
            // Track user threads for all users
            foreach (var userId in userIds)
            {
                if (!_userThreads.ContainsKey(userId))
                {
                    _userThreads[userId] = new List<string>();
                }
                _userThreads[userId].Add(threadId);
            }

            _logger.LogInformation("✅ Chat thread created with live ACS backend: {ThreadId}", threadId);
            
            // Send welcome message
            await SendSystemMessageAsync(threadId, $"💬 Chat thread '{topic}' created");

            await Task.Delay(50); // Small delay for demo effect

            // Cache new thread for reuse (e.g., reconnect, refresh)
            if (!string.IsNullOrEmpty(userIds.FirstOrDefault()))
            {
                var cacheKey = $"chat_thread_user:{userIds.First()}";
                _memoryCache.Set(cacheKey, chatThread, TimeSpan.FromHours(2));
            }

            return chatThread;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating chat thread: {Topic}", topic);
            throw;
        }
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
            // Reuse cached thread if present
            var cacheKey = $"chat_thread_user:{creator.Id}";
            if (_memoryCache.TryGetValue(cacheKey, out ChatThread? cachedThread) && cachedThread != null)
            {
                if (!_chatThreads.ContainsKey(cachedThread.Id))
                {
                    _chatThreads[cachedThread.Id] = cachedThread;
                    if (!_threadMessages.ContainsKey(cachedThread.Id))
                    {
                        _threadMessages[cachedThread.Id] = new List<Models.ChatMessage>();
                    }
                }
                if (!_userThreads.ContainsKey(creator.Id))
                {
                    _userThreads[creator.Id] = new List<string>();
                }
                if (!_userThreads[creator.Id].Contains(cachedThread.Id))
                {
                    _userThreads[creator.Id].Add(cachedThread.Id);
                }

                _logger.LogInformation("🔁 Reusing cached chat thread for user {UserId}: {ThreadId}", creator.Id, cachedThread.Id);
                return cachedThread;
            }

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
            
            // Auto-add the counterpart demo participants, excluding the creator's email if it matches
            foreach (var p in GetDefaultParticipants().Where(p => !string.Equals(p.Email, creator.Email, StringComparison.OrdinalIgnoreCase)))
            {
                TryAddParticipantInternal(chatThread, p);
            }
            
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

            // Cache for reuse
            _memoryCache.Set(cacheKey, chatThread, TimeSpan.FromHours(2));

            return chatThread;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating chat thread: {Topic}", topic);
            throw;
        }
    }

    // Returns a cached or existing chat thread for the user if available; does not create new threads.
    public Task<ChatThread?> GetOrCreateUserThreadAsync(ChatUser user, string? defaultTopic = null)
    {
        var cacheKey = $"chat_thread_user:{user.Id}";
        if (_memoryCache.TryGetValue(cacheKey, out ChatThread? cachedThread) && cachedThread != null)
        {
            if (!_chatThreads.ContainsKey(cachedThread.Id))
            {
                _chatThreads[cachedThread.Id] = cachedThread;
                if (!_threadMessages.ContainsKey(cachedThread.Id))
                {
                    _threadMessages[cachedThread.Id] = new List<Models.ChatMessage>();
                }
            }
            if (!_userThreads.ContainsKey(user.Id))
            {
                _userThreads[user.Id] = new List<string>();
            }
            if (!_userThreads[user.Id].Contains(cachedThread.Id))
            {
                _userThreads[user.Id].Add(cachedThread.Id);
            }

            _logger.LogInformation("🔁 Reusing cached chat thread for user {UserId}: {ThreadId}", user.Id, cachedThread.Id);
            return Task.FromResult<ChatThread?>(cachedThread);
        }

        // Check in-memory existing threads for this user
        if (_userThreads.TryGetValue(user.Id, out var threadIds))
        {
            var existingId = threadIds.FirstOrDefault(id => _chatThreads.ContainsKey(id));
            if (!string.IsNullOrEmpty(existingId))
            {
                var existing = _chatThreads[existingId];
                _memoryCache.Set(cacheKey, existing, TimeSpan.FromHours(2));
        _logger.LogInformation("📦 Cached existing thread {ThreadId} for user {UserId}", existing.Id, user.Id);
        return Task.FromResult<ChatThread?>(existing);
            }
        }
    // Do not auto-create a thread; require explicit user action
    _logger.LogInformation("ℹ️ No existing thread found for user {UserId}; user must create a new thread", user.Id);
    return Task.FromResult<ChatThread?>(null);
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
            // Ensure any placeholder participant with matching email is bound to this user's real ID/membership
            EnsureMembershipForUserByEmail(user);

            // Live demo UX optimization:
            // Show all current threads, not only those the user created or was added to.
            // In a real app, you'd filter by membership. For demo, this makes discovery easy.
            var allThreads = _chatThreads.Values.OrderByDescending(t => t.CreatedOn).ToList();
            await Task.Delay(10); // Small delay for demo effect
            return allThreads;
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

    // --- Helpers: auto-participants and membership reconciliation ---
    private IEnumerable<ChatUser> GetDefaultParticipants()
    {
        // Use placeholder IDs based on email; bind to real user on login via EnsureMembershipForUserByEmail
        yield return new ChatUser
        {
            Id = $"email:{ContosoAutoEmail}",
            Name = "Contoso User",
            Email = ContosoAutoEmail,
            TenantName = "Contoso",
            IsFromFabrikam = false,
            TenantId = ""
        };
        yield return new ChatUser
        {
            Id = $"email:{FabrikamAutoEmail}",
            Name = "Fabrikam User",
            Email = FabrikamAutoEmail,
            TenantName = "Fabrikam",
            IsFromFabrikam = true,
            TenantId = ""
        };
    }

    private void TryAddParticipantInternal(ChatThread thread, ChatUser participant)
    {
        // Avoid duplicates by email
        if (thread.Participants.Any(u => u.Email.Equals(participant.Email, StringComparison.OrdinalIgnoreCase)))
            return;

        thread.Participants.Add(participant);

        // Mark cross-tenant when a Fabrikam user is present
        if (participant.IsFromFabrikam)
        {
            thread.IsCrossTenant = true;
        }
    }

    private void EnsureMembershipForUserByEmail(ChatUser user)
    {
        if (string.IsNullOrEmpty(user.Email)) return;

        foreach (var thread in _chatThreads.Values)
        {
            var placeholder = thread.Participants.FirstOrDefault(p =>
                !string.IsNullOrEmpty(p.Email) &&
                p.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(p.Id, user.Id, StringComparison.Ordinal));

            if (placeholder != null)
            {
                // Update participant to reflect real logged-in user identity
                placeholder.Id = user.Id;
                placeholder.Name = string.IsNullOrWhiteSpace(user.Name) ? placeholder.Name : user.Name;
                placeholder.TenantName = user.TenantName;
                placeholder.TenantId = user.TenantId;
                placeholder.IsFromFabrikam = user.IsFromFabrikam;
                placeholder.AcsUserId = user.AcsUserId;

                // Track membership
                if (!_userThreads.ContainsKey(user.Id))
                {
                    _userThreads[user.Id] = new List<string>();
                }
                if (!_userThreads[user.Id].Contains(thread.Id))
                {
                    _userThreads[user.Id].Add(thread.Id);
                }

                // Cross-tenant flag if applicable
                if (user.IsFromFabrikam)
                {
                    thread.IsCrossTenant = true;
                }
            }
        }
    }
}
