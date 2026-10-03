param([switch]$Interactive, [string]$Profile='', [string]$Result='')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot\..\..").Path
if(!$Interactive){
  foreach($port in @(18787,18788)){if(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue){throw "Dictionary UI fixture requires free development port $port"}}
  $Profile=Join-Path $repo ('test-dictionary-profile-'+[Guid]::NewGuid().ToString('N'))
  $Result=Join-Path $repo 'dictionary-ui-result.json'
  New-Item -ItemType Directory -Force $Profile|Out-Null
  Remove-Item $Result -ErrorAction SilentlyContinue
  $action=New-ScheduledTaskAction -Execute powershell.exe -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Interactive -Profile `"$Profile`" -Result `"$Result`""
  $principal=New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
  $taskName='VaultDictionaryUi-'+[Guid]::NewGuid().ToString('N')
  Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal|Out-Null
  try{
    Start-ScheduledTask -TaskName $taskName
    $deadline=[DateTime]::UtcNow.AddSeconds(150)
    while(!(Test-Path $Result) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200}
    if(!(Test-Path $Result)){throw 'Dictionary UI fixture did not finish'}
    $raw=Get-Content $Result -Raw; $raw
    if(!(($raw|ConvertFrom-Json).ok)){throw 'Dictionary UI assertions failed'}
  }finally{Unregister-ScheduledTask -TaskName $taskName -Confirm:$false}
  return
}
Add-Type @'
using System;using System.Text;using System.Collections.Generic;using System.Runtime.InteropServices;
public static class VaultDictionaryWindows {
  delegate bool Visitor(IntPtr h,IntPtr p);
  [DllImport("user32.dll")] static extern bool EnumWindows(Visitor v,IntPtr p);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr dialog,int id);
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h,int field);
  [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
  public static bool Default(IntPtr dialog,int id){return (GetWindowLong(GetDlgItem(dialog,id),-16)&15)==1;}
  public static void Click(IntPtr dialog,int id){var h=GetDlgItem(dialog,id);if(h==IntPtr.Zero || !PostMessage(dialog,0x111,new IntPtr(id),h))throw new Exception("Native dialog button unavailable");}
  public static void Close(IntPtr h){PostMessage(h,0x10,IntPtr.Zero,IntPtr.Zero);}
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder text,int length);
  public static IntPtr Find(int pid,string title){foreach(var h in Get(pid)){var text=new StringBuilder(512);GetWindowText(h,text,512);if(text.ToString()==title)return h;}return IntPtr.Zero;}
  public static IntPtr[] Get(int pid){var a=new List<IntPtr>();EnumWindows((h,p)=>{uint n;GetWindowThreadProcessId(h,out n);if(n==pid)a.Add(h);return true;},IntPtr.Zero);return a.ToArray();}
}
'@

$env:VAULT_ENVIRONMENT='development';$env:VAULT_STORAGE_ROOT=$Profile;$env:VAULT_MCP_CLIENT_ROOT="$Profile\mcp-clients"
$env:DOTNET_ROOT='C:\dotnet'
$checks=[Collections.Generic.List[string]]::new();$owned=$null;$failure=$null
function Check($condition,[string]$message){if(!$condition){throw $message};$checks.Add($message)}
function WaitFor([scriptblock]$find,[int]$seconds=40){$until=[DateTime]::UtcNow.AddSeconds($seconds);do{$found=& $find;if($found){return $found};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $until);throw 'Timed out waiting for native dictionary UI'}
function Window([string]$title){$h=[VaultDictionaryWindows]::Find($owned.Id,$title);if($h -ne [IntPtr]::Zero){return $h}}
function State { try{(Get-Content "$Profile\Classifier\state.json" -Raw|ConvertFrom-Json).settings.dictionaries}catch{return $null} }
function CloseOwned {
  if($owned -and !$owned.HasExited){
    $choice=Window 'Help improve the creator dictionary'
    if($choice){[VaultDictionaryWindows]::Click($choice,7);Start-Sleep -Milliseconds 500}
    $main=Window 'Windows Vault'
    if(!$main){throw 'Owned main window unavailable'}
    [VaultDictionaryWindows]::Close($main)
    $until=[DateTime]::UtcNow.AddSeconds(5)
    do{$warning=Window 'Windows Vault is still running';if($warning){[VaultDictionaryWindows]::Close($warning);Start-Sleep -Milliseconds 500;break};if($owned.HasExited){return};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $until)
    [VaultDictionaryWindows]::Close($main)
    if(!$owned.WaitForExit(15000)){throw 'Owned dictionary app did not close normally'}
  }
}
try{
  $exe="$repo\src\WindowsBlocker\bin\Release\net8.0-windows\WindowsBlocker.exe"
  $owned=Start-Process $exe -PassThru
  $prompt=WaitFor {Window 'Help improve the creator dictionary'}
  $promptHandle=$prompt
  $yes=WaitFor {$h=[VaultDictionaryWindows]::GetDlgItem($promptHandle,6);if($h -ne [IntPtr]::Zero){$h}}
  $no=WaitFor {$h=[VaultDictionaryWindows]::GetDlgItem($promptHandle,7);if($h -ne [IntPtr]::Zero){$h}}
  Check ($yes -and $no) 'First app launch shows the native contribution choice before opening Classifier'
  Check ([VaultDictionaryWindows]::Default($promptHandle,6)) 'Contribution choice defaults to Yes'
  [VaultDictionaryWindows]::Click($promptHandle,7)
  $settings=WaitFor { $d=State;if($d.contributionChoiceMade -and !$d.contributionEnabled){$d} }
  Check ($settings.creatorMode -eq 'cache' -and $settings.creatorCacheSize -eq 10000) 'Default creator cache is 10,000 and opting out persists'
  CloseOwned
  $owned=Start-Process $exe -PassThru
  [void](WaitFor {Window 'Windows Vault'})
  [void](WaitFor {Get-NetTCPConnection -LocalPort 18788 -State Listen -ErrorAction SilentlyContinue})
  Start-Sleep -Seconds 5
  Check (!(Window 'Help improve the creator dictionary')) 'Saved first-launch choice does not prompt again'
  Check (!(State).contributionEnabled -and (State).contributionChoiceMade) 'Contribution disablement survives a native restart'
}catch{$failure=$_.Exception.Message+' '+$_.ScriptStackTrace}
finally{
  try{CloseOwned}catch{if(!$failure){$failure=$_.Exception.Message+' '+$_.ScriptStackTrace}}
  @{ok=(!$failure);error=$failure;checks=@($checks);profile=$Profile}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 $Result
}
