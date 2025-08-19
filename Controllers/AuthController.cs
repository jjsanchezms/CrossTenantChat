using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        public IActionResult Challenge(string tenant = "Contoso", string returnUrl = "/chat")
        {
            _logger.LogInformation("Starting authentication challenge for tenant: {Tenant}", tenant);

            var properties = new AuthenticationProperties
            {
                RedirectUri = returnUrl
            };

            // Store tenant information for later use
            properties.Items["tenant"] = tenant;
            
            if (tenant == "Fabrikam")
            {
                properties.Items["tenant_id"] = _configuration["Azure:AzureAd:FabrikamTenantId"];
                // In a real implementation, you might want to override the authority here
                // for true multi-tenant support
            }
            else
            {
                properties.Items["tenant_id"] = _configuration["Azure:AzureAd:ContosoTenantId"];
            }

            return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
        }

        [HttpGet("/logout")]
        public async Task<IActionResult> Logout()
        {
            _logger.LogInformation("User logout initiated");
            
            await HttpContext.SignOutAsync("Cookies");
            await HttpContext.SignOutAsync("oidc");
            
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