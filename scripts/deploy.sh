#!/usr/bin/env bash
set -euo pipefail

RESOURCE_GROUP="${1:-contractdb-rg}"
LOCATION="${2:-eastus}"
BASE_NAME="${3:-contractdb}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "=== ContractDB Deployment ==="

# 1. Create resource group
echo "1. Creating resource group: $RESOURCE_GROUP..."
az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none

# 2. Deploy Bicep
echo "2. Deploying infrastructure..."
DEPLOYMENT=$(az deployment group create \
    --resource-group "$RESOURCE_GROUP" \
    --template-file "$SCRIPT_DIR/../infra/main.bicep" \
    --parameters baseName="$BASE_NAME" location="$LOCATION" \
    --output json)

SEARCH_ENDPOINT=$(echo "$DEPLOYMENT" | jq -r '.properties.outputs.searchEndpoint.value')
STORAGE_ACCOUNT=$(echo "$DEPLOYMENT" | jq -r '.properties.outputs.storageAccountName.value')
KV_NAME=$(echo "$DEPLOYMENT" | jq -r '.properties.outputs.keyVaultName.value')
FUNC_APP_NAME=$(echo "$DEPLOYMENT" | jq -r '.properties.outputs.functionAppName.value')
WEB_APP_URL=$(echo "$DEPLOYMENT" | jq -r '.properties.outputs.webAppUrl.value')

echo "  Search Endpoint:  $SEARCH_ENDPOINT"
echo "  Storage Account:  $STORAGE_ACCOUNT"
echo "  Key Vault:        $KV_NAME"
echo "  Function App:     $FUNC_APP_NAME"
echo "  Web App URL:      $WEB_APP_URL"

# 3. Get secrets
echo "3. Retrieving secrets from Key Vault..."
SEARCH_ADMIN_KEY=$(az keyvault secret show --vault-name "$KV_NAME" --name SearchAdminKey --query value -o tsv)
STORAGE_CONN_STR=$(az keyvault secret show --vault-name "$KV_NAME" --name StorageConnectionString --query value -o tsv)

# 4. Create search assets
echo "4. Creating search assets..."
bash "$SCRIPT_DIR/create-search-assets.sh" "$SEARCH_ENDPOINT" "$SEARCH_ADMIN_KEY" "$STORAGE_CONN_STR"

# 5. Build and deploy Function App
echo "5. Building and deploying Function App..."
cd "$SCRIPT_DIR/../src/ContractDb.IndexerWorker"
dotnet publish -c Release -o ./publish
cd publish && zip -r ../deploy.zip . && cd ..
az functionapp deployment source config-zip \
    --resource-group "$RESOURCE_GROUP" \
    --name "$FUNC_APP_NAME" \
    --src ./deploy.zip
rm -rf publish deploy.zip

# 6. Build and deploy Web App
echo "6. Building and deploying Web App..."
cd "$SCRIPT_DIR/../src/ContractDb.WebApp"
dotnet publish -c Release -o ./publish
cd publish && zip -r ../deploy.zip . && cd ..
az webapp deploy \
    --resource-group "$RESOURCE_GROUP" \
    --name "${BASE_NAME}-web" \
    --src-path ./deploy.zip \
    --type zip
rm -rf publish deploy.zip

echo ""
echo "=== Deployment Complete ==="
echo "Web App URL: $WEB_APP_URL"
