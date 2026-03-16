#!/usr/bin/env bash
set -euo pipefail

# Usage: ./create-search-assets.sh <SearchEndpoint> <SearchAdminKey> <StorageConnectionString>
SEARCH_ENDPOINT="${1:?Usage: $0 <SearchEndpoint> <SearchAdminKey> <StorageConnectionString>}"
SEARCH_ADMIN_KEY="${2:?}"
STORAGE_CONNECTION_STRING="${3:?}"

API_VERSION="2025-05-01-preview"

echo "Creating index: contracts-index..."
curl -s -X PUT "${SEARCH_ENDPOINT}/indexes/contracts-index?api-version=${API_VERSION}" \
  -H "Content-Type: application/json" \
  -H "api-key: ${SEARCH_ADMIN_KEY}" \
  -d '{
    "name": "contracts-index",
    "fields": [
      { "name": "id",             "type": "Edm.String", "key": true,  "filterable": true },
      { "name": "sourceFileName", "type": "Edm.String", "key": false, "filterable": true,  "sortable": true, "searchable": true },
      { "name": "blobPath",       "type": "Edm.String", "key": false, "filterable": true },
      { "name": "content",        "type": "Edm.String", "key": false, "searchable": true },
      { "name": "lastModified",   "type": "Edm.DateTimeOffset", "key": false, "filterable": true, "sortable": true },
      { "name": "metadata_storage_path",  "type": "Edm.String", "key": false, "filterable": true },
      { "name": "metadata_storage_name",  "type": "Edm.String", "key": false, "filterable": true, "searchable": true }
    ]
  }'
echo "  Index created/updated."

echo "Creating data source: contracts-datasource..."
curl -s -X PUT "${SEARCH_ENDPOINT}/datasources/contracts-datasource?api-version=${API_VERSION}" \
  -H "Content-Type: application/json" \
  -H "api-key: ${SEARCH_ADMIN_KEY}" \
  -d "{
    \"name\": \"contracts-datasource\",
    \"type\": \"azureblob\",
    \"credentials\": { \"connectionString\": \"${STORAGE_CONNECTION_STRING}\" },
    \"container\": { \"name\": \"contracts\" }
  }"
echo "  Data source created/updated."

echo "Creating indexer: contracts-indexer..."
curl -s -X PUT "${SEARCH_ENDPOINT}/indexers/contracts-indexer?api-version=${API_VERSION}" \
  -H "Content-Type: application/json" \
  -H "api-key: ${SEARCH_ADMIN_KEY}" \
  -d '{
    "name": "contracts-indexer",
    "dataSourceName": "contracts-datasource",
    "targetIndexName": "contracts-index",
    "parameters": {
      "configuration": {
        "parsingMode": "default",
        "dataToExtract": "contentAndMetadata",
        "imageAction": "none"
      }
    },
    "fieldMappings": [
      { "sourceFieldName": "metadata_storage_path", "targetFieldName": "id", "mappingFunction": { "name": "base64Encode" } },
      { "sourceFieldName": "metadata_storage_name", "targetFieldName": "sourceFileName" },
      { "sourceFieldName": "metadata_storage_path", "targetFieldName": "blobPath" },
      { "sourceFieldName": "metadata_storage_last_modified", "targetFieldName": "lastModified" }
    ]
  }'
echo "  Indexer created/updated."

echo "Running indexer..."
curl -s -X POST "${SEARCH_ENDPOINT}/indexers('contracts-indexer')/search.run?api-version=${API_VERSION}" \
  -H "api-key: ${SEARCH_ADMIN_KEY}"
echo "  Indexer run triggered."

echo "Done. Search assets created successfully."
