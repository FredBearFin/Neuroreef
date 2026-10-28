# Builds dist\Neuroreef.exe (Windows x64, single file, no runtime needed). Requires the .NET 8 SDK.
$ErrorActionPreference = "Stop"
dotnet test tests/Neuroreef.Tests -c Release
dotnet publish src/Neuroreef.Server -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
Write-Host "built dist\Neuroreef.exe"
