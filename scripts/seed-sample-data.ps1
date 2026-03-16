#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Seeds sample PDF contracts into the blob storage container for testing.
.PARAMETER StorageConnectionString
  The Azure Storage connection string.
.PARAMETER SampleDir
  Path to directory containing sample PDFs (default: ../samples).
#>
param(
    [Parameter(Mandatory)][string]$StorageConnectionString,
    [string]$SampleDir = "$PSScriptRoot/../samples"
)

if (-not (Test-Path $SampleDir)) {
    Write-Warning "Sample directory not found at $SampleDir. Create it and add PDF files."
    exit 1
}

$pdfs = Get-ChildItem -Path $SampleDir -Filter "*.pdf"
if ($pdfs.Count -eq 0) {
    Write-Warning "No PDF files found in $SampleDir."
    exit 1
}

foreach ($pdf in $pdfs) {
    Write-Host "Uploading $($pdf.Name)..."
    az storage blob upload `
        --connection-string $StorageConnectionString `
        --container-name contracts `
        --file $pdf.FullName `
        --name $pdf.Name `
        --overwrite
}

Write-Host "Uploaded $($pdfs.Count) sample PDF(s)."
