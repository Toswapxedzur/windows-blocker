param(
    [Parameter(Mandatory=$true)][string]$ClassifierSource,
    [string]$TestDirectory = (Join-Path $env:TEMP ('VaultClassifierPlatform-' + [Guid]::NewGuid()))
)
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$environment = & cmd.exe /d /s /c "`"$visualStudio\VC\Auxiliary\Build\vcvars64.bat`" >nul && set"
foreach ($line in $environment) { if ($line -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($matches[1],$matches[2],'Process') } }
New-Item -ItemType Directory -Force $TestDirectory | Out-Null
$native = Join-Path $ClassifierSource 'Sources\CVaultWindows'
$executable = Join-Path $TestDirectory 'platform-smoke.exe'
Push-Location $TestDirectory
try {
    & cl.exe /nologo /std:c++17 /EHsc /I"$native\include" "$native\platform.cpp" "$PSScriptRoot\platform-smoke.cpp" /Fe:$executable /link crypt32.lib bcrypt.lib advapi32.lib ole32.lib windowscodecs.lib shlwapi.lib uuid.lib
    if ($LASTEXITCODE) { throw 'Native platform test build failed.' }
    & $executable (Join-Path $TestDirectory 'private-data')
    if ($LASTEXITCODE) { throw 'Native platform checks failed.' }
} finally { Pop-Location }
