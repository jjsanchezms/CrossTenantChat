# Cross-Tenant Chat Demo with Azure Communication Services

This project demonstrates cross-tenant chat using Azure Communication Services (ACS) with Microsoft Entra ID authentication. It showcases how a user from one Azure tenant (Fabrikam) can authenticate and access Azure Communication Services resources hosted in another tenant (Contoso).

## � Features

✅ **Cross-Tenant Authentication**: Real Entra ID authentication between Fabrikam and Contoso tenants  
✅ **Live Azure Integration**: Real Azure Communication Services with token exchange  
✅ **Demo Mode**: Simulated authentication for testing without live Azure resources  
✅ **Visual Indicators**: Clear tenant identification in chat interface (🌐 Fabrikam, 🏢 Contoso)  
✅ **Infrastructure as Code**: Automated Azure resource provisioning with Bicep  
✅ **Secure Configuration**: Azure Key Vault integration for secrets management

## �🏗️ Architecture Overview

```
Fabrikam Tenant (User Source)    →    Contoso Tenant (ACS Host)
├─ Entra Workforce ID                  ├─ Azure Communication Services
├─ User Authentication                 ├─ Chat Resources
├─ MSAL.NET Integration                ├─ Token exchange endpoint
└─ Cross-tenant permissions            └─ Azure Key Vault
```

### Tenants:
- **Fabrikam Corp**: User identity source via Entra Workforce ID
- **Contoso Ltd**: ACS resource host with live Azure infrastructure

### Flow:
1. User from Fabrikam logs into Contoso's ACS instance using Entra ID credentials
2. Cross-tenant token validation and exchange with MSAL.NET
3. Live ACS access token generation
4. Real-time chat session with cross-tenant indicators

## 🚀 Quick Start Options

### Option 1: Demo Mode (No Azure Setup Required)

Perfect for exploring the cross-tenant concept without live Azure resources.

```bash
git clone <repository-url>
cd CrossTenantChat
dotnet restore
dotnet run
```

Navigate to `https://localhost:5068` and explore the simulated cross-tenant flow.

### Option 2: Live Azure Integration

Deploy with real Azure services for production-ready cross-tenant authentication.

**Prerequisites:**
- Two Azure AD tenants (Fabrikam + Contoso)
- Global Administrator access to both tenants
- Azure subscription with Contributor access
- Azure CLI and PowerShell

**Quick Deploy:**
```powershell
# 1. Deploy Azure resources
cd Infrastructure
.\deploy-azure-resources.ps1

# 2. Setup app registrations  
.\setup-app-registrations.ps1 -ContosoTenantId "your-contoso-id" -FabrikamTenantId "your-fabrikam-id"

# 3. Run with live services
cd ..
dotnet run --environment=Live
```

## 🏗️ Project Structure

```
CrossTenantChat/
├── Components/
│   ├── Pages/
│   │   ├── Chat.razor              # Main chat interface with cross-tenant indicators
│   │   └── Home.razor              # Landing page with demo information
│   └── Layout/                     # Blazor layout components
├── Services/
│   ├── EntraIdAuthenticationService.cs      # Demo authentication service
│   ├── LiveEntraIdAuthenticationService.cs  # Real MSAL.NET authentication
│   ├── AzureCommunicationService.cs         # Demo ACS integration
│   └── LiveAzureCommunicationService.cs     # Real ACS integration
├── Infrastructure/
│   ├── main.bicep                           # Azure resource definitions
│   ├── deploy-azure-resources.ps1           # Azure deployment script
│   └── setup-app-registrations.ps1          # Entra ID setup script
├── Models/                         # Data models and DTOs
├── Configuration/                  # App configuration classes
├── appsettings.json               # Base configuration
├── appsettings.Live.json          # Live environment configuration
└── LIVE_DEPLOYMENT_GUIDE.md       # Comprehensive deployment guide
```

## 🔧 Technology Stack

- **Frontend**: ASP.NET Core Blazor Server (.NET 9.0)
- **Authentication**: Microsoft Identity Platform (MSAL.NET)
- **Backend Services**: Azure Communication Services SDK
- **Infrastructure**: Azure Bicep, Azure CLI, PowerShell
- **Configuration**: Azure Key Vault, ASP.NET Configuration
- **Security**: Cross-tenant Entra ID, JWT token validation

## 🌐 Cross-Tenant Scenarios Demonstrated
   ```

4. **Navigate to**: `https://localhost:5001/chat`

## 🔧 Setup Instructions

### Step 1: Contoso Tenant Setup (ACS Host)

1. **Create Azure Communication Services Resource**:
   ```bash
   az communication create \
     --name "contoso-acs-resource" \
     --resource-group "contoso-rg" \
     --location "EastUS"
   ```

2. **Get ACS Connection String**:
   - Navigate to Azure Portal → Communication Services
   - Copy the connection string from "Keys" section

3. **App Registration in Contoso**:
   ```bash
   az ad app create \
     --display-name "CrossTenantChatApp" \
     --sign-in-audience "AzureADMultipleOrgs"
   ```

### Step 2: Fabrikam Tenant Setup (User Source)

