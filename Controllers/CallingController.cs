using Azure.Communication;
using Azure.Communication.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

namespace CrossTenantChat.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CallingController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<CallingController> _logger;
    private readonly IMemoryCache _cache;

    public CallingController(IConfiguration configuration, ILogger<CallingController> logger, IMemoryCache cache)
    {
        _configuration = configuration;
        _logger = logger;
        _cache = cache;
    }

    // Returns an ACS token with VoIP scope for Calling SDK
    [HttpGet("token")]
    [Authorize]
    public async Task<IActionResult> GetCallingToken()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("oid")?.Value
                     ?? User.FindFirst("sub")?.Value
                     ?? "anonymous";

        var acsConnectionString = _configuration["Azure:AzureCommunicationServices:ConnectionString"]; 
        if (string.IsNullOrWhiteSpace(acsConnectionString) ||
            acsConnectionString.Contains("your-acs-connection-string-here", StringComparison.OrdinalIgnoreCase) ||
            acsConnectionString.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("ACS Calling requested but no valid connection string configured");
            return Problem(title: "ACS not configured", detail: "Calling requires a valid Azure Communication Services connection string.", statusCode: 501);
        }

        try
        {
            var cacheKeyUser = $"calling_comm_user:{userId}";
            var cacheKeyToken = $"calling_voip_token:{userId}";

            // Ensure a stable Communication User ID per app user
            if (!_cache.TryGetValue(cacheKeyUser, out string? commUserId) || string.IsNullOrEmpty(commUserId))
            {
                var idClient = new CommunicationIdentityClient(acsConnectionString);
                var createResp = await idClient.CreateUserAsync();
                commUserId = createResp.Value.Id;
                _cache.Set(cacheKeyUser, commUserId, TimeSpan.FromDays(1));
            }

            // Issue/refresh VoIP token
            if (!_cache.TryGetValue(cacheKeyToken, out (string token, DateTimeOffset expiresOn) tokenInfo) || tokenInfo.expiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
            {
                var idClient = new CommunicationIdentityClient(acsConnectionString);
                var tokenResp = await idClient.GetTokenAsync(new CommunicationUserIdentifier(commUserId), new[] { CommunicationTokenScope.VoIP });
                tokenInfo = (tokenResp.Value.Token, tokenResp.Value.ExpiresOn);
                var lifetime = tokenInfo.expiresOn - DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
                if (lifetime < TimeSpan.Zero) lifetime = TimeSpan.FromMinutes(10);
                _cache.Set(cacheKeyToken, tokenInfo, lifetime);
            }

            return Ok(new { token = tokenInfo.token, userId = commUserId, expiresOn = tokenInfo.expiresOn });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error issuing ACS Calling token");
            return Problem(title: "Token issuance failed", detail: ex.Message, statusCode: 500);
        }
    }
}
