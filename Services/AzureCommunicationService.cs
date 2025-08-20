using Azure.Communication.Identity;
using CrossTenantChat.Configuration;
using CrossTenantChat.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;

namespace CrossTenantChat.Services
{
    public interface IAzureCommunicationService
    {
        Task<TokenExchangeResult> ExchangeEntraIdTokenForAcsTokenAsync(ChatUser user);
        Task<(string Token, string UserId)> GetCommunicationUserTokenAsync(string userId);
    Task<ChatThread> CreateChatThreadAsync(string topic, string[] userIds);
    // Returns a cached or existing chat thread for the user if available; does not create new threads.
    Task<ChatThread?> GetOrCreateUserThreadAsync(ChatUser user, string? defaultTopic = null);
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
    private readonly IMemoryCache _memoryCache;
        
        // In-memory storage for demo purposes
        private readonly Dictionary<string, ChatThread> _chatThreads;
        private readonly Dictionary<string, List<Models.ChatMessage>> _threadMessages;
        private readonly Dictionary<string, List<string>> _userThreads;

    // Demo: auto-add these participants to any newly created thread
    private static readonly string ContosoAutoEmail = "contoso@juanjosesshotmail.onmicrosoft.com";
    private static readonly string FabrikamAutoEmail = "fabrikam@juanjosesanchezsanchezoutlo.onmicrosoft.com";

        public AzureCommunicationService(
            IOptions<AzureConfiguration> azureConfig,
            ILogger<AzureCommunicationService> logger,
            IMemoryCache memoryCache)
        {
            _azureConfig = azureConfig.Value;
            _logger = logger;
            _memoryCache = memoryCache;
            _chatThreads = new Dictionary<string, ChatThread>();
            _threadMessages = new Dictionary<string, List<Models.ChatMessage>>();
            _userThreads = new Dictionary<string, List<string>>();

            // Initialize ACS client if connection string is provided and valid
            var connectionString = _azureConfig.AzureCommunicationServices.ConnectionString;
            if (!string.IsNullOrEmpty(connectionString) && 
                !connectionString.Contains("your-acs-connection-string-here") &&
                !connectionString.Contains("placeholder"))
            {
                try
                {
                    _identityClient = new CommunicationIdentityClient(connectionString);
                    _logger.LogInformation("Azure Communication Services client initialized successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not initialize ACS client, running in demo mode");
                }
            }
            else
            {
                _logger.LogInformation("No valid ACS connection string provided, running in demo mode");
            }
        }

        public async Task<(string Token, string UserId)> GetCommunicationUserTokenAsync(string userId)
        {
            try
            {
                _logger.LogInformation("🔄 Getting ACS token for user: {UserId}", userId);

                if (_identityClient != null)
                {
                    // Real ACS integration
                    var identityResponse = await _identityClient.CreateUserAsync();
                    var tokenResponse = await _identityClient.GetTokenAsync(identityResponse.Value, new[] { CommunicationTokenScope.Chat });

                    _logger.LogInformation("✅ Real ACS token obtained for user: {UserId}", userId);
                    return (tokenResponse.Value.Token, identityResponse.Value.Id);
                }
                else
                {
                    // Demo mode - simulate token
                    var demoToken = $"demo_acs_token_{Guid.NewGuid():N}_{userId}";
                    var demoUserId = $"8:acs:demo-{userId.Substring(0, Math.Min(8, userId.Length))}";

                    _logger.LogInformation("✅ Demo ACS token generated for user: {UserId}", userId);
                    return (demoToken, demoUserId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting ACS token for user: {UserId}", userId);
                throw;
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

        public async Task<ChatThread> CreateChatThreadAsync(string topic, string[] userIds)
        {
            try
            {
                _logger.LogInformation("💬 Creating chat thread: '{Topic}' with {UserCount} users", topic, userIds.Length);
                // Always create a brand-new thread when explicitly requested via the UI.
                // Cached thread reuse is handled elsewhere via GetOrCreateUserThreadAsync.

                var threadId = $"thread_{Guid.NewGuid():N}";
                var chatThread = new ChatThread
                {
                    Id = threadId,
                    Topic = topic,
                    CreatedBy = userIds.FirstOrDefault() ?? "unknown",
                    CreatedOn = DateTime.UtcNow,
                    Participants = new List<ChatUser>(), // Will be populated later when users join
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

                // Store in cache for quick reuse on reconnects (expires after 2 hours)
                if (!string.IsNullOrEmpty(userIds.FirstOrDefault()))
                {
                    var cacheKey = $"chat_thread_user:{userIds.First()}";
                    _memoryCache.Set(cacheKey, chatThread, TimeSpan.FromHours(2));
                }

                _logger.LogInformation("✅ Chat thread created: {ThreadId}", threadId);
                
                // Send welcome message
                await SendSystemMessageAsync(threadId, $"💬 Chat thread '{topic}' created");

                await Task.Delay(50); // Small delay for demo effect

                return chatThread;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating chat thread: {Topic}", topic);
                throw;
            }
        }

        public async Task<ChatThread> CreateChatThreadAsync(string topic, ChatUser creator)
        {
            try
            {
                _logger.LogInformation("💬 Creating chat thread: '{Topic}' by {UserName}", topic, creator.Name);

                // Check for an existing cached thread for this user and reuse if present
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

                // Cache for reuse on reconnects
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
            // Try cache first
            var cacheKey = $"chat_thread_user:{user.Id}";
            if (_memoryCache.TryGetValue(cacheKey, out ChatThread? cachedThread) && cachedThread != null)
            {
                // Ensure local dictionaries are populated (service lifetime is scoped)
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

            // If no cache, check existing in-memory threads for this user
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

        // Do not auto-create a thread; require explicit user action to create one
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
                    // Show email as sender label when available for clearer identification
                    SenderName = string.IsNullOrWhiteSpace(sender.Email) ? sender.Name : sender.Email,
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

                var messages = _threadMessages[threadId]
                    .OrderBy(m => m.Timestamp)
                    .ToList();
                // Note: Do NOT mutate message content here. Rendering layer will
                // conditionally display cross-tenant indicators to keep retrieval idempotent.

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
                // Ensure any placeholder participant with matching email is bound to this user's real ID/membership
                EnsureMembershipForUserByEmail(user);

                // Demo UX optimization:
                // Show all existing threads regardless of creator or participant membership.
                // This makes the demo flow simple: any user can see/select current threads without explicit invites.
                var allThreads = _chatThreads.Values.OrderByDescending(t => t.CreatedOn).ToList();
                await Task.Delay(10); // Small delay for demo effect
                return allThreads;
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

            // Don't register placeholder IDs in membership map to avoid confusion; visible list already shows all threads
            // If needed, we could track email-based mapping here as well.

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
}
