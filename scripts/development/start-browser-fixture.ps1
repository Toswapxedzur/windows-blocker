param([string]$BrowserPath='C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',[string]$ExtensionDirectory='C:\vault-porting-browser-extension',[int]$CdpPort=9227,[string]$ModelSource='', [string]$WorkerBundle='C:\vault-porting-classifier\bundle-bcd9ac3')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot\..\..").Path
foreach($port in @(18787,18788,$CdpPort)){if(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue){throw "Browser fixture requires free owned-test port $port"}}
$fixture="$repo\test-browser-fixture"
if(Test-Path "$fixture\state.json"){throw 'Stop the existing owned browser fixture before starting another'}
New-Item -ItemType Directory -Force $fixture|Out-Null
Remove-Item "$fixture\stop","$fixture\failure.txt","$fixture\pause-app","$fixture\resume-app" -ErrorAction SilentlyContinue
$browserProfile="$fixture\browser-$([Guid]::NewGuid().ToString('N'))"
$storage="$fixture\vault-$([Guid]::NewGuid().ToString('N'))"
$workerDestination="$repo\src\WindowsBlocker\bin\Release\net8.0-windows\ClassifierWorker"
$manifest=Get-Content "$WorkerBundle\bundle-manifest.json" -Raw|ConvertFrom-Json
foreach($item in $manifest.files){if((Get-FileHash (Join-Path $WorkerBundle $item.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.sha256){throw "Worker bundle hash mismatch: $($item.path)"}}
New-Item -ItemType Directory -Force $workerDestination|Out-Null
Copy-Item "$WorkerBundle\*" $workerDestination -Recurse -Force
foreach($item in $manifest.files){if((Get-FileHash (Join-Path $workerDestination $item.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.sha256){throw "Copied worker bundle hash mismatch: $($item.path)"}}
if($ModelSource){$ModelSource=(Resolve-Path $ModelSource).Path}
$task='VaultPortingBrowserFixture-'+[Guid]::NewGuid().ToString('N')
@{repo=$repo;browser=$BrowserPath;extension=$ExtensionDirectory;profile=$browserProfile;storage=$storage;cdpPort=$CdpPort;task=$task;modelSource=$ModelSource}|ConvertTo-Json|Set-Content -Encoding utf8 "$fixture\config.json"
$session="$PSScriptRoot\browser-fixture-session.ps1"
$action=New-ScheduledTaskAction -Execute powershell.exe -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$session`" -FixtureDirectory `"$fixture`""
$principal=New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName $task -Action $action -Principal $principal|Out-Null
Start-ScheduledTask -TaskName $task
$deadline=[DateTime]::UtcNow.AddSeconds(45)
while(!(Test-Path "$fixture\state.json") -and !(Test-Path "$fixture\failure.txt") -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200}
if(Test-Path "$fixture\failure.txt"){throw (Get-Content "$fixture\failure.txt" -Raw)}
if(!(Test-Path "$fixture\state.json")){throw 'Browser fixture startup timed out'}
Get-Content "$fixture\state.json" -Raw