1. **Configure Guest Access** (in Contoso tenant):
   - Azure Portal → Entra ID → External Identities
   - Configure B2B collaboration settings
   - Allow external users from Fabrikam

2. **Add API Permissions**:
   - Microsoft Graph: `User.Read`
   - Azure Communication Services: Custom scopes

### Step 3: Cross-Tenant Permissions

1. **In Contoso Tenant** (modify App Registration):
   ```json
   {
     "signInAudience": "AzureADMultipleOrgs",
     "api": {
       "oauth2PermissionScopes": [
         {
           "id": "<guid>",
           "adminConsentDescription": "Allow cross-tenant chat access",
           "adminConsentDisplayName": "Cross-tenant chat",
           "isEnabled": true,
           "type": "User",
           "userConsentDescription": "Access chat services",
           "userConsentDisplayName": "Chat access",
           "value": "Chat.Access"
         }
       ]
     }
   }
   ```

2. **Grant Admin Consent** in both tenants

## 🎯 Demo Features

### Authentication Flow Visualization
The application provides detailed logging of the cross-tenant authentication process:

1. **User Selection**: Choose between Fabrikam (cross-tenant) or Contoso (local) user
2. **Token Validation**: Simulates Entra ID token validation
3. **Cross-Tenant Check**: Validates cross-tenant permissions
4. **ACS Token Exchange**: Exchanges Entra ID token for ACS access token
5. **Chat Session**: Initiates chat with visual cross-tenant indicators

### Visual Indicators
- 🌐 Cross-tenant users and messages
- 🏢 Local tenant users and messages
- 📊 Real-time authentication flow tracking
- 🎯 ACS user ID mapping

## 📁 Project Structure

```
CrossTenantChat/
├── Components/
│   ├── Pages/
│   │   ├── Home.razor          # Landing page with demo info
│   │   └── Chat.razor          # Main chat interface
│   └── Layout/
├── Configuration/
│   └── AzureConfiguration.cs   # Azure AD and ACS settings
├── Models/
│   └── ChatModels.cs          # Chat and authentication models
├── Services/
│   ├── EntraIdAuthenticationService.cs  # Cross-tenant auth logic
│   └── AzureCommunicationService.cs     # ACS integration
├── Program.cs                 # Service configuration
└── appsettings.json          # Configuration file
```

## 🔐 Security Considerations

### Production Recommendations:

1. **Token Validation**:
   - Implement proper JWT signature validation
   - Use certificate-based validation
   - Validate token issuer and audience

2. **Cross-Tenant Security**:
   - Implement tenant allowlisting
   - Use Azure AD B2B for guest access
   - Audit cross-tenant access logs

3. **ACS Security**:
   - Store ACS connection strings in Azure Key Vault
   - Implement token rotation
   - Use managed identities where possible

## 🔍 Monitoring and Logging

The application provides comprehensive logging for:

- Cross-tenant authentication attempts
- Token exchange processes
- Chat thread creation and management
- Message flow tracking
- Error conditions and debugging

### Key Log Entries:
- `🔄 CROSS-TENANT SUCCESS`: Successful cross-tenant authentication
- `🌐 CROSS-TENANT MESSAGE`: Messages from external tenant users
- `📊 Flow`: Authentication flow tracking
- `🎯 ACS User ID`: ACS identity mapping

## 🧪 Testing Cross-Tenant Scenarios

### Test Case 1: Fabrikam User Authentication
1. Select "Fabrikam Corp" as user tenant
2. Enter Fabrikam user credentials
3. Observe cross-tenant authentication flow
4. Verify ACS token generation

### Test Case 2: Cross-Tenant Chat Creation
1. Authenticate as Fabrikam user
2. Create new chat thread
3. Observe cross-tenant indicators in UI and logs
4. Send messages and verify cross-tenant message flow

### Test Case 3: Mixed Tenant Chat
1. Authenticate Fabrikam user and create thread
2. Simulate adding Contoso user to thread
3. Observe multi-tenant chat indicators

## 🛠️ Development Notes

### Running in Demo Mode
When ACS connection string is not provided, the application runs in demo mode:
- Simulates ACS token generation
- Uses in-memory chat storage
- Provides full cross-tenant authentication visualization

### Real ACS Integration
To enable real ACS integration:
1. Provide valid ACS connection string in configuration
2. Ensure proper Azure permissions
3. Update token scopes as needed

## 📚 References

- [Azure Communication Services Documentation](https://docs.microsoft.com/en-us/azure/communication-services/)
- [Microsoft Entra ID Authentication Integration](https://learn.microsoft.com/en-us/azure/communication-services/quickstarts/identity/microsoft-entra-id-authentication-integration?pivots=programming-language-csharp)
- [Communication Services .NET Quickstarts](https://github.com/Azure-Samples/communication-services-dotnet-quickstarts/tree/main/EntraIdUsersSupportQuickstart)

## 🤝 Contributing

This is a demonstration project. For production use, please implement proper security measures and follow Azure security best practices.

---

**Note**: This demo simulates cross-tenant authentication for educational purposes. In production environments, ensure all security requirements and compliance standards are met.
