# Cross-Tenant ACS Demo - Live Azure Services Deployment Guide

This guide walks you through deploying the Cross-Tenant Chat Demo with real Azure services, enabling live authentication between Fabrikam and Contoso tenants.

## 🎯 Overview

This deployment enables:
- **Real Entra ID Authentication**: MSAL.NET with cross-tenant token exchange
- **Live Azure Communication Services**: Real chat functionality with ACS
- **Tenant-Level Authorization**: Cross-tenant access between entire organizations
- **Cross-Tenant Flow**: Fabrikam tenant users → Contoso tenant ACS resources

## 📋 Prerequisites

Before starting, ensure you have:

- [ ] **Azure CLI** installed and logged in
- [ ] **Azure PowerShell** module installed
- [ ] **Two Azure AD tenants** (or access to them):
  - Contoso tenant (ACS host)
  - Fabrikam tenant (user source)
- [ ] **Global Administrator** privileges in both tenants
- [ ] **Contributor** access to Azure subscription in Contoso tenant
- [ ] **.NET 9.0 SDK** installed locally

## 🚀 Deployment Steps

### Step 1: Verify Existing Azure Resources

You already have the following resources created:
- **Resource Group:** `contoso-resource-group` 
- **ACS Resource:** `acsresourcecontoso`
- **Resource Group:** `fabrikam-di-resource-group`

Verify these resources are available:

```powershell
# Check Contoso resources
az group show --name contoso-resource-group
az communication show --name acsresourcecontoso --resource-group contoso-resource-group

# Check Fabrikam resource group  
az group show --name fabrikam-di-resource-group
```

**Get your ACS connection string:**
```powershell
az communication list-key --name acsresourcecontoso --resource-group contoso-resource-group --query "primaryConnectionString" -o tsv
```

### Step 2: Setup App Registrations

Create Entra ID app registrations in both tenants:

```powershell
.\setup-app-registrations.ps1 -ContosoTenantId "your-contoso-tenant-id" -FabrikamTenantId "your-fabrikam-tenant-id"
```

**What this does:**
- Creates app registration in Contoso tenant (host tenant)
- Creates app registration in Fabrikam tenant (external tenant)
- Configures required API permissions for cross-tenant access
- Generates client secrets
- Sets up multi-tenant authorization at the tenant level
- Creates `appsettings.AppRegistrations.json`

📋 Deployment Summary:
   Contoso Resource Group: contoso-resource-group
   ACS Resource Name: acsresourcecontoso
   ACS Endpoint: https://acsresourcecontoso.communication.azure.com
   Fabrikam Resource Group: fabrikam-di-resource-group
```

### Step 3: Grant Tenant-Level Admin Consent

**Manual step required in Azure Portal - Tenant Authorization:**

This exercise focuses on **tenant-level authorization**, not resource group permissions.

1. **Contoso Tenant Authorization:**
   - Go to Azure Portal → Azure Active Directory → App registrations
   - Find "CrossTenantChatApp-Contoso"
   - Navigate to API permissions
   - Click "Grant admin consent for [Contoso Tenant]"
   - This authorizes the entire Contoso tenant for cross-tenant authentication

2. **Fabrikam Tenant Authorization:**
   - Switch to Fabrikam tenant in Azure Portal
   - Repeat same process for "CrossTenantChatApp-Fabrikam"
   - Click "Grant admin consent for [Fabrikam Tenant]"
   - This authorizes the entire Fabrikam tenant for cross-tenant authentication

**Note:** We're granting consent at the **tenant level**, enabling cross-tenant token exchange between the entire Fabrikam and Contoso organizations, not just specific resource groups.

### Step 4: Configure Application Settings

Update the generated configuration files with your specific values:

#### `appsettings.Live.json`:
```json
{
  "Azure": {
    "AzureAd": {
      "ClientId": "your-contoso-app-registration-id",
      "ClientSecret": "your-contoso-app-secret", 
      "TenantId": "your-contoso-tenant-id",
      "ContosoTenantId": "your-contoso-tenant-id",
      "FabrikamTenantId": "your-fabrikam-tenant-id"
    },
    "AzureCommunicationServices": {
      "ConnectionString": "your-live-acs-connection-string"
    },
    "KeyVault": {
      "VaultUri": "https://your-keyvault.vault.azure.net/"
    }
  }
}
```

### Step 5: Test Local Development

Run the application in Live mode:

```powershell
# Navigate to project root
cd ..

