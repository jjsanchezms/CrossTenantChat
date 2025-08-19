#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Health check script for tenant-config.json validation
.DESCRIPTION
    This script validates all parameters in the tenant-config.json file to ensure they are correctly configured.
    It checks for proper JSON structure, required fields, format validation, and attempts to verify connectivity where possible.
.PARAMETER ConfigPath
    Path to the tenant-config.json file. Defaults to ./tenant-config.json
.PARAMETER SkipConnectivityTests
    Skip network connectivity tests (useful for offline validation)
.EXAMPLE
    .\validate-tenant-config.ps1
.EXAMPLE
    .\validate-tenant-config.ps1 -ConfigPath "C:\path\to\tenant-config.json" -SkipConnectivityTests
#>

param(
    [string]$ConfigPath = "./tenant-config.json",
    [switch]$SkipConnectivityTests
)

# Colors for output
$Red = "Red"
$Green = "Green"
$Yellow = "Yellow"
$Cyan = "Cyan"
$White = "White"

function Write-Status {
    param($Message, $Status = "Info")
    switch ($Status) {
        "Success" { Write-Host "[SUCCESS] $Message" -ForegroundColor $Green }
        "Error" { Write-Host "[ERROR] $Message" -ForegroundColor $Red }
        "Warning" { Write-Host "[WARNING] $Message" -ForegroundColor $Yellow }
        "Info" { Write-Host "[INFO] $Message" -ForegroundColor $Cyan }
        default { Write-Host $Message -ForegroundColor $White }
    }
}

function Test-GuidFormat {
    param([string]$Value, [string]$FieldName)
    
    if ([string]::IsNullOrEmpty($Value)) {
        Write-Status "$FieldName is empty or null" "Error"
        return $false
    }
    
    try {
        [System.Guid]::Parse($Value) | Out-Null
        Write-Status "$FieldName has valid GUID format" "Success"
        return $true
    }
    catch {
        Write-Status "$FieldName has invalid GUID format: $Value" "Error"
        return $false
    }
}

function Test-ClientSecretFormat {
    param([string]$Value, [string]$FieldName)
    
    if ([string]::IsNullOrEmpty($Value)) {
        Write-Status "$FieldName is empty or null" "Error"
        return $false
    }
    
    if ($Value -match "YOUR_.*_HERE") {
        Write-Status "$FieldName still contains placeholder value" "Error"
        return $false
    }
    
    # Azure client secrets typically have specific patterns
    if ($Value.Length -ge 30 -and $Value -match "^[a-zA-Z0-9~._-]+$") {
        Write-Status "$FieldName appears to be a valid Azure client secret" "Success"
        return $true
    }
    else {
        Write-Status "$FieldName may have invalid format (expected Azure client secret pattern)" "Warning"
        return $false
    }
}

function Test-AzureConnectionString {
    param([string]$Value, [string]$FieldName)
    
    if ([string]::IsNullOrEmpty($Value)) {
        Write-Status "$FieldName is empty or null" "Error"
        return $false
    }
    
    if ($Value -match "YOUR_.*_HERE") {
        Write-Status "$FieldName still contains placeholder value" "Error"
        return $false
    }
    
    # Azure Communication Services connection string format
    if ($Value -match "^endpoint=https://[^/]+\.communication\.azure\.com/;accesskey=[a-zA-Z0-9+/]+=*$") {
        Write-Status "$FieldName has valid ACS connection string format" "Success"
        return $true
    }
    else {
        Write-Status "$FieldName has invalid ACS connection string format" "Error"
        return $false
    }
}

function Test-AzureEndpointUrl {
    param([string]$Value, [string]$FieldName)
    
    if ([string]::IsNullOrEmpty($Value)) {
        Write-Status "$FieldName is empty or null" "Error"
        return $false
    }
    
    if ($Value -match "YOUR_.*_HERE") {
        Write-Status "$FieldName still contains placeholder value" "Error"
        return $false
    }
    
    try {
        $uri = [System.Uri]$Value
        if ($uri.Scheme -eq "https" -and $uri.Host -match "\.communication\.azure\.com$") {
            Write-Status "$FieldName has valid ACS endpoint URL format" "Success"
            return $true
        }
        else {
            Write-Status "$FieldName is not a valid ACS endpoint URL" "Error"
            return $false
        }
    }
    catch {
        Write-Status "$FieldName is not a valid URL format" "Error"
        return $false
    }
}

