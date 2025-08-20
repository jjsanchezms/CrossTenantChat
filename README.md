# Cross-Tenant Chat (ACS + Entra ID)

A minimal demo showing how a user from one tenant (Fabrikam) can chat in Azure Communication Services (ACS) hosted in another tenant (Contoso) using Microsoft Entra ID.

## Features
- Cross-tenant sign-in and ACS token exchange
- Blazor Server UI (.NET 9)
- Clear tenant indicators in the chat UI
- Auto-refresh for messages and newly created threads
- Enter key sends message; button click also supported

## Quick Start (no Azure setup)
Run locally in demo mode to explore the UI and flows.

```powershell
dotnet restore
dotnet run
```

Then open the app and go to `/chat`. Create a thread and start messaging.

Notes
- The app no longer auto-creates a default thread; you’ll be prompted to create one.
- New threads include a couple of demo participants by email so each side can see/join when they sign in.

## Run against Live Azure
Use real ACS and Entra ID resources. For full steps, see `LIVE_DEPLOYMENT_GUIDE.md` and `Infrastructure/MANUAL_SETUP_GUIDE.md`.

Prereqs
- Two tenants (Contoso = ACS host, Fabrikam = user source)
- Azure subscription and permissions to deploy

Quick path
```powershell
# From repo root
cd Infrastructure
./deploy-azure-resources.ps1
./setup-app-registrations.ps1 -ContosoTenantId "<contoso-tenant-guid>" -FabrikamTenantId "<fabrikam-tenant-guid>"

# Back to app root and run in Live mode
cd ..
$env:ASPNETCORE_ENVIRONMENT="Live"
dotnet run --project CrossTenantChat.csproj --no-launch-profile
```

Configuration
- Base: `appsettings.json`
- Live overrides: `appsettings.Live.json`
- Useful UI options: `Chat:AutoRefreshEnabled` (bool), `Chat:AutoRefreshIntervalMs` (500–10000)

## Project layout (short)
```
CrossTenantChat/
├─ Components/Pages/Chat.razor        # Main chat UI
├─ Services/                          # Demo + Live ACS/Auth services
├─ Infrastructure/                    # Scripts to deploy and configure Azure
├─ appsettings*.json                  # Config (includes Live)
└─ LIVE_DEPLOYMENT_GUIDE.md           # Detailed live setup
```

## UX tips
- Press Enter to send; the send button is also available.
- Messages and the thread list auto-refresh at a short interval.

## Troubleshooting (quick)
- Build locking on Windows: if `CrossTenantChat.exe` is in use, stop the running process and rebuild.
- Live config not loading: ensure `ASPNETCORE_ENVIRONMENT` is set to `Live` before running.

## References
- Azure Communication Services docs: https://learn.microsoft.com/azure/communication-services/
- Entra ID + ACS quickstart (C#): https://learn.microsoft.com/azure/communication-services/quickstarts/identity/microsoft-entra-id-authentication-integration?pivots=programming-language-csharp
