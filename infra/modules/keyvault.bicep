@description('Name of the Key Vault')
param keyVaultName string

@description('Location')
param location string = resourceGroup().location

@description('Tenant ID')
param tenantId string = subscription().tenantId

@description('Principal IDs to grant access')
param accessPrincipalIds array = []

@description('Search admin key to store')
@secure()
param searchAdminKey string

@description('Search endpoint to store')
param searchEndpoint string

@description('Tags to apply')
param tags object = {}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: false
    publicNetworkAccess: 'Enabled'
  }
}

resource searchAdminKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'SearchAdminKey'
  properties: {
    value: searchAdminKey
  }
}

resource searchEndpointSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'SearchEndpoint'
  properties: {
    value: searchEndpoint
  }
}

// Grant Key Vault Secrets User role to each principal
@description('Key Vault Secrets User role')
var kvSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource roleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for (principalId, i) in accessPrincipalIds: {
    name: guid(keyVault.id, principalId, kvSecretsUserRoleId)
    scope: keyVault
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
      principalId: principalId
      principalType: 'ServicePrincipal'
    }
  }
]

output keyVaultId string = keyVault.id
output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultName string = keyVault.name
