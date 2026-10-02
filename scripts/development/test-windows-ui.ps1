param([string]$Dotnet='C:\dotnet\dotnet.exe')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot\..\..").Path
& $Dotnet build "$repo\tests\WindowsUiContracts\WindowsUiContracts.csproj" -c Release
if($LASTEXITCODE -ne 0){throw 'UI contract runner build failed'}
$profile="$repo\test-ui-profile-$([Guid]::NewGuid().ToString('N'))"
$result="$repo\windows-ui-result.json"
New-Item -ItemType Directory -Force $profile|Out-Null
$now=[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$groups=@()
foreach($id in @('time-finished','budget-used','budget-unspent')){
  $groups+=@{id=$id;name=$id;groupType='site';enabled=$true;mode=$(if($id -eq 'time-finished'){'instant'}else{'after-minutes'});allowedMinutes=1;resetIntervalHours=24;activeDays=@('mon','tue','wed','thu','fri','sat','sun');scopes=@(@{id='apps-1';surface='apps';action='block';apps=@()});snoozeKind=$(if($id -eq 'time-finished'){'time'}else{'budget'})}
}
$seed=@{blockedGroups=$groups;usageTimersMs=@{'budget-used'=120000;'budget-unspent'=61000};usageResetAtMs=@{'budget-used'=$now;'budget-unspent'=$now};groupSnoozeTotalsMs=@{'time-finished'=17;'budget-used'=45000;'budget-unspent'=1000};groupSnoozes=@{
  'time-finished'=@{startsAtMs=($now-5000);untilMs=($now-3000);activeMsApplied=$false};
  'budget-used'=@{kind='budget';startsAtMs=($now-1000);untilMs=($now+120000);extraMs=60000;activeMsApplied=$false};
  'budget-unspent'=@{kind='budget';startsAtMs=($now-1000);untilMs=($now+120000);extraMs=60000;activeMsApplied=$false}
}}
[IO.File]::WriteAllText("$profile\web-store.json",($seed|ConvertTo-Json -Depth 12),(New-Object Text.UTF8Encoding($false)))
Remove-Item $result -ErrorAction SilentlyContinue
$bootstrap="$repo\start-ui-contracts.ps1"
@"
`$env:DOTNET_ROOT='C:\dotnet'
`$env:VAULT_ENVIRONMENT='development'
`$env:VAULT_STORAGE_ROOT='$profile'
`$env:VAULT_MCP_CLIENT_ROOT='$profile\mcp-clients'
`$app=Start-Process '$repo\src\WindowsBlocker\bin\Release\net8.0-windows\WindowsBlocker.exe' -PassThru
try {
  `$deadline=[DateTime]::UtcNow.AddSeconds(35)
  do {
    if(`$app.HasExited){throw "Windows Vault exited before MCP was ready"}
    `$socket=New-Object System.Net.Sockets.TcpClient
    try { `$socket.Connect('127.0.0.1',18788); break } catch { Start-Sleep -Milliseconds 250 } finally { `$socket.Dispose() }
  }while([DateTime]::UtcNow -lt `$deadline)
  & '$repo\tests\WindowsUiContracts\bin\Release\net8.0-windows\WindowsUiContracts.exe' '$profile' '$result' '$repo\src\VaultNativeHost\bin\Release\net8.0-windows\VaultNativeHost.exe' `$app.Id *> '$repo\windows-ui-stdout.txt'
} finally { if(!`$app.HasExited){ Stop-Process -Id `$app.Id -Force } }
"@|Set-Content -Encoding utf8 $bootstrap
$action=New-ScheduledTaskAction -Execute powershell.exe -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$bootstrap`""
$principal=New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName VaultPortingUiContracts -Action $action -Principal $principal -Force|Out-Null
try{
  Start-ScheduledTask -TaskName VaultPortingUiContracts
  $deadline=[DateTime]::UtcNow.AddSeconds(90)
  while(!(Test-Path $result) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200}
  if(!(Test-Path $result)){Get-Content "$repo\windows-ui-stdout.txt" -ErrorAction SilentlyContinue;throw 'UI tests did not produce a result'}
  Get-Content $result -Raw
  $report=Get-Content $result -Raw|ConvertFrom-Json
  if(!$report.ok){throw $report.error}
}finally{Unregister-ScheduledTask -TaskName VaultPortingUiContracts -Confirm:$false}
