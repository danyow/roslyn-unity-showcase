#!/bin/bash
set -e
cd "$(dirname "$0")/.."

echo "pwd: $(pwd)"
echo "--- Build: WriteFile (Release) ---"
dotnet build showcase.sln -c Release --no-incremental
echo "=== Done: WriteFile DLLs deployed. Unity will generate .g.cs on reimport ==="
