param([string]$Dotnet = 'C:\dotnet\dotnet.exe')
$ErrorActionPreference = 'Stop'
$env:VAULT_ENVIRONMENT = 'development'
$repo = (Resolve-Path "$PSScriptRoot\..\..").Path
& $Dotnet build "$repo\src\WindowsBlocker\WindowsBlocker.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed' }
& $Dotnet build "$repo\src\VaultNativeHost\VaultNativeHost.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Native messaging host build failed' }
& $Dotnet run --project "$repo\tests\NativeContracts\NativeContracts.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Native contracts failed' }

& $Dotnet build "$repo\tests\WorkerFixture\WorkerFixture.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Worker fixture build failed' }
& $Dotnet run --project "$repo\tests\WorkerClientContracts\WorkerClientContracts.csproj" -c Release -- "$repo\tests\WorkerFixture\bin\Release\net8.0-windows\WorkerFixture.exe"
if ($LASTEXITCODE -ne 0) { throw 'Worker client contracts failed' }
if (Test-Path "$repo\tests\McpConnectorContracts\McpConnectorContracts.csproj") {
  & $Dotnet run --project "$repo\tests\McpConnectorContracts\McpConnectorContracts.csproj" -c Release
  if ($LASTEXITCODE -ne 0) { throw 'MCP connector contracts failed' }
}
