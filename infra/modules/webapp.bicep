@description('Name of the Web App')
param webAppName string

@description('Location')
param location string = resourceGroup().location

@description('Application Insights connection string')
@secure()
param appInsightsConnectionString string

@description('Key Vault URI')
param keyVaultUri string

@description('Storage account name for identity-based connection')
param storageAccountName string

@description('Search endpoint')
param searchEndpoint string

@description('Azure OpenAI endpoint')
param openAiEndpoint string

@description('Azure OpenAI chat deployment name')
param openAiDeploymentName string = 'gpt-4o'

@description('Tags to apply')
param tags object = {}

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${webAppName}-plan'
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        { name: 'KeyVaultUri', value: keyVaultUri }
        { name: 'StorageAccountName', value: storageAccountName }
        { name: 'SearchEndpoint', value: searchEndpoint }
        { name: 'SearchIndexName', value: 'contracts-index' }
        { name: 'IndexerName', value: 'contracts-indexer' }
        { name: 'BlobContainerName', value: 'contracts' }
        { name: 'QueueName', value: 'indexing-requests' }
        { name: 'TableName', value: 'indexingJobs' }
        { name: 'AzureOpenAiEndpoint', value: openAiEndpoint }
        { name: 'AzureOpenAiDeployment', value: openAiDeploymentName }
      ]
    }
  }
}

output webAppId string = webApp.id
output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output webAppPrincipalId string = webApp.identity.principalId
