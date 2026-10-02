param([string]$Dotnet='C:\dotnet\dotnet.exe')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot\..\..").Path
& $Dotnet build "$repo\tests\ExternalLinkContracts\ExternalLinkContracts.csproj" -c Release
if($LASTEXITCODE -ne 0){throw 'External link fixture build failed'}
$profile="$repo\test-links-profile-$([Guid]::NewGuid().ToString('N'))"
$result="$repo\external-link-result.json"
New-Item -ItemType Directory -Force $profile|Out-Null
Remove-Item $result -ErrorAction SilentlyContinue
$bootstrap="$profile\launch.ps1"
"`$env:DOTNET_ROOT='C:\dotnet'; & '$repo\tests\ExternalLinkContracts\bin\Release\net8.0-windows\ExternalLinkContracts.exe' '$profile' '$result'"|Set-Content -Encoding utf8 $bootstrap
$action=New-ScheduledTaskAction -Execute powershell.exe -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$bootstrap`""
$principal=New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName VaultExternalLinkContracts -Action $action -Principal $principal -Force|Out-Null
try {
    Start-ScheduledTask -TaskName VaultExternalLinkContracts
    $deadline=[DateTime]::UtcNow.AddSeconds(35)
    while(!(Test-Path $result) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200}
    if(!(Test-Path $result)){throw 'External link fixture timed out'}
    Get-Content $result -Raw
    $report=Get-Content $result -Raw|ConvertFrom-Json
    if(!$report.ok){throw $report.error}
} finally { Stop-ScheduledTask -TaskName VaultExternalLinkContracts -ErrorAction SilentlyContinue; Unregister-ScheduledTask -TaskName VaultExternalLinkContracts -Confirm:$false }
