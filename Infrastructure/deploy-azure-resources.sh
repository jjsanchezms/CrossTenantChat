#!/bin/bash

# Cross-Tenant ACS Demo - Azure Resources Deployment Script
# This script provisions Azure resources in the Contoso tenant

set -e

# Configuration
RESOURCE_GROUP_NAME="rg-crosstenant-demo"
LOCATION="eastus"
DEPLOYMENT_NAME="crosstenant-deployment-$(date +%Y%m%d-%H%M%S)"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}🚀 Cross-Tenant ACS Demo - Azure Resources Deployment${NC}"
echo -e "${BLUE}====================================================${NC}"

# Check if Azure CLI is installed
if ! command -v az &> /dev/null; then
    echo -e "${RED}❌ Azure CLI is not installed. Please install Azure CLI first.${NC}"
    exit 1
fi

echo -e "${GREEN}✅ Azure CLI found${NC}"

# Login check
echo -e "${YELLOW}🔐 Checking Azure login status...${NC}"
if ! az account show &> /dev/null; then
    echo -e "${YELLOW}⚠️  Not logged in to Azure. Please login:${NC}"
    az login
fi

# Get current subscription
SUBSCRIPTION_INFO=$(az account show --query "{subscriptionId:id, tenantId:tenantId, name:name}" -o json)
SUBSCRIPTION_ID=$(echo $SUBSCRIPTION_INFO | jq -r '.subscriptionId')
TENANT_ID=$(echo $SUBSCRIPTION_INFO | jq -r '.tenantId')
SUBSCRIPTION_NAME=$(echo $SUBSCRIPTION_INFO | jq -r '.name')

echo -e "${GREEN}✅ Logged in to Azure${NC}"
echo -e "   Subscription: ${SUBSCRIPTION_NAME}"
echo -e "   Subscription ID: ${SUBSCRIPTION_ID}"
echo -e "   Tenant ID: ${TENANT_ID}"

# Prompt for tenant IDs
echo ""
echo -e "${YELLOW}📋 Please provide tenant information:${NC}"
read -p "Enter Contoso Tenant ID (current tenant): " CONTOSO_TENANT_ID
read -p "Enter Fabrikam Tenant ID (external tenant): " FABRIKAM_TENANT_ID

if [ -z "$CONTOSO_TENANT_ID" ] || [ -z "$FABRIKAM_TENANT_ID" ]; then
    echo -e "${RED}❌ Both tenant IDs are required${NC}"
    exit 1
fi

# Create resource group
echo -e "${YELLOW}🏗️  Creating resource group: ${RESOURCE_GROUP_NAME}${NC}"
az group create \
    --name $RESOURCE_GROUP_NAME \
    --location $LOCATION \
    --tags Environment=demo Project=CrossTenantChat

if [ $? -eq 0 ]; then
    echo -e "${GREEN}✅ Resource group created successfully${NC}"
else
    echo -e "${RED}❌ Failed to create resource group${NC}"
    exit 1
fi

# Update parameters file
echo -e "${YELLOW}📝 Updating deployment parameters...${NC}"
PARAMS_FILE="main.parameters.json"
cp $PARAMS_FILE "${PARAMS_FILE}.backup"

# Create updated parameters file
cat > $PARAMS_FILE << EOF
{
  "\$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
  "contentVersion": "1.0.0.0",
  "parameters": {
    "projectName": {
      "value": "crosstenant"
    },
    "location": {
      "value": "$LOCATION"
    },
    "contosoTenantId": {
      "value": "$CONTOSO_TENANT_ID"
    },
    "fabrikamTenantId": {
      "value": "$FABRIKAM_TENANT_ID"
    },
    "environment": {
      "value": "dev"
    }
  }
}
EOF

# Deploy Bicep template
echo -e "${YELLOW}🚀 Deploying Azure resources...${NC}"
echo "This may take a few minutes..."

DEPLOYMENT_OUTPUT=$(az deployment group create \
    --resource-group $RESOURCE_GROUP_NAME \
    --name $DEPLOYMENT_NAME \
    --template-file main.bicep \
    --parameters @$PARAMS_FILE \
    --query properties.outputs \
    -o json)

if [ $? -eq 0 ]; then
    echo -e "${GREEN}✅ Azure resources deployed successfully!${NC}"
    
    # Extract outputs
    ACS_NAME=$(echo $DEPLOYMENT_OUTPUT | jq -r '.acsResourceName.value')
    ACS_ENDPOINT=$(echo $DEPLOYMENT_OUTPUT | jq -r '.acsEndpoint.value')
    ACS_CONNECTION_STRING=$(echo $DEPLOYMENT_OUTPUT | jq -r '.acsConnectionString.value')
    KEY_VAULT_NAME=$(echo $DEPLOYMENT_OUTPUT | jq -r '.keyVaultName.value')
    KEY_VAULT_URI=$(echo $DEPLOYMENT_OUTPUT | jq -r '.keyVaultUri.value')
    
    echo ""
    echo -e "${GREEN}📋 Deployment Summary:${NC}"
    echo -e "   Resource Group: ${RESOURCE_GROUP_NAME}"
    echo -e "   ACS Resource Name: ${ACS_NAME}"
    echo -e "   ACS Endpoint: ${ACS_ENDPOINT}"
    echo -e "   Key Vault Name: ${KEY_VAULT_NAME}"
    echo -e "   Key Vault URI: ${KEY_VAULT_URI}"
    
    # Save configuration to file
    CONFIG_FILE="../appsettings.Live.json"
    echo -e "${YELLOW}💾 Creating configuration file: ${CONFIG_FILE}${NC}"
    
    cat > $CONFIG_FILE << EOF
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "CrossTenantChat": "Information"
    }
  },
  "AllowedHosts": "*",
  "Azure": {
    "AzureAd": {
      "Instance": "https://login.microsoftonline.com/",
      "ClientId": "your-app-registration-client-id",
      "ClientSecret": "your-app-registration-secret",
      "TenantId": "$CONTOSO_TENANT_ID",
      "FabrikamTenantId": "$FABRIKAM_TENANT_ID",
      "ContosoTenantId": "$CONTOSO_TENANT_ID",
      "Authority": "https://login.microsoftonline.com/$CONTOSO_TENANT_ID",
      "Scopes": [
        "https://communication.azure.com/.default",
        "https://graph.microsoft.com/User.Read"
      ]
    },
    "AzureCommunicationServices": {
      "ConnectionString": "$ACS_CONNECTION_STRING",
      "EndpointUrl": "https://$ACS_ENDPOINT",
      "ResourceId": "$(echo $DEPLOYMENT_OUTPUT | jq -r '.acsResourceId.value')"
    },
    "KeyVault": {
      "VaultUri": "$KEY_VAULT_URI"
    }
  }
}
EOF
    
    echo ""
    echo -e "${GREEN}🎉 Deployment completed successfully!${NC}"
    echo ""
    echo -e "${BLUE}Next steps:${NC}"
    echo "1. Configure Entra ID app registrations (run setup-app-registrations.sh)"
    echo "2. Update appsettings.Live.json with your app registration details"
    echo "3. Grant yourself access to Key Vault if needed"
    echo "4. Run the application with: dotnet run --environment=Live"
    
else
    echo -e "${RED}❌ Failed to deploy Azure resources${NC}"
    echo "Check the error messages above for details."
    exit 1
fi
