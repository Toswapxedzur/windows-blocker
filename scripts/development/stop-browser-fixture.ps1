$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot\..\..").Path
$fixture="$repo\test-browser-fixture"
if(Test-Path "$fixture\state.json") {
  New-Item -ItemType File -Force "$fixture\stop"|Out-Null
  $deadline=[DateTime]::UtcNow.AddSeconds(45)
  while((Test-Path "$fixture\state.json") -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200}
  if(Test-Path "$fixture\state.json"){throw 'Owned fixture cleanup did not finish; registry restoration requires its launch process'}
}
$configPath="$fixture\config.json"
if(Test-Path $configPath){
  $config=Get-Content $configPath -Raw|ConvertFrom-Json
  Unregister-ScheduledTask -TaskName $config.task -Confirm:$false -ErrorAction SilentlyContinue
  # Delete only this fixture's GUID-named browser profile after its process tree
  # has stopped. Keep native state/evidence available for acceptance review.
  $profile=[IO.Path]::GetFullPath($config.profile)
  if([IO.Path]::GetDirectoryName($profile) -ne [IO.Path]::GetFullPath($fixture) -or [IO.Path]::GetFileName($profile) -notmatch '^browser-[a-f0-9]{32}$'){throw 'Owned profile cleanup path is invalid'}
  if(Test-Path $profile){Remove-Item $profile -Recurse -Force}
}
