# Tenant Configuration Health Check Scripts

This directory contains health check scripts to validate the `tenant-config.json` file parameters for the CrossTenantChat application.

## Files

- **`validate-tenant-config-fixed.ps1`** - PowerShell version (Windows/Cross-platform)
- **`validate-tenant-config.sh`** - Bash version (Linux/macOS/WSL)

## What These Scripts Validate

### 📋 JSON Structure
- Validates JSON syntax and format
- Checks for required sections: `tenants`, `azureCommunicationServices`, `keyVault`

### 🏢 Tenant Configurations
- **Tenant IDs**: Validates GUID format
- **Tenant Names**: Ensures they are not empty
- **App Registration Client IDs**: Validates GUID format
- **Client Secrets**: Checks format and ensures not placeholder values

### ☁️ Azure Communication Services
- **Connection String**: Validates ACS connection string format
- **Endpoint URL**: Validates HTTPS URL format and ACS domain
- **Resource ID**: Validates Azure resource ID format
- **Network Connectivity**: Tests if ACS endpoint is reachable (optional)

### 🔐 Key Vault Configuration
- **Vault URI**: Validates HTTPS URL format and Key Vault domain
- **Network Connectivity**: Tests if Key Vault endpoint is reachable (optional)

### 🔍 Cross-Validation
- Ensures ACS endpoint URL matches the endpoint in the connection string

## Usage

### PowerShell (Recommended for Windows)

```powershell
# Basic usage - validates ./tenant-config.json
.\validate-tenant-config-fixed.ps1

# Specify custom config file path
.\validate-tenant-config-fixed.ps1 -ConfigPath "C:\path\to\your\tenant-config.json"

# Skip network connectivity tests (useful for offline validation)
.\validate-tenant-config-fixed.ps1 -SkipConnectivityTests

# Both custom path and skip connectivity
.\validate-tenant-config-fixed.ps1 -ConfigPath "C:\path\to\tenant-config.json" -SkipConnectivityTests
```

### Bash (Linux/macOS/WSL)

```bash
# Make script executable (first time only)
chmod +x validate-tenant-config.sh

# Basic usage - validates ./tenant-config.json
./validate-tenant-config.sh

# Specify custom config file path
./validate-tenant-config.sh /path/to/your/tenant-config.json

# Skip network connectivity tests
./validate-tenant-config.sh ./tenant-config.json true

# Show help
./validate-tenant-config.sh --help
```

## Prerequisites

### PowerShell Script
- PowerShell 5.1+ or PowerShell Core 6+
- Internet connection (for connectivity tests, optional)

### Bash Script
- Bash shell
- `jq` command-line JSON processor
- `curl` (for connectivity tests, optional)

**Install jq on Ubuntu/Debian:**
```bash
sudo apt-get install jq
```

**Install jq on macOS:**
```bash
brew install jq
```

**Install jq on CentOS/RHEL:**
```bash
sudo yum install jq
```

## Sample Output

```
[INFO] Starting tenant configuration validation...
[INFO] Config file: ./tenant-config.json
============================================================
[SUCCESS] Configuration file exists
[SUCCESS] JSON file is valid and parseable

[INFO] Validating JSON Structure...
[SUCCESS] Found 'tenants' section
[SUCCESS] Found 'azureCommunicationServices' section
[SUCCESS] Found 'keyVault' section

[INFO] Validating Tenant Configurations...

[INFO] Validating tenant: contoso
[SUCCESS] Tenant 'contoso' has name: Contoso (Host Tenant)
[SUCCESS] Tenant 'contoso' tenantId has valid GUID format
[SUCCESS] Tenant 'contoso' clientId has valid GUID format
[SUCCESS] Tenant 'contoso' clientSecret appears to be a valid Azure client secret

[INFO] Validating Azure Communication Services...
[SUCCESS] ACS connectionString has valid ACS connection string format
[SUCCESS] ACS endpointUrl has valid ACS endpoint URL format
[SUCCESS] ACS resourceId has valid Azure resource ID format

[INFO] Validating Key Vault Configuration...
[SUCCESS] Key Vault vaultUri has valid Key Vault URI format

[INFO] Performing Cross-Validation...
[SUCCESS] ACS endpoint URL matches connection string endpoint

[INFO] Validation Summary
============================================================
[SUCCESS] All validations passed! Configuration appears to be correct.

[SUCCESS] Configuration validation completed successfully!
```

## Exit Codes

- **0**: All validations passed
- **1**: One or more validations failed or error occurred

## Common Issues and Solutions

### ❌ Placeholder Values
**Issue**: `[ERROR] Tenant 'contoso' clientSecret still contains placeholder value`

**Solution**: Replace placeholder values like `YOUR_CONTOSO_APP_CLIENT_ID_HERE` with actual values from Azure Portal.

### ❌ Invalid GUID Format
**Issue**: `[ERROR] Tenant 'contoso' tenantId has invalid GUID format`

**Solution**: Ensure tenant IDs and client IDs are valid GUIDs in format: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`

### ❌ Invalid Connection String
**Issue**: `[ERROR] ACS connectionString has invalid ACS connection string format`

**Solution**: Ensure the connection string follows the format: `endpoint=https://resource.communication.azure.com/;accesskey=base64key==`

### ⚠️ Network Connectivity Issues
**Issue**: `[ERROR] Azure Communication Services endpoint is not reachable`

**Solution**: This may be normal if:
- Services require authentication
- Running from a restricted network
- Resources are not yet deployed
- Use `-SkipConnectivityTests` parameter to skip these checks

## Integration with CI/CD

Add to your deployment pipeline:

```yaml
# Azure DevOps Pipeline
- task: PowerShell@2
  displayName: 'Validate Tenant Configuration'
  inputs:
    filePath: 'Infrastructure/validate-tenant-config-fixed.ps1'
    arguments: '-ConfigPath "$(System.DefaultWorkingDirectory)/Infrastructure/tenant-config.json"'
    failOnStderr: true
```

```yaml
# GitHub Actions
- name: Validate Tenant Configuration
  run: |
    chmod +x ./Infrastructure/validate-tenant-config.sh
    ./Infrastructure/validate-tenant-config.sh ./Infrastructure/tenant-config.json
```

## Troubleshooting

1. **PowerShell Execution Policy**: If you get execution policy errors, run:
   ```powershell
   Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
   ```

2. **jq not found**: Install jq package manager for your system (see Prerequisites)

3. **Permission denied**: Make the bash script executable:
   ```bash
   chmod +x validate-tenant-config.sh
   ```

## Contributing

Feel free to enhance these scripts by adding more validation rules or improving error messages. Common enhancements:
- Additional Azure service validations
- More specific error messages
- Support for additional configuration formats
- Integration with Azure CLI for deeper validation