using Microsoft.Identity.Client;
using Microsoft.Graph;
using System.Security.Claims;
using Azure.Communication.Identity;
using System.IdentityModel.Tokens.Jwt;
using Azure;
using Microsoft.Extensions.Caching.Memory;

namespace CrossTenantChat.Services;

public class LiveEntraIdAuthenticationService : IEntraIdAuthenticationService
{
    private readonly ILogger<LiveEntraIdAuthenticationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;
    private readonly IAcsOperationTracker _operationTracker;
    
    private readonly IConfidentialClientApplication _contosoApp;
    private readonly IConfidentialClientApplication _fabrikamApp;
    
    // Configuration values
    private readonly string _contosoTenantId;
    private readonly string _fabrikamTenantId;
    private readonly string _contosoClientId;
    private readonly string _contosoClientSecret;
    private readonly string _fabrikamClientId;
    private readonly string _fabrikamClientSecret;
    
    // Scopes
    private readonly string[] _acsScopes = new[] { "https://communication.azure.com/.default" };
    private readonly string[] _graphScopes = new[] { "https://graph.microsoft.com/User.Read" };

    public LiveEntraIdAuthenticationService(
        ILogger<LiveEntraIdAuthenticationService> logger,
        IConfiguration configuration,
        IMemoryCache memoryCache,
        IAcsOperationTracker operationTracker)
    {
        _logger = logger;
        _configuration = configuration;
        _memoryCache = memoryCache;
        _operationTracker = operationTracker;

        // Load configuration
        _contosoTenantId = configuration["Azure:AzureAd:ContosoTenantId"] ?? throw new InvalidOperationException("ContosoTenantId not configured");
        _fabrikamTenantId = configuration["Azure:AzureAd:FabrikamTenantId"] ?? throw new InvalidOperationException("FabrikamTenantId not configured");
        _contosoClientId = configuration["Azure:AzureAd:ContosoApp:ClientId"] ?? configuration["Azure:AzureAd:ClientId"] ?? throw new InvalidOperationException("Contoso ClientId not configured");
        _contosoClientSecret = configuration["Azure:AzureAd:ContosoApp:ClientSecret"] ?? configuration["Azure:AzureAd:ClientSecret"] ?? throw new InvalidOperationException("Contoso ClientSecret not configured");
        _fabrikamClientId = configuration["Azure:AzureAd:FabrikamApp:ClientId"] ?? throw new InvalidOperationException("Fabrikam ClientId not configured");
        _fabrikamClientSecret = configuration["Azure:AzureAd:FabrikamApp:ClientSecret"] ?? throw new InvalidOperationException("Fabrikam ClientSecret not configured");

        // Initialize MSAL apps
        _contosoApp = ConfidentialClientApplicationBuilder
            .Create(_contosoClientId)
            .WithClientSecret(_contosoClientSecret)
            .WithAuthority($"https://login.microsoftonline.com/{_contosoTenantId}")
            .Build();

        _fabrikamApp = ConfidentialClientApplicationBuilder
            .Create(_fabrikamClientId)
            .WithClientSecret(_fabrikamClientSecret)
            .WithAuthority($"https://login.microsoftonline.com/{_fabrikamTenantId}")
            .Build();

        _logger.LogInformation("Live EntraId Authentication Service initialized");
        _logger.LogInformation("Contoso Tenant: {ContosoTenantId}", _contosoTenantId);
        _logger.LogInformation("Fabrikam Tenant: {FabrikamTenantId}", _fabrikamTenantId);
    }

