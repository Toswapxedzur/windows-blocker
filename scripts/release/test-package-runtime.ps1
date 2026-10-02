param(
    [Parameter(Mandatory=$true)][string]$PackageDirectory,
    [string]$EvidenceDirectory=(Join-Path $env:TEMP ('Vault-Package-Runtime-'+[Guid]::NewGuid().ToString('N'))),
    [switch]$Interactive
)
$ErrorActionPreference='Stop'
$package=(Resolve-Path $PackageDirectory).Path
$evidence=[IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force $evidence|Out-Null
$result=Join-Path $evidence 'result.json'
if(Test-Path $result){throw 'Choose a new evidence directory for this package verification.'}
if(!$Interactive) {
    # SSH sessions are not the logged-in desktop. Run the installer and GUI
    # unelevated in that desktop, rather than accidentally testing as admin.
    $action=New-ScheduledTaskAction -Execute powershell.exe -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$PSCommandPath`" -PackageDirectory `"$package`" -EvidenceDirectory `"$evidence`" -Interactive"
    $principal=New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
    $task='VaultPackageRuntime-'+[Guid]::NewGuid().ToString('N')
    Register-ScheduledTask -TaskName $task -Action $action -Principal $principal|Out-Null
    try {
        Start-ScheduledTask -TaskName $task
        $deadline=[DateTime]::UtcNow.AddMinutes(10)
        while(!(Test-Path $result) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 250}
        if(!(Test-Path $result)){throw "Package test timed out; evidence: $evidence"}
        $report=Get-Content $result -Raw|ConvertFrom-Json
        Get-Content $result -Raw
        if(!$report.ok){throw $report.error}
    } finally {Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue;Unregister-ScheduledTask -TaskName $task -Confirm:$false}
    return
}

$checks=[Collections.Generic.List[string]]::new()
$install=Join-Path $evidence 'Installed'
$profile=Join-Path $evidence 'UserData'
$app=$null
$mcpHelper=$null
$previous=@{}
$shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) 'Windows Vault Development.lnk'
$shortcutBackup=Join-Path $evidence 'shortcut-before.lnk'
$nativeName='com.adamancia.vault.local_hub.development'
$keys=@("HKCU:\Software\Google\Chrome\NativeMessagingHosts\$nativeName","HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$nativeName")
$sequence=0
function Check($ok,[string]$label){if(!$ok){throw $label};$checks.Add($label)}
function Rpc([string]$method,$parameters=@{}) {
    $script:sequence++
    $body=@{jsonrpc='2.0';id=$script:sequence;method=$method;params=$parameters}|ConvertTo-Json -Depth 40 -Compress
    $reply=Invoke-RestMethod 'http://127.0.0.1:18788/mcp' -Method Post -Headers @{Authorization="Bearer $script:token"} -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 130
    if($reply.error){throw ($reply.error|ConvertTo-Json -Compress)}
    return $reply.result
}
function Tool([string]$name,$arguments=@{}) {
    $reply=Rpc 'tools/call' @{name=$name;arguments=$arguments}
    if($reply.isError){throw $reply.content[0].text}
    return ($reply.content[0].text|ConvertFrom-Json)
}
function Start-App {
    $script:app=Start-Process "$install\WindowsBlocker.exe" -PassThru
    $deadline=[DateTime]::UtcNow.AddSeconds(50)
    do {
        $script:app.Refresh()
        if($script:app.HasExited){throw 'Installed app exited during startup'}
        try {
            $secret=[IO.File]::ReadAllBytes("$profile\local-hub-secret.bin")
            $hmac=New-Object Security.Cryptography.HMACSHA256
            $hmac.Key=$secret
            try {$script:token=[Convert]::ToBase64String($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes('vault-mcp-bearer-v1'))).TrimEnd('=').Replace('+','-').Replace('/','_')} finally {$hmac.Dispose()}
            $null=Rpc 'initialize' @{protocolVersion='2025-06-18'}
            if($script:app.MainWindowHandle -ne [IntPtr]::Zero){return}
        } catch {Start-Sleep -Milliseconds 250}
    }while([DateTime]::UtcNow -lt $deadline)
    throw 'Installed app did not open its desktop window and authenticated endpoint'
}
function Stop-App {
    if(!$script:app -or $script:app.HasExited){return}
    $null=$script:app.CloseMainWindow()
    # The app deliberately warns on its first quit. Dismiss only its owned
    # confirmation, then exercise the ordinary second quit and worker flush.
    $deadline=[DateTime]::UtcNow.AddSeconds(8)
    do {
        $dialog=[Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.AndCondition((New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ProcessIdProperty,$script:app.Id)),(New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ClassNameProperty,'#32770')))))
        if($dialog){
            $button=$dialog.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Button)))
            if($button){$pattern=$button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern);$pattern.Invoke();break}
        }
        if($script:app.HasExited){return}
        Start-Sleep -Milliseconds 100
    }while([DateTime]::UtcNow -lt $deadline)
    $null=$script:app.CloseMainWindow()
    if(!$script:app.WaitForExit(15000)){throw 'Installed app did not flush and close normally'}
}
try {
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    $principal=New-Object Security.Principal.WindowsPrincipal($identity)
    Check (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) 'Installer and GUI run as the normal interactive user'
    Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
    foreach($key in $keys){$previous[$key]=@{exists=(Test-Path $key);value=$(if(Test-Path $key){(Get-Item $key).GetValue('')}else{$null})}}
    if(Test-Path $shortcut){Copy-Item $shortcut $shortcutBackup}
    $env:PATH="$env:WINDIR\System32;$env:WINDIR"
    foreach($variable in @('DOTNET_ROOT','DOTNET_ROOT_X64','SDKROOT','VAULT_LLAMA_PREFIX','ADAMANCIA_VAULT_LLM_MODEL')){Remove-Item "Env:$variable" -ErrorAction SilentlyContinue}
    $env:VAULT_ENVIRONMENT='development'
    $env:VAULT_STORAGE_ROOT=$profile
    $env:VAULT_MCP_CLIENT_ROOT=Join-Path $profile 'McpClients'
    & "$package\Install.ps1" -Environment development -Destination $install *> "$evidence\install.log"
    Check ((Test-Path "$install\WindowsBlocker.exe") -and (Test-Path "$install\ClassifierWorker\VaultClassifierWorker.exe")) 'Current-user installer includes the app, helper and shared Swift worker'
    foreach($key in $keys){$manifest=(Get-Item $key).GetValue('');Check ($manifest.StartsWith($install+'\',[StringComparison]::OrdinalIgnoreCase) -and (Test-Path $manifest)) 'Installed browser native registration points to this installation'}
    Check (Test-Path $shortcut) 'Current-user Start menu launcher exists'
    Start-App
    Check (@((Rpc 'tools/list').tools).Count -eq 38) 'Self-contained installed app exposes the canonical 38 MCP tools without SDK or runtime paths'
    $state=Tool 'classifier_state' @{section='all'}
    Check ($null -ne $state.assets) 'Installed native host communicates interactively with the production Swift Classifier'
    $name='Installed '+[char]0x4e2d+[char]0x6587
    $created=Tool 'classifier_action' @{action='createClassifierType';data=@{name=$name;platformIDs=@('youtube','reddit')}}
    Check (!$created.issue) 'Installed Classifier action succeeds'
    $state=Tool 'classifier_state' @{section='all'}
    $group=@($state.assets.classifierTypes|Where-Object name -eq $name)[0]
    Check ($null -ne $group) 'UTF-8 Classifier state travels through the installed app and private worker pipe'
    $null=Tool 'classifier_action' @{action='setClassifierTypePaused';data=@{typeID=$group.id;paused=$true}}
    $worker=@(Get-Process VaultClassifierWorker -ErrorAction SilentlyContinue|Where-Object{$_.Path -eq "$install\ClassifierWorker\VaultClassifierWorker.exe"})
    Check ($worker.Count -eq 1) 'Installed app owns one bundled Classifier worker'
    $oldWorker=$worker[0].Id
    $savedDeadline=[DateTime]::UtcNow.AddSeconds(10)
    do {
        $saved=$null;try{$saved=Get-Content "$profile\Classifier\state.json" -Raw|ConvertFrom-Json}catch{}
        if(@($saved.workspaceCatalog.classifierTypes|Where-Object id -eq $group.id)[0].isPaused){break}
        Start-Sleep -Milliseconds 100
    }while([DateTime]::UtcNow -lt $savedDeadline)
    Check (@($saved.workspaceCatalog.classifierTypes|Where-Object id -eq $group.id)[0].isPaused) 'Classifier activation reaches durable storage before the deliberate crash'
    Stop-Process -Id $oldWorker -Force
    $state=Tool 'classifier_state' @{section='all'}
    $fresh=@(Get-Process VaultClassifierWorker -ErrorAction SilentlyContinue|Where-Object{$_.Path -eq "$install\ClassifierWorker\VaultClassifierWorker.exe"})
    Check ($fresh.Count -eq 1 -and $fresh[0].Id -ne $oldWorker -and @($state.assets.classifierTypes|Where-Object id -eq $group.id)[0].isPaused) 'Installed app recovers from a worker crash without losing saved activation'
    $refused=$false
    try {& "$package\Install.ps1" -Environment development -Destination $install *> "$evidence\update-running.log"}catch{if($_.Exception.Message -notlike '*Close this Windows Vault*'){throw};$refused=$true}
    Check $refused 'Updating a running installation is refused before changing its files'
    Stop-App
    $helperStart=New-Object Diagnostics.ProcessStartInfo
    $helperStart.FileName="$install\NativeHost\VaultNativeHost.exe"
    $helperStart.Arguments='--mcp-proxy development'
    $helperStart.UseShellExecute=$false
    $helperStart.RedirectStandardInput=$true
    $helperStart.RedirectStandardOutput=$true
    $helperStart.RedirectStandardError=$true
    $mcpHelper=New-Object Diagnostics.Process
    $mcpHelper.StartInfo=$helperStart
    $null=$mcpHelper.Start()
    try {
        Start-Sleep -Milliseconds 500
        Check (!$mcpHelper.HasExited) 'A connected MCP stdio helper can remain alive after the GUI closes'
        $manifestBefore=(Get-FileHash "$install\package-manifest.json").Hash
        $refused=$false
        try {& "$package\Install.ps1" -Environment development -Destination $install *> "$evidence\update-helper-running.log"}catch{if($_.Exception.Message -notlike '*Close this Windows Vault*'){throw};$refused=$true}
        Check ($refused -and (Get-FileHash "$install\package-manifest.json").Hash -eq $manifestBefore -and (Test-Path "$install\Start-WindowsVault.ps1")) 'Updating while an installed MCP helper remains alive is refused without changing files or launcher'
    } finally {
        $mcpHelper.StandardInput.Close()
        if(!$mcpHelper.WaitForExit(10000)){throw 'Owned MCP helper did not exit after stdin EOF'}
        $mcpHelper.Dispose();$mcpHelper=$null
    }
    Set-Content "$install\obsolete-fixture.txt" 'obsolete application file'
    $secretBefore=(Get-FileHash "$profile\local-hub-secret.bin").Hash
    & "$package\Install.ps1" -Environment development -Destination $install *> "$evidence\update.log"
    Check (!(Test-Path "$install\obsolete-fixture.txt") -and (Get-FileHash "$profile\local-hub-secret.bin").Hash -eq $secretBefore) 'Application update removes obsolete files and preserves separate private user data'
    Start-App
    $state=Tool 'classifier_state' @{section='all'}
    Check (@($state.assets.classifierTypes|Where-Object id -eq $group.id)[0].isPaused) 'Classifier data and activation survive ordinary quit, update and restart'
    Stop-App
    Check (@(Get-Process VaultClassifierWorker -ErrorAction SilentlyContinue|Where-Object{$_.Path -eq "$install\ClassifierWorker\VaultClassifierWorker.exe"}).Count -eq 0) 'Ordinary installed-app shutdown flushes and stops its owned worker'
    $report=@{ok=$true;osBuild=[Environment]::OSVersion.Version.Build;checks=@($checks)}
}catch{
    $report=@{ok=$false;error=$_.Exception.ToString();checks=@($checks)}
}finally{
    if($app -and !$app.HasExited){Stop-Process -Id $app.Id -Force}
    if($mcpHelper){if(!$mcpHelper.HasExited){$mcpHelper.Kill();$mcpHelper.WaitForExit()};$mcpHelper.Dispose()}
    Get-Process VaultClassifierWorker -ErrorAction SilentlyContinue|Where-Object{$_.Path -eq "$install\ClassifierWorker\VaultClassifierWorker.exe"}|Stop-Process -Force
    foreach($key in $previous.Keys){if($previous[$key].exists){New-Item $key -Force|Out-Null;Set-Item $key $previous[$key].value}else{Remove-Item $key -Force -ErrorAction SilentlyContinue}}
    if(Test-Path $shortcutBackup){Copy-Item $shortcutBackup $shortcut -Force}else{Remove-Item $shortcut -ErrorAction SilentlyContinue}
}
$pendingResult=$result+'.writing-'+[Guid]::NewGuid().ToString('N')
try {
    $report|ConvertTo-Json -Depth 5|Set-Content $pendingResult -Encoding UTF8
    Move-Item $pendingResult $result
} finally {if(Test-Path $pendingResult){Remove-Item $pendingResult -Force}}
