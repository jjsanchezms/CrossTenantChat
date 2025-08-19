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
        public async Task<IActionResult> Logout()
        {
            _logger.LogInformation("User logout initiated");
            
            // Check if we're in demo mode
            var services = HttpContext.RequestServices;
            var authSchemeProvider = services.GetRequiredService<IAuthenticationSchemeProvider>();
            var contosoScheme = await authSchemeProvider.GetSchemeAsync("oidc-contoso");
            var fabrikamScheme = await authSchemeProvider.GetSchemeAsync("oidc-fabrikam");

            await HttpContext.SignOutAsync("Cookies");
            
            if (contosoScheme != null || fabrikamScheme != null)
            {
                // Real authentication mode - determine which scheme to sign out from based on current user
                var tenantClaim = User.FindFirst("tenant")?.Value;
                if (tenantClaim == "Fabrikam" && fabrikamScheme != null)
                {
                    await HttpContext.SignOutAsync("oidc-fabrikam");
                }
                else if (contosoScheme != null)
                {
                    await HttpContext.SignOutAsync("oidc-contoso");
                }
            }
            
            return Redirect("/");
        }

        [HttpGet("/profile")]
        [Authorize]
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