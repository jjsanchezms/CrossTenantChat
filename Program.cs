using CrossTenantChat.Components;
using CrossTenantChat.Configuration;
using CrossTenantChat.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Core;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;

var builder = WebApplication.CreateBuilder(args);

// Configure static web assets for all environments (when running from source)
// This enables component library and wwwroot assets even when not using a publish output
StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);

// Add Azure Key Vault configuration
var keyVaultUri = builder.Configuration["Azure:KeyVault:VaultUri"];
var keyVaultEnabled = builder.Configuration.GetValue<bool?>("Azure:KeyVault:Enabled") ?? true;
var preferAzureCli = builder.Configuration.GetValue<bool>("Azure:KeyVault:PreferAzureCliCredential");
var enableInteractiveBrowser = builder.Configuration.GetValue<bool>("Azure:KeyVault:EnableInteractiveBrowserCredential");

if (keyVaultEnabled && !string.IsNullOrEmpty(keyVaultUri))
{
    try
    {
        // Build credential chain honoring configuration
        var defaultOptions = new DefaultAzureCredentialOptions { ExcludeAzurePowerShellCredential = true };
        var tenantId = builder.Configuration["Azure:AzureAd:TenantId"];
        var creds = new List<TokenCredential>();
        if (preferAzureCli) creds.Add(new AzureCliCredential());
        if (enableInteractiveBrowser)
        {
            creds.Add(new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
            {
                TenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId
            }));
        }
        creds.Add(new DefaultAzureCredential(defaultOptions));
        TokenCredential credential = creds.Count == 1 ? creds[0] : new ChainedTokenCredential(creds.ToArray());

        builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), credential);
        Console.WriteLine($"✅ Connected to Azure Key Vault: {keyVaultUri}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ Could not connect to Azure Key Vault: {keyVaultUri} - {ex.Message}");
    }
}

// Configure Azure settings
builder.Services.Configure<AzureConfiguration>(builder.Configuration.GetSection("Azure"));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MVC controllers for authentication
builder.Services.AddControllers();

// Add memory cache for live services
builder.Services.AddMemoryCache();

// Add custom services - using Live services as default
builder.Services.AddScoped<IEntraIdAuthenticationService, LiveEntraIdAuthenticationService>();
builder.Services.AddSingleton<IAzureCommunicationService, LiveAzureCommunicationService>();
builder.Services.AddSingleton<IAcsOperationTracker, AcsOperationTracker>();
Console.WriteLine("🚀 Live Azure Services registered");

// Add authentication
var azureAdClientId = builder.Configuration["Azure:AzureAd:ClientId"];
var azureAdClientSecret = builder.Configuration["Azure:AzureAd:ClientSecret"];
var azureAdTenantId = builder.Configuration["Azure:AzureAd:TenantId"];

// Only configure real authentication if we have valid Azure AD configuration
if (!string.IsNullOrEmpty(azureAdClientId) && 
    !azureAdClientId.Contains("your-client-id") && 
    !azureAdClientId.Contains("contoso-client-id") &&
    !string.IsNullOrEmpty(azureAdClientSecret) && 
    !azureAdClientSecret.Contains("your-client-secret") &&
    !string.IsNullOrEmpty(azureAdTenantId) && 
    !azureAdTenantId.Contains("your-tenant-id") &&
    !azureAdTenantId.Contains("contoso-tenant-id") &&
    !azureAdTenantId.Contains("12345678-1234-1234-1234"))
{
    builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = "Cookies";
            options.DefaultChallengeScheme = "oidc-contoso";
        })
        .AddCookie("Cookies")
        .AddOpenIdConnect("oidc-contoso", options =>
        {
            var contosoAuthority = $"{builder.Configuration["Azure:AzureAd:Instance"]?.TrimEnd('/')}/{builder.Configuration["Azure:AzureAd:ContosoTenantId"]}";
            options.Authority = contosoAuthority;
            options.ClientId = builder.Configuration["Azure:AzureAd:ContosoApp:ClientId"];
            options.ClientSecret = builder.Configuration["Azure:AzureAd:ContosoApp:ClientSecret"];
            options.CallbackPath = "/signin-oidc-contoso";
            options.ResponseType = "code";
            options.SaveTokens = true;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.Scope.Add("https://communication.azure.com/.default");
            
            // Add custom claim to identify tenant
            options.Events = new OpenIdConnectEvents
            {
                OnTokenValidated = context =>
                {
                    if (context.Principal?.Identity is ClaimsIdentity identity)
                    {
                        identity.AddClaim(new System.Security.Claims.Claim("tenant", "Contoso"));
                        identity.AddClaim(new System.Security.Claims.Claim("tenant_id", builder.Configuration["Azure:AzureAd:ContosoTenantId"] ?? ""));
                    }
                    return Task.CompletedTask;
                }
            };
        })
        .AddOpenIdConnect("oidc-fabrikam", options =>
        {
            var fabrikamAuthority = $"{builder.Configuration["Azure:AzureAd:Instance"]?.TrimEnd('/')}/{builder.Configuration["Azure:AzureAd:FabrikamTenantId"]}";
            options.Authority = fabrikamAuthority;
            options.ClientId = builder.Configuration["Azure:AzureAd:FabrikamApp:ClientId"];
            options.ClientSecret = builder.Configuration["Azure:AzureAd:FabrikamApp:ClientSecret"];
            options.CallbackPath = "/signin-oidc-fabrikam";
            options.ResponseType = "code";
            options.SaveTokens = true;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.Scope.Add("https://communication.azure.com/.default");
            
            // Add custom claim to identify tenant
            options.Events = new OpenIdConnectEvents
            {
                OnTokenValidated = context =>
                {
                    if (context.Principal?.Identity is ClaimsIdentity identity)
                    {
                        identity.AddClaim(new System.Security.Claims.Claim("tenant", "Fabrikam"));
                        identity.AddClaim(new System.Security.Claims.Claim("tenant_id", builder.Configuration["Azure:AzureAd:FabrikamTenantId"] ?? ""));
                    }
                    return Task.CompletedTask;
                }
            };
        })
        .AddJwtBearer(options =>
        {
            var contosoAuthority = $"{builder.Configuration["Azure:AzureAd:Instance"]?.TrimEnd('/')}/{builder.Configuration["Azure:AzureAd:ContosoTenantId"]}";
            options.Authority = contosoAuthority;
            options.Audience = builder.Configuration["Azure:AzureAd:ContosoApp:ClientId"];
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            };
        });

    Console.WriteLine("✅ Azure AD Authentication configured");
}
else
{
    // Demo mode - no real authentication
    builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = "Cookies";
        })
        .AddCookie("Cookies");

    Console.WriteLine("🧪 Demo mode - Authentication disabled");
}

