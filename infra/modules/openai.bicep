@description('Name of the Azure OpenAI resource')
param openAiName string

@description('Location')
param location string = resourceGroup().location

@description('Chat model deployment name')
param chatDeploymentName string = 'gpt-4o'

@description('Chat model name')
param chatModelName string = 'gpt-4o'

@description('Chat model version')
param chatModelVersion string = '2024-08-06'

@description('Model capacity in tokens-per-minute (thousands)')
param chatModelCapacity int = 30

@description('Tags to apply')
param tags object = {}

resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: openAiName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: openAiName
    publicNetworkAccess: 'Enabled'
  }
  identity: {
    type: 'SystemAssigned'
  }
}

resource chatDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: chatDeploymentName
  sku: {
    name: 'Standard'
    capacity: chatModelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: chatModelName
      version: chatModelVersion
    }
  }
}

output openAiId string = openAi.id
output openAiEndpoint string = openAi.properties.endpoint
output openAiName string = openAi.name
output chatDeploymentName string = chatDeployment.name