# Run with Live environment
dotnet run --environment=Live
```

**Expected Console Output:**
```
🎯 Cross-Tenant Chat Application Starting
Environment: Live
🚀 Live Azure Services registered for environment: Live
🌐 Live Azure Integration Enabled
✅ Real Entra ID Authentication
✅ Live Azure Communication Services
ACS Connection: ✅ Configured
Contoso Tenant: ✅ Configured  
Fabrikam Tenant: ✅ Configured
🚀 Cross-Tenant Chat Application Started Successfully
🌐 Ready for live cross-tenant authentication: Fabrikam ↔ Contoso
```

### Step 6: Test Cross-Tenant Authentication

1. **Open browser** to `https://localhost:5001`
2. **Select Fabrikam** tenant for authentication  
3. **Sign in** with a Fabrikam tenant user account
4. **Verify** cross-tenant token exchange works between tenants
5. **Create chat thread** using Contoso tenant ACS resources
6. **Check logs** for live ACS integration and tenant-level authorization

## 🔧 Configuration Details

### Environment Variables

The application supports these configuration sources (in order of precedence):

1. **Azure Key Vault** (Live environment only)
2. **appsettings.Live.json** (Live environment)
3. **appsettings.AppRegistrations.json** (Generated by script)
4. **appsettings.json** (Base configuration)

### Required Settings

**Entra ID Configuration:**
- `Azure:AzureAd:ClientId` - Contoso app registration ID
- `Azure:AzureAd:ClientSecret` - Contoso app secret
- `Azure:AzureAd:TenantId` - Contoso tenant ID
- `Azure:AzureAd:ContosoTenantId` - Contoso tenant ID
- `Azure:AzureAd:FabrikamTenantId` - Fabrikam tenant ID

**ACS Configuration:**
- `Azure:AzureCommunicationServices:ConnectionString` - Live ACS connection
- `Azure:AzureCommunicationServices:EndpointUrl` - ACS endpoint URL

**Key Vault Configuration:**
- `Azure:KeyVault:VaultUri` - Key Vault URI for secrets

## 🔍 Troubleshooting

### Common Issues

**1. Authentication Failures**
```
Error: AADSTS50020: User account from identity provider does not exist in tenant
```
**Solution:** Ensure app registrations have multi-tenant access configured and tenant-level admin consent is granted for both organizations.

**2. ACS Connection Issues**
```
Error: Connection string doesn't have value for keyword
```
**Solution:** Verify ACS connection string format in appsettings.Live.json.

**3. Key Vault Access Denied**
```
Error: The user, group or application does not have secrets list permission
```
**Solution:** Grant your account Key Vault Secrets User role.

### Debug Mode

Enable detailed logging by updating `appsettings.Live.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "CrossTenantChat": "Information",
      "Azure": "Information",
      "Microsoft.AspNetCore.Authentication": "Information"
    }
  }
}
```

### Verification Commands

**Check Contoso ACS Resource:**
```bash
az communication show --name acsresourcecontoso --resource-group contoso-resource-group
```

**Get ACS Connection String:**
```bash
az communication list-key --name acsresourcecontoso --resource-group contoso-resource-group --query "primaryConnectionString" -o tsv
```

**Check Fabrikam Resource Group:**
```bash
az group show --name fabrikam-di-resource-group
```

**Validate App Registration:**
```bash
az ad app list --display-name "CrossTenantChatApp-Contoso"
```

## 🚀 Production Deployment

For production deployment:

1. **Deploy to Azure App Service** using the provided App Service Plan
2. **Configure Managed Identity** for Key Vault access
3. **Set up custom domains** with SSL certificates
4. **Configure monitoring** with Application Insights
5. **Implement proper secret rotation**

### Azure App Service Deployment

```bash
# Build and publish
dotnet publish -c Release -o ./publish

# Deploy to App Service in your existing resource groups
# For Contoso tenant hosting:
az webapp deployment source config-zip \
  --resource-group contoso-resource-group \
  --name your-webapp-name \
  --src publish.zip
```

## 📚 Additional Resources

- [Azure Communication Services Documentation](https://docs.microsoft.com/azure/communication-services/)
- [Microsoft Identity Platform Multi-tenant Apps](https://docs.microsoft.com/azure/active-directory/develop/howto-convert-app-to-be-multi-tenant)
- [Azure Key Vault Configuration Provider](https://docs.microsoft.com/aspnet/core/security/key-vault-configuration)

## ✅ Success Criteria

Your deployment is successful when:

- [ ] Application starts with Live environment logs
- [ ] Real Entra ID authentication works for both tenants  
- [ ] ACS tokens are generated from live Azure resources
- [ ] Cross-tenant chat functionality operates correctly
- [ ] All configuration values are properly secured
- [ ] Console shows live integration confirmations

## 📞 Support

If you encounter issues:
1. Check the troubleshooting section above
2. Review Azure portal for resource status
3. Verify app registration permissions
4. Check application logs for detailed error messages

---

**Next:** Proceed to testing the complete cross-tenant authentication flow with live Azure services!
