param([string]$Dotnet='C:\dotnet\dotnet.exe')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot\..\..").Path
& $Dotnet build "$repo\tests\RuleInitializationContracts\RuleInitializationContracts.csproj" -c Release
if($LASTEXITCODE -ne 0){throw 'Initialization contract runner build failed'}
$id=[Guid]::NewGuid().ToString('N')
$profile="$repo\test-initialization-$id"
$result="$repo\rule-initialization-result.json"
New-Item -ItemType Directory $profile | Out-Null
Remove-Item $result -ErrorAction SilentlyContinue
$action=New-ScheduledTaskAction -Execute "$repo\tests\RuleInitializationContracts\bin\Release\net8.0-windows\RuleInitializationContracts.exe" -Argument "`"$repo\src\WindowsBlocker\WebAssets`" `"$profile`" `"$result`""
$principal=New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
$taskName="VaultRuleInitialization-$id"
Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal | Out-Null
try {
    Start-ScheduledTask -TaskName $taskName
    $deadline=[DateTime]::UtcNow.AddSeconds(90)
    while(!(Test-Path $result) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200}
    if(!(Test-Path $result)){throw 'Initialization tests did not produce a result'}
    $cleanupDeadline=[DateTime]::UtcNow.AddSeconds(15)
    while((Get-ScheduledTask -TaskName $taskName).State -eq 'Running' -and [DateTime]::UtcNow -lt $cleanupDeadline){Start-Sleep -Milliseconds 100}
    if((Get-ScheduledTask -TaskName $taskName).State -eq 'Running'){throw 'Initialization runner did not finish cleanup'}
    Get-Content $result -Raw
    $report=Get-Content $result -Raw | ConvertFrom-Json
    if(!$report.ok){throw $report.error}
} finally {
    if((Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue).State -eq 'Running'){Stop-ScheduledTask -TaskName $taskName}
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    # This GUID profile belongs only to the contract runner.
    $profileDeadline=[DateTime]::UtcNow.AddSeconds(10)
    do { Remove-Item $profile -Recurse -Force -ErrorAction SilentlyContinue; if(!(Test-Path $profile)){break}; Start-Sleep -Milliseconds 200 } while([DateTime]::UtcNow -lt $profileDeadline)
    if(Test-Path $profile){throw "Owned initialization profile cleanup pending: $profile"}
}
