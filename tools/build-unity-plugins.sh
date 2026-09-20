#!/usr/bin/env bash
#
# Builds the shared game-rule libraries and drops them into the Unity project, then copies
# the content pack into StreamingAssets.
#
# Unity consumes DarkMyst.Combat and DarkMyst.Content as plain DLLs rather than as source,
# so the server and the client provably run the same compiled rules. Run this after any
# change under src/ or content/, and before opening the Unity project.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${1:-Release}"
PLUGINS="$ROOT/unity/DarkMyst/Assets/Plugins/DarkMyst"
STREAMING="$ROOT/unity/DarkMyst/Assets/StreamingAssets/content"

echo "==> Building $CONFIGURATION"
dotnet build "$ROOT/src/DarkMyst.Combat/DarkMyst.Combat.csproj"   -c "$CONFIGURATION" -v minimal
dotnet build "$ROOT/src/DarkMyst.Content/DarkMyst.Content.csproj" -c "$CONFIGURATION" -v minimal

echo "==> Copying assemblies into $PLUGINS"
mkdir -p "$PLUGINS"
for project in DarkMyst.Combat DarkMyst.Content; do
  cp "$ROOT/src/$project/bin/$CONFIGURATION/netstandard2.1/$project.dll" "$PLUGINS/"
  # The XML docs give IDE tooltips inside Unity; harmless if they are missing.
  cp "$ROOT/src/$project/bin/$CONFIGURATION/netstandard2.1/$project.xml" "$PLUGINS/" 2>/dev/null || true
done

# Newtonsoft.Json is deliberately NOT copied: Unity provides it through
# com.unity.nuget.newtonsoft-json, and shipping a second copy causes duplicate-assembly
# errors at build time.
rm -f "$PLUGINS/Newtonsoft.Json.dll"

echo "==> Syncing content into $STREAMING"
mkdir -p "$STREAMING"
rm -f "$STREAMING"/*.json
cp "$ROOT"/content/*.json "$STREAMING/"

echo "==> Validating the copied content pack"
dotnet run --project "$ROOT/tools/DarkMyst.SimRunner" -c "$CONFIGURATION" -- validate --content "$STREAMING"

echo "Done. Open unity/DarkMyst in Unity Hub (2022.3 LTS)."
