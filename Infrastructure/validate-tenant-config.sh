#!/bin/bash

# Health check script for tenant-config.json validation
# This script validates all parameters in the tenant-config.json file

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Configuration
CONFIG_PATH=${1:-"./tenant-config.json"}
SKIP_CONNECTIVITY_TESTS=${2:-false}

print_status() {
    local message=$1
    local status=${2:-"INFO"}
    
    case $status in
        "SUCCESS")
            echo -e "✅ ${GREEN}$message${NC}"
            ;;
        "ERROR")
            echo -e "❌ ${RED}$message${NC}"
            ;;
        "WARNING")
            echo -e "⚠️  ${YELLOW}$message${NC}"
            ;;
        "INFO")
            echo -e "ℹ️  ${CYAN}$message${NC}"
            ;;
        *)
            echo -e "$message"
            ;;
    esac
}

validate_guid() {
    local value=$1
    local field_name=$2
    
    if [[ -z "$value" ]]; then
        print_status "$field_name is empty or null" "ERROR"
        return 1
    fi
    
    # GUID regex pattern
    if [[ $value =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
        print_status "$field_name has valid GUID format" "SUCCESS"
        return 0
    else
        print_status "$field_name has invalid GUID format: $value" "ERROR"
        return 1
    fi
}

validate_client_secret() {
    local value=$1
    local field_name=$2
    
    if [[ -z "$value" ]]; then
        print_status "$field_name is empty or null" "ERROR"
        return 1
    fi
    
    if [[ $value =~ YOUR_.*_HERE ]]; then
        print_status "$field_name still contains placeholder value" "ERROR"
        return 1
    fi
    
    # Azure client secret pattern
    if [[ $value =~ ^[a-zA-Z0-9~._-]{30,}$ ]]; then
        print_status "$field_name appears to be a valid Azure client secret" "SUCCESS"
        return 0
    else
        print_status "$field_name may have invalid format (expected Azure client secret pattern)" "WARNING"
        return 1
    fi
}

validate_connection_string() {
    local value=$1
    local field_name=$2
    
    if [[ -z "$value" ]]; then
        print_status "$field_name is empty or null" "ERROR"
        return 1
    fi
    
    if [[ $value =~ YOUR_.*_HERE ]]; then
        print_status "$field_name still contains placeholder value" "ERROR"
        return 1
    fi
    
    # ACS connection string pattern
    if [[ $value =~ ^endpoint=https://[^/]+\.communication\.azure\.com/;accesskey=[a-zA-Z0-9+/]+=*$ ]]; then
        print_status "$field_name has valid ACS connection string format" "SUCCESS"
        return 0
    else
        print_status "$field_name has invalid ACS connection string format" "ERROR"
        return 1
    fi
}

validate_endpoint_url() {
    local value=$1
    local field_name=$2
    
    if [[ -z "$value" ]]; then
        print_status "$field_name is empty or null" "ERROR"
        return 1
    fi
    
    if [[ $value =~ YOUR_.*_HERE ]]; then
        print_status "$field_name still contains placeholder value" "ERROR"
        return 1
    fi
    
    # ACS endpoint URL pattern
    if [[ $value =~ ^https://[^/]+\.communication\.azure\.com/$ ]]; then
        print_status "$field_name has valid ACS endpoint URL format" "SUCCESS"
        return 0
    else
        print_status "$field_name is not a valid ACS endpoint URL" "ERROR"
        return 1
    fi
}

validate_resource_id() {
    local value=$1
    local field_name=$2
    
    if [[ -z "$value" ]]; then
        print_status "$field_name is empty or null" "ERROR"
        return 1
    fi
    
    if [[ $value =~ YOUR_.*_HERE ]]; then
        print_status "$field_name still contains placeholder value" "ERROR"
        return 1
    fi
    
    # Azure resource ID pattern
    if [[ $value =~ ^/subscriptions/[a-f0-9-]{36}/resourceGroups/[^/]+/providers/Microsoft\.Communication/communicationServices/[^/]+$ ]]; then
        print_status "$field_name has valid Azure resource ID format" "SUCCESS"
        return 0
    else
        print_status "$field_name has invalid Azure resource ID format" "ERROR"
        return 1
    fi
}

validate_keyvault_uri() {
    local value=$1
    local field_name=$2
    
    if [[ -z "$value" ]]; then
        print_status "$field_name is empty or null" "ERROR"
        return 1
    fi
    
    if [[ $value =~ YOUR_.*_HERE ]]; then
        print_status "$field_name still contains placeholder value" "ERROR"
        return 1
    fi
    
    # Key Vault URI pattern
    if [[ $value =~ ^https://[^/]+\.vault\.azure\.net/$ ]]; then
        print_status "$field_name has valid Key Vault URI format" "SUCCESS"
        return 0
    else
        print_status "$field_name is not a valid Key Vault URI" "ERROR"
        return 1
    fi
}

test_connectivity() {
    local url=$1
    local service_name=$2
    
    if [[ "$SKIP_CONNECTIVITY_TESTS" == "true" ]]; then
        print_status "Skipping connectivity test for $service_name" "INFO"
        return 0
    fi
    
    if command -v curl &> /dev/null; then
        if curl -s --head --fail --connect-timeout 10 "$url" &> /dev/null; then
            print_status "$service_name endpoint is reachable" "SUCCESS"
            return 0
        else
            print_status "$service_name endpoint is not reachable" "ERROR"
            return 1
        fi
    else
        print_status "curl not available, skipping connectivity test for $service_name" "WARNING"
        return 0
    fi
}

# Main validation function
validate_tenant_config() {
    local config_file=$1
    local overall_status=0
    
    print_status "Starting tenant configuration validation..." "INFO"
    print_status "Config file: $config_file" "INFO"
    print_status "$(printf '%*s' 60 '' | tr ' ' '-')" "INFO"
    
    # Check if file exists
    if [[ ! -f "$config_file" ]]; then
        print_status "Configuration file not found: $config_file" "ERROR"
        return 1
    fi
    
    print_status "Configuration file exists" "SUCCESS"
    
    # Check if jq is available for JSON parsing
    if ! command -v jq &> /dev/null; then
        print_status "jq is not installed. Please install jq to run this validation script." "ERROR"
        return 1
    fi
    
    # Validate JSON format
    if ! jq empty "$config_file" 2>/dev/null; then
        print_status "Invalid JSON format in configuration file" "ERROR"
        return 1
    fi
    
    print_status "JSON file is valid and parseable" "SUCCESS"
    
    # Validate structure
    print_status "" "INFO"
    print_status "📋 Validating JSON Structure..." "INFO"
    
    if ! jq -e '.tenants' "$config_file" >/dev/null 2>&1; then
        print_status "Missing 'tenants' section" "ERROR"
        overall_status=1
    else
        print_status "Found 'tenants' section" "SUCCESS"
    fi
    
    if ! jq -e '.azureCommunicationServices' "$config_file" >/dev/null 2>&1; then
        print_status "Missing 'azureCommunicationServices' section" "ERROR"
        overall_status=1
    else
        print_status "Found 'azureCommunicationServices' section" "SUCCESS"
    fi
    
    if ! jq -e '.keyVault' "$config_file" >/dev/null 2>&1; then
        print_status "Missing 'keyVault' section" "ERROR"
        overall_status=1
    else
        print_status "Found 'keyVault' section" "SUCCESS"
    fi
    
    # Validate tenant configurations
    print_status "" "INFO"
    print_status "🏢 Validating Tenant Configurations..." "INFO"
    
    for tenant_key in $(jq -r '.tenants | keys[]' "$config_file" 2>/dev/null); do
        print_status "" "INFO"
        print_status "Validating tenant: $tenant_key" "INFO"
        
        # Validate tenant name
        tenant_name=$(jq -r ".tenants.$tenant_key.name" "$config_file" 2>/dev/null)
        if [[ -z "$tenant_name" || "$tenant_name" == "null" ]]; then
            print_status "Tenant '$tenant_key' missing name" "ERROR"
            overall_status=1
        else
            print_status "Tenant '$tenant_key' has name: $tenant_name" "SUCCESS"
        fi
        
        # Validate tenant ID
        tenant_id=$(jq -r ".tenants.$tenant_key.tenantId" "$config_file" 2>/dev/null)
        if ! validate_guid "$tenant_id" "Tenant '$tenant_key' tenantId"; then
            overall_status=1
        fi
        
        # Validate client ID
        client_id=$(jq -r ".tenants.$tenant_key.appRegistration.clientId" "$config_file" 2>/dev/null)
        if ! validate_guid "$client_id" "Tenant '$tenant_key' clientId"; then
            overall_status=1
        fi
        
        # Validate client secret
        client_secret=$(jq -r ".tenants.$tenant_key.appRegistration.clientSecret" "$config_file" 2>/dev/null)
        if ! validate_client_secret "$client_secret" "Tenant '$tenant_key' clientSecret"; then
            overall_status=1
        fi
    done
    
    # Validate Azure Communication Services
    print_status "" "INFO"
    print_status "☁️  Validating Azure Communication Services..." "INFO"
    
    connection_string=$(jq -r '.azureCommunicationServices.connectionString' "$config_file" 2>/dev/null)
    if ! validate_connection_string "$connection_string" "ACS connectionString"; then
        overall_status=1
    fi
    
    endpoint_url=$(jq -r '.azureCommunicationServices.endpointUrl' "$config_file" 2>/dev/null)
    if ! validate_endpoint_url "$endpoint_url" "ACS endpointUrl"; then
        overall_status=1
    fi
    
    resource_id=$(jq -r '.azureCommunicationServices.resourceId' "$config_file" 2>/dev/null)
    if ! validate_resource_id "$resource_id" "ACS resourceId"; then
        overall_status=1
    fi
    
    # Test ACS endpoint connectivity
    if [[ -n "$endpoint_url" && ! "$endpoint_url" =~ YOUR_.*_HERE ]]; then
        test_connectivity "$endpoint_url" "Azure Communication Services"
    fi
    
    # Validate Key Vault
    print_status "" "INFO"
    print_status "🔐 Validating Key Vault Configuration..." "INFO"
    
    vault_uri=$(jq -r '.keyVault.vaultUri' "$config_file" 2>/dev/null)
    if ! validate_keyvault_uri "$vault_uri" "Key Vault vaultUri"; then
        overall_status=1
    fi
    
    # Test Key Vault connectivity
    if [[ -n "$vault_uri" && ! "$vault_uri" =~ YOUR_.*_HERE ]]; then
        test_connectivity "$vault_uri" "Key Vault"
    fi
    
    # Cross-validation checks
    print_status "" "INFO"
    print_status "🔍 Performing Cross-Validation..." "INFO"
    
    # Check if ACS endpoint URL matches connection string
    if [[ -n "$connection_string" && -n "$endpoint_url" ]]; then
        if [[ $connection_string =~ endpoint=([^;]+); ]]; then
            connection_string_endpoint="${BASH_REMATCH[1]}"
            if [[ "$connection_string_endpoint" == "$endpoint_url" ]]; then
                print_status "ACS endpoint URL matches connection string endpoint" "SUCCESS"
            else
                print_status "ACS endpoint URL does not match connection string endpoint" "WARNING"
                print_status "  Connection string: $connection_string_endpoint" "INFO"
                print_status "  Endpoint URL: $endpoint_url" "INFO"
            fi
        fi
    fi
    
    # Summary
    print_status "" "INFO"
    print_status "📊 Validation Summary" "INFO"
    print_status "$(printf '%*s' 60 '' | tr ' ' '-')" "INFO"
    
    if [[ $overall_status -eq 0 ]]; then
        print_status "✅ All validations passed! Configuration appears to be correct." "SUCCESS"
        return 0
    else
        print_status "❌ Some validations failed. Please review and fix the issues above." "ERROR"
        return 1
    fi
}

# Script execution
main() {
    if validate_tenant_config "$CONFIG_PATH"; then
        print_status "" "INFO"
        print_status "🎉 Configuration validation completed successfully!" "SUCCESS"
        exit 0
    else
        print_status "" "INFO"
        print_status "💥 Configuration validation failed!" "ERROR"
        exit 1
    fi
}

# Check for help flag
if [[ "$1" == "-h" || "$1" == "--help" ]]; then
    echo "Usage: $0 [config_file_path] [skip_connectivity_tests]"
    echo ""
    echo "Parameters:"
    echo "  config_file_path         Path to tenant-config.json (default: ./tenant-config.json)"
    echo "  skip_connectivity_tests  Set to 'true' to skip network connectivity tests (default: false)"
    echo ""
    echo "Examples:"
    echo "  $0"
    echo "  $0 /path/to/tenant-config.json"
    echo "  $0 ./tenant-config.json true"
    echo ""
    echo "Requirements:"
    echo "  - jq (for JSON parsing)"
    echo "  - curl (for connectivity tests, optional)"
    exit 0
fi

main