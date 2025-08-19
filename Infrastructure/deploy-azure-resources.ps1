# Cross-Tenant ACS Demo - Azure Resources Deployment Script (PowerShell)
# This script provisions Azure resources in the Contoso tenant

# Configuration
$ResourceGroupName = "rg-crosstenant-demo"
$Location = "eastus"
$DeploymentName = "crosstenant-deployment-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

Write-Host "🚀 Cross-Tenant ACS Demo - Azure Resources Deployment" -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan

# Check if Azure CLI is installed
try {
    $azVersion = az version | ConvertFrom-Json
    Write-Host "✅ Azure CLI found: $($azVersion.'azure-cli')" -ForegroundColor Green
} catch {
    Write-Host "❌ Azure CLI is not installed. Please install Azure CLI first." -ForegroundColor Red
    exit 1
}

# Login check
Write-Host "🔐 Checking Azure login status..." -ForegroundColor Yellow
try {
    $accountInfo = az account show | ConvertFrom-Json
    Write-Host "✅ Logged in to Azure" -ForegroundColor Green
    Write-Host "   Subscription: $($accountInfo.name)"
    Write-Host "   Subscription ID: $($accountInfo.id)"
    Write-Host "   Tenant ID: $($accountInfo.tenantId)"
} catch {
    Write-Host "⚠️  Not logged in to Azure. Please login:" -ForegroundColor Yellow
    az login
    $accountInfo = az account show | ConvertFrom-Json
}

# Prompt for tenant IDs
Write-Host ""
Write-Host "📋 Please provide tenant information:" -ForegroundColor Yellow
$ContosoTenantId = Read-Host "Enter Contoso Tenant ID (current tenant)"
$FabrikamTenantId = Read-Host "Enter Fabrikam Tenant ID (external tenant)"

if ([string]::IsNullOrEmpty($ContosoTenantId) -or [string]::IsNullOrEmpty($FabrikamTenantId)) {
    Write-Host "❌ Both tenant IDs are required" -ForegroundColor Red
    exit 1
}

# Create resource group
Write-Host "🏗️  Creating resource group: $ResourceGroupName" -ForegroundColor Yellow
$rgResult = az group create --name $ResourceGroupName --location $Location --tags Environment=demo Project=CrossTenantChat

if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Resource group created successfully" -ForegroundColor Green
} else {
    Write-Host "❌ Failed to create resource group" -ForegroundColor Red
    exit 1
}

# Update parameters file
Write-Host "📝 Updating deployment parameters..." -ForegroundColor Yellow
$ParamsFile = "main.parameters.json"
Copy-Item $ParamsFile "$ParamsFile.backup" -Force

# Create updated parameters file
$parametersContent = @{
    '$schema' = "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#"
    'contentVersion' = "1.0.0.0"
    'parameters' = @{
        'projectName' = @{ 'value' = "crosstenant" }
        'location' = @{ 'value' = $Location }
        'contosoTenantId' = @{ 'value' = $ContosoTenantId }
        'fabrikamTenantId' = @{ 'value' = $FabrikamTenantId }
        'environment' = @{ 'value' = "dev" }
    }
}

$parametersContent | ConvertTo-Json -Depth 10 | Set-Content $ParamsFile

# Deploy Bicep template
Write-Host "🚀 Deploying Azure resources..." -ForegroundColor Yellow
Write-Host "This may take a few minutes..."

$deploymentOutput = az deployment group create `
    --resource-group $ResourceGroupName `
    --name $DeploymentName `
    --template-file main.bicep `
    --parameters "@$ParamsFile" `
    --query properties.outputs `
    -o json | ConvertFrom-Json

if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Azure resources deployed successfully!" -ForegroundColor Green
    
    # Extract outputs
    $AcsName = $deploymentOutput.acsResourceName.value
    $AcsEndpoint = $deploymentOutput.acsEndpoint.value
    $AcsConnectionString = $deploymentOutput.acsConnectionString.value
    $KeyVaultName = $deploymentOutput.keyVaultName.value
    $KeyVaultUri = $deploymentOutput.keyVaultUri.value
    $AcsResourceId = $deploymentOutput.acsResourceId.value
    
    Write-Host ""
    Write-Host "📋 Deployment Summary:" -ForegroundColor Green
    Write-Host "   Resource Group: $ResourceGroupName"
    Write-Host "   ACS Resource Name: $AcsName"
    Write-Host "   ACS Endpoint: $AcsEndpoint"
    Write-Host "   Key Vault Name: $KeyVaultName"
    Write-Host "   Key Vault URI: $KeyVaultUri"
    
    # Save configuration to file
    $ConfigFile = "../appsettings.Live.json"
    Write-Host "💾 Creating configuration file: $ConfigFile" -ForegroundColor Yellow
    
    $configContent = @{
        'Logging' = @{
            'LogLevel' = @{
                'Default' = 'Information'
                'Microsoft.AspNetCore' = 'Warning'
                'CrossTenantChat' = 'Information'
            }
        }
        'AllowedHosts' = '*'
        'Azure' = @{
            'AzureAd' = @{
                'Instance' = 'https://login.microsoftonline.com/'
                'ClientId' = 'your-app-registration-client-id'
                'ClientSecret' = 'your-app-registration-secret'
                'TenantId' = $ContosoTenantId
                'FabrikamTenantId' = $FabrikamTenantId
                'ContosoTenantId' = $ContosoTenantId
                'Authority' = "https://login.microsoftonline.com/$ContosoTenantId"
                'Scopes' = @(
                    'https://communication.azure.com/.default',
                    'https://graph.microsoft.com/User.Read'
                )
            }
            'AzureCommunicationServices' = @{
                'ConnectionString' = $AcsConnectionString
                'EndpointUrl' = "https://$AcsEndpoint"
                'ResourceId' = $AcsResourceId
            }
            'KeyVault' = @{
                'VaultUri' = $KeyVaultUri
            }
        }
    }
    
    $configContent | ConvertTo-Json -Depth 10 | Set-Content $ConfigFile
    
    Write-Host ""
    Write-Host "🎉 Deployment completed successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Blue
    Write-Host "1. Configure Entra ID app registrations (run setup-app-registrations.ps1)"
    Write-Host "2. Update appsettings.Live.json with your app registration details"
    Write-Host "3. Grant yourself access to Key Vault if needed"
    Write-Host "4. Run the application with: dotnet run --environment=Live"
    
} else {
    Write-Host "❌ Failed to deploy Azure resources" -ForegroundColor Red
    Write-Host "Check the error messages above for details."
    exit 1
}
