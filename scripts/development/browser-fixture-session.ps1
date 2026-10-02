param([Parameter(Mandatory=$true)][string]$FixtureDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$config=Get-Content "$FixtureDirectory\config.json" -Raw|ConvertFrom-Json
$principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Browser fixture must run as the normal interactive user'}
$env:DOTNET_ROOT='C:\dotnet'
$env:VAULT_ENVIRONMENT='development'
$env:VAULT_STORAGE_ROOT=$config.storage
$env:VAULT_MCP_CLIENT_ROOT="$FixtureDirectory\clients"
$previous=@();$app=$null;$browser=$null
function Save-State([bool]$paused=$false){
  $state=@{appPid=$(if($app -and !$app.HasExited){$app.Id}else{$null});browserPid=$browser.Id;browser=$config.browser;extension=$config.extension;profile=$config.profile;storage=$config.storage;cdpPort=$config.cdpPort;task=$config.task;paused=$paused}|ConvertTo-Json
  $path="$FixtureDirectory\state.json";$temporary="$path.tmp"
  [IO.File]::WriteAllText($temporary,$state,(New-Object Text.UTF8Encoding($false)))
  if(Test-Path $path){$backup="$path.bak";[IO.File]::Replace($temporary,$path,$backup);Remove-Item $backup -ErrorAction SilentlyContinue}else{[IO.File]::Move($temporary,$path)}
}
function Start-App {
  $script:app=Start-Process "$($config.repo)\src\WindowsBlocker\bin\Release\net8.0-windows\WindowsBlocker.exe" -PassThru
}
function Stop-App {
  if(!$app -or $app.HasExited){return}
  $null=$app.CloseMainWindow()
  $deadline=[DateTime]::UtcNow.AddSeconds(12)
  do {
    if($app.HasExited){return}
    try {
      $dialog=[Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.AndCondition((New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ProcessIdProperty,$app.Id)),(New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ClassNameProperty,'#32770')))))
      if($dialog){
        $button=$dialog.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Button)))
        if($button){$button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();break}
      }
    }catch [Windows.Automation.ElementNotAvailableException] { }
    Start-Sleep -Milliseconds 100
  }while([DateTime]::UtcNow -lt $deadline)
  $null=$app.CloseMainWindow()
  if(!$app.WaitForExit(20000)){throw 'Owned app refused ordinary shutdown; outage fixture cannot continue'}
}
try {
  foreach($browserName in @('Google\Chrome','Microsoft\Edge')){
    $key="HKCU:\Software\$browserName\NativeMessagingHosts\com.adamancia.vault.local_hub.development"
    $exists=Test-Path $key;$previous+=@{key=$key;exists=$exists;value=$(if($exists){(Get-Item $key).GetValue('')}else{$null})}
  }
  $previous|ConvertTo-Json -Depth 4|Set-Content -Encoding utf8 "$FixtureDirectory\registry-before.json"
  & "$($config.repo)\scripts\development\install-native-host.ps1" -HostDirectory "$($config.repo)\src\VaultNativeHost\bin\Release\net8.0-windows" -Environment development
  # Create support state as the same normal user as the worker. Elevated SSH
  # staging would leave admin ownership and correctly fail its private ACL gate.
  New-Item -ItemType Directory -Force $config.storage|Out-Null
  if($config.modelSource){
    $models="$($config.storage)\Classifier\models"
    New-Item -ItemType Directory -Force $models|Out-Null
    $modelDestination="$models\Qwen2.5-3B-Instruct-Q4_K_M.gguf"
    New-Item -ItemType HardLink -Path $modelDestination -Target $config.modelSource -ErrorAction Stop|Out-Null
    $links=@(& fsutil.exe hardlink list $modelDestination)
    if($LASTEXITCODE -ne 0 -or $links.Count -lt 2){throw 'Owned model fixture requires a verified same-volume NTFS hardlink'}
    $links|Set-Content -Encoding utf8 "$FixtureDirectory\model-hardlink.txt"
  }
  Start-App
  $browserArguments=@("--remote-debugging-port=$($config.cdpPort)","--user-data-dir=$($config.profile)",'--no-first-run','--no-default-browser-check')
  # Branded Google Chrome retired both extension CLI flags. Its isolated profile
  # uses the supported Developer mode / Load unpacked UI instead.
  if($config.browser -ne 'C:\Program Files\Google\Chrome\Application\chrome.exe'){
    $browserArguments+=@("--disable-extensions-except=$($config.extension)","--load-extension=$($config.extension)")
  }
  $browserArguments+='about:blank'
  $browser=Start-Process $config.browser -ArgumentList $browserArguments -PassThru
  Save-State
  while(!(Test-Path "$FixtureDirectory\stop")){
    if(Test-Path "$FixtureDirectory\pause-app"){
      Stop-App;Remove-Item "$FixtureDirectory\pause-app";Save-State $true
    }
    if(Test-Path "$FixtureDirectory\resume-app"){
      if($app -and !$app.HasExited){throw 'Outage fixture app is still running'}
      Start-App;Remove-Item "$FixtureDirectory\resume-app";Save-State
    }
    Start-Sleep -Milliseconds 250
  }
  Stop-App
}catch{$_|Out-String|Set-Content -Encoding utf8 "$FixtureDirectory\failure.txt"}
finally{
  # This fixture owns these captured processes and fresh profile exclusively.
  # Outage/reconnect uses ordinary shutdown; bounded final cleanup removes only
  # this disposable fixture if an assertion interrupted its normal workflow.
  foreach($process in @($browser,$app)){if($process -and !$process.HasExited){& taskkill.exe /PID $process.Id /T /F|Out-Null}}
  foreach($item in $previous){if($item.exists){Set-Item $item.key $item.value}else{Remove-Item $item.key -Force -ErrorAction SilentlyContinue}}
  Remove-Item "$FixtureDirectory\state.json" -ErrorAction SilentlyContinue
}
