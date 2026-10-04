param(
    [ValidateSet('development','production')][string]$Environment='development',
    [string]$Dotnet='C:\dotnet\dotnet.exe',
    [string]$ClassifierWorkerDirectory
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot\..\..").Path
$env:VAULT_ENVIRONMENT=$Environment
$env:DOTNET_ROOT=Split-Path ([IO.Path]::GetFullPath($Dotnet)) -Parent
$appDirectory="$repo\src\WindowsBlocker\bin\Release\net8.0-windows"
if (!$ClassifierWorkerDirectory) { $ClassifierWorkerDirectory="$repo\src\WindowsBlocker\ClassifierWorker" }
$worker=(Resolve-Path $ClassifierWorkerDirectory).Path
if (!(Test-Path "$worker\VaultClassifierWorker.exe") -or !(Test-Path "$worker\bundle-manifest.json")) { throw 'Build and verify the bundled Classifier worker before delivery.' }
. "$repo\scripts\classifier-worker\dictionary-worker-guard.ps1"
Assert-VaultDictionaryWorker (Get-Content "$worker\bundle-manifest.json" -Raw | ConvertFrom-Json)
& $Dotnet build "$repo\src\WindowsBlocker\WindowsBlocker.csproj" -c Release
if($LASTEXITCODE -ne 0){throw 'Windows Vault build failed'}
& $Dotnet build "$repo\src\VaultNativeHost\VaultNativeHost.csproj" -c Release
if($LASTEXITCODE -ne 0){throw 'Browser/MCP helper build failed'}
# A fresh owner-visible delivery instance replaces every prior Windows Vault
# instance on this test VM. Ask it to close/flush first, then finish the explicit
# delivery relaunch if an old instance is unresponsive or protects its own quit.
Get-Process WindowsBlocker -ErrorAction SilentlyContinue|ForEach-Object {
    $process=$_
    $process.CloseMainWindow()|Out-Null
    if (!$process.WaitForExit(10000)) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(5000)|Out-Null }
}
foreach($child in @('NativeHost','ClassifierWorker')) { if(Test-Path "$appDirectory\$child"){Remove-Item "$appDirectory\$child" -Recurse -Force} }
Copy-Item "$repo\src\VaultNativeHost\bin\Release\net8.0-windows" "$appDirectory\NativeHost" -Recurse
Copy-Item $worker "$appDirectory\ClassifierWorker" -Recurse
& "$PSScriptRoot\install-native-host.ps1" -HostDirectory "$appDirectory\NativeHost" -Environment $Environment
Start-Process "$appDirectory\WindowsBlocker.exe"
Write-Output "Relaunched Windows Vault ($Environment) on mini1's Windows VM."
