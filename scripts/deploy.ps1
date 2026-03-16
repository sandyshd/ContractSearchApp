#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Deploys the entire ContractDB solution to Azure.
.PARAMETER ResourceGroup
  The Azure resource group name.
.PARAMETER Location
  The Azure region (default: eastus2).
.PARAMETER BaseName
  The base name prefix for all resources (default: contractdb).
#>
param(
    [string]$ResourceGroup = "contractdb-rg",
    [string]$Location = "eastus2",
    [string]$BaseName = "contractdb"
)

$ErrorActionPreference = "Stop"

Write-Host "=== ContractDB Deployment ==="

# 0. Get deployer principal ID for RBAC assignments
Write-Host "0. Resolving deployer identity..."
$deployerPrincipalId = az ad signed-in-user show --query id -o tsv 2>$null
if (-not $deployerPrincipalId) {
    Write-Warning "  Could not resolve signed-in user. Key Vault / Storage deployer roles will be skipped."
    $deployerPrincipalId = ""
} else {
    Write-Host "  Deployer: $deployerPrincipalId"
}

# 1. Create resource group
Write-Host "1. Creating resource group: $ResourceGroup..."
az group create --name $ResourceGroup --location $Location --output none

# 2. Deploy Bicep (infra + all RBAC role assignments)
Write-Host "2. Deploying infrastructure..."
$deployment = az deployment group create `
    --resource-group $ResourceGroup `
    --template-file "$PSScriptRoot/../infra/main.bicep" `
    --parameters baseName=$BaseName location=$Location deployerPrincipalId=$deployerPrincipalId `
    --output json | ConvertFrom-Json

$searchEndpoint   = $deployment.properties.outputs.searchEndpoint.value
$storageAccount   = $deployment.properties.outputs.storageAccountName.value
$storageAccountId = $deployment.properties.outputs.storageAccountId.value
$keyVaultName     = $deployment.properties.outputs.keyVaultName.value
$functionAppName  = $deployment.properties.outputs.functionAppName.value
$webAppUrl        = $deployment.properties.outputs.webAppUrl.value

Write-Host "  Search Endpoint:  $searchEndpoint"
Write-Host "  Storage Account:  $storageAccount"
Write-Host "  Key Vault:        $keyVaultName"
Write-Host "  Function App:     $functionAppName"
Write-Host "  Web App URL:      $webAppUrl"

# 3. Wait for RBAC propagation
Write-Host "3. Waiting 30s for RBAC role propagation..."
Start-Sleep -Seconds 30

# 4. Get secrets for search asset creation
Write-Host "4. Retrieving secrets from Key Vault..."
$searchAdminKey = az keyvault secret show --vault-name $keyVaultName --name SearchAdminKey --query value -o tsv
$storageResourceId = "ResourceId=$storageAccountId;"

# 5. Create search assets (data source uses managed identity, not key)
Write-Host "5. Creating search assets..."
& "$PSScriptRoot/create-search-assets.ps1" `
    -SearchEndpoint $searchEndpoint `
    -SearchAdminKey $searchAdminKey `
    -StorageConnectionString $storageResourceId

# 6. Build and deploy Function App (via blob run-from-package)
Write-Host "6. Building and deploying Function App..."
Push-Location "$PSScriptRoot/../src/ContractDb.IndexerWorker"
dotnet publish -c Release -o ./publish
Compress-Archive -Path ./publish/* -DestinationPath ./deploy.zip -Force

Write-Host "  Uploading Function App package to blob..."
az storage blob upload `
    --account-name $storageAccount `
    --container-name deployments `
    --name func-deploy.zip `
    --file ./deploy.zip `
    --auth-mode login `
    --overwrite `
    --output none

az functionapp config appsettings set `
    --resource-group $ResourceGroup `
    --name $functionAppName `
    --settings `
        "WEBSITE_RUN_FROM_PACKAGE=https://${storageAccount}.blob.core.windows.net/deployments/func-deploy.zip" `
        "WEBSITE_RUN_FROM_PACKAGE_BLOB_MI_RESOURCE_ID=SystemAssigned" `
    --output none

Remove-Item ./publish -Recurse -Force
Remove-Item ./deploy.zip -Force
Pop-Location
Write-Host "  Function App deployed."

# 7. Build and deploy Web App (via blob run-from-package)
Write-Host "7. Building and deploying Web App..."
Push-Location "$PSScriptRoot/../src/ContractDb.WebApp"
dotnet publish -c Release -o ./publish
Compress-Archive -Path ./publish/* -DestinationPath ./deploy.zip -Force

Write-Host "  Uploading Web App package to blob..."
az storage blob upload `
    --account-name $storageAccount `
    --container-name deployments `
    --name web-deploy.zip `
    --file ./deploy.zip `
    --auth-mode login `
    --overwrite `
    --output none

az webapp config appsettings set `
    --resource-group $ResourceGroup `
    --name "${BaseName}-web" `
    --settings `
        "WEBSITE_RUN_FROM_PACKAGE=https://${storageAccount}.blob.core.windows.net/deployments/web-deploy.zip" `
        "WEBSITE_RUN_FROM_PACKAGE_BLOB_MI_RESOURCE_ID=SystemAssigned" `
    --output none

Remove-Item ./publish -Recurse -Force
Remove-Item ./deploy.zip -Force
Pop-Location
Write-Host "  Web App deployed."

Write-Host ""
Write-Host "=== Deployment Complete ==="
Write-Host "Web App URL: $webAppUrl"
