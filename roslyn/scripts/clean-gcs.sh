#!/bin/bash
cd "$(dirname "$0")/.."

echo "pwd: $(pwd)"
echo ".g.cs before: $(find ../game/Assets/Showcase -name '*.g.cs' 2>/dev/null | wc -l)"
find ../game/Assets/Showcase -name '*.g.cs' -print -delete || true
find ../game/Assets/Showcase -name '*.g.cs.meta' -print -delete || true
echo ".g.cs after: $(find ../game/Assets/Showcase -name '*.g.cs' 2>/dev/null | wc -l)"
