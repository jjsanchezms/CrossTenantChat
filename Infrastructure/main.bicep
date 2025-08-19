@description('Azure Communication Services and Entra ID setup for cross-tenant chat demo')

@minLength(3)
@maxLength(10)
param projectName string = 'crosstenant'

@description('Primary Azure region for resources')
param location string = resourceGroup().location

@description('Contoso tenant ID (ACS host)')
param contosoTenantId string

@description('Fabrikam tenant ID (user source)')
param fabrikamTenantId string

@description('Environment name (dev, staging, prod)')
param environment string = 'dev'

// Variables
var resourcePrefix = '${projectName}-${environment}'
var acsName = '${resourcePrefix}-acs'
var keyVaultName = '${resourcePrefix}-kv-${uniqueString(resourceGroup().id)}'

// Azure Communication Services
resource communicationService 'Microsoft.Communication/communicationServices@2023-03-31' = {
  name: acsName
  location: 'global'
  properties: {
    dataLocation: location == 'eastus' ? 'United States' : 'Europe'
    linkedDomains: []
  }
  tags: {
    Environment: environment
    Project: 'CrossTenantChat'
    Purpose: 'Demo'
  }
}

// Key Vault for storing secrets
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: contosoTenantId
    accessPolicies: []
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enableRbacAuthorization: true
    publicNetworkAccess: 'Enabled'
  }
  tags: {
    Environment: environment
    Project: 'CrossTenantChat'
  }
}

// Store ACS connection string in Key Vault
resource acsConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'AcsConnectionString'
  properties: {
    value: communicationService.listKeys().primaryConnectionString
    contentType: 'text/plain'
  }
}

// Store tenant IDs in Key Vault
resource contosoTenantSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'ContosoTenantId'
  properties: {
    value: contosoTenantId
    contentType: 'text/plain'
  }
}

resource fabrikamTenantSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'FabrikamTenantId'
  properties: {
    value: fabrikamTenantId
    contentType: 'text/plain'
  }
}

// App Service Plan (for hosting if needed)
resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: '${resourcePrefix}-asp'
  location: location
  kind: 'app'
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: false
  }
  tags: {
    Environment: environment
    Project: 'CrossTenantChat'
  }
}

// Outputs
output acsResourceName string = communicationService.name
output acsEndpoint string = communicationService.properties.hostName
output acsConnectionString string = communicationService.listKeys().primaryConnectionString
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
output appServicePlanName string = appServicePlan.name

// Resource identifiers for configuration
output acsResourceId string = communicationService.id
output resourceGroupName string = resourceGroup().name
output subscriptionId string = subscription().subscriptionId
