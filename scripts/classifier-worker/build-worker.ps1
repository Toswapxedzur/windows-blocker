param(
    [Parameter(Mandatory=$true)][string]$ClassifierSource,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\dist\ClassifierWorker'),
    [string]$DependencyDirectory = (Join-Path $PSScriptRoot '..\..\.native-dependencies'),
    [string]$SwiftExecutable = '',
    [ValidateSet('debug','release')][string]$Configuration = 'release',
    [string]$ClassifierRevision = '',
    [switch]$RunTests
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$ClassifierSource = (Resolve-Path $ClassifierSource).Path
$DependencyDirectory = [IO.Path]::GetFullPath($DependencyDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $DependencyDirectory,$OutputDirectory | Out-Null

$SwiftVersion = '6.4.0'
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path $vswhere)) { throw 'Install Visual Studio 2022 C++ Build Tools and Windows SDK 22621 first.' }
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$visualStudio) { throw 'The x64 Visual Studio C++ toolchain is unavailable.' }
$environment = & cmd.exe /d /s /c "`"$visualStudio\VC\Auxiliary\Build\vcvars64.bat`" >nul && set"
foreach ($line in $environment) { if ($line -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($matches[1],$matches[2],'Process') } }
$swift = if ($SwiftExecutable) { (Resolve-Path $SwiftExecutable).Path } else { (Get-Command swift.exe -ErrorAction Stop).Source }
$env:Path = (Split-Path $swift -Parent) + ';' + $env:Path
$version = (& $swift --version | Out-String)
if ($version -notmatch 'Swift version 6\.4(?:\.0)? \(swift-6\.4(?:\.0)?-RELEASE\)') { throw "Use pinned Swift $SwiftVersion; observed: $version" }
Push-Location $ClassifierSource
try {
    # The current Windows Swift compiler needs its allocator redirect disabled
    # during compilation. Restore it before running the finished worker.
    $priorAllocator = $env:MIMALLOC_DISABLE_REDIRECT
    try {
        $env:MIMALLOC_DISABLE_REDIRECT = '1'
        & $swift build --build-system native --product VaultClassifierWorker -c $Configuration --jobs 2
        if ($LASTEXITCODE) { throw 'The shared Classifier worker build failed.' }
        $binaryDirectory = (& $swift build --build-system native -c $Configuration --show-bin-path | Select-Object -Last 1).Trim()
    } finally {
        if ($null -eq $priorAllocator) { Remove-Item Env:MIMALLOC_DISABLE_REDIRECT -ErrorAction SilentlyContinue }
        else { $env:MIMALLOC_DISABLE_REDIRECT = $priorAllocator }
    }
    $worker = Join-Path $binaryDirectory 'VaultClassifierWorker.exe'
    if (!(Test-Path $worker)) { throw 'The worker executable was not produced.' }
    Copy-Item -Force $worker $OutputDirectory
    Get-ChildItem $binaryDirectory -Directory | Where-Object { $_.Name -match '\.(resources|bundle)$' } | ForEach-Object { Copy-Item -Force -Recurse $_.FullName $OutputDirectory }
    if (Test-Path (Join-Path $ClassifierSource 'Package.resolved')) { Copy-Item -Force (Join-Path $ClassifierSource 'Package.resolved') (Join-Path $OutputDirectory 'swift-package-resolved.json') }
} finally { Pop-Location }

# Package imported runtime dependencies, including FoundationNetworking's curl
# stack and the app-local VC runtime. Do not require Swift on an end-user PC.
$target = (& $swift -print-target-info | Out-String | ConvertFrom-Json)
$swiftRoot = Split-Path (Split-Path (Split-Path $swift -Parent) -Parent) -Parent
$candidateRoots = @($target.paths.runtimeLibraryPaths) + @($swiftRoot)
$installedRoot = Join-Path $env:LOCALAPPDATA 'Programs\Swift'
if (Test-Path $installedRoot) { $candidateRoots += $installedRoot }
if (Test-Path 'C:\Library\Developer') { $candidateRoots += 'C:\Library\Developer' }
$redist = Join-Path $visualStudio 'VC\Redist\MSVC'
if (Test-Path $redist) { $candidateRoots += $redist }
$dlls = @{}
foreach ($root in $candidateRoots | Select-Object -Unique) {
    if (!(Test-Path $root)) { continue }
    foreach ($file in Get-ChildItem $root -Recurse -File -Filter '*.dll') {
        if ($file.FullName -match '(?i)(\\|/)(arm64|arm64ec|x86)(\\|/)') { continue }
        if (!$dlls.ContainsKey($file.Name.ToLowerInvariant())) { $dlls[$file.Name.ToLowerInvariant()] = $file.FullName }
    }
}
$dumpbin = (Get-Command dumpbin.exe -ErrorAction Stop).Source
$pending = [Collections.Generic.Queue[string]]::new()
Get-ChildItem $OutputDirectory -File | Where-Object { $_.Extension -in '.exe','.dll' } | ForEach-Object { $pending.Enqueue($_.FullName) }
$visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
while ($pending.Count) {
    $binary = $pending.Dequeue()
    if (!$visited.Add($binary)) { continue }
    foreach ($line in & $dumpbin /nologo /dependents $binary) {
        if ($line -notmatch '^\s+([A-Za-z0-9_.+-]+\.dll)\s*$') { continue }
        $name = $matches[1]; $destination = Join-Path $OutputDirectory $name
        if (Test-Path $destination) { continue }
        if ($dlls.ContainsKey($name.ToLowerInvariant())) {
            Copy-Item -Force $dlls[$name.ToLowerInvariant()] $destination
            $pending.Enqueue($destination)
        } elseif (!(Test-Path (Join-Path $env:WINDIR "System32\$name")) -and $name -notmatch '^(api-ms-|ext-ms-)') {
            throw "Unbundled native dependency: $name (required by $binary)"
        }
    }
}
if ($RunTests) {
    # SwiftPM launches tests outside the bundle. Use the complete app-local
    # dependency closure, including FoundationNetworking and the VC runtime.
    $testPath = $env:Path
    Push-Location $ClassifierSource
    try {
        $env:Path = $OutputDirectory + ';' + $testPath
        & $swift test --build-system native -c $Configuration --jobs 2
        if ($LASTEXITCODE) { throw 'The shared Windows Classifier test suite failed.' }
    } finally {
        $env:Path = $testPath
        Pop-Location
    }
}
$notices = Join-Path $OutputDirectory 'Notices'
New-Item -ItemType Directory -Force $notices | Out-Null
$runtimeNotices = Join-Path $PSScriptRoot 'runtime-notices'
if (!(Test-Path $runtimeNotices)) { throw 'The pinned runtime notice directory is missing.' }
Copy-Item -Recurse -Force (Join-Path $runtimeNotices '*') $notices
foreach ($checkout in Get-ChildItem (Join-Path $ClassifierSource '.build\checkouts') -Directory) {
    $directory = Join-Path $notices $checkout.Name
    New-Item -ItemType Directory -Force $directory | Out-Null
    Get-ChildItem $checkout.FullName -File | Where-Object { $_.Name -match '^(LICENSE|NOTICE|COPYING|COPYRIGHT)(\.|$)' } | ForEach-Object { Copy-Item -Force $_.FullName $directory }
}
foreach ($root in @((Join-Path $visualStudio 'VC\Redist'), (Join-Path $installedRoot 'Redistributables'))) {
    if (!(Test-Path $root)) { continue }
    Get-ChildItem $root -Recurse -File | Where-Object { $_.Name -match '^(LICENSE|NOTICE|COPYING|COPYRIGHT)(\.|$)' } | ForEach-Object {
        $name = $_.FullName.Substring($root.Length + 1).Replace('\','_')
        Copy-Item -Force $_.FullName (Join-Path $notices ('Runtime-' + $name))
    }
}
$sourceFiles = @(Get-ChildItem (Join-Path $ClassifierSource 'Sources') -Recurse -File) + @(Get-Item (Join-Path $ClassifierSource 'Package.swift'))
$sourceHashes = $sourceFiles | Sort-Object FullName | ForEach-Object {
    @{ path=$_.FullName.Substring($ClassifierSource.Length + 1).Replace('\','/'); sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$hashes = Get-ChildItem $OutputDirectory -Recurse -File | Where-Object { $_.Name -ne 'bundle-manifest.json' } | Sort-Object FullName | ForEach-Object {
    @{ path=$_.FullName.Substring($OutputDirectory.Length + 1).Replace('\','/'); sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
@{ schema=1; swift=$SwiftVersion; compilerVersion=$version.Trim(); architecture='x64'; classifierRevision=$ClassifierRevision; sources=@($sourceHashes); files=@($hashes) } | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $OutputDirectory 'bundle-manifest.json')
Write-Output "Bundled shared Classifier worker: $OutputDirectory"
