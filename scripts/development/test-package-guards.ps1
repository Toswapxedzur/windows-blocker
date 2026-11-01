param([string]$Repository=(Resolve-Path "$PSScriptRoot\..\..").Path)
$ErrorActionPreference='Stop'
$temporary=Join-Path $env:TEMP ('Vault-Package-Guards-'+[Guid]::NewGuid().ToString('N'))
$count=0
$nativeName='com.adamancia.vault.local_hub.development'
$nativeKeys=@("HKCU:\Software\Google\Chrome\NativeMessagingHosts\$nativeName","HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$nativeName")
$nativeBefore=@($nativeKeys|ForEach-Object {$exists=Test-Path $_;@{key=$_;exists=$exists;value=$(if($exists){(Get-Item $_).GetValue('')}else{$null})}})
$shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) 'Windows Vault Development.lnk'
$shortcutBefore=if(Test-Path $shortcut){[IO.File]::ReadAllBytes($shortcut)}else{$null}
function Refuses([scriptblock]$Action,[string]$Expected,[string]$Label) {
    $rejected=$false
    try { & $Action } catch { if($_.Exception.Message -notlike "*$Expected*"){throw};$rejected=$true }
    if(!$rejected){throw "Did not refuse: $Label"}
    $script:count++;Write-Output "PASS $Label"
}
New-Item -ItemType Directory $temporary|Out-Null
try {
    $fixture=Join-Path $temporary 'package'
    $unrelated=Join-Path $temporary 'unrelated'
    $worker=Join-Path $temporary 'worker'
    New-Item -ItemType Directory $fixture,$unrelated,$worker|Out-Null
    Copy-Item "$Repository\scripts\release\install-windows-vault.ps1" "$fixture\Install.ps1"
    Set-Content "$unrelated\keep.txt" 'keep existing files'
    Refuses { & "$fixture\Install.ps1" -Environment development -Destination $unrelated } 'unrelated application folder' 'Installer preserves unrelated destination'
    Refuses { & "$fixture\Install.ps1" -Environment development -Destination $temporary } 'separate application folder' 'Installer refuses source ancestor'
    Refuses { & "$fixture\Install.ps1" -Environment development -Destination (Join-Path $fixture 'nested-install') } 'separate application folder' 'Installer refuses destination inside its payload'
    $target=Join-Path $temporary 'install'
    Set-Content "$fixture\WindowsBlocker.exe" 'fixture'
    @{schema=1;product='Windows Vault';architecture='x64';files=@(@{path='WindowsBlocker.exe';sha256=('0'*64)})}|ConvertTo-Json -Depth 5|Set-Content "$fixture\package-manifest.json"
    Refuses { & "$fixture\Install.ps1" -Environment development -Destination $target } 'integrity check failed' 'Installer rejects altered payload before mutation'
    @{schema=1;product='Windows Vault';architecture='x64';files=@(@{path='../unrelated/keep.txt';sha256=(Get-FileHash "$unrelated\keep.txt").Hash})}|ConvertTo-Json -Depth 5|Set-Content "$fixture\package-manifest.json"
    Refuses { & "$fixture\Install.ps1" -Environment development -Destination $target } 'integrity check failed' 'Installer refuses manifest path escape'
    Refuses { & "$Repository\scripts\release\package-windows-vault.ps1" -ClassifierWorkerDirectory $worker -OutputDirectory $unrelated } 'unrelated output directory' 'Packager preserves unrelated output'
    @{schema=1;product='Other product'}|ConvertTo-Json|Set-Content "$unrelated\package-manifest.json"
    Refuses { & "$Repository\scripts\release\package-windows-vault.ps1" -ClassifierWorkerDirectory $worker -OutputDirectory $unrelated } 'unrelated output directory' 'Packager refuses another product marker'
    @{product='Other product';environment='development'}|ConvertTo-Json|Set-Content "$unrelated\.vault-install.json"
    Refuses { & "$fixture\Install.ps1" -Environment development -Destination $unrelated } 'unrelated application folder' 'Installer refuses another product marker'
    Refuses { & "$Repository\scripts\release\package-windows-vault.ps1" -ClassifierWorkerDirectory $worker -OutputDirectory (Split-Path $Repository) } 'dedicated package output folder' 'Packager refuses repository ancestor'
    Refuses { & "$Repository\scripts\release\package-windows-vault.ps1" -ClassifierWorkerDirectory $worker -OutputDirectory (Join-Path $worker 'nested-package') } 'separate from the Classifier worker' 'Packager refuses output inside the worker payload'
    if((Get-Content "$unrelated\keep.txt" -Raw).Trim() -ne 'keep existing files' -or (Test-Path $target)){throw 'Refused operation mutated fixture destination'}
    # A post-swap host failure must restore the old app and both browser registrations.
    New-Item -ItemType Directory $target|Out-Null
    @{product='Windows Vault';environment='development';version='0.3.1'}|ConvertTo-Json|Set-Content "$target\.vault-install.json"
    Set-Content "$target\WindowsBlocker.exe" 'old application'
    Set-Content "$target\keep.txt" 'old private installation'
    foreach($key in $nativeKeys){New-Item $key -Force|Out-Null;Set-Item $key "$target\old-host.json"}
    Set-Content $shortcut 'old shortcut fixture'
    Set-Content "$fixture\WindowsBlocker.exe" 'new application'
    @'
param([string]$HostDirectory,[string]$Environment)
$key='HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.adamancia.vault.local_hub.development'
Set-Item $key "$HostDirectory\partial-host.json"
throw 'deliberate post-swap host failure'
'@|Set-Content "$fixture\install-native-host.ps1"
    function WriteFixtureManifest {
        @{schema=1;product='Windows Vault';architecture='x64';version='0.3.2';files=@(
            @{path='WindowsBlocker.exe';sha256=(Get-FileHash "$fixture\WindowsBlocker.exe").Hash},
            @{path='install-native-host.ps1';sha256=(Get-FileHash "$fixture\install-native-host.ps1").Hash}
        )}|ConvertTo-Json -Depth 5|Set-Content "$fixture\package-manifest.json"
    }
    WriteFixtureManifest
    Refuses { & "$fixture\Install.ps1" -Environment development -Destination $target } 'deliberate post-swap host failure' 'Installer reports a post-swap host failure'
    if((Get-Content "$target\WindowsBlocker.exe" -Raw).Trim() -ne 'old application' -or !(Test-Path "$target\keep.txt")){throw 'Failed installer did not restore old app'}
    foreach($key in $nativeKeys){if((Get-Item $key).GetValue('') -ne "$target\old-host.json"){throw 'Failed installer did not restore browser registration'}}
    if((Get-Content $shortcut -Raw).Trim() -ne 'old shortcut fixture' -or @(Get-ChildItem $temporary -Filter 'install.previous-*').Count){throw 'Failed installer left shortcut or old backup changed'}
    $count++;Write-Output 'PASS Failed post-swap install restores app, registrations and shortcut'
    @'
param([string]$HostDirectory,[string]$Environment)
foreach($browser in @('Google\Chrome','Microsoft\Edge')) {
    $key="HKCU:\Software\$browser\NativeMessagingHosts\com.adamancia.vault.local_hub.development"
    Set-Item $key "$HostDirectory\new-host.json"
}
'@|Set-Content "$fixture\install-native-host.ps1"
    WriteFixtureManifest
    & "$fixture\Install.ps1" -Environment development -Destination $target|Out-Null
    if((Get-Content "$target\WindowsBlocker.exe" -Raw).Trim() -ne 'new application' -or (Test-Path "$target\keep.txt")){throw 'Retry did not install new app cleanly'}
    foreach($key in $nativeKeys){if((Get-Item $key).GetValue('') -ne "$target\NativeHost\new-host.json"){throw 'Retry did not register new browser host'}}
    $count++;Write-Output 'PASS A valid retry replaces the restored app'
    Write-Output "$count package/installer guards passed"
} finally {
    foreach($registration in $nativeBefore){if($registration.exists){New-Item $registration.key -Force|Out-Null;Set-Item $registration.key $registration.value}else{Remove-Item $registration.key -Recurse -Force -ErrorAction SilentlyContinue}}
    if($null -ne $shortcutBefore){[IO.File]::WriteAllBytes($shortcut,$shortcutBefore)}elseif(Test-Path $shortcut){Remove-Item $shortcut -Force}
    Remove-Item $temporary -Recurse -Force
}
