# Manual App Registration Setup Guide

This guide walks you through creating the required Azure AD app registrations manually through the Azure portal, then using our simplified configuration approach.

## Step 1: Create App Registrations in Azure Portal

### For Contoso (Host) Tenant:
1. Go to Azure portal → Azure Active Directory → App registrations
2. Click "New registration"
3. Name: `CrossTenantChatApp-Contoso`
4. Supported account types: **Accounts in any organizational directory (Any Azure AD directory - Multitenant)**
5. Redirect URI: Web → `https://localhost:5001/signin-oidc`
6. Click Register

### For Fabrikam (External) Tenant:
1. Switch to the Fabrikam tenant in Azure portal
2. Go to Azure Active Directory → App registrations  
3. Click "New registration"
4. Name: `CrossTenantChatApp-Fabrikam`
5. Supported account types: **Accounts in any organizational directory (Any Azure AD directory - Multitenant)**
6. Redirect URI: Web → `https://localhost:5001/signin-oidc`
7. Click Register

## Step 2: Configure API Permissions

### For both app registrations:
1. Go to "API permissions"
2. Click "Add a permission"
3. Add **Microsoft Graph** → Delegated permissions → `User.Read`
4. Add **Azure Communication Services** → Delegated permissions → `Communication.Default`
5. Click "Grant admin consent" for your organization

## Step 3: Create Client Secrets

### For both app registrations:
1. Go to "Certificates & secrets"
2. Click "New client secret"
3. Description: `ClientSecret`
4. Expires: Choose your preferred expiration
5. Click Add
6. **Copy the secret value immediately** (you won't see it again)

## Step 4: Collect Required Information

You'll need to collect these values for each app registration:
- **Application (client) ID**
- **Directory (tenant) ID**  
- **Client secret value** (from step 3)

## Step 5: Update Configuration File

1. Open `tenant-config.json`
2. Replace the placeholder values with your actual values:

```json
{
  "tenants": {
    "contoso": {
      "tenantId": "your-contoso-tenant-id",
      "appRegistration": {
        "clientId": "your-contoso-app-client-id",
        "clientSecret": "your-contoso-app-client-secret"
      }
    },
    "fabrikam": {
      "tenantId": "your-fabrikam-tenant-id", 
      "appRegistration": {
        "clientId": "your-fabrikam-app-client-id",
        "clientSecret": "your-fabrikam-app-client-secret"
      }
    }
  }
}
```

## Step 6: Generate Configuration

Run the configuration generator:
```powershell
.\generate-config.ps1
```

This will create `appsettings.AppRegistrations.json` with the proper format for your application.

## Step 7: Test Your Setup

Run your application:
```powershell
dotnet run --environment=Live
```

The application should now be able to authenticate users from both tenants.