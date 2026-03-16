@description('Name of the Azure AI Search service')
param searchServiceName string

@description('Location for the search service')
param location string = resourceGroup().location

@description('SKU for the search service')
@allowed(['basic', 'standard', 'standard2', 'standard3'])
param sku string = 'basic'

@description('Tags to apply')
param tags object = {}

resource searchService 'Microsoft.Search/searchServices@2024-06-01-preview' = {
  name: searchServiceName
  location: location
  tags: tags
  sku: {
    name: sku
  }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'default'
    publicNetworkAccess: 'enabled'
  }
  identity: {
    type: 'SystemAssigned'
  }
}

output searchServiceId string = searchService.id
output searchServiceName string = searchService.name
output searchEndpoint string = 'https://${searchService.name}.search.windows.net'
#disable-next-line outputs-should-not-contain-secrets
output searchAdminKey string = searchService.listAdminKeys().primaryKey
output principalId string = searchService.identity.principalId
