# Cross-Tenant ACS Demo - App Registrations Setup
# This script creates the required Entra ID app registrations in both tenants

param(
    [Parameter(Mandatory=$true)]
    [string]$ContosoTenantId,
    
    [Parameter(Mandatory=$true)]
    [string]$FabrikamTenantId,
    
    [Parameter(Mandatory=$false)]
    [string]$AppName = "CrossTenantChatApp"
)

Write-Host "Cross-Tenant ACS Demo - App Registrations Setup" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

# Ensure TLS 1.2 for PSGallery
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}

# Ensure Az module is available and imported
$requiredAzModules = @("Az.Accounts", "Az.Resources")
foreach ($mod in $requiredAzModules) {
    if (-not (Get-Module -ListAvailable -Name $mod)) {
        Write-Host "Azure PowerShell module '$mod' not found. Attempting to install..." -ForegroundColor Yellow
        try {
            $gallery = Get-PSRepository -Name 'PSGallery' -ErrorAction SilentlyContinue
            if (-not $gallery) { Set-PSRepository -Name 'PSGallery' -InstallationPolicy Trusted -ErrorAction SilentlyContinue }
            Install-Module -Name $mod -Scope CurrentUser -Force -AllowClobber -ErrorAction Stop
        } catch {
            Write-Host "Failed to install PowerShell module '$mod'. Error: $($_.Exception.Message)" -ForegroundColor Red
            Write-Host "If another session is locking 'Az.Accounts', close other PowerShell/VS Code terminals and retry, or run: 'Install-Module -Name Az -Scope CurrentUser -Force'" -ForegroundColor Yellow
            exit 1
        }
    }
    if (-not (Get-Module -Name $mod)) {
        try { Import-Module $mod -ErrorAction Stop } catch {
            Write-Host "Failed to import module '$mod'. Error: $($_.Exception.Message)" -ForegroundColor Red
            exit 1
        }
    }
}

