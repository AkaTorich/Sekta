$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "=== Building Sekta Client ===" -ForegroundColor Cyan
dotnet publish "$root\src\Sekta.Client\Sekta.Client.csproj" `
    -f net9.0-windows10.0.19041.0 `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:WindowsPackageType=None `
    -p:EnableMsixTooling=true `
    -p:CiTargetFramework=net9.0-windows10.0.19041.0 `
    -o "$root\publish\client"

if ($LASTEXITCODE -ne 0) { Write-Host "Client build FAILED" -ForegroundColor Red; exit 1 }
Write-Host "Client OK" -ForegroundColor Green

Write-Host ""
Write-Host "=== Building Sekta Server ===" -ForegroundColor Cyan
dotnet publish "$root\src\Sekta.Server\Sekta.Server.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o "$root\publish\server"

if ($LASTEXITCODE -ne 0) { Write-Host "Server build FAILED" -ForegroundColor Red; exit 1 }

# Clean up unnecessary server files
$serverDir = "$root\publish\server"
@("*.pdb", "web.config", "appsettings.Development.json", "aspnetcorev2_inprocess.dll", "*.staticwebassets.*") | ForEach-Object {
    Get-ChildItem -Path $serverDir -Filter $_ -ErrorAction SilentlyContinue | Remove-Item -Force
}
# Clean up client pdb
Get-ChildItem -Path "$root\publish\client" -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "Server OK" -ForegroundColor Green

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Cyan
Write-Host "Client: $root\publish\client\Sekta.Client.exe"
Write-Host "Server: $root\publish\server\Sekta.Server.exe"