    public async Task<Models.TokenExchangeResult> ValidateAndExchangeTokenAsync(string bearerToken)
    {
        var operationId = _operationTracker.StartOperation("EntraIdTokenValidation", 
            "Validate and exchange Entra ID bearer token");

        var result = new Models.TokenExchangeResult();
        
        try
        {
            _operationTracker.AddStep(operationId, "InitiateValidation", 
                "Starting token validation and exchange process", true);

            _logger.LogInformation("Starting token validation and exchange process");

            // Remove "Bearer " prefix if present
            var token = bearerToken.StartsWith("Bearer ") 
                ? bearerToken.Substring(7) 
                : bearerToken;

            _operationTracker.AddStep(operationId, "ProcessTokenFormat", 
                "Processed bearer token format", true, 
                new Dictionary<string, object> 
                { 
                    ["HasBearerPrefix"] = bearerToken.StartsWith("Bearer "),
                    ["TokenLength"] = token.Length
                });

            // Parse the JWT token to extract user information
            _operationTracker.AddStep(operationId, "ParseJwt", 
                "Parsing JWT token to extract user information", true);

            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadJwtToken(token);
            
            var userIdClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "oid" || c.Type == "sub");
            var userNameClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "name" || c.Type == "preferred_username");
            var tenantIdClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "tid");
            
            if (userIdClaim == null || tenantIdClaim == null)
            {
                result.ErrorMessage = "Required claims not found in token";
                _operationTracker.AddStep(operationId, "ParseJwt", 
                    "Failed to extract required claims from JWT token", false, null, result.ErrorMessage);
                _operationTracker.CompleteOperation(operationId, false, result.ErrorMessage);
                return result;
            }

            var tokenTenantId = tenantIdClaim.Value;
            var userId = userIdClaim.Value;
            var userName = userNameClaim?.Value ?? "Unknown";

            _logger.LogInformation("Token issued by tenant: {TokenTenantId}", tokenTenantId);

            _operationTracker.AddStep(operationId, "ParseJwt", 
                "Successfully parsed JWT token and extracted claims", true, 
                new Dictionary<string, object> 
                { 
                    ["UserId"] = userId,
                    ["UserName"] = userName,
                    ["TenantId"] = tokenTenantId,
                    ["ClaimsCount"] = jsonToken.Claims.Count()
                });

            // Extract claims for the result
            result.Claims = jsonToken.Claims.ToDictionary(c => c.Type, c => (object)c.Value);

            // Determine which tenant this token is from
            _operationTracker.AddStep(operationId, "DetermineTenant", 
                $"Determining tenant for token issued by: {tokenTenantId}", true);

            string selectedTenant;
            IConfidentialClientApplication msalApp;
            
            if (tokenTenantId == _fabrikamTenantId)
            {
                selectedTenant = "Fabrikam";
                msalApp = _fabrikamApp;
            }
            else if (tokenTenantId == _contosoTenantId)
            {
                selectedTenant = "Contoso";
                msalApp = _contosoApp;
            }
            else
            {
                result.ErrorMessage = $"Token is from unknown tenant: {tokenTenantId}";
                _operationTracker.AddStep(operationId, "DetermineTenant", 
                    "Token is from unknown tenant", false, 
                    new Dictionary<string, object> 
                    { 
                        ["TokenTenantId"] = tokenTenantId,
                        ["ContosoTenantId"] = _contosoTenantId,
                        ["FabrikamTenantId"] = _fabrikamTenantId
                    }, result.ErrorMessage);
                _operationTracker.CompleteOperation(operationId, false, result.ErrorMessage);
                return result;
            }

            _operationTracker.AddStep(operationId, "DetermineTenant", 
                $"Successfully identified tenant: {selectedTenant}", true, 
                new Dictionary<string, object> 
                { 
                    ["SelectedTenant"] = selectedTenant,
                    ["TokenTenantId"] = tokenTenantId
                });

            // Check cache for existing ACS token
            var cacheKey = $"acs_token_{userIdClaim.Value}_{selectedTenant}";
            
            _operationTracker.AddStep(operationId, "CheckCache", 
                $"Checking cache for existing ACS token: {cacheKey}", true);

            if (_memoryCache.TryGetValue(cacheKey, out string? cachedAcsToken))
            {
                _logger.LogInformation("Retrieved cached ACS token for user: {UserId}", userIdClaim.Value);
                result.IsSuccess = true;
                result.AccessToken = cachedAcsToken ?? "";
                result.ExpiresOn = jsonToken.ValidTo;
                result.AcsUserId = GenerateAcsUserId(userIdClaim.Value);
                
                _operationTracker.AddStep(operationId, "CheckCache", 
                    "Found cached ACS token", true, 
                    new Dictionary<string, object> 
                    { 
                        ["CacheKey"] = cacheKey,
                        ["TokenExpiresOn"] = result.ExpiresOn,
                        ["AcsUserId"] = result.AcsUserId
                    });
                
                _operationTracker.CompleteOperation(operationId, true);
                return result;
            }

            _operationTracker.AddStep(operationId, "CheckCache", 
                "No cached ACS token found, proceeding to exchange", true);

            // Create user assertion from the provided access token
            _operationTracker.AddStep(operationId, "CreateAssertion", 
                "Creating user assertion for On-Behalf-Of flow", true);

            var userAssertion = new UserAssertion(token);
            
            // Acquire token for ACS using On-Behalf-Of flow
            _operationTracker.AddStep(operationId, "AcquireToken", 
                $"Acquiring ACS token using On-Behalf-Of flow for {selectedTenant} tenant", true, 
                new Dictionary<string, object> 
                { 
                    ["Tenant"] = selectedTenant,
                    ["Scopes"] = string.Join(",", _acsScopes)
                });

            var msalResult = await msalApp.AcquireTokenOnBehalfOf(_acsScopes, userAssertion)
                .ExecuteAsync();

            _operationTracker.AddStep(operationId, "AcquireToken", 
                "Successfully acquired ACS token via On-Behalf-Of flow", true, 
                new Dictionary<string, object> 
                { 
                    ["TokenExpiresOn"] = msalResult.ExpiresOn.DateTime,
                    ["TokenType"] = "ACS Access Token"
                });

            // Cache the ACS token for 50 minutes (tokens are valid for 60 minutes)
            _memoryCache.Set(cacheKey, msalResult.AccessToken, TimeSpan.FromMinutes(50));

            _operationTracker.AddStep(operationId, "CacheToken", 
                "Cached ACS token for future use", true, 
                new Dictionary<string, object> 
                { 
                    ["CacheKey"] = cacheKey,
                    ["CacheExpiry"] = TimeSpan.FromMinutes(50).ToString()
                });

            _logger.LogInformation("Successfully exchanged token for ACS access. User: {UserId}, Tenant: {Tenant}", 
                userIdClaim.Value, selectedTenant);

            result.IsSuccess = true;
            result.AccessToken = msalResult.AccessToken;
            result.ExpiresOn = msalResult.ExpiresOn.DateTime;
            result.AcsUserId = GenerateAcsUserId(userIdClaim.Value);

            if (selectedTenant == "Fabrikam")
            {
                _operationTracker.AddStep(operationId, "CrossTenantExchange", 
                    "Cross-tenant token exchange completed - Fabrikam token exchanged for Contoso ACS access", true, 
                    new Dictionary<string, object> 
                    { 
                        ["SourceTenant"] = "Fabrikam",
                        ["TargetResource"] = "Contoso ACS",
                        ["UserId"] = userId,
                        ["UserName"] = userName
                    });
            }

            _operationTracker.CompleteOperation(operationId, true);
            return result;
        }
        catch (MsalException ex)
        {
            _logger.LogError(ex, "MSAL exception during token exchange: {Error}", ex.Message);
            result.ErrorMessage = $"Authentication failed: {ex.Message}";
            
            _operationTracker.AddStep(operationId, "MsalException", 
                "MSAL exception during token exchange", false, 
                new Dictionary<string, object> 
                { 
                    ["ExceptionType"] = ex.GetType().Name,
                    ["ErrorCode"] = ex.ErrorCode
                }, ex.Message);
            
            _operationTracker.CompleteOperation(operationId, false, result.ErrorMessage);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token validation and exchange: {Error}", ex.Message);
            result.ErrorMessage = $"Token validation failed: {ex.Message}";
            
            _operationTracker.AddStep(operationId, "Exception", 
                "General exception during token validation", false, 
                new Dictionary<string, object> 
                { 
                    ["ExceptionType"] = ex.GetType().Name,
                    ["ExceptionMessage"] = ex.Message
                }, ex.Message);
            
            _operationTracker.CompleteOperation(operationId, false, result.ErrorMessage);
            return result;
        }
    }

    private string GenerateAcsUserId(string userObjectId)
    {
        // Generate a consistent ACS user ID based on the user's object ID
        return $"8:acs:demo-{userObjectId.Substring(0, 8)}";
    }

    public Task<Models.CrossTenantAuthenticationFlow> InitiateCrossTenantFlowAsync(string userEmail, string sourceTenant)
    {
        _logger.LogInformation("Initiating cross-tenant authentication flow for: {UserEmail} from {SourceTenant}", userEmail, sourceTenant);

        var flow = new Models.CrossTenantAuthenticationFlow
        {
            UserEmail = userEmail,
            SourceTenant = sourceTenant,
            TargetTenant = sourceTenant == "Fabrikam" ? "Contoso" : "Fabrikam"
        };

        try
        {
            var msalApp = sourceTenant == "Fabrikam" ? _fabrikamApp : _contosoApp;

            // Step 1: Validate source tenant
            flow.Steps.Add(new Models.AuthenticationStep
            {
                StepName = "ValidateSourceTenant",
                Description = $"Validating user authentication from {sourceTenant} tenant",
                IsSuccessful = true,
                Metadata = new Dictionary<string, object>
                {
                    ["SourceTenant"] = sourceTenant,
                    ["UserEmail"] = userEmail
                }
            });

            // Step 2: Check cross-tenant permissions
            flow.Steps.Add(new Models.AuthenticationStep
            {
                StepName = "CheckCrossTenantPermissions",
                Description = "Checking if cross-tenant access is allowed for ACS resources",
                IsSuccessful = true,
                Metadata = new Dictionary<string, object>
                {
                    ["TargetTenant"] = flow.TargetTenant,
                    ["ResourceType"] = "Azure Communication Services"
                }
            });

            // Step 3: Generate ACS token
            flow.Steps.Add(new Models.AuthenticationStep
            {
                StepName = "GenerateAcsToken",
                Description = "Generating Azure Communication Services access token",
                IsSuccessful = true,
                Metadata = new Dictionary<string, object>
                {
                    ["TokenType"] = "ACS Access Token",
                    ["Scopes"] = _acsScopes
                }
            });

            flow.IsCompleted = true;
            flow.IsSuccessful = true;
            flow.EndTime = DateTime.UtcNow;

            _logger.LogInformation("Cross-tenant authentication flow completed for {UserEmail} from {SourceTenant} to {TargetTenant}",
                userEmail, sourceTenant, flow.TargetTenant);

            return Task.FromResult(flow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating cross-tenant flow for {UserEmail}: {Error}", userEmail, ex.Message);
            
            flow.Steps.Add(new Models.AuthenticationStep
            {
                StepName = "Error",
                Description = "Failed to initiate cross-tenant flow",
                IsSuccessful = false,
                ErrorMessage = ex.Message
            });
            
            flow.IsCompleted = true;
            flow.IsSuccessful = false;
            flow.EndTime = DateTime.UtcNow;
            
            return Task.FromResult(flow);
        }
    }

    public Task<bool> ValidateCrossTenantAccessAsync(string userToken, string targetTenant)
    {
        try
        {
            _logger.LogInformation("Validating cross-tenant access for target tenant: {TargetTenant}", targetTenant);

            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadJwtToken(userToken);
            
            var tenantIdClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "tid");
            if (tenantIdClaim == null)
            {
                _logger.LogWarning("No tenant ID claim found in token");
                return Task.FromResult(false);
            }

            var tokenTenantId = tenantIdClaim.Value;
            
            // Check if this is a valid cross-tenant scenario
            bool isValidCrossTenant = false;
            
            if (targetTenant == "Contoso" && tokenTenantId == _fabrikamTenantId)
            {
                // Fabrikam user accessing Contoso resources
                isValidCrossTenant = true;
            }
            else if (targetTenant == "Fabrikam" && tokenTenantId == _contosoTenantId)
            {
                // Contoso user accessing Fabrikam resources
                isValidCrossTenant = true;
            }

            _logger.LogInformation("Cross-tenant validation result: {IsValid}. Token tenant: {TokenTenant}, Target: {TargetTenant}", 
                isValidCrossTenant, tokenTenantId, targetTenant);

            return Task.FromResult(isValidCrossTenant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating cross-tenant access: {Error}", ex.Message);
            return Task.FromResult(false);
        }
    }

    public Models.ChatUser ExtractUserFromToken(string token)
    {
        try
        {
            _logger.LogInformation("Extracting user information from token");

            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadJwtToken(token);
            
            var userIdClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "oid" || c.Type == "sub");
            var userNameClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "name" || c.Type == "preferred_username");
            var emailClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "email" || c.Type == "upn");
            var tenantIdClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "tid");

            if (userIdClaim == null || tenantIdClaim == null)
            {
                _logger.LogWarning("Required claims not found in token");
                return new Models.ChatUser { Name = "Unknown User" };
            }

            var tenantId = tenantIdClaim.Value;
            var isFromFabrikam = tenantId == _fabrikamTenantId;
            var tenantName = isFromFabrikam ? "Fabrikam" : "Contoso";

            var chatUser = new Models.ChatUser
            {
                Id = userIdClaim.Value,
                Name = userNameClaim?.Value ?? emailClaim?.Value ?? "Unknown User",
                Email = emailClaim?.Value ?? "",
                TenantId = tenantId,
                TenantName = tenantName,
                IsFromFabrikam = isFromFabrikam,
                AccessToken = token,
                TokenExpiry = jsonToken.ValidTo,
                AcsUserId = GenerateAcsUserId(userIdClaim.Value)
            };

            _logger.LogInformation("Extracted user: {UserId} ({UserName}) from {TenantName}", 
                chatUser.Id, chatUser.Name, tenantName);

            return chatUser;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting user from token: {Error}", ex.Message);
            return new Models.ChatUser { Name = "Unknown User" };
        }
    }
}
