#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Creates Azure AI Search index, data source, and indexer for ContractDB.
.PARAMETER SearchEndpoint
  The Azure AI Search endpoint URL.
.PARAMETER SearchAdminKey
  The Azure AI Search admin API key.
.PARAMETER StorageConnectionString
  Storage connection string. Supports both identity-based (ResourceId=...) and key-based formats.
  When using ResourceId format, the Search service's system-assigned managed identity is used.
#>
param(
    [Parameter(Mandatory)][string]$SearchEndpoint,
    [Parameter(Mandatory)][string]$SearchAdminKey,
    [Parameter(Mandatory)][string]$StorageConnectionString
)

$apiVersion = "2024-07-01"
$headers = @{
    "Content-Type" = "application/json"
    "api-key"      = $SearchAdminKey
}

# Determine if -SkipCertificateCheck is available (PowerShell 7+)
$skipCertParam = @{}
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $skipCertParam['SkipCertificateCheck'] = $true
}

# ── 1. Create Index ──
Write-Host "Creating index: contracts-index..."
$indexBody = @{
    name   = "contracts-index"
    fields = @(
        @{ name = "id";             type = "Edm.String"; key = $true;  filterable = $true }
        @{ name = "sourceFileName"; type = "Edm.String"; key = $false; filterable = $true;  sortable = $true; searchable = $true }
        @{ name = "blobPath";       type = "Edm.String"; key = $false; filterable = $true }
        @{ name = "content";        type = "Edm.String"; key = $false; searchable = $true; filterable = $false; sortable = $false; facetable = $false }
        @{ name = "lastModified";   type = "Edm.DateTimeOffset"; key = $false; filterable = $true; sortable = $true }
        @{ name = "metadata_storage_path";  type = "Edm.String"; key = $false; filterable = $true }
        @{ name = "metadata_storage_name";  type = "Edm.String"; key = $false; filterable = $true; searchable = $true }
    )
} | ConvertTo-Json -Depth 10

try {
    Invoke-RestMethod -Uri "$SearchEndpoint/indexes/contracts-index?api-version=$apiVersion" `
        -Method PUT -Headers $headers -Body $indexBody @skipCertParam
    Write-Host "  Index created/updated."
} catch {
    Write-Warning "  Index creation failed: $_"
}

# ── 2. Create Data Source ──
Write-Host "Creating data source: contracts-datasource..."
$dsBody = @{
    name        = "contracts-datasource"
    type        = "azureblob"
    credentials = @{ connectionString = $StorageConnectionString }
    container   = @{ name = "contracts" }
} | ConvertTo-Json -Depth 5

try {
    Invoke-RestMethod -Uri "$SearchEndpoint/datasources/contracts-datasource?api-version=$apiVersion" `
        -Method PUT -Headers $headers -Body $dsBody @skipCertParam
    Write-Host "  Data source created/updated."
} catch {
    Write-Warning "  Data source creation failed: $_"
}

# ── 3. Create Indexer ──
Write-Host "Creating indexer: contracts-indexer..."
$indexerBody = @{
    name             = "contracts-indexer"
    dataSourceName   = "contracts-datasource"
    targetIndexName  = "contracts-index"
    parameters       = @{
        configuration = @{
            parsingMode                  = "default"
            dataToExtract                = "contentAndMetadata"
            imageAction                  = "none"
        }
    }
    fieldMappings = @(
        @{ sourceFieldName = "metadata_storage_path"; targetFieldName = "id"; mappingFunction = @{ name = "base64Encode" } }
        @{ sourceFieldName = "metadata_storage_name"; targetFieldName = "sourceFileName" }
        @{ sourceFieldName = "metadata_storage_path"; targetFieldName = "blobPath" }
        @{ sourceFieldName = "metadata_storage_last_modified"; targetFieldName = "lastModified" }
    )
    schedule = $null
} | ConvertTo-Json -Depth 10

try {
    Invoke-RestMethod -Uri "$SearchEndpoint/indexers/contracts-indexer?api-version=$apiVersion" `
        -Method PUT -Headers $headers -Body $indexerBody @skipCertParam
    Write-Host "  Indexer created/updated."
} catch {
    Write-Warning "  Indexer creation failed: $_"
}

# ── 4. Run Indexer ──
Write-Host "Running indexer..."
try {
    Invoke-RestMethod -Uri "$SearchEndpoint/indexers('contracts-indexer')/search.run?api-version=$apiVersion" `
        -Method POST -Headers $headers @skipCertParam
    Write-Host "  Indexer run triggered."
} catch {
    Write-Warning "  Indexer run failed: $_"
}

Write-Host "Done. Search assets created successfully."