# Function to create app registration
function New-AppRegistration {
    param(
        [string]$TenantId,
        [string]$DisplayName,
        [string]$TenantName,
        [array]$RequiredResourceAccess,
        [array]$RedirectUris
    )
    
    $graphScopes = @(
        "https://graph.microsoft.com/.default",
        "User.Read",
        "Application.ReadWrite.All",
        "Directory.AccessAsUser.All"
    )

    Write-Host "Creating app registration in $TenantName tenant..." -ForegroundColor Yellow
    
    # Connect to tenant
    try {
        # Ensure login with Graph scopes so MSGraph cmdlets can call Microsoft Graph
        Connect-AzAccount -TenantId $TenantId -Force -Scope $graphScopes -ErrorAction Stop | Out-Null
    Write-Host "Connected to $TenantName tenant" -ForegroundColor Green
    } catch {
    Write-Host "Failed to connect to $TenantName tenant: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
    
    try {
        # Create app registration
        $app = New-AzADApplication -DisplayName $DisplayName -RequiredResourceAccess $RequiredResourceAccess -Web @{
            RedirectUris = $RedirectUris
            ImplicitGrantSettings = @{
                EnableAccessTokenIssuance = $true
                EnableIdTokenIssuance = $true
            }
        }
        
        # Create service principal
        New-AzADServicePrincipal -ApplicationId $app.AppId | Out-Null
        
        # Create client secret
        $secret = New-AzADAppCredential -ApplicationId $app.AppId -DisplayName "ClientSecret"
        
    Write-Host "App registration created successfully in $TenantName" -ForegroundColor Green
        Write-Host "   Application ID: $($app.AppId)"
        Write-Host "   Object ID: $($app.Id)"
        
        return @{
            AppId = $app.AppId
            ObjectId = $app.Id
            Secret = $secret.SecretText
            TenantId = $TenantId
        }
    } catch {
    Write-Host "Failed to create app registration in $TenantName`: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

# Define required permissions
$MicrosoftGraphPermissions = @{
    ResourceAppId = "00000003-0000-0000-c000-000000000000"  # Microsoft Graph
    ResourceAccess = @(
        @{
            Id = "e1fe6dd8-ba31-4d61-89e7-88639da4683d"  # User.Read
            Type = "Scope"
        }
    )
}

$AzureCommunicationServicesPermissions = @{
    ResourceAppId = "1fd5118e-2576-4263-8130-9503064c837a"  # Azure Communication Services
    ResourceAccess = @(
        @{
            Id = "692bb921-b1be-4ad8-bdc1-d4cdc8bc54b5"  # Communication.Default
            Type = "Scope"
        }
    )
}

$RequiredResourceAccess = @($MicrosoftGraphPermissions, $AzureCommunicationServicesPermissions)
$RedirectUris = @(
    "https://localhost:5001/signin-oidc",
    "http://localhost:5000/signin-oidc"
)

# Create app registration in Contoso tenant (host tenant)
Write-Host "Setting up Contoso (Host) Tenant App Registration" -ForegroundColor Blue
$ContosoApp = New-AppRegistration -TenantId $ContosoTenantId -DisplayName "$AppName-Contoso" -TenantName "Contoso" -RequiredResourceAccess $RequiredResourceAccess -RedirectUris $RedirectUris

if (-not $ContosoApp) {
    Write-Host "Failed to create Contoso app registration. Exiting." -ForegroundColor Red
    exit 1
}

# Create app registration in Fabrikam tenant (external tenant)
Write-Host ""
Write-Host "Setting up Fabrikam (External) Tenant App Registration" -ForegroundColor Blue
$FabrikamApp = New-AppRegistration -TenantId $FabrikamTenantId -DisplayName "$AppName-Fabrikam" -TenantName "Fabrikam" -RequiredResourceAccess $RequiredResourceAccess -RedirectUris $RedirectUris

if (-not $FabrikamApp) {
    Write-Host "Failed to create Fabrikam app registration. Exiting." -ForegroundColor Red
    exit 1
}

# Configure cross-tenant access for Contoso app
Write-Host ""
Write-Host "Configuring cross-tenant access..." -ForegroundColor Yellow

try {
    # Connect back to Contoso tenant
    Connect-AzAccount -TenantId $ContosoTenantId -Force -Scope @("https://graph.microsoft.com/.default","Application.ReadWrite.All") | Out-Null
    
    # Update Contoso app to accept tokens from Fabrikam tenant
    Update-AzADApplication -ObjectId $ContosoApp.ObjectId -SignInAudience "AzureADMultipleOrgs"
    Write-Host "Configured Contoso app for multi-tenant access" -ForegroundColor Green
    
} catch {
    Write-Host "Warning: Could not configure multi-tenant access automatically: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host "Please configure this manually in the Azure portal." -ForegroundColor Yellow
}

# Generate configuration file
$ConfigurationFile = "../appsettings.AppRegistrations.json"
Write-Host ""
Write-Host "Generating configuration file: $ConfigurationFile" -ForegroundColor Yellow

$appRegistrationConfig = @{
    "Azure" = @{
        "AzureAd" = @{
            "ContosoApp" = @{
                "ClientId" = $ContosoApp.AppId
                "ClientSecret" = $ContosoApp.Secret
                "TenantId" = $ContosoApp.TenantId
                "Authority" = "https://login.microsoftonline.com/" + $ContosoApp.TenantId
            }
            "FabrikamApp" = @{
                "ClientId" = $FabrikamApp.AppId
                "ClientSecret" = $FabrikamApp.Secret
                "TenantId" = $FabrikamApp.TenantId
                "Authority" = "https://login.microsoftonline.com/" + $FabrikamApp.TenantId
            }
            "Instance" = "https://login.microsoftonline.com/"
            "ClientId" = $ContosoApp.AppId
            "ClientSecret" = $ContosoApp.Secret
            "TenantId" = $ContosoApp.TenantId
            "ContosoTenantId" = $ContosoApp.TenantId
            "FabrikamTenantId" = $FabrikamApp.TenantId
            "Authority" = "https://login.microsoftonline.com/" + $ContosoApp.TenantId
            "Scopes" = @(
                "https://communication.azure.com/.default",
                "https://graph.microsoft.com/User.Read"
            )
        }
    }
}

$appRegistrationConfig | ConvertTo-Json -Depth 10 | Set-Content $ConfigurationFile

# Generate summary
Write-Host ""
Write-Host "App Registrations Setup Complete!" -ForegroundColor Green
Write-Host "====================================" -ForegroundColor Green
Write-Host ""
Write-Host "Contoso (Host) App Registration:" -ForegroundColor Blue
Write-Host "   Application ID: $($ContosoApp.AppId)"
Write-Host "   Tenant ID: $($ContosoApp.TenantId)"
Write-Host "   Object ID: $($ContosoApp.ObjectId)"
Write-Host ""
Write-Host "Fabrikam (External) App Registration:" -ForegroundColor Blue
Write-Host "   Application ID: $($FabrikamApp.AppId)"
Write-Host "   Tenant ID: $($FabrikamApp.TenantId)"
Write-Host "   Object ID: $($FabrikamApp.ObjectId)"
Write-Host ""
Write-Host "Important Security Notes:" -ForegroundColor Yellow
Write-Host "   • Client secrets have been saved to appsettings.AppRegistrations.json"
Write-Host "   • Store these secrets securely and rotate them regularly"
Write-Host "   • Consider using Azure Key Vault for production deployments"
Write-Host ""
Write-Host "Manual Configuration Required:" -ForegroundColor Yellow
Write-Host "   1. In Azure portal, go to both app registrations"
Write-Host "   2. Grant admin consent for the requested permissions"
Write-Host "   3. Verify redirect URIs are correct for your deployment"
Write-Host "   4. Configure any additional cross-tenant policies if needed"
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Green
Write-Host "1. Run: dotnet run --environment=Live"
Write-Host "2. Test cross-tenant authentication flow"
Write-Host "3. Verify ACS integration with live resources"

