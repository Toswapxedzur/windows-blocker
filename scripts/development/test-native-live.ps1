param([string]$Dotnet='C:\dotnet\dotnet.exe')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot\..\..").Path
foreach($project in @('QuitFixture','NativeLiveContracts')){
  & $Dotnet build "$repo\tests\$project\$project.csproj" -c Release
  if($LASTEXITCODE -ne 0){throw "$project build failed"}
}
$result="$repo\native-live-result.json"
Remove-Item $result -ErrorAction SilentlyContinue
$runner="$repo\tests\NativeLiveContracts\bin\Release\net8.0-windows\NativeLiveContracts.exe"
$fixture="$repo\tests\QuitFixture\bin\Release\net8.0-windows\QuitFixture.exe"
$action=New-ScheduledTaskAction -Execute $runner -Argument "`"$fixture`" `"$result`""
$principal=New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName VaultPortingNativeLive -Action $action -Principal $principal -Force|Out-Null
try{
  Start-ScheduledTask -TaskName VaultPortingNativeLive
  $deadline=[DateTime]::UtcNow.AddSeconds(30)
  while(!(Test-Path $result) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200}
  if(!(Test-Path $result)){throw 'Normal-user live tests did not produce a result'}
  $report=Get-Content $result -Raw|ConvertFrom-Json
  Get-Content $result -Raw
  if(!$report.ok){throw $report.error}
}finally{Unregister-ScheduledTask -TaskName VaultPortingNativeLive -Confirm:$false}
