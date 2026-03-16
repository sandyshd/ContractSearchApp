#!/usr/bin/env bash
set -euo pipefail

STORAGE_CONNECTION_STRING="${1:?Usage: $0 <StorageConnectionString> [SampleDir]}"
SAMPLE_DIR="${2:-$(dirname "$0")/../samples}"

if [ ! -d "$SAMPLE_DIR" ]; then
  echo "Sample directory not found at $SAMPLE_DIR. Create it and add PDF files."
  exit 1
fi

COUNT=0
for pdf in "$SAMPLE_DIR"/*.pdf; do
  [ -f "$pdf" ] || continue
  echo "Uploading $(basename "$pdf")..."
  az storage blob upload \
    --connection-string "$STORAGE_CONNECTION_STRING" \
    --container-name contracts \
    --file "$pdf" \
    --name "$(basename "$pdf")" \
    --overwrite
  COUNT=$((COUNT + 1))
done

echo "Uploaded $COUNT sample PDF(s)."
