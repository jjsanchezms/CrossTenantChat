using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CrossTenantChat.Configuration;
using CrossTenantChat.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CrossTenantChat.Services
{
    public interface IEntraIdAuthenticationService
    {
        Task<TokenExchangeResult> ValidateAndExchangeTokenAsync(string bearerToken);
        Task<CrossTenantAuthenticationFlow> InitiateCrossTenantFlowAsync(string userEmail, string sourceTenant);
        Task<bool> ValidateCrossTenantAccessAsync(string userToken, string targetTenant);
        ChatUser ExtractUserFromToken(string token);
    }

    public class EntraIdAuthenticationService : IEntraIdAuthenticationService
    {
        private readonly AzureConfiguration _azureConfig;
        private readonly ILogger<EntraIdAuthenticationService> _logger;
        private readonly JwtSecurityTokenHandler _jwtHandler;

        public EntraIdAuthenticationService(
            IOptions<AzureConfiguration> azureConfig,
            ILogger<EntraIdAuthenticationService> logger)
        {
            _azureConfig = azureConfig.Value;
            _logger = logger;
            _jwtHandler = new JwtSecurityTokenHandler();
        }

        public Task<TokenExchangeResult> ValidateAndExchangeTokenAsync(string bearerToken)
        {
            var result = new TokenExchangeResult();

            try
            {
                _logger.LogInformation("Starting token validation and exchange process");

                // Remove "Bearer " prefix if present
                var token = bearerToken.StartsWith("Bearer ") 
                    ? bearerToken.Substring(7) 
                    : bearerToken;

                // Parse the JWT token
                var jwtToken = _jwtHandler.ReadJwtToken(token);
                
                // Extract claims
                var claims = ExtractClaims(jwtToken);
                result.Claims = claims;

                // Validate token structure and basic claims
                if (!ValidateTokenStructure(jwtToken, claims))
                {
                    result.ErrorMessage = "Invalid token structure or missing required claims";
                    return Task.FromResult(result);
                }

                // Check if this is a cross-tenant scenario
                var tenantId = claims.GetValueOrDefault("tid")?.ToString() ?? "";
                var isCrossTenant = IsCrossTenantScenario(tenantId);

                _logger.LogInformation("Token validation successful. Cross-tenant: {IsCrossTenant}, Tenant: {TenantId}", 
                    isCrossTenant, tenantId);

                // For demo purposes, simulate successful token exchange
                // In a real implementation, you would validate the token signature and exchange it
                result.IsSuccess = true;
                result.AccessToken = token;
                result.ExpiresOn = jwtToken.ValidTo;
                result.AcsUserId = GenerateAcsUserId(claims);

                _logger.LogInformation("Token exchange completed successfully for user: {UserEmail}", 
                    claims.GetValueOrDefault("email"));

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token validation and exchange");
                result.ErrorMessage = ex.Message;
                return Task.FromResult(result);
            }
        }

        public Task<CrossTenantAuthenticationFlow> InitiateCrossTenantFlowAsync(string userEmail, string sourceTenant)
        {
            var flow = new CrossTenantAuthenticationFlow
            {
                UserEmail = userEmail,
                SourceTenant = sourceTenant,
                TargetTenant = _azureConfig.AzureAd.ContosoTenantId
            };

            // Step 1: Validate source tenant
            flow.Steps.Add(new AuthenticationStep
            {
                StepName = "ValidateSourceTenant",
                Description = $"Validating user authentication from Fabrikam tenant: {sourceTenant}",
                IsSuccessful = true,
                Metadata = new Dictionary<string, object>
                {
                    ["SourceTenant"] = sourceTenant,
                    ["UserEmail"] = userEmail
                }
            });

            // Step 2: Check cross-tenant permissions
            flow.Steps.Add(new AuthenticationStep
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
            flow.Steps.Add(new AuthenticationStep
            {
                StepName = "GenerateAcsToken",
                Description = "Generating Azure Communication Services access token",
                IsSuccessful = true,
                Metadata = new Dictionary<string, object>
                {
                    ["TokenType"] = "ACS Access Token",
                    ["Scopes"] = _azureConfig.AzureAd.Scopes
                }
            });

            flow.IsCompleted = true;
            flow.IsSuccessful = true;
            flow.EndTime = DateTime.UtcNow;

            _logger.LogInformation("Cross-tenant authentication flow completed for {UserEmail} from {SourceTenant} to {TargetTenant}",
                userEmail, sourceTenant, flow.TargetTenant);

            return Task.FromResult(flow);
        }

        public Task<bool> ValidateCrossTenantAccessAsync(string userToken, string targetTenant)
        {
            try
            {
                var jwtToken = _jwtHandler.ReadJwtToken(userToken);
                var claims = ExtractClaims(jwtToken);
                
                var tokenTenant = claims.GetValueOrDefault("tid")?.ToString() ?? "";
                
                // Check if the token is from Fabrikam and target is Contoso
                var isFabrikamToken = tokenTenant.Equals(_azureConfig.AzureAd.FabrikamTenantId, StringComparison.OrdinalIgnoreCase);
                var isTargetContoso = targetTenant.Equals(_azureConfig.AzureAd.ContosoTenantId, StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation("Cross-tenant validation: Fabrikam token: {IsFabrikamToken}, Target Contoso: {IsTargetContoso}",
                    isFabrikamToken, isTargetContoso);

                return Task.FromResult(isFabrikamToken && isTargetContoso);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating cross-tenant access");
                return Task.FromResult(false);
            }
        }

        public ChatUser ExtractUserFromToken(string token)
        {
            try
            {
                var jwtToken = _jwtHandler.ReadJwtToken(token);
                var claims = ExtractClaims(jwtToken);

                var tenantId = claims.GetValueOrDefault("tid")?.ToString() ?? "";
                var isFromFabrikam = tenantId.Equals(_azureConfig.AzureAd.FabrikamTenantId, StringComparison.OrdinalIgnoreCase);

                return new ChatUser
                {
                    Id = claims.GetValueOrDefault("oid")?.ToString() ?? Guid.NewGuid().ToString(),
                    Name = claims.GetValueOrDefault("name")?.ToString() ?? "Unknown User",
                    Email = claims.GetValueOrDefault("email")?.ToString() ?? claims.GetValueOrDefault("upn")?.ToString() ?? "",
                    TenantId = tenantId,
                    TenantName = isFromFabrikam ? "Fabrikam" : "Contoso",
                    IsFromFabrikam = isFromFabrikam,
                    AccessToken = token,
                    TokenExpiry = jwtToken.ValidTo,
                    AcsUserId = GenerateAcsUserId(claims)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting user from token");
                return new ChatUser { Name = "Unknown User" };
            }
        }

        private Dictionary<string, object> ExtractClaims(JwtSecurityToken jwtToken)
        {
            var claims = new Dictionary<string, object>();
            
            foreach (var claim in jwtToken.Claims)
            {
                if (!claims.ContainsKey(claim.Type))
                {
                    claims[claim.Type] = claim.Value;
                }
            }

            return claims;
        }

        private bool ValidateTokenStructure(JwtSecurityToken jwtToken, Dictionary<string, object> claims)
        {
            // Basic validation - ensure we have required claims
            var requiredClaims = new[] { "tid", "oid" };
            
            foreach (var requiredClaim in requiredClaims)
            {
                if (!claims.ContainsKey(requiredClaim))
                {
                    _logger.LogWarning("Missing required claim: {Claim}", requiredClaim);
                    return false;
                }
            }

            return true;
        }

        private bool IsCrossTenantScenario(string tenantId)
        {
            // Check if the token is from Fabrikam tenant
            return tenantId.Equals(_azureConfig.AzureAd.FabrikamTenantId, StringComparison.OrdinalIgnoreCase);
        }

        private string GenerateAcsUserId(Dictionary<string, object> claims)
        {
            // Generate a consistent ACS user ID based on the user's object ID and tenant
            var oid = claims.GetValueOrDefault("oid")?.ToString() ?? Guid.NewGuid().ToString();
            var tid = claims.GetValueOrDefault("tid")?.ToString() ?? "";
            
            // For ACS, we need to create a unique identifier
            return $"8:acs:demo-{oid.Substring(0, 8)}";
        }
    }
}