function Test-AzureResourceId {
    param([string]$Value, [string]$FieldName)
    
    if ([string]::IsNullOrEmpty($Value)) {
        Write-Status "$FieldName is empty or null" "Error"
        return $false
    }
    
    if ($Value -match "YOUR_.*_HERE") {
        Write-Status "$FieldName still contains placeholder value" "Error"
        return $false
    }
    
    # Azure resource ID format
    if ($Value -match "^/subscriptions/[a-f0-9-]{36}/resourceGroups/[^/]+/providers/Microsoft\.Communication/communicationServices/[^/]+$") {
        Write-Status "$FieldName has valid Azure resource ID format" "Success"
        return $true
    }
    else {
        Write-Status "$FieldName has invalid Azure resource ID format" "Error"
        return $false
    }
}

function Test-KeyVaultUri {
    param([string]$Value, [string]$FieldName)
    
    if ([string]::IsNullOrEmpty($Value)) {
        Write-Status "$FieldName is empty or null" "Error"
        return $false
    }
    
    if ($Value -match "YOUR_.*_HERE") {
        Write-Status "$FieldName still contains placeholder value" "Error"
        return $false
    }
    
    try {
        $uri = [System.Uri]$Value
        if ($uri.Scheme -eq "https" -and $uri.Host -match "\.vault\.azure\.net$") {
            Write-Status "$FieldName has valid Key Vault URI format" "Success"
            return $true
        }
        else {
            Write-Status "$FieldName is not a valid Key Vault URI" "Error"
            return $false
        }
    }
    catch {
        Write-Status "$FieldName is not a valid URI format" "Error"
        return $false
    }
}

function Test-NetworkConnectivity {
    param([string]$Url, [string]$ServiceName)
    
    if ($SkipConnectivityTests) {
        Write-Status "Skipping connectivity test for $ServiceName" "Info"
        return $true
    }
    
    try {
        $response = Invoke-WebRequest -Uri $Url -Method Head -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop
        if ($response.StatusCode -eq 200 -or $response.StatusCode -eq 404) {
            Write-Status "$ServiceName endpoint is reachable" "Success"
            return $true
        }
        else {
            Write-Status "$ServiceName endpoint returned status code: $($response.StatusCode)" "Warning"
            return $false
        }
    }
    catch {
        Write-Status "$ServiceName endpoint is not reachable: $($_.Exception.Message)" "Error"
        return $false
    }
}

