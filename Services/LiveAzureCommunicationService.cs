using Azure.Communication.Chat;
using Azure.Communication.Identity;
using Azure;
using Azure.Communication;
using Microsoft.Extensions.Caching.Memory;
using CrossTenantChat.Models;

namespace CrossTenantChat.Services;

public class LiveAzureCommunicationService : IAzureCommunicationService
{
    private readonly ILogger<LiveAzureCommunicationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;
    private readonly IAcsOperationTracker _operationTracker;
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
        IMemoryCache memoryCache,
        IAcsOperationTracker operationTracker)
    {
        _logger = logger;
        _configuration = configuration;
        _memoryCache = memoryCache;
        _operationTracker = operationTracker;
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
            // Resolve ACS user identities for participants (ensure ACS user exists)
            var acsParticipants = new List<ChatParticipant>();
            foreach (var uid in userIds.Distinct())
            {
                var acsUserId = await EnsureAcsUserForAppUserAsync(uid);
                acsParticipants.Add(new ChatParticipant(new CommunicationUserIdentifier(acsUserId))
                {
                    DisplayName = uid
                });
            }

            // Create ACS thread
            var serviceChatClient = await GetServiceChatClientAsync();
            var createResponse = await serviceChatClient.CreateChatThreadAsync(topic, acsParticipants);
            var acsThread = createResponse.Value.ChatThread;
            var threadId = acsThread.Id;

            // Maintain lightweight in-memory tracking for UI continuity
            var chatThread = new ChatThread
            {
                Id = threadId,
                Topic = topic,
                CreatedBy = userIds.FirstOrDefault() ?? "unknown",
                CreatedOn = DateTime.UtcNow,
                Participants = new List<ChatUser>(),
                IsCrossTenant = false
            };
            _chatThreads[threadId] = chatThread;
            _threadMessages[threadId] = new List<Models.ChatMessage>();

            // Track user threads for all users
            foreach (var userId in userIds)
            {
                if (!_userThreads.ContainsKey(userId))
                {
                    _userThreads[userId] = new List<string>();
                }
                _userThreads[userId].Add(threadId);
            }

            _logger.LogInformation("✅ Chat thread created in ACS: {ThreadId}", threadId);

            await SendSystemMessageAsync(threadId, $"💬 Chat thread '{topic}' created");

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
        var operationId = _operationTracker.StartOperation("TokenExchange", 
            $"Exchange Entra ID token for ACS token", user.Id, user.TenantName);

        _operationTracker.AddStep(operationId, "InitiateExchange", 
            $"Starting Entra ID token exchange for user: {user.Id} ({user.TenantName})", true, 
            new Dictionary<string, object> 
            { 
                ["UserId"] = user.Id,
                ["UserEmail"] = user.Email,
                ["TenantName"] = user.TenantName,
                ["IsFromFabrikam"] = user.IsFromFabrikam
            });

        _logger.LogInformation("🔄 Starting Entra ID token exchange for ACS token - User: {UserId} ({TenantName}), " +
            "IsFromFabrikam: {IsFromFabrikam}, Email: {Email}", 
            user.Id, user.TenantName, user.IsFromFabrikam, user.Email);

        var result = new TokenExchangeResult();

        try
        {
            // Validate prerequisites
            _operationTracker.AddStep(operationId, "ValidatePrerequisites", 
                "Validating ACS client and configuration", true);

            if (_identityClient == null)
            {
                _logger.LogError("❌ ACS Identity Client is null - cannot exchange tokens");
                result.ErrorMessage = "ACS Identity Client not initialized";
                result.IsSuccess = false;
                _operationTracker.AddStep(operationId, "ValidatePrerequisites", 
                    "ACS Identity Client validation failed", false, null, "ACS Identity Client is null");
                _operationTracker.CompleteOperation(operationId, false, result.ErrorMessage);
                return result;
            }

            if (string.IsNullOrEmpty(_acsConnectionString))
            {
                _logger.LogError("❌ ACS Connection String is not configured");
                result.ErrorMessage = "ACS Connection String not configured";
                result.IsSuccess = false;
                _operationTracker.AddStep(operationId, "ValidatePrerequisites", 
                    "ACS Connection String validation failed", false, null, "Connection string is null or empty");
                _operationTracker.CompleteOperation(operationId, false, result.ErrorMessage);
                return result;
            }

            _logger.LogInformation("✅ Prerequisites validated - proceeding with token exchange");
            _operationTracker.AddStep(operationId, "ValidatePrerequisites", 
                "Prerequisites validation completed successfully", true, 
                new Dictionary<string, object> 
                { 
                    ["HasIdentityClient"] = true,
                    ["HasConnectionString"] = true
                });

            // Check cache first
            var cacheKey = $"acs_token_{user.Id}_{user.TenantName}";
            _logger.LogInformation("🔍 Checking cache for key: {CacheKey}", cacheKey);
            _operationTracker.AddStep(operationId, "CheckCache", 
                $"Checking cache for existing token: {cacheKey}", true);
            
            if (_memoryCache.TryGetValue(cacheKey, out TokenExchangeResult? cachedResult) && cachedResult != null)
            {
                _logger.LogInformation("✅ Retrieved cached ACS token for user: {UserId}, expires: {ExpiresOn}", 
                    user.Id, cachedResult.ExpiresOn);
                
                // IMPORTANT: Update the user object with cached token information
                user.AcsUserId = cachedResult.AcsUserId;
                user.AcsAccessToken = cachedResult.AccessToken;
                user.TokenExpiry = cachedResult.ExpiresOn;
                
                _logger.LogInformation("🔑 Updated user with cached token - Token length: {TokenLength}, starts with: {TokenPrefix}...", 
                    cachedResult.AccessToken?.Length ?? 0, 
                    !string.IsNullOrEmpty(cachedResult.AccessToken) && cachedResult.AccessToken.Length > 20 
                        ? cachedResult.AccessToken.Substring(0, 20) 
                        : cachedResult.AccessToken ?? "null");
                
                _operationTracker.AddStep(operationId, "CheckCache", 
                    "Found cached token, updating user object", true, 
                    new Dictionary<string, object> 
                    { 
                        ["TokenExpiresOn"] = cachedResult.ExpiresOn,
                        ["TokenLength"] = cachedResult.AccessToken?.Length ?? 0,
                        ["AcsUserId"] = cachedResult.AcsUserId ?? ""
                    });
                
                _operationTracker.CompleteOperation(operationId, true);
                return cachedResult;
            }

            _operationTracker.AddStep(operationId, "CheckCache", 
                "No cached token found, proceeding to create new token", true);

            // Create or get cached communication user identity
            var userCacheKey = $"communication_user_{user.Id}_{user.TenantName}";
            _logger.LogInformation("🔍 Looking for cached communication user with key: {UserCacheKey}", userCacheKey);
            _operationTracker.AddStep(operationId, "GetCommunicationUser", 
                $"Looking for cached communication user: {userCacheKey}", true);

            string communicationUserId;

            if (_memoryCache.TryGetValue(userCacheKey, out string? cachedUserId) && 
                !string.IsNullOrEmpty(cachedUserId))
            {
                communicationUserId = cachedUserId;
                _logger.LogInformation("✅ Using cached communication user: {CommunicationUserId} for user: {UserId}", 
                    communicationUserId, user.Id);
                _operationTracker.AddStep(operationId, "GetCommunicationUser", 
                    $"Found cached communication user: {communicationUserId}", true, 
                    new Dictionary<string, object> 
                    { 
                        ["CommunicationUserId"] = communicationUserId,
                        ["FromCache"] = true
                    });
            }
            else
            {
                _logger.LogInformation("🔄 Creating new communication user for EntraId user: {UserId}", user.Id);
                _operationTracker.AddStep(operationId, "CreateCommunicationUser", 
                    $"Creating new communication user for: {user.Id}", true);
                
                // IMPORTANT: Use the same helper method to ensure consistency across the app
                communicationUserId = await EnsureAcsUserForAppUserAsync(user.Id, user);
                
                _logger.LogInformation("✅ Created new communication user: {CommunicationUserId} for EntraId user: {UserId}", 
                    communicationUserId, user.Id);

                _operationTracker.AddStep(operationId, "CreateCommunicationUser", 
                    $"Successfully created communication user: {communicationUserId}", true, 
                    new Dictionary<string, object> 
                    { 
                        ["CommunicationUserId"] = communicationUserId,
                        ["CacheExpiry"] = TimeSpan.FromHours(24).ToString()
                    });
            }

            // Generate access token for the communication user
            _logger.LogInformation("🔄 Generating access token for communication user: {CommunicationUserId}", communicationUserId);
            _operationTracker.AddStep(operationId, "GenerateAccessToken", 
                $"Generating access token for communication user: {communicationUserId}", true, 
                new Dictionary<string, object> 
                { 
                    ["CommunicationUserId"] = communicationUserId,
                    ["Scopes"] = "Chat"
                });
            
            var tokenResponse = await _identityClient.GetTokenAsync(
                new Azure.Communication.CommunicationUserIdentifier(communicationUserId), 
                new[] { CommunicationTokenScope.Chat });

            if (tokenResponse?.Value == null || string.IsNullOrEmpty(tokenResponse.Value.Token))
            {
                _logger.LogError("❌ GetTokenAsync returned null or invalid token response for user: {UserId}", user.Id);
                result.ErrorMessage = "Failed to generate ACS access token - null or empty token response";
                result.IsSuccess = false;
                _operationTracker.AddStep(operationId, "GenerateAccessToken", 
                    "Failed to generate access token - null response", false, null, result.ErrorMessage);
                _operationTracker.CompleteOperation(operationId, false, result.ErrorMessage);
                return result;
            }

            _logger.LogInformation("✅ Successfully received token response for user: {UserId}, expires: {ExpiresOn}", 
                user.Id, tokenResponse.Value.ExpiresOn);

            result.IsSuccess = true;
            result.AccessToken = tokenResponse.Value.Token;
            result.AcsUserId = communicationUserId;
            result.ExpiresOn = tokenResponse.Value.ExpiresOn.DateTime;

            // Validate the token before using it
            if (string.IsNullOrWhiteSpace(result.AccessToken))
            {
                _logger.LogError("❌ Generated ACS access token is null or empty for user: {UserId}", user.Id);
                result.IsSuccess = false;
                result.ErrorMessage = "Generated token is null or empty";
                _operationTracker.AddStep(operationId, "ValidateToken", 
                    "Generated token validation failed - token is null or empty", false, null, result.ErrorMessage);
                _operationTracker.CompleteOperation(operationId, false, result.ErrorMessage);
                return result;
            }

            _logger.LogInformation("🔑 Generated ACS token for user {UserId}, token length: {TokenLength}, starts with: {TokenPrefix}...", 
                user.Id, result.AccessToken.Length, result.AccessToken.Length > 20 ? result.AccessToken.Substring(0, 20) : result.AccessToken);

            _operationTracker.AddStep(operationId, "ValidateToken", 
                "Generated token validation successful", true, 
                new Dictionary<string, object> 
                { 
                    ["TokenLength"] = result.AccessToken.Length,
                    ["TokenExpiresOn"] = result.ExpiresOn,
                    ["AcsUserId"] = result.AcsUserId
                });

            // Update user with ACS information
            user.AcsUserId = result.AcsUserId;
            user.AcsAccessToken = result.AccessToken;

            _operationTracker.AddStep(operationId, "UpdateUser", 
                "Updated user object with ACS token and user ID", true, 
                new Dictionary<string, object> 
                { 
                    ["AcsUserId"] = result.AcsUserId,
                    ["TokenLength"] = result.AccessToken.Length
                });

            // Cache the result for 50 minutes (tokens are valid for 60 minutes)
            _memoryCache.Set(cacheKey, result, TimeSpan.FromMinutes(50));

            _operationTracker.AddStep(operationId, "CacheResult", 
                "Cached token result for future use", true, 
                new Dictionary<string, object> 
                { 
                    ["CacheKey"] = cacheKey,
                    ["CacheExpiry"] = TimeSpan.FromMinutes(50).ToString()
                });

            _logger.LogInformation("✅ Successfully generated ACS access token for user: {UserId}", user.Id);
            
            if (user.IsFromFabrikam)
            {
                _logger.LogInformation("🌐 CROSS-TENANT TOKEN EXCHANGE: Fabrikam user authenticated to Contoso ACS");
                _operationTracker.AddStep(operationId, "CrossTenantSuccess", 
                    "Cross-tenant token exchange completed - Fabrikam user authenticated to Contoso ACS", true, 
                    new Dictionary<string, object> 
                    { 
                        ["IsCrossTenant"] = true,
                        ["SourceTenant"] = "Fabrikam",
                        ["TargetTenant"] = "Contoso"
                    });
            }

            _operationTracker.CompleteOperation(operationId, true);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error exchanging Entra ID token for ACS token for user {UserId} ({TenantName}). " +
                "Exception Type: {ExceptionType}, Message: {ErrorMessage}, Stack Trace: {StackTrace}",
                user.Id, user.TenantName, ex.GetType().Name, ex.Message, ex.StackTrace);
                
            // Log additional details about the user and configuration
            _logger.LogError("🔍 Token exchange failure details - User ID: {UserId}, Tenant: {TenantName}, " +
                "IsFromFabrikam: {IsFromFabrikam}, ACS Connection String configured: {HasConnectionString}",
                user.Id, user.TenantName, user.IsFromFabrikam, !string.IsNullOrEmpty(_acsConnectionString));
                
            result.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
            result.IsSuccess = false;
            
            _operationTracker.AddStep(operationId, "Exception", 
                $"Token exchange failed with exception: {ex.GetType().Name}", false, 
                new Dictionary<string, object> 
                { 
                    ["ExceptionType"] = ex.GetType().Name,
                    ["ExceptionMessage"] = ex.Message,
                    ["UserId"] = user.Id,
                    ["TenantName"] = user.TenantName,
                    ["IsFromFabrikam"] = user.IsFromFabrikam
                }, ex.Message);
            
            _operationTracker.CompleteOperation(operationId, false, result.ErrorMessage);
            return result;
        }
    }

    public async Task<ChatThread> CreateChatThreadAsync(string topic, ChatUser creator)
    {
        var operationId = _operationTracker.StartOperation("ThreadCreation", 
            $"Create chat thread: '{topic}'", creator.Id, creator.TenantName);

        _operationTracker.AddStep(operationId, "InitiateCreation", 
            $"Starting thread creation with topic: '{topic}' by {creator.Name} ({creator.TenantName})", true, 
            new Dictionary<string, object> 
            { 
                ["Topic"] = topic,
                ["CreatorName"] = creator.Name,
                ["CreatorTenant"] = creator.TenantName,
                ["IsFromFabrikam"] = creator.IsFromFabrikam
            });

        _logger.LogInformation("💬 Creating chat thread: '{Topic}' by {UserName} ({TenantName})", 
            topic, creator.Name, creator.TenantName);

        try
        {
            // Reuse cached thread if present
            var cacheKey = $"chat_thread_user:{creator.Id}";
            _operationTracker.AddStep(operationId, "CheckCache", 
                $"Checking for cached thread with key: {cacheKey}", true);

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
                
                _operationTracker.AddStep(operationId, "CheckCache", 
                    $"Found cached thread, reusing existing thread: {cachedThread.Id}", true, 
                    new Dictionary<string, object> 
                    { 
                        ["ThreadId"] = cachedThread.Id,
                        ["ThreadTopic"] = cachedThread.Topic
                    });
                
                _operationTracker.CompleteOperation(operationId, true);
                return cachedThread;
            }

            _operationTracker.AddStep(operationId, "CheckCache", 
                "No cached thread found, creating new thread", true);

            // Ensure creator has an ACS identity
            _operationTracker.AddStep(operationId, "EnsureAcsUser", 
                $"Ensuring ACS user identity exists for creator: {creator.Id}", true);

            var creatorAcsUserId = await EnsureAcsUserForAppUserAsync(creator.Id, creator);
            
            _operationTracker.AddStep(operationId, "EnsureAcsUser", 
                $"ACS user identity confirmed: {creatorAcsUserId}", true, 
                new Dictionary<string, object> 
                { 
                    ["CreatorAcsUserId"] = creatorAcsUserId
                });

            var participants = new List<ChatParticipant>
            {
                new ChatParticipant(new CommunicationUserIdentifier(creatorAcsUserId))
                {
                    DisplayName = string.IsNullOrWhiteSpace(creator.Name) ? creator.Email : creator.Name
                }
            };

            _operationTracker.AddStep(operationId, "PrepareParticipants", 
                "Prepared participant list for thread creation", true, 
                new Dictionary<string, object> 
                { 
                    ["ParticipantCount"] = participants.Count,
                    ["CreatorDisplayName"] = participants[0].DisplayName
                });

            // Create ACS thread
            _operationTracker.AddStep(operationId, "CreateAcsThread", 
                "Creating thread in Azure Communication Services", true);

            var serviceChatClient = await GetServiceChatClientAsync();
            var createResponse = await serviceChatClient.CreateChatThreadAsync(topic, participants);
            var acsThread = createResponse.Value.ChatThread;
            var threadId = acsThread.Id;

            _operationTracker.AddStep(operationId, "CreateAcsThread", 
                $"Successfully created ACS thread with ID: {threadId}", true, 
                new Dictionary<string, object> 
                { 
                    ["ThreadId"] = threadId,
                    ["AcsThreadTopic"] = acsThread.Topic
                });

            // Maintain lightweight in-memory tracking for UI continuity
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

            _operationTracker.AddStep(operationId, "StoreLocally", 
                "Stored thread information locally for UI", true, 
                new Dictionary<string, object> 
                { 
                    ["LocalThreadId"] = threadId,
                    ["ParticipantCount"] = chatThread.Participants.Count,
                    ["IsCrossTenant"] = chatThread.IsCrossTenant
                });

            _logger.LogInformation("✅ Chat thread created in ACS: {ThreadId}", threadId);

            // Send welcome messages (system)
            await SendSystemMessageAsync(threadId, 
                $"💬 Chat thread '{topic}' created by {creator.Name} ({creator.TenantName})");

            _operationTracker.AddStep(operationId, "SendWelcomeMessage", 
                "Sent system welcome message to thread", true);

            if (creator.IsFromFabrikam)
            {
                await SendSystemMessageAsync(threadId, 
                    "🌐 Cross-tenant chat enabled! Fabrikam user connected to Contoso ACS resources");
                _logger.LogInformation("🎉 CROSS-TENANT THREAD: Fabrikam user created thread in live Contoso ACS");
                
                _operationTracker.AddStep(operationId, "CrossTenantSetup", 
                    "Cross-tenant thread created - Fabrikam user connected to Contoso ACS", true, 
                    new Dictionary<string, object> 
                    { 
                        ["SourceTenant"] = "Fabrikam",
                        ["TargetTenant"] = "Contoso",
                        ["ThreadId"] = threadId
                    });
            }

            // Cache for reuse
            _memoryCache.Set(cacheKey, chatThread, TimeSpan.FromHours(2));

            _operationTracker.AddStep(operationId, "CacheThread", 
                "Cached thread for future reuse", true, 
                new Dictionary<string, object> 
                { 
                    ["CacheKey"] = cacheKey,
                    ["CacheExpiry"] = TimeSpan.FromHours(2).ToString()
                });

            _operationTracker.CompleteOperation(operationId, true);
            return chatThread;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating chat thread: {Topic}", topic);
            
            _operationTracker.AddStep(operationId, "Exception", 
                $"Thread creation failed with exception: {ex.GetType().Name}", false, 
                new Dictionary<string, object> 
                { 
                    ["ExceptionType"] = ex.GetType().Name,
                    ["ExceptionMessage"] = ex.Message,
                    ["Topic"] = topic,
                    ["CreatorId"] = creator.Id
                }, ex.Message);
            
            _operationTracker.CompleteOperation(operationId, false, ex.Message);
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

            // Ensure ACS identity exists for participant
            var acsUserId = await EnsureAcsUserForAppUserAsync(participant.Id, participant);

            // Add to ACS thread
            var serviceChatClient = await GetServiceChatClientAsync();
            var threadClient = serviceChatClient.GetChatThreadClient(threadId);
            await threadClient.AddParticipantAsync(new ChatParticipant(new CommunicationUserIdentifier(acsUserId))
            {
                DisplayName = string.IsNullOrWhiteSpace(participant.Name) ? participant.Email : participant.Name
            });

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
        var operationId = _operationTracker.StartOperation("MessageSend", 
            $"Send message to thread {threadId}", sender.Id, sender.TenantName);

        _operationTracker.AddStep(operationId, "ValidateThread", 
            $"Validating thread exists: {threadId}", true);

        try
        {
            if (!_threadMessages.ContainsKey(threadId))
            {
                _logger.LogWarning("⚠️ Thread not found: {ThreadId}", threadId);
                _operationTracker.AddStep(operationId, "ValidateThread", 
                    "Thread not found in local storage", false, null, $"Thread {threadId} not found");
                _operationTracker.CompleteOperation(operationId, false, "Thread not found");
                return false;
            }

            _operationTracker.AddStep(operationId, "ValidateThread", 
                "Thread found, proceeding with message send", true, 
                new Dictionary<string, object> { ["ThreadId"] = threadId });

            // Send message to ACS using the sender's ACS token so the message is attributed correctly
            _operationTracker.AddStep(operationId, "ValidateToken", 
                "Validating sender's ACS access token", true);

            if (string.IsNullOrWhiteSpace(sender.AcsAccessToken))
            {
                // Try to ensure token via exchange if missing
                _operationTracker.AddStep(operationId, "EnsureToken", 
                    "ACS token missing, attempting to exchange for new token", true);

                var tokenResult = await ExchangeEntraIdTokenForAcsTokenAsync(sender);
                if (!tokenResult.IsSuccess || string.IsNullOrWhiteSpace(sender.AcsAccessToken))
                {
                    _logger.LogError("❌ Failed to get valid ACS access token for user {UserId}: {Error}", 
                        sender.Id, tokenResult.ErrorMessage ?? "Token exchange failed or returned empty token");
                    
                    _operationTracker.AddStep(operationId, "EnsureToken", 
                        "Failed to obtain valid ACS access token", false, null, 
                        tokenResult.ErrorMessage ?? "Token exchange failed");
                    _operationTracker.CompleteOperation(operationId, false, "Failed to obtain ACS token");
                    return false;
                }

                _operationTracker.AddStep(operationId, "EnsureToken", 
                    "Successfully obtained ACS access token", true, 
                    new Dictionary<string, object> { ["TokenLength"] = sender.AcsAccessToken?.Length ?? 0 });
            }

            // Validate token format before using it
            if (string.IsNullOrWhiteSpace(sender.AcsAccessToken))
            {
                _logger.LogError("❌ ACS access token is null or empty for user {UserId}", sender.Id);
                _operationTracker.AddStep(operationId, "ValidateToken", 
                    "ACS access token is null or empty", false, null, "Token is null or empty");
                _operationTracker.CompleteOperation(operationId, false, "Invalid ACS token");
                return false;
            }

            _logger.LogInformation("🔑 Using ACS token for user {UserId}, token starts with: {TokenPrefix}...", 
                sender.Id, sender.AcsAccessToken.Length > 20 ? sender.AcsAccessToken.Substring(0, 20) : sender.AcsAccessToken);

            _operationTracker.AddStep(operationId, "ValidateToken", 
                "ACS access token validation successful", true, 
                new Dictionary<string, object> 
                { 
                    ["TokenLength"] = sender.AcsAccessToken.Length,
                    ["SenderId"] = sender.Id
                });

            var endpoint = ExtractEndpoint(_acsConnectionString);
            _operationTracker.AddStep(operationId, "CreateChatClient", 
                $"Creating ACS chat client for endpoint: {endpoint}", true);

            ChatClient userChatClient;
            try 
            {
                userChatClient = new ChatClient(new Uri(endpoint), new CommunicationTokenCredential(sender.AcsAccessToken));
                _operationTracker.AddStep(operationId, "CreateChatClient", 
                    "Successfully created ACS chat client", true, 
                    new Dictionary<string, object> { ["Endpoint"] = endpoint });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to create ChatClient with token for user {UserId}. Token length: {TokenLength}", 
                    sender.Id, sender.AcsAccessToken?.Length ?? 0);
                _operationTracker.AddStep(operationId, "CreateChatClient", 
                    "Failed to create ACS chat client", false, null, ex.Message);
                _operationTracker.CompleteOperation(operationId, false, "Failed to create chat client");
                return false;
            }

            _operationTracker.AddStep(operationId, "SendToAcs", 
                "Sending message to ACS thread", true, 
                new Dictionary<string, object> 
                { 
                    ["MessageLength"] = message.Length,
                    ["SenderName"] = sender.Name,
                    ["ThreadId"] = threadId
                });

            ChatThreadClient userThreadClient;
            try 
            {
                userThreadClient = userChatClient.GetChatThreadClient(threadId);
                await userThreadClient.SendMessageAsync(message, ChatMessageType.Text, senderDisplayName: sender.Name);
                
                _operationTracker.AddStep(operationId, "SendToAcs", 
                    "Message successfully sent to ACS", true, 
                    new Dictionary<string, object> 
                    { 
                        ["MessageType"] = "Text",
                        ["SenderDisplayName"] = sender.Name
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to send message to ACS thread {ThreadId} for user {UserId}", threadId, sender.Id);
                _operationTracker.AddStep(operationId, "SendToAcs", 
                    "Failed to send message to ACS thread", false, null, ex.Message);
                _operationTracker.CompleteOperation(operationId, false, "Failed to send to ACS");
                return false;
            }

            // Store message locally for UI continuity
            _operationTracker.AddStep(operationId, "StoreLocally", 
                "Storing message in local cache for UI", true);

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

            _operationTracker.AddStep(operationId, "StoreLocally", 
                "Message stored in local cache", true, 
                new Dictionary<string, object> 
                { 
                    ["MessageId"] = chatMessage.Id,
                    ["CrossTenant"] = sender.IsFromFabrikam
                });

            if (sender.IsFromFabrikam)
            {
                _logger.LogInformation("🔄 CROSS-TENANT MESSAGE: Fabrikam user sent message via live Contoso ACS");
                _operationTracker.AddStep(operationId, "CrossTenantMessage", 
                    "Cross-tenant message sent - Fabrikam user via Contoso ACS", true, 
                    new Dictionary<string, object> 
                    { 
                        ["SourceTenant"] = "Fabrikam",
                        ["TargetTenant"] = "Contoso",
                        ["MessagePreview"] = message.Length > 30 ? message.Substring(0, 30) + "..." : message
                    });
            }

            await Task.Delay(20); // Small delay for demo effect
            
            _operationTracker.CompleteOperation(operationId, true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error sending message from {UserEmail}", sender.Email);
            
            _operationTracker.AddStep(operationId, "Exception", 
                $"Message send failed with exception: {ex.GetType().Name}", false, 
                new Dictionary<string, object> 
                { 
                    ["ExceptionType"] = ex.GetType().Name,
                    ["ExceptionMessage"] = ex.Message,
                    ["SenderEmail"] = sender.Email,
                    ["ThreadId"] = threadId
                }, ex.Message);
            
            _operationTracker.CompleteOperation(operationId, false, ex.Message);
            return false;
        }
    }

    public async Task<List<Models.ChatMessage>> GetMessagesAsync(string threadId)
    {
        try
        {
            var serviceChatClient = await GetServiceChatClientAsync();
            var threadClient = serviceChatClient.GetChatThreadClient(threadId);

            var results = new List<Models.ChatMessage>();
            await foreach (var m in threadClient.GetMessagesAsync())
            {
                // Map ACS chat messages to our model
                if (m.Type == Azure.Communication.Chat.ChatMessageType.Text)
                {
                    results.Add(new Models.ChatMessage
                    {
                        Id = m.Id,
                        ThreadId = threadId,
                        Content = m.Content?.Message ?? string.Empty,
                        SenderId = (m.Sender as CommunicationUserIdentifier)?.Id ?? "",
                        SenderName = m.SenderDisplayName ?? "",
                        SenderTenant = "", // Tenant not exposed by ACS; leave empty or infer separately
                        Timestamp = m.CreatedOn.UtcDateTime,
                        Type = MessageType.Text
                    });
                }
                else
                {
                    // Treat non-text as system messages for the demo UI
                    string systemText;
                    if (m.Type == Azure.Communication.Chat.ChatMessageType.ParticipantAdded)
                        systemText = "👤 Participant added";
                    else if (m.Type == Azure.Communication.Chat.ChatMessageType.ParticipantRemoved)
                        systemText = "👤 Participant removed";
                    else if (m.Type == Azure.Communication.Chat.ChatMessageType.TopicUpdated)
                        systemText = "📝 Topic updated";
                    else
                        systemText = m.Content?.Message ?? m.Type.ToString();

                    results.Add(new Models.ChatMessage
                    {
                        Id = m.Id,
                        ThreadId = threadId,
                        Content = systemText,
                        SenderId = "system",
                        SenderName = "System",
                        SenderTenant = "System",
                        Timestamp = m.CreatedOn.UtcDateTime,
                        Type = MessageType.System
                    });
                }
            }

            return results.OrderBy(r => r.Timestamp).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting messages from live ACS thread {ThreadId}", threadId);
            // Fallback to any local cache if exists to avoid empty UI
            if (_threadMessages.TryGetValue(threadId, out var cached))
            {
                return cached.OrderBy(m => m.Timestamp).ToList();
            }
            return new List<Models.ChatMessage>();
        }
    }

    public async Task<List<ChatThread>> GetUserChatThreadsAsync(ChatUser user)
    {
        try
        {
            // Prefer querying ACS for threads
            var serviceChatClient = await GetServiceChatClientAsync();
            var result = new List<ChatThread>();
        await foreach (var item in serviceChatClient.GetChatThreadsAsync())
            {
                result.Add(new ChatThread
                {
                    Id = item.Id,
                    Topic = item.Topic ?? "",
            CreatedOn = item.LastMessageReceivedOn?.UtcDateTime ?? DateTime.UtcNow,
            CreatedBy = string.Empty,
                    Participants = new List<ChatUser>(),
                    IsCrossTenant = false
                });
            }

            // If none found in ACS, fall back to any local cache to avoid empty UI
            if (result.Count == 0 && _chatThreads.Count > 0)
            {
                return _chatThreads.Values.OrderByDescending(t => t.CreatedOn).ToList();
            }

            // Ensure placeholder membership binding for the local cache structure
            EnsureMembershipForUserByEmail(user);
            return result.OrderByDescending(t => t.CreatedOn).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting chat threads from live ACS for user {UserEmail}", user.Email);
            return _chatThreads.Values.OrderByDescending(t => t.CreatedOn).ToList();
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

    // --- Utilities ---
    private async Task<string> EnsureAcsUserForAppUserAsync(string appUserId, ChatUser? user = null)
    {
        var userCacheKey = user == null
            ? $"communication_user_simple_{appUserId}"
            : $"communication_user_{appUserId}_{user.TenantName}";

        if (_memoryCache.TryGetValue(userCacheKey, out string? cachedUserId) && !string.IsNullOrEmpty(cachedUserId))
        {
            return cachedUserId;
        }

        // Create new communication user and cache
        var communicationUserResponse = await _identityClient.CreateUserAsync();
        var communicationUserId = communicationUserResponse.Value.Id;
        _memoryCache.Set(userCacheKey, communicationUserId, TimeSpan.FromHours(24));
        return communicationUserId;
    }

    private static string ExtractEndpoint(string connectionString)
    {
        // Basic parser for 'endpoint=...;accesskey=...'
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("endpoint=", StringComparison.OrdinalIgnoreCase))
            {
                return part.Substring("endpoint=".Length);
            }
        }
        throw new InvalidOperationException("Could not extract ACS endpoint from connection string");
    }

    private async Task<ChatClient> GetServiceChatClientAsync()
    {
        var endpoint = ExtractEndpoint(_acsConnectionString);

        // Get or create a service communication user id
        var serviceUserKey = "service_communication_user_id";
        if (!_memoryCache.TryGetValue(serviceUserKey, out string? serviceUserId) || string.IsNullOrEmpty(serviceUserId))
        {
            var userResponse = await _identityClient.CreateUserAsync();
            serviceUserId = userResponse.Value.Id;
            _memoryCache.Set(serviceUserKey, serviceUserId, TimeSpan.FromDays(1));
        }

        // Get or refresh a service chat token
        var tokenKey = "service_chat_token";
        var now = DateTimeOffset.UtcNow;
        if (!_memoryCache.TryGetValue(tokenKey, out (string token, DateTimeOffset expires) tokenInfo) || tokenInfo.expires <= now.AddMinutes(5))
        {
            var tokenResponse = await _identityClient.GetTokenAsync(new CommunicationUserIdentifier(serviceUserId), new[] { CommunicationTokenScope.Chat });
            tokenInfo = (tokenResponse.Value.Token, tokenResponse.Value.ExpiresOn);
            var lifetime = tokenInfo.expires - now - TimeSpan.FromMinutes(5);
            if (lifetime < TimeSpan.Zero) lifetime = TimeSpan.FromMinutes(10);
            _memoryCache.Set(tokenKey, tokenInfo, lifetime);
        }

        return new ChatClient(new Uri(endpoint), new CommunicationTokenCredential(tokenInfo.token));
    }
}
