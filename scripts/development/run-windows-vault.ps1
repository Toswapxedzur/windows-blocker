param([ValidateSet('development','production')][string]$Environment='development',[string]$Dotnet='C:\dotnet\dotnet.exe')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot\..\..").Path
$env:VAULT_ENVIRONMENT=$Environment
& $Dotnet build "$repo\src\WindowsBlocker\WindowsBlocker.csproj" -c Release
if($LASTEXITCODE -ne 0){throw 'Windows Vault build failed'}
Get-Process WindowsBlocker -ErrorAction SilentlyContinue|ForEach-Object{$_.CloseMainWindow()|Out-Null}
Start-Process "$repo\src\WindowsBlocker\bin\Release\net8.0-windows\WindowsBlocker.exe"