builder.Services.AddAuthorization();

// Enhanced logging
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
    
    // Use appropriate logging level
    logging.SetMinimumLevel(LogLevel.Debug);
    logging.AddFilter("CrossTenantChat", LogLevel.Information);
    logging.AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.Information);
    logging.AddFilter("Azure", LogLevel.Information);
});

var app = builder.Build();

// Log startup information
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("🎯 Cross-Tenant Chat Application Starting");
logger.LogInformation("Configuration Sources: {ConfigSources}", 
    string.Join(", ", builder.Configuration.Sources.Select(s => s.GetType().Name)));

logger.LogInformation("🌐 Live Azure Integration Enabled");
logger.LogInformation("✅ Real Entra ID Authentication");
logger.LogInformation("✅ Live Azure Communication Services");

// Log configuration validation
var acsConnectionString = builder.Configuration["Azure:AzureCommunicationServices:ConnectionString"];
var contosoTenantId = builder.Configuration["Azure:AzureAd:ContosoTenantId"];
var fabrikamTenantId = builder.Configuration["Azure:AzureAd:FabrikamTenantId"];

logger.LogInformation("ACS Connection: {HasAcsConnection}", !string.IsNullOrEmpty(acsConnectionString) ? "✅ Configured" : "❌ Missing");
logger.LogInformation("Contoso Tenant: {ContosoTenant}", !string.IsNullOrEmpty(contosoTenantId) ? "✅ Configured" : "❌ Missing");
logger.LogInformation("Fabrikam Tenant: {FabrikamTenant}", !string.IsNullOrEmpty(fabrikamTenantId) ? "✅ Configured" : "❌ Missing");

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();

    // Only enable HTTPS redirection when an HTTPS endpoint is configured
    var urls = app.Configuration["ASPNETCORE_URLS"];
    var httpsConfigured = (!string.IsNullOrEmpty(urls) && urls.Contains("https", StringComparison.OrdinalIgnoreCase))
        || !string.IsNullOrEmpty(app.Configuration["ASPNETCORE_HTTPS_PORT"])
        || !string.IsNullOrEmpty(app.Configuration["Kestrel:Endpoints:Https:Url"]) 
        || app.Configuration.GetSection("Kestrel:Endpoints:Https").Exists();

    if (httpsConfigured)
    {
        app.UseHttpsRedirection();
    }
    else
    {
        logger.LogWarning("HTTPS redirection disabled: no HTTPS endpoint configured. Set 'ASPNETCORE_URLS' to include https or configure Kestrel endpoints to enable.");
    }
}

// Enable static files serving
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

logger.LogInformation("🚀 Cross-Tenant Chat Application Started Successfully");
logger.LogInformation("🌐 Ready for live cross-tenant authentication: Fabrikam ↔ Contoso");

app.Run();
