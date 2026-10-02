param(
    [Parameter(Mandatory=$true)][string]$BundleDirectory,
    [string]$EvidenceDirectory = (Join-Path $env:TEMP ('vault-worker-evidence-' + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$BundleDirectory = (Resolve-Path $BundleDirectory).Path
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force $EvidenceDirectory | Out-Null
$relocated = Join-Path $EvidenceDirectory 'RelocatedWorker'
Copy-Item -Recurse $BundleDirectory $relocated
$manifest = Get-Content (Join-Path $relocated 'bundle-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1 -or $manifest.architecture -ne 'x64') { throw 'Invalid worker bundle manifest.' }
foreach ($entry in $manifest.files) {
    $path = [IO.Path]::GetFullPath((Join-Path $relocated $entry.path))
    if (!$path.StartsWith($relocated + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escapes the worker bundle.' }
    if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $entry.sha256) { throw ('Worker bundle hash mismatch: ' + $entry.path) }
}
$script:sequence = 0
$script:process = $null
$script:stderr = $null
$script:input = $null
$script:transcript = [Collections.Generic.List[string]]::new()
# Windows PowerShell 5.1 reads a BOM-free script using the system code page.
# Construct the exact Unicode fixture instead of relying on its script encoding.
$chinese = [string][char]0x4e2d + [char]0x6587
$groupName = 'Fixture ' + $chinese
$tagName = 'Tag ' + $chinese
$savedMeaning = 'Saved meaning ' + $chinese + [char]0x3002
$appName = 'App ' + $chinese
$data = Join-Path $EvidenceDirectory 'Data\Classifier'
New-Item -ItemType Directory -Force $data | Out-Null

function Assert-Worker($condition, [string]$message) { if (!$condition) { throw $message } }
function Start-Worker {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = Join-Path $relocated 'VaultClassifierWorker.exe'
    $info.WorkingDirectory = $EvidenceDirectory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $encoding = [Text.UTF8Encoding]::new($false)
    $info.StandardOutputEncoding = $encoding
    $info.StandardErrorEncoding = $encoding
    # Only Windows system paths remain. Every Swift, VC and llama dependency
    # must resolve beside the relocated executable, as on an end-user PC.
    $info.EnvironmentVariables['PATH'] = $env:WINDIR + '\System32;' + $env:WINDIR
    $info.EnvironmentVariables['VAULT_ENVIRONMENT'] = 'development'
    $info.EnvironmentVariables['ADAMANCIA_VAULT_ENVIRONMENT'] = 'development'
    $info.EnvironmentVariables['VAULT_DATA_ROOT'] = $data
    foreach ($key in @('SDKROOT','VAULT_LLAMA_PREFIX','ADAMANCIA_VAULT_LLM_MODEL','ADAMANCIA_VAULT_TAG_TEST')) { $info.EnvironmentVariables.Remove($key) }
    $script:process = [Diagnostics.Process]::Start($info)
    $script:input = [IO.StreamWriter]::new($script:process.StandardInput.BaseStream, $encoding)
    $script:stderr = $script:process.StandardError.ReadToEndAsync()
    for ($i = 0; $i -lt 100; $i++) {
        $message = Read-Worker
        if ($message.event -eq 'ready') { Assert-Worker ($message.protocol -eq 1) 'Wrong worker protocol'; return }
    }
    throw 'The relocated worker did not announce readiness.'
}
function Read-Worker {
    $read = $script:process.StandardOutput.ReadLineAsync()
    if (!$read.Wait(60000)) { throw 'Worker output timed out.' }
    $line = $read.Result
    if ($null -eq $line) { throw 'Worker stdout closed unexpectedly.' }
    $script:transcript.Add($line)
    return ($line | ConvertFrom-Json)
}
function Request-Worker([string]$operation, $body, [bool]$expectSuccess = $true) {
    $script:sequence++
    $id = 'fixture-' + $script:sequence
    $json = @{id=$id; operation=$operation; data=$body} | ConvertTo-Json -Depth 30 -Compress
    $script:input.WriteLine($json)
    $script:input.Flush()
    for ($i = 0; $i -lt 100; $i++) {
        $message = Read-Worker
        if ($message.event -eq 'fatal') { throw ('Worker fatal: ' + $message.error) }
        if ($message.id -ne $id) { continue }
        Assert-Worker ($message.ok -eq $expectSuccess) ('Unexpected worker reply: ' + ($message | ConvertTo-Json -Depth 4 -Compress))
        if ($expectSuccess) { return $message.value }
        return $message.error
    }
    throw 'No matching worker response.'
}
function Stop-Worker {
    $null = Request-Worker 'hostEvent' @{kind='flush'}
    $script:input.Close()
    if (!$script:process.WaitForExit(20000)) { throw 'Worker did not flush and stop after EOF.' }
    Assert-Worker ($script:process.ExitCode -eq 0) 'Worker exited unsuccessfully.'
    $script:stderr.GetAwaiter().GetResult() | Add-Content -Encoding UTF8 (Join-Path $EvidenceDirectory 'stderr.log')
    $script:process.Dispose()
    $script:process = $null
}

try {
    Start-Worker
    $initial = Request-Worker 'snapshot' @{}
    Assert-Worker ($null -ne $initial.assets) 'Initial snapshot is incomplete.'
    $created = Request-Worker 'action' @{action='createClassifierType'; data=@{name=$groupName; platformIDs=@('youtube','reddit')}}
    Assert-Worker (!$created.issue) 'Could not create classifier group.'
    $group = @($created.snapshot.assets.classifierTypes | Where-Object name -eq $groupName)[0]
    Assert-Worker ($null -ne $group) 'UTF-8 group name did not round trip.'
    $null = Request-Worker 'action' @{action='setClassifierTypePaused'; data=@{typeID=$group.id; paused=$true}}
    $refusal = Request-Worker 'action' @{action='configureClassifierType'; data=@{typeID=$group.id; name='Changed'; applicablePlatformIDs=@('twitter')}}
    Assert-Worker ($refusal.issue -eq 'Platforms cannot be changed after the group is created.') 'Fixed platforms were not enforced.'
    $null = Request-Worker 'action' @{action='addTag'; data=@{treeID=$group.treeID; name=$tagName; positionX=0; positionY=0}}
    $names = Request-Worker 'tagNames' @{platform='youtube'}
    Assert-Worker (@($names) -contains $tagName) 'Portable taxonomy is incomplete.'
    $null = Request-Worker 'action' @{action='addKnowledgeTerm';data=@{subject='Fixture knowledge';meaning='Initial meaning.'}}
    $page = Request-Worker 'action' @{action='knowledgePage';data=@{requestID='knowledge-page';kind='term';platformID='';query='Fixture knowledge';offset=0;limit=1}}
    Assert-Worker ($page.list.total -eq 1 -and $page.list.requestID -eq 'knowledge-page') 'Knowledge list callback is incomplete.'
    $row = @($page.list.items)[0]
    $edited = Request-Worker 'action' @{action='editKnowledgeEntry';data=@{id=$row.id;meaning=$savedMeaning}}
    Assert-Worker ($edited.knowledgeRow.meaning -eq $savedMeaning) 'Knowledge edit acknowledgement lost UTF-8.'
    Assert-Worker ($edited.snapshot.assets.knowledge.paged -and @($edited.snapshot.assets.knowledge.terms).Count -eq 0) 'Knowledge snapshots must use native list paging.'
    $null = Request-Worker 'action' @{action='restoreClassifierType'; data=@{}} $false
    $null = Request-Worker 'resource' @{url='file:///C:/Windows/win.ini'} $false
    $null = Request-Worker 'activity' @{kind='setSettings'; category='app-usage'; enabled=$true}
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() - 60000
    foreach ($entry in @(@{appId='a.exe';name=$appName},@{appId='a.exe';name=$appName},@{appId='b.exe';name='B'})) {
        $sample = @{kind='native-sample';appId=$entry.appId;name=$entry.name;elapsedMs=1000;atMs=$now;icon='data:image/png;base64,fixture'}
        $null = Request-Worker 'activity' $sample
        $now += 1000
    }
    $saved = Request-Worker 'activity' @{kind='group-save';request='group-fixture';group=@{name='Work';merge=$true;members=@('app|a.exe')}}
    Assert-Worker ($saved.answer.ok -and $saved.answer.request -eq 'group-fixture') 'Group callback lost its request.'
    $history = Request-Worker 'activity' @{kind='history';section='usage';pick='group|'+$saved.answer.id;barDays=7}
    Assert-Worker ($history.request.barDays -eq 7 -and @($history.value.map.dayStartsMs).Count -eq 365) 'History scene protocol differs.'
    $null = Request-Worker 'action' @{action='setBackupOwnerCode';data=@{ownerCode='mini1-fixture-owner-code'}}
    Stop-Worker
    Start-Worker
    $restored = Request-Worker 'snapshot' @{}
    $restoredGroup = @($restored.assets.classifierTypes | Where-Object id -eq $group.id)[0]
    Assert-Worker ($restoredGroup.isPaused -eq $true) 'Native activation did not persist.'
    $known = Request-Worker 'activity' @{kind='known-items'}
    Assert-Worker (@($known.items | Where-Object id -eq 'app|a.exe').Count -eq 1) 'Activity did not flush before EOF.'
    $unlocked = Request-Worker 'action' @{action='unlockBackup';data=@{ownerCode='mini1-fixture-owner-code'}}
    Assert-Worker (!$unlocked.issue) 'Protected Windows owner code did not persist.'
    Stop-Worker
    $script:transcript | Set-Content -Encoding UTF8 (Join-Path $EvidenceDirectory 'worker.jsonl')
    Write-Output ('PASS: relocated worker, app-local dependencies, UTF-8 actions, taxonomy, Activity callbacks, DPAPI persistence and EOF flush. Evidence: ' + $EvidenceDirectory)
} finally {
    if ($script:process -and !$script:process.HasExited) { $script:process.Kill() }
    if ($script:process) { $script:process.Dispose() }
    $script:transcript | Set-Content -Encoding UTF8 (Join-Path $EvidenceDirectory 'worker.jsonl')
}
