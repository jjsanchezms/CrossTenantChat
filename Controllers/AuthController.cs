using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Extensions;
using System.Security.Claims;

namespace CrossTenantChat.Controllers
{
    [AllowAnonymous]
    public class AuthController : Controller
    {
        private readonly ILogger<AuthController> _logger;
        private readonly IConfiguration _configuration;

        public AuthController(ILogger<AuthController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet("/challenge/oidc")]
        public async Task<IActionResult> Challenge(string tenant = "Contoso", string returnUrl = "/chat")
        {
            _logger.LogInformation("🚀 Starting authentication challenge for tenant: {Tenant}", tenant);
            _logger.LogInformation("📍 Return URL: {ReturnUrl}", returnUrl);
            _logger.LogInformation("🌐 Request URL: {RequestUrl}", HttpContext.Request.GetDisplayUrl());

            // Check if we're in demo mode (OpenID Connect not configured)
            var services = HttpContext.RequestServices;
            var authSchemeProvider = services.GetRequiredService<IAuthenticationSchemeProvider>();
            
            // Check for tenant-specific schemes
            var contosoScheme = await authSchemeProvider.GetSchemeAsync("oidc-contoso");
            var fabrikamScheme = await authSchemeProvider.GetSchemeAsync("oidc-fabrikam");

            if (contosoScheme == null && fabrikamScheme == null)
            {
                // Demo mode - simulate authentication
                _logger.LogInformation("🧪 Demo mode detected: Simulating authentication for tenant {Tenant}", tenant);
                
                var claims = new List<System.Security.Claims.Claim>
                {
                    new("sub", Guid.NewGuid().ToString()),
                    new("name", $"Demo User ({tenant})"),
                    new("email", $"demo.user@{tenant.ToLower()}.com"),
                    new("tenant", tenant),
                    new("tenant_id", tenant == "Fabrikam" ? "307083d3-52ba-4934-a29a-97cefcedc6a6" : "8c7f97d6-f873-4732-a074-8d2ab93da886")
                };

                var identity = new System.Security.Claims.ClaimsIdentity(claims, "Demo");
                var principal = new System.Security.Claims.ClaimsPrincipal(identity);

                _logger.LogInformation("✅ Demo authentication successful. Claims created: {ClaimCount}", claims.Count);
                _logger.LogInformation("🍪 Signing in with cookie authentication");

                await HttpContext.SignInAsync("Cookies", principal);
                
                _logger.LogInformation("🏁 Redirecting to: {ReturnUrl}", returnUrl);
                return Redirect(returnUrl);
            }

            _logger.LogInformation("🔐 Real authentication mode detected");
            
            // Real authentication mode - determine the correct scheme for the tenant
            string authScheme;
            if (tenant == "Fabrikam")
            {
                authScheme = "oidc-fabrikam";
                _logger.LogInformation("🏢 Using Fabrikam authentication scheme");
            }
            else
            {
                authScheme = "oidc-contoso";
                _logger.LogInformation("🏛️ Using Contoso authentication scheme");
            }
            
            // Real authentication mode
            var properties = new AuthenticationProperties
            {
                RedirectUri = returnUrl
            };

            // Store tenant information for later use
            properties.Items["tenant"] = tenant;
            
            if (tenant == "Fabrikam")
            {
                properties.Items["tenant_id"] = _configuration["Azure:AzureAd:FabrikamTenantId"];
                _logger.LogInformation("🏢 Configured for Fabrikam tenant authentication");
            }
            else
            {
                properties.Items["tenant_id"] = _configuration["Azure:AzureAd:ContosoTenantId"];
                _logger.LogInformation("🏛️ Configured for Contoso tenant authentication");
            }

            _logger.LogInformation("🌐 Initiating OpenID Connect challenge with scheme: {AuthScheme}", authScheme);
            return Challenge(properties, authScheme);
        }

        [HttpGet("/logout")]
        public async Task<IActionResult> Logout([FromQuery] string? returnUrl = "/")
        {
            _logger.LogInformation("User logout initiated");

            var services = HttpContext.RequestServices;
            var authSchemeProvider = services.GetRequiredService<IAuthenticationSchemeProvider>();
            var contosoScheme = await authSchemeProvider.GetSchemeAsync("oidc-contoso");
            var fabrikamScheme = await authSchemeProvider.GetSchemeAsync("oidc-fabrikam");

            // If real OIDC auth is configured, sign out from both Cookie and the correct OIDC scheme
            if (contosoScheme != null || fabrikamScheme != null)
            {
                var tenantClaim = User.FindFirst("tenant")?.Value;
                string? scheme = null;
                if (tenantClaim == "Fabrikam" && fabrikamScheme != null)
                {
                    scheme = "oidc-fabrikam";
                    _logger.LogInformation("Signing out using Fabrikam OIDC scheme");
                }
                else if (contosoScheme != null)
                {
                    scheme = "oidc-contoso";
                    _logger.LogInformation("Signing out using Contoso OIDC scheme");
                }

                var safeReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
                var props = new AuthenticationProperties { RedirectUri = safeReturnUrl };

                if (scheme != null)
                {
                    // Returning SignOut issues the end-session request to the IdP and clears the cookie
                    return SignOut(props, "Cookies", scheme);
                }
            }

            // Demo mode or no OIDC scheme available: clear cookie and redirect locally
            await HttpContext.SignOutAsync("Cookies");
            var localUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
            return LocalRedirect(localUrl);
        }

        [HttpGet("/profile")]
        public IActionResult Profile()
        {
            return Json(new
            {
                IsAuthenticated = User.Identity?.IsAuthenticated ?? false,
                Name = User.Identity?.Name,
                Claims = User.Claims.Select(c => new { c.Type, c.Value })
            });
        }
    }
}