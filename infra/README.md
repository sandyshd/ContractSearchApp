# Infrastructure

This folder contains Azure Bicep templates to deploy all resources for the ContractDB Prompt Runner.

## Resources Provisioned

| Resource | Module | Purpose |
|----------|--------|---------|
| Storage Account (GPv2) | `storage.bicep` | Blob (contracts), Queue (indexing-requests), Table (indexingJobs) |
| Azure AI Search (Basic) | `search.bicep` | Full-text search over contract PDFs |
| Function App (Linux) | `function.bicep` | IndexerWorker – queue-triggered indexing |
| App Service (Linux) | `webapp.bicep` | Blazor Web App + Minimal APIs |
| Key Vault | `keyvault.bicep` | Stores Search admin key, endpoint, storage conn string |
| App Insights + Log Analytics | `monitoring.bicep` | Observability |

## Deploy

```bash
az deployment group create \
  --resource-group contractdb-rg \
  --template-file main.bicep \
  --parameters @main.parameters.json
```

After deployment, run `scripts/create-search-assets.sh` (or `.ps1`) to create the search index, data source, and indexer.
