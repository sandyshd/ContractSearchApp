@description('Base name prefix for all resources')
param baseName string

@description('Location for all resources')
param location string = resourceGroup().location

@description('Object ID of the deployer principal (for Key Vault secrets access during deployment)')
param deployerPrincipalId string = ''

@description('Tags to apply to all resources')
param tags object = {
  project: 'ContractDB'
  environment: 'dev'
}

// ── Role Definition IDs ──
var kvSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'   // Key Vault Secrets User
var kvSecretsOfficerRoleId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7' // Key Vault Secrets Officer
var storageBlobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'       // Storage Blob Data Owner
var storageBlobDataReaderRoleId = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'      // Storage Blob Data Reader
var storageQueueDataContributorRoleId = '974c5e8b-45b9-4653-ba55-5f855dd0fb88' // Storage Queue Data Contributor
var storageTableDataContributorRoleId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3' // Storage Table Data Contributor
var cognitiveServicesOpenAiUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'  // Cognitive Services OpenAI User

// ── Monitoring ──
module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    logAnalyticsName: '${baseName}-logs'
    appInsightsName: '${baseName}-ai'
    location: location
    tags: tags
  }
}

// ── Storage ──
module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    storageAccountName: replace('${baseName}store', '-', '')
    location: location
    tags: tags
  }
}

// ── AI Search ──
module search 'modules/search.bicep' = {
  name: 'search'
  params: {
    searchServiceName: '${baseName}-search'
    location: location
    tags: tags
  }
}

// ── Azure OpenAI ──
module openAi 'modules/openai.bicep' = {
  name: 'openAi'
  params: {
    openAiName: '${baseName}-openai'
    location: location
    tags: tags
  }
}

// ── Key Vault (deployed before apps so they can reference its URI) ──
var kvSuffix = substring(uniqueString(resourceGroup().id), 0, 5)
module keyVault 'modules/keyvault.bicep' = {
  name: 'keyVault'
  params: {
    keyVaultName: '${baseName}kv${kvSuffix}'
    location: location
    searchAdminKey: search.outputs.searchAdminKey
    searchEndpoint: search.outputs.searchEndpoint
    accessPrincipalIds: []
    tags: tags
  }
}

// ── Function App ──
module functionApp 'modules/function.bicep' = {
  name: 'functionApp'
  params: {
    functionAppName: '${baseName}-func'
    location: location
    storageAccountName: storage.outputs.storageAccountName
    keyVaultUri: keyVault.outputs.keyVaultUri
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    tags: tags
  }
}

// ── Web App ──
module webApp 'modules/webapp.bicep' = {
  name: 'webApp'
  params: {
    webAppName: '${baseName}-web'
    location: location
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultUri: keyVault.outputs.keyVaultUri
    storageAccountName: storage.outputs.storageAccountName
    searchEndpoint: search.outputs.searchEndpoint
    openAiEndpoint: openAi.outputs.openAiEndpoint
    openAiDeploymentName: openAi.outputs.chatDeploymentName
    tags: tags
  }
}

// ══════════════════════════════════════════════════════════════════════
// RBAC Role Assignments  (scoped to the specific resource, least-privilege)
// ══════════════════════════════════════════════════════════════════════

// Reference existing resources so we can scope assignments to them
resource storageRef 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: replace('${baseName}store', '-', '')
}

resource kvRef 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: '${baseName}kv${kvSuffix}'
}

resource openAiRef 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: '${baseName}-openai'
}

// ── Key Vault: grant Secrets User to Function App & Web App ──
resource kvRoleFunc 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kvRef.id, '${baseName}-func', kvSecretsUserRoleId)
  scope: kvRef
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: functionApp.outputs.functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource kvRoleWeb 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kvRef.id, '${baseName}-web', kvSecretsUserRoleId)
  scope: kvRef
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: webApp.outputs.webAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ── Key Vault: grant Secrets Officer to deployer (for reading secrets during deployment) ──
resource kvRoleDeployer 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(deployerPrincipalId)) {
  name: guid(kvRef.id, 'deployer', kvSecretsOfficerRoleId)
  scope: kvRef
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsOfficerRoleId)
    principalId: deployerPrincipalId
    principalType: 'User'
  }
}

// ── Storage: Function App needs Blob Owner + Queue Contributor + Table Contributor ──
resource storBlobFunc 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageRef.id, '${baseName}-func', storageBlobDataOwnerRoleId)
  scope: storageRef
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwnerRoleId)
    principalId: functionApp.outputs.functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource storQueueFunc 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageRef.id, '${baseName}-func', storageQueueDataContributorRoleId)
  scope: storageRef
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageQueueDataContributorRoleId)
    principalId: functionApp.outputs.functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource storTableFunc 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageRef.id, '${baseName}-func', storageTableDataContributorRoleId)
  scope: storageRef
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataContributorRoleId)
    principalId: functionApp.outputs.functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ── Storage: Web App needs Blob Owner + Queue Contributor + Table Contributor ──
resource storBlobWeb 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageRef.id, '${baseName}-web', storageBlobDataOwnerRoleId)
  scope: storageRef
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwnerRoleId)
    principalId: webApp.outputs.webAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource storQueueWeb 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageRef.id, '${baseName}-web', storageQueueDataContributorRoleId)
  scope: storageRef
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageQueueDataContributorRoleId)
    principalId: webApp.outputs.webAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource storTableWeb 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageRef.id, '${baseName}-web', storageTableDataContributorRoleId)
  scope: storageRef
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataContributorRoleId)
    principalId: webApp.outputs.webAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ── Storage: Search service needs Blob Data Reader for blob indexer ──
resource storBlobSearch 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageRef.id, '${baseName}-search', storageBlobDataReaderRoleId)
  scope: storageRef
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataReaderRoleId)
    principalId: search.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── Storage: Deployer needs Blob Data Owner for uploading deployment packages ──
resource storBlobDeployer 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(deployerPrincipalId)) {
  name: guid(storageRef.id, 'deployer', storageBlobDataOwnerRoleId)
  scope: storageRef
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwnerRoleId)
    principalId: deployerPrincipalId
    principalType: 'User'
  }
}

// ── Azure OpenAI: Web App needs Cognitive Services OpenAI User ──
resource openAiRoleWeb 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(openAiRef.id, '${baseName}-web', cognitiveServicesOpenAiUserRoleId)
  scope: openAiRef
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAiUserRoleId)
    principalId: webApp.outputs.webAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ── Outputs ──
output webAppUrl string = webApp.outputs.webAppUrl
output searchEndpoint string = search.outputs.searchEndpoint
output storageAccountName string = storage.outputs.storageAccountName
output storageAccountId string = storage.outputs.storageAccountId
output blobEndpoint string = storage.outputs.blobEndpoint
output keyVaultName string = keyVault.outputs.keyVaultName
output functionAppName string = functionApp.outputs.functionAppName
output openAiEndpoint string = openAi.outputs.openAiEndpoint
output openAiName string = openAi.outputs.openAiName