# Main validation function
function Test-TenantConfig {
    param([string]$ConfigFilePath)
    
    Write-Status "Starting tenant configuration validation..." "Info"
    Write-Status "Config file: $ConfigFilePath" "Info"
    Write-Status "============================================================" "Info"
    
    $overallStatus = $true
    
    # Check if file exists
    if (-not (Test-Path $ConfigFilePath)) {
        Write-Status "Configuration file not found: $ConfigFilePath" "Error"
        return $false
    }
    
    Write-Status "Configuration file exists" "Success"
    
    # Load and parse JSON
    try {
        $config = Get-Content $ConfigFilePath -Raw | ConvertFrom-Json
        Write-Status "JSON file is valid and parseable" "Success"
    }
    catch {
        Write-Status "Failed to parse JSON file: $($_.Exception.Message)" "Error"
        return $false
    }
    
    # Validate structure
    Write-Status "" "Info"
    Write-Status "Validating JSON Structure..." "Info"
    
    if (-not $config.tenants) {
        Write-Status "Missing 'tenants' section" "Error"
        $overallStatus = $false
    }
    else {
        Write-Status "Found 'tenants' section" "Success"
    }
    
    if (-not $config.azureCommunicationServices) {
        Write-Status "Missing 'azureCommunicationServices' section" "Error"
        $overallStatus = $false
    }
    else {
        Write-Status "Found 'azureCommunicationServices' section" "Success"
    }
    
    if (-not $config.keyVault) {
        Write-Status "Missing 'keyVault' section" "Error"
        $overallStatus = $false
    }
    else {
        Write-Status "Found 'keyVault' section" "Success"
    }
    
    # Validate tenant configurations
    Write-Status "" "Info"
    Write-Status "Validating Tenant Configurations..." "Info"
    
    if ($config.tenants) {
        foreach ($tenantKey in $config.tenants.PSObject.Properties.Name) {
            $tenant = $config.tenants.$tenantKey
            Write-Status "" "Info"
            Write-Status "Validating tenant: $tenantKey" "Info"
            
            # Validate tenant name
            if ([string]::IsNullOrEmpty($tenant.name)) {
                Write-Status "Tenant '$tenantKey' missing name" "Error"
                $overallStatus = $false
            }
            else {
                Write-Status "Tenant '$tenantKey' has name: $($tenant.name)" "Success"
            }
            
            # Validate tenant ID
            if (-not (Test-GuidFormat $tenant.tenantId "Tenant '$tenantKey' tenantId")) {
                $overallStatus = $false
            }
            
            # Validate app registration
            if (-not $tenant.appRegistration) {
                Write-Status "Tenant '$tenantKey' missing appRegistration section" "Error"
                $overallStatus = $false
            }
            else {
                if (-not (Test-GuidFormat $tenant.appRegistration.clientId "Tenant '$tenantKey' clientId")) {
                    $overallStatus = $false
                }
                
                if (-not (Test-ClientSecretFormat $tenant.appRegistration.clientSecret "Tenant '$tenantKey' clientSecret")) {
                    $overallStatus = $false
                }
            }
        }
    }
    
    # Validate Azure Communication Services
    Write-Status "" "Info"
    Write-Status "Validating Azure Communication Services..." "Info"
    
    if ($config.azureCommunicationServices) {
        $acs = $config.azureCommunicationServices
        
        if (-not (Test-AzureConnectionString $acs.connectionString "ACS connectionString")) {
            $overallStatus = $false
        }
        
        if (-not (Test-AzureEndpointUrl $acs.endpointUrl "ACS endpointUrl")) {
            $overallStatus = $false
        }
        
        if (-not (Test-AzureResourceId $acs.resourceId "ACS resourceId")) {
            $overallStatus = $false
        }
        
        # Test ACS endpoint connectivity
        if ($acs.endpointUrl -and $acs.endpointUrl -notmatch "YOUR_.*_HERE") {
            Test-NetworkConnectivity $acs.endpointUrl "Azure Communication Services"
        }
    }
    
    # Validate Key Vault
    Write-Status "" "Info"
    Write-Status "Validating Key Vault Configuration..." "Info"
    
    if ($config.keyVault) {
        if (-not (Test-KeyVaultUri $config.keyVault.vaultUri "Key Vault vaultUri")) {
            $overallStatus = $false
        }
        
        # Test Key Vault connectivity
        if ($config.keyVault.vaultUri -and $config.keyVault.vaultUri -notmatch "YOUR_.*_HERE") {
            Test-NetworkConnectivity $config.keyVault.vaultUri "Key Vault"
        }
    }
    
    # Cross-validation checks
    Write-Status "" "Info"
    Write-Status "Performing Cross-Validation..." "Info"
    
    # Check if ACS endpoint URL matches connection string
    if ($config.azureCommunicationServices.connectionString -and $config.azureCommunicationServices.endpointUrl) {
        if ($config.azureCommunicationServices.connectionString -match "endpoint=([^;]+);") {
            $connectionStringEndpoint = $matches[1]
            if ($connectionStringEndpoint -eq $config.azureCommunicationServices.endpointUrl) {
                Write-Status "ACS endpoint URL matches connection string endpoint" "Success"
            }
            else {
                Write-Status "ACS endpoint URL does not match connection string endpoint" "Warning"
                Write-Status "  Connection string: $connectionStringEndpoint" "Info"
                Write-Status "  Endpoint URL: $($config.azureCommunicationServices.endpointUrl)" "Info"
            }
        }
    }
    
    # Summary
    Write-Status "" "Info"
    Write-Status "Validation Summary" "Info"
    Write-Status "============================================================" "Info"
    
    if ($overallStatus) {
        Write-Status "All validations passed! Configuration appears to be correct." "Success"
        return $true
    }
    else {
        Write-Status "Some validations failed. Please review and fix the issues above." "Error"
        return $false
    }
}

# Script execution
try {
    $result = Test-TenantConfig -ConfigFilePath $ConfigPath
    
    if ($result) {
        Write-Status "" "Info"
        Write-Status "Configuration validation completed successfully!" "Success"
        exit 0
    }
    else {
        Write-Status "" "Info"
        Write-Status "Configuration validation failed!" "Error"
        exit 1
    }
}
catch {
    Write-Status "Unexpected error during validation: $($_.Exception.Message)" "Error"
    Write-Status "Stack trace: $($_.ScriptStackTrace)" "Error"
    exit 1
}