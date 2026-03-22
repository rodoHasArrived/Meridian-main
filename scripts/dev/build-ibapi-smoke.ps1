param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "Running Interactive Brokers compile-only smoke build with the local IBApi stub..."
dotnet build "src/Meridian.Infrastructure/Meridian.Infrastructure.csproj" `
    -c $Configuration `
    -p:EnableWindowsTargeting=true `
    -p:EnableIbApiSmoke=true `
    -maxcpucount:1
