#!/bin/sh
# Builds dist/<rid>/Neuroreef (single file). Requires the .NET 8 SDK. Default rid: linux-x64; pass win-x64 for Windows.
set -e
RID=${1:-linux-x64}
dotnet test tests/Neuroreef.Tests -c Release
dotnet publish src/Neuroreef.Server -c Release -r "$RID" --self-contained -p:PublishSingleFile=true -o "dist/$RID"
echo "built dist/$RID"
