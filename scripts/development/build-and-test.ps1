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
