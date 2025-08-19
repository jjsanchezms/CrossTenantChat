# Generate Configuration Script
# This script reads from tenant-config.json and generates the appsettings files

param(
    [Parameter(Mandatory=$false)]
    [string]$ConfigFile = "tenant-config.json",
    
    [Parameter(Mandatory=$false)]
    [string]$OutputFile = "../appsettings.AppRegistrations.json"
)

Write-Host "Cross-Tenant ACS Demo - Configuration Generator" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

# Check if config file exists
if (-not (Test-Path $ConfigFile)) {
    Write-Host "Config file '$ConfigFile' not found!" -ForegroundColor Red
    Write-Host "Please create and fill out the tenant-config.json file first." -ForegroundColor Yellow
    exit 1
}

# Read and parse config file
try {
    $config = Get-Content $ConfigFile -Raw | ConvertFrom-Json
    Write-Host "Config file loaded successfully" -ForegroundColor Green
} catch {
    Write-Host "Failed to parse config file: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Validate required fields
$requiredFields = @(
    "tenants.contoso.tenantId",
    "tenants.contoso.appRegistration.clientId", 
    "tenants.contoso.appRegistration.clientSecret",
    "tenants.fabrikam.tenantId",
    "tenants.fabrikam.appRegistration.clientId",
    "tenants.fabrikam.appRegistration.clientSecret"
)

$missingFields = @()
foreach ($field in $requiredFields) {
    $parts = $field.Split('.')
    $value = $config
    foreach ($part in $parts) {
        if ($value.$part) {
            $value = $value.$part
        } else {
            $value = $null
            break
        }
    }
    
    if (-not $value -or $value -like "*_HERE" -or $value -eq "") {
        $missingFields += $field
    }
}

if ($missingFields.Count -gt 0) {
    Write-Host "Missing or incomplete configuration for:" -ForegroundColor Red
    foreach ($field in $missingFields) {
        Write-Host "  - $field" -ForegroundColor Yellow
    }
    Write-Host "Please fill in all required values in $ConfigFile" -ForegroundColor Yellow
    exit 1
}

# Generate appsettings configuration
Write-Host "Generating appsettings configuration..." -ForegroundColor Yellow

$appSettingsConfig = @{
    "Azure" = @{
        "AzureAd" = @{
            "ContosoApp" = @{
                "ClientId" = $config.tenants.contoso.appRegistration.clientId
                "ClientSecret" = $config.tenants.contoso.appRegistration.clientSecret
                "TenantId" = $config.tenants.contoso.tenantId
                "Authority" = "https://login.microsoftonline.com/" + $config.tenants.contoso.tenantId
            }
            "FabrikamApp" = @{
                "ClientId" = $config.tenants.fabrikam.appRegistration.clientId
                "ClientSecret" = $config.tenants.fabrikam.appRegistration.clientSecret
                "TenantId" = $config.tenants.fabrikam.tenantId
                "Authority" = "https://login.microsoftonline.com/" + $config.tenants.fabrikam.tenantId
            }
            "Instance" = "https://login.microsoftonline.com/"
            "ClientId" = $config.tenants.contoso.appRegistration.clientId
            "ClientSecret" = $config.tenants.contoso.appRegistration.clientSecret
            "TenantId" = $config.tenants.contoso.tenantId
            "ContosoTenantId" = $config.tenants.contoso.tenantId
            "FabrikamTenantId" = $config.tenants.fabrikam.tenantId
            "Authority" = "https://login.microsoftonline.com/" + $config.tenants.contoso.tenantId
            "Scopes" = @(
                "https://communication.azure.com/.default",
                "https://graph.microsoft.com/User.Read"
            )
        }
    }
}

# Add Azure Communication Services config if available
if ($config.azureCommunicationServices.connectionString -and 
    -not ($config.azureCommunicationServices.connectionString -like "*_HERE")) {
    $appSettingsConfig.Azure["AzureCommunicationServices"] = @{
        "ConnectionString" = $config.azureCommunicationServices.connectionString
        "EndpointUrl" = $config.azureCommunicationServices.endpointUrl
        "ResourceId" = $config.azureCommunicationServices.resourceId
    }
}

# Add Key Vault config if available  
if ($config.keyVault.vaultUri -and -not ($config.keyVault.vaultUri -like "*_HERE")) {
    $appSettingsConfig.Azure["KeyVault"] = @{
        "VaultUri" = $config.keyVault.vaultUri
    }
}

# Save configuration to file
try {
    $appSettingsConfig | ConvertTo-Json -Depth 10 | Set-Content $OutputFile
    Write-Host "Configuration saved to: $OutputFile" -ForegroundColor Green
} catch {
    Write-Host "Failed to save configuration: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Display summary
Write-Host ""
Write-Host "Configuration Summary:" -ForegroundColor Cyan
Write-Host "=====================" -ForegroundColor Cyan
Write-Host "Contoso Tenant ID: $($config.tenants.contoso.tenantId)" -ForegroundColor White
Write-Host "Contoso App ID: $($config.tenants.contoso.appRegistration.clientId)" -ForegroundColor White
Write-Host "Fabrikam Tenant ID: $($config.tenants.fabrikam.tenantId)" -ForegroundColor White  
Write-Host "Fabrikam App ID: $($config.tenants.fabrikam.appRegistration.clientId)" -ForegroundColor White

if ($config.azureCommunicationServices.connectionString -and 
    -not ($config.azureCommunicationServices.connectionString -like "*_HERE")) {
    Write-Host "ACS Endpoint: $($config.azureCommunicationServices.endpointUrl)" -ForegroundColor White
}

Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Green
Write-Host "1. Review the generated configuration in $OutputFile"
Write-Host "2. Run: dotnet run --environment=Live (or your preferred environment)"
Write-Host "3. Test the cross-tenant authentication flow"
Write-Host ""
Write-Host "Manual App Registration Setup Required:" -ForegroundColor Yellow
Write-Host "- In Azure portal, grant admin consent for API permissions on both apps"
Write-Host "- Verify redirect URIs are configured correctly"
Write-Host "- Configure multi-tenant access if needed"