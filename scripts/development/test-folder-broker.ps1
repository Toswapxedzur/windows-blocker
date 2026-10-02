param([string]$Dotnet='C:\dotnet\dotnet.exe',[string]$EvidenceDirectory='', [switch]$Interactive)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot\..\..").Path
if(!$EvidenceDirectory){$EvidenceDirectory=Join-Path $repo ('test-folder-'+[Guid]::NewGuid().ToString('N'))}
New-Item -ItemType Directory -Force $EvidenceDirectory|Out-Null
$result=Join-Path $EvidenceDirectory 'result.json'
$log=Join-Path $EvidenceDirectory 'broker.log'
if($Interactive){
  $principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
  if($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Folder broker fixture requires a normal interactive user'}
  $env:DOTNET_ROOT=Split-Path $Dotnet
  $ErrorActionPreference='Continue' # Preserve a failing native assertion's stderr and exit code.
  & "$repo\tests\FolderBrokerContracts\bin\Release\net8.0-windows\FolderBrokerContracts.exe" *> $log
  @{ok=($LASTEXITCODE -eq 0);normalUser=$true}|ConvertTo-Json|Set-Content -Encoding utf8 $result
  return
}
& $Dotnet build "$repo\tests\FolderBrokerContracts\FolderBrokerContracts.csproj" -c Release -m:1
if($LASTEXITCODE -ne 0){throw 'Folder broker contracts build failed'}
$task='VaultFolderBroker-'+[Guid]::NewGuid().ToString('N')
$action=New-ScheduledTaskAction -Execute powershell.exe -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$PSCommandPath`" -Dotnet `"$Dotnet`" -EvidenceDirectory `"$EvidenceDirectory`" -Interactive"
$principal=New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName $task -Action $action -Principal $principal|Out-Null
try{
  Start-ScheduledTask -TaskName $task
  $deadline=[DateTime]::UtcNow.AddSeconds(60)
  while(!(Test-Path $result) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200}
  if(Test-Path $log){Get-Content $log}
  if(!(Test-Path $result)){throw 'Folder broker contracts timed out'}
  $answer=Get-Content $result -Raw|ConvertFrom-Json
  if(!$answer.ok){throw 'Folder broker contracts failed'}
  Get-Content $result -Raw
}finally{Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue;Unregister-ScheduledTask -TaskName $task -Confirm:$false}
