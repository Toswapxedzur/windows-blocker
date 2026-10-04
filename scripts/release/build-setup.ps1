param(
    [Parameter(Mandatory=$true)][string]$PackageZip,
    [Parameter(Mandatory=$true)][string]$OutputExe
)
$ErrorActionPreference='Stop'
$zip=(Resolve-Path $PackageZip).Path
$output=[IO.Path]::GetFullPath($OutputExe)
if([IO.Path]::GetExtension($output) -ne '.exe'){throw 'Setup output must be an .exe file.'}
if(Test-Path $output){throw 'Choose a new setup output; a verified installer is never overwritten.'}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$temporary=Join-Path $env:TEMP ('VaultSetupBuild-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $temporary|Out-Null
try {
    $hash=Join-Path $temporary 'payload.sha256'
    [IO.File]::WriteAllText($hash,(Get-FileHash $zip -Algorithm SHA256).Hash)
    $repo=(Resolve-Path "$PSScriptRoot\..\..").Path
    [xml]$project=Get-Content "$repo\src\WindowsBlocker\WindowsBlocker.csproj" -Raw
    $version=($project.Project.PropertyGroup|Where-Object{$_.Version}|Select-Object -First 1).Version
    if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Missing current setup version.'}
    $metadata=Join-Path $temporary 'SetupVersion.cs'
    [IO.File]::WriteAllText($metadata,"using System.Reflection; [assembly: AssemblyTitle(`"Windows Vault Setup`")] [assembly: AssemblyProduct(`"Windows Vault`")] [assembly: AssemblyVersion(`"$version.0`")] [assembly: AssemblyFileVersion(`"$version.0`")] [assembly: AssemblyInformationalVersion(`"$version-alpha`")]")
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ "/win32manifest:$repo\src\WindowsBlocker\app.manifest" "/win32icon:$repo\src\WindowsBlocker\Assets\windows-vault.ico" "/out:$output" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/resource:$zip,VaultPayload" "/resource:$hash,VaultPayloadHash" "$PSScriptRoot\WindowsVaultSetup.cs" $metadata
    if($LASTEXITCODE -ne 0){throw 'Setup compilation failed.'}
    $setupHash=(Get-FileHash $output -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($output+'.sha256',"$setupHash  $([IO.Path]::GetFileName($output))`n")
    @{setup=$output;sha256=$setupHash;bytes=(Get-Item $output).Length;payloadSha256=(Get-FileHash $zip).Hash.ToLowerInvariant();signed=$false}|ConvertTo-Json
} finally {Remove-Item $temporary -Recurse -Force}
