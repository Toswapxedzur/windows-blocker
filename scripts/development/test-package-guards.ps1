param([string]$Repository=(Resolve-Path "$PSScriptRoot\..\..").Path)
$ErrorActionPreference='Stop'
$temporary=Join-Path $env:TEMP ('Vault-Package-Guards-'+[Guid]::NewGuid().ToString('N'))
$count=0
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
    # A valid legacy manifest must be rejected before building or closing apps.
    Set-Content "$worker\VaultClassifierWorker.exe" 'fixture'
    $legacy=@{schema=1;architecture='x64';sources=@();files=@(@{path='VaultClassifierWorker.exe';sha256=(Get-FileHash "$worker\VaultClassifierWorker.exe").Hash})}
    $legacy|ConvertTo-Json -Depth 5|Set-Content "$worker\bundle-manifest.json"
    Refuses { & "$Repository\scripts\development\run-windows-vault.ps1" -ClassifierWorkerDirectory $worker -Dotnet 'must-not-run.exe' } 'predates the dictionary feature' 'Launcher refuses a stale worker before build or process changes'
    Refuses { & "$Repository\scripts\release\package-windows-vault.ps1" -ClassifierWorkerDirectory $worker -OutputDirectory (Join-Path $temporary 'stale-package') -Dotnet 'must-not-run.exe' } 'predates the dictionary feature' 'Packager refuses a stale worker before output changes'
    if(Test-Path (Join-Path $temporary 'stale-package')){throw 'Stale worker refusal created a package'}
    if((Get-Content "$unrelated\keep.txt" -Raw).Trim() -ne 'keep existing files' -or (Test-Path $target)){throw 'Refused operation mutated fixture destination'}
    Write-Output "$count package/installer guards passed"
} finally { Remove-Item $temporary -Recurse -Force }
