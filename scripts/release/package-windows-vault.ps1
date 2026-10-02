param(
    [Parameter(Mandatory=$true)][string]$ClassifierWorkerDirectory,
    [string]$Dotnet='C:\dotnet\dotnet.exe',
    [string]$OutputDirectory=(Join-Path $PSScriptRoot '..\..\dist\WindowsVault-x64'),
    [switch]$SkipArchive
)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$repo=(Resolve-Path "$PSScriptRoot\..\..").Path
$worker=(Resolve-Path $ClassifierWorkerDirectory).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
if ($output -eq [IO.Path]::GetPathRoot($output) -or $repo.StartsWith($output.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or $output -eq $repo) { throw 'Choose a dedicated package output folder.' }
if ($output -eq $worker -or $output.StartsWith($worker.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or $worker.StartsWith($output.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Choose a package output folder separate from the Classifier worker.' }
if ((Test-Path $output) -and @(Get-ChildItem $output -Force).Count -gt 0) {
    $previousPackage=$null
    try { $previousPackage=Get-Content "$output\package-manifest.json" -Raw|ConvertFrom-Json } catch { }
    if($previousPackage.product -ne 'Windows Vault' -or $previousPackage.schema -ne 1) { throw 'Refusing to replace an unrelated output directory.' }
}
if (!(Test-Path "$worker\VaultClassifierWorker.exe") -or !(Test-Path "$worker\bundle-manifest.json")) { throw 'A verified bundled Classifier worker is required.' }
$manifest=Get-Content "$worker\bundle-manifest.json" -Raw|ConvertFrom-Json
if ($manifest.architecture -ne 'x64' -or $manifest.schema -ne 1 -or @($manifest.files).Count -eq 0) { throw 'Expected the verified x64 Classifier worker manifest.' }
foreach ($file in $manifest.files) {
    $relative=$file.path.Replace('/','\')
    $path=[IO.Path]::GetFullPath((Join-Path $worker $relative))
    if (!$path.StartsWith($worker.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path $path -PathType Leaf)) { throw 'Invalid Classifier manifest path.' }
    if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Classifier dependency hash mismatch: $relative" }
}
# Stage first so a failed publish cannot replace the last usable package.
$stage=$output+'.staging-'+[Guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Force $stage|Out-Null
try {
    & $Dotnet publish "$repo\src\WindowsBlocker\WindowsBlocker.csproj" -c Release -r win-x64 --self-contained true -m:1 -p:PublishSingleFile=false -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'Windows Vault publish failed.' }
    & $Dotnet publish "$repo\src\VaultNativeHost\VaultNativeHost.csproj" -c Release -r win-x64 --self-contained true -m:1 -p:PublishSingleFile=false -o "$stage\NativeHost"
    if ($LASTEXITCODE -ne 0) { throw 'Native helper publish failed.' }
    Copy-Item $worker "$stage\ClassifierWorker" -Recurse
    Copy-Item "$repo\scripts\development\install-native-host.ps1" $stage
    Copy-Item "$PSScriptRoot\install-windows-vault.ps1" "$stage\Install.ps1"
    Copy-Item "$PSScriptRoot\README.md" "$stage\README.md"
    # Bundle the notices from the exact toolchain/SDK package used to publish,
    # alongside the worker's independently pinned runtime notices.
    $notices=(New-Item -ItemType Directory -Force "$stage\RuntimeNotices").FullName
    $dotnetDirectory=Split-Path (Resolve-Path $Dotnet).Path -Parent
    foreach($name in @('LICENSE.txt','ThirdPartyNotices.txt')) {
        if(!(Test-Path "$dotnetDirectory\$name")){throw "Missing .NET runtime notice: $name"}
        Copy-Item "$dotnetDirectory\$name" "$notices\Dotnet-$name"
    }
    [xml]$project=Get-Content "$repo\src\WindowsBlocker\WindowsBlocker.csproj" -Raw
    $webViewVersion=($project.Project.ItemGroup.PackageReference|Where-Object Include -eq 'Microsoft.Web.WebView2').Version
    $nugetRoot=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget\packages'}
    $webViewPackage=Join-Path $nugetRoot "microsoft.web.webview2\$webViewVersion"
    foreach($name in @('LICENSE.txt','NOTICE.txt')) {
        if(!(Test-Path "$webViewPackage\$name")){throw "Missing WebView2 SDK notice: $name"}
        Copy-Item "$webViewPackage\$name" "$notices\WebView2-$name"
    }
    # No user state, model files, test-only assets or toolchain are packaged.
    Get-ChildItem $stage -Recurse -File -Filter '*.pdb'|Remove-Item -Force
    $files=Get-ChildItem $stage -Recurse -File|Sort-Object FullName|ForEach-Object {
        @{path=$_.FullName.Substring($stage.Length+1).Replace('\','/');sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    $version=($project.Project.PropertyGroup|Where-Object{$_.Version}|Select-Object -First 1).Version
    if(!$version){throw 'Windows Vault project version is missing.'}
    @{schema=1;product='Windows Vault';architecture='x64';version=$version;files=@($files)}|ConvertTo-Json -Depth 5|Set-Content "$stage\package-manifest.json" -Encoding UTF8
    $previous=$output+'.previous-'+[Guid]::NewGuid().ToString('N')
    if (Test-Path $output) { Move-Item $output $previous }
    try { Move-Item $stage $output } catch { if(Test-Path $previous){Move-Item $previous $output}; throw }
    if (Test-Path $previous) { Remove-Item $previous -Recurse -Force }
    if(!$SkipArchive) {
        $zip=$output+'.zip'
        if (Test-Path $zip) { Remove-Item $zip -Force }
        Compress-Archive -Path "$output\*" -DestinationPath $zip
        Write-Output "Packaged Windows Vault: $zip"
    } else {Write-Output "Packaged Windows Vault directory: $output"}
} finally { if (Test-Path $stage) { Remove-Item $stage -Recurse -Force } }
