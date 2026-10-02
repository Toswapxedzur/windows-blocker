param([ValidateSet('development','production')][string]$Environment='production',[string]$Destination)
$ErrorActionPreference='Stop'
if (![Environment]::Is64BitOperatingSystem) { throw 'This Windows Vault package requires x64 Windows.' }
if ([Environment]::OSVersion.Version.Build -lt 19045) { throw 'Use Windows 10 22H2 or Windows 11.' }
$source=$PSScriptRoot
if (!$Destination) { $Destination=Join-Path $env:LOCALAPPDATA ('Programs\AdamanciaVault\'+$Environment) }
$Destination=[IO.Path]::GetFullPath($Destination)
if ($Destination -eq [IO.Path]::GetPathRoot($Destination) -or $Destination -eq $source -or $source.StartsWith($Destination.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or $Destination.StartsWith($source.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Choose a separate application folder.' }
if ((Test-Path $Destination) -and @(Get-ChildItem $Destination -Force).Count -gt 0) {
    $previousInstall=$null
    try { $previousInstall=Get-Content "$Destination\.vault-install.json" -Raw|ConvertFrom-Json } catch { }
    if($previousInstall.product -ne 'Windows Vault' -or $previousInstall.environment -ne $Environment) { throw 'Refusing to replace an unrelated application folder.' }
}
$package=Get-Content "$source\package-manifest.json" -Raw|ConvertFrom-Json
if ($package.schema -ne 1 -or $package.product -ne 'Windows Vault' -or $package.architecture -ne 'x64' -or @($package.files).Count -eq 0) { throw 'Invalid Windows Vault package manifest.' }
foreach($file in $package.files) {
    $path=[IO.Path]::GetFullPath((Join-Path $source $file.path.Replace('/','\')))
    if (!$path.StartsWith($source.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path $path -PathType Leaf) -or (Get-FileHash $path -Algorithm SHA256).Hash -ne $file.sha256) { throw 'Windows Vault package integrity check failed.' }
}
# Evergreen is a per-user install when launched normally. Download only the
# Microsoft-signed bootstrapper when the runtime is absent; no admin request.
$runtimeId='{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
$runtimeKeys=@("HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\$runtimeId","HKCU:\Software\Microsoft\EdgeUpdate\Clients\$runtimeId")
$hasRuntime=$false
foreach ($key in $runtimeKeys) { $pv=(Get-ItemProperty $key -Name pv -ErrorAction SilentlyContinue).pv; $version=$null; if($pv -and [version]::TryParse($pv,[ref]$version) -and $version -gt [version]'0.0.0.0'){$hasRuntime=$true} }
if (!$hasRuntime) {
    $bootstrap=Join-Path $env:TEMP ('Vault-WebView2-'+[Guid]::NewGuid().ToString('N')+'.exe')
    try {
        Write-Output 'Installing the required Microsoft WebView2 Runtime.'
        Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrap -UseBasicParsing
        $signature=Get-AuthenticodeSignature $bootstrap
        if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'The WebView2 installer is not signed by Microsoft.' }
        $process=Start-Process $bootstrap -ArgumentList '/silent','/install' -PassThru -Wait
        if($process.ExitCode -notin 0,3010){throw 'Microsoft WebView2 installation failed.'}
    } finally { if(Test-Path $bootstrap){Remove-Item $bootstrap -Force} }
}
# Refuse an update while this install is running, preserving its queued writes.
Get-Process WindowsBlocker -ErrorAction SilentlyContinue|ForEach-Object {
    if ($_.Path -and $_.Path.StartsWith($Destination.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Close this Windows Vault instance before updating it.' }
}
$stage=$Destination+'.staging-'+[Guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Force $stage|Out-Null
try {
    Get-ChildItem $source -Exclude Install.ps1|Copy-Item -Destination $stage -Recurse
    # Clear obsolete files by replacing the application directory, while data
    # stays in the separate current-user support directory.
    @{product='Windows Vault';environment=$Environment;version=$package.version}|ConvertTo-Json|Set-Content "$stage\.vault-install.json" -Encoding UTF8
    $previous=$Destination+'.previous-'+[Guid]::NewGuid().ToString('N')
    if(Test-Path $Destination){Move-Item $Destination $previous}
    try { Move-Item $stage $Destination } catch { if(Test-Path $previous){Move-Item $previous $Destination}; throw }
    if(Test-Path $previous){Remove-Item $previous -Recurse -Force}
} finally { if(Test-Path $stage){Remove-Item $stage -Recurse -Force} }
& "$Destination\install-native-host.ps1" -HostDirectory "$Destination\NativeHost" -Environment $Environment
$launcher=Join-Path $Destination 'Start-WindowsVault.ps1'
@("`$env:VAULT_ENVIRONMENT='$Environment'", "Start-Process -FilePath '$($Destination.Replace("'","''"))\WindowsBlocker.exe'")|Set-Content $launcher -Encoding UTF8
$shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) ('Windows Vault'+$(if($Environment -eq 'development'){' Development'}else{''})+'.lnk')
$shell=New-Object -ComObject WScript.Shell
$link=$shell.CreateShortcut($shortcut)
$link.TargetPath="$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
$link.Arguments="-NoProfile -ExecutionPolicy Bypass -File `"$launcher`""
$link.WorkingDirectory=$Destination
$link.IconLocation="$Destination\WindowsBlocker.exe,0"
$link.Save()
Write-Output "Installed Windows Vault for the current user: $Destination"
