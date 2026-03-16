# ContractDb.PromptLibrary

This folder contains the prompt templates and schemas used by the ContractDB Prompt Runner.

## Files

| File | Purpose |
|------|---------|
| `prompts.json` | All prompt templates (Section A, B, Master) |
| `promptGroups.json` | Logical groupings of prompts |
| `schemas/resultSchemas.json` | JSON schema for prompt result output |

## Adding Prompts

Add entries to `prompts.json` with:
- Unique `id` (e.g., `A06`, `B06`)
- `section`: `A` (priority), `B` (extended), or `Master`
- `searchSynonyms`: terms used to build Azure AI Search queries
- `expectedResultType`: `text`, `date`, `boolean`, or `currency`
