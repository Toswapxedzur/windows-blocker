param([switch]$InSession)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot\..\..").Path
$fixture="$repo\test-browser-fixture"
$result="$fixture\install-result.json"
if(!$InSession){
  Remove-Item $result -ErrorAction SilentlyContinue
  $name='VaultPortingBrowserInstall-'+[Guid]::NewGuid().ToString('N')
  $action=New-ScheduledTaskAction -Execute powershell.exe -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$PSCommandPath`" -InSession"
  $principal=New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
  Register-ScheduledTask -TaskName $name -Action $action -Principal $principal|Out-Null
  try {
    Start-ScheduledTask -TaskName $name
    $deadline=[DateTime]::UtcNow.AddSeconds(80)
    while(!(Test-Path $result) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200}
    if(!(Test-Path $result)){throw 'Owned browser installation fixture timed out'}
    Get-Content $result -Raw
    $answer=Get-Content $result -Raw|ConvertFrom-Json
    if(!$answer.ok){throw $answer.error}
  }finally{if((Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue).State -eq 'Running'){Stop-ScheduledTask -TaskName $name};Unregister-ScheduledTask -TaskName $name -Confirm:$false}
  exit
}
try {
  Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms
  Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class BrowserInstallInput {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window,int command);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int w,int h,uint flags);
  public static void Top(IntPtr window,bool top){SetWindowPos(window,new IntPtr(top?-1:-2),0,0,0,0,3);}
  public delegate bool WindowCallback(IntPtr window,IntPtr data);
  [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback,IntPtr data);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window,uint command);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,System.Text.StringBuilder text,int length);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,System.Text.StringBuilder text,int length);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr window,int id);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,WindowCallback callback,IntPtr data);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr w,string value);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr w,System.Text.StringBuilder value);
  [StructLayout(LayoutKind.Sequential)] public struct Rect {public int left,top,right,bottom;}
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window,out Rect rectangle);
  public static void ClickControl(IntPtr control){Rect rect;if(!GetWindowRect(control,out rect))throw new InvalidOperationException("Owned control rectangle unavailable");Click((rect.left+rect.right)/2,(rect.top+rect.bottom)/2);}
  public static string FolderInputEvidence;
  public static bool SetFolder(IntPtr picker,string value) {
    IntPtr field=GetDlgItem(picker,1152);if(field==IntPtr.Zero)return false;
    IntPtr edit=IntPtr.Zero;
    EnumChildWindows(field,(window,data)=>{var cls=new System.Text.StringBuilder(64);GetClassName(window,cls,64);if(cls.ToString()=="Edit")edit=window;return edit==IntPtr.Zero;},IntPtr.Zero);
    if(edit==IntPtr.Zero)edit=field;
    var clsName=new System.Text.StringBuilder(64);GetClassName(edit,clsName,64);
    var accepted=SendMessage(edit,0xC,IntPtr.Zero,value)!=IntPtr.Zero;
    var text=new System.Text.StringBuilder(1024);SendMessage(edit,0xD,new IntPtr(1024),text);
    FolderInputEvidence="field="+field+" edit="+edit+" class="+clsName+" accepted="+accepted+" value="+text;
    return accepted&&text.ToString()==value;
  }
  public static IntPtr[] Pickers(IntPtr owner) {
    var result=new System.Collections.Generic.List<IntPtr>();
    EnumWindows((window,data)=>{
      var title=new System.Text.StringBuilder(256);var cls=new System.Text.StringBuilder(64);
      GetWindowText(window,title,title.Capacity);GetClassName(window,cls,cls.Capacity);
      if(IsWindowVisible(window)&&GetWindow(window,4)==owner&&cls.ToString()=="#32770"&&title.ToString()=="Select the extension directory.")result.Add(window);
      return true;
    },IntPtr.Zero);return result.ToArray();
  }
  [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
  public static void Click(double x,double y){SetCursorPos((int)x,(int)y);mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);}
}
'@
  $state=Get-Content "$fixture\state.json" -Raw|ConvertFrom-Json
  if($state.browser -ne 'C:\Program Files\Google\Chrome\Application\chrome.exe'){throw 'This normal UI sideload fixture requires its owned signed Google Chrome profile'}
  $browser=Get-Process -Id $state.browserPid
  $deadline=[DateTime]::UtcNow.AddSeconds(20)
  do{$browser.Refresh();if($browser.MainWindowHandle -ne [IntPtr]::Zero){break};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $deadline)
  function Find-Picker {
    $handles=[BrowserInstallInput]::Pickers($browser.MainWindowHandle)
    if($handles.Count -gt 0){return [Windows.Automation.AutomationElement]::FromHandle($handles[0])}
  }
  # Chrome's native picker is owned by its browser window but lives in a broker
  # process. Close only prior pickers with that exact owner, then open one.
  foreach($handle in [BrowserInstallInput]::Pickers($browser.MainWindowHandle)){
    [BrowserInstallInput]::PostMessage($handle,0x10,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null
  }
  $deadline=[DateTime]::UtcNow.AddSeconds(10)
  while([BrowserInstallInput]::Pickers($browser.MainWindowHandle).Count -gt 0 -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 100}
  if([BrowserInstallInput]::Pickers($browser.MainWindowHandle).Count -gt 0){throw 'Owned prior Chrome folder picker refused ordinary close'}
  $window=[Windows.Automation.AutomationElement]::FromHandle($browser.MainWindowHandle)
  [BrowserInstallInput]::ShowWindow($browser.MainWindowHandle,9)|Out-Null
  [BrowserInstallInput]::Top($browser.MainWindowHandle,$true)
  $deadline=[DateTime]::UtcNow.AddSeconds(5)
  do {
    [BrowserInstallInput]::SetForegroundWindow($browser.MainWindowHandle)|Out-Null
    if([BrowserInstallInput]::GetForegroundWindow() -eq $browser.MainWindowHandle){break}
    Start-Sleep -Milliseconds 100
  }while([DateTime]::UtcNow -lt $deadline)
  if([BrowserInstallInput]::GetForegroundWindow() -ne $browser.MainWindowHandle){throw 'Owned Chrome window did not become foreground'}
  [Windows.Forms.SendKeys]::SendWait('^l')
  [Windows.Forms.SendKeys]::SendWait('chrome://extensions{ENTER}')
  function Find-Button([string]$label){
    $name=New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty,$label)
    $type=New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Button)
    $window.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.AndCondition($name,$type)))
  }
  $deadline=[DateTime]::UtcNow.AddSeconds(20);$developerToggled=$false
  do {
    $load=Find-Button 'Load unpacked'
    if($load -and $load.Current.IsEnabled -and !$load.Current.IsOffscreen){break}
    $dev=Find-Button 'Developer mode'
    if($dev -and !$developerToggled){
      $toggle=$null;$invoke=$null
      if($dev.TryGetCurrentPattern([Windows.Automation.TogglePattern]::Pattern,[ref]$toggle)){
        if($toggle.Current.ToggleState -eq [Windows.Automation.ToggleState]::Off){$toggle.Toggle()}
      }elseif($dev.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern,[ref]$invoke)){$invoke.Invoke()}
      else{$bounds=$dev.Current.BoundingRectangle;[BrowserInstallInput]::Click($bounds.X+$bounds.Width/2,$bounds.Y+$bounds.Height/2)}
      $developerToggled=$true
    }
    Start-Sleep -Milliseconds 200
  }while([DateTime]::UtcNow -lt $deadline)
  if(!$load){
    $nodes=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|ForEach-Object{@{name=$_.Current.Name;type=$_.Current.ControlType.ProgrammaticName;id=$_.Current.AutomationId;bounds=$_.Current.BoundingRectangle.ToString()}})
    $nodes|ConvertTo-Json -Depth 4|Set-Content -Encoding UTF8 "$fixture\chrome-controls.json"
    throw "Supported Developer mode / Load unpacked control was not reachable (toggle attempted: $developerToggled)"
  }
  ([Windows.Automation.InvokePattern]$load.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
  $deadline=[DateTime]::UtcNow.AddSeconds(20)
  do {$picker=Find-Picker;if($picker){break};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $deadline)
  if(!$picker){throw 'Normal Chrome folder picker did not open'}
  $deadline=[DateTime]::UtcNow.AddSeconds(15)
  do {
    $path=$picker.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty,'1152')))
    if($path){break};Start-Sleep -Milliseconds 100
  }while([DateTime]::UtcNow -lt $deadline)
  if(!$path){throw 'Chrome folder picker path field was not reachable'}
  $pickerHandle=[IntPtr]$picker.Current.NativeWindowHandle
  if(![BrowserInstallInput]::SetFolder($pickerHandle,$state.extension)){throw ('Owned Chrome folder field refused ordinary text input: '+[BrowserInstallInput]::FolderInputEvidence)}
  [BrowserInstallInput]::FolderInputEvidence|Set-Content -Encoding utf8 "$fixture\folder-input.txt"

  if([BrowserInstallInput]::GetWindow($pickerHandle,4) -ne $browser.MainWindowHandle){throw 'Chrome picker ownership changed'}
  $deadline=[DateTime]::UtcNow.AddSeconds(5)
  do{[BrowserInstallInput]::SetForegroundWindow($pickerHandle)|Out-Null;if([BrowserInstallInput]::GetForegroundWindow() -eq $pickerHandle){break};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $deadline)
  if([BrowserInstallInput]::GetForegroundWindow() -ne $pickerHandle){throw 'Owned Chrome picker did not become foreground'}
  $deadline=[DateTime]::UtcNow.AddSeconds(15)
  do {
    if([BrowserInstallInput]::Pickers($browser.MainWindowHandle).Count -eq 0){break}
    $select=[BrowserInstallInput]::GetDlgItem($pickerHandle,1)
    if($select -eq [IntPtr]::Zero){throw 'Owned Chrome picker Select Folder control is unavailable'}
    # The broker provider reports Pane for native controls. Use an ordinary
    # mouse action at the captured Select Folder control's native rectangle.
    if([BrowserInstallInput]::GetForegroundWindow() -ne $pickerHandle){throw 'Owned Chrome picker lost foreground before selection'}
    [BrowserInstallInput]::ClickControl($select)
    Start-Sleep -Milliseconds 500
  }while([DateTime]::UtcNow -lt $deadline)
  if([BrowserInstallInput]::Pickers($browser.MainWindowHandle).Count -gt 0){throw 'Supported Chrome folder picker did not finish selecting the owned extension'}
  @{ok=$true;workflow='Supported Chrome Developer mode and Load unpacked in an isolated owned profile'}|ConvertTo-Json|Set-Content -Encoding utf8 $result
}catch{@{ok=$false;error=($_|Out-String)}|ConvertTo-Json|Set-Content -Encoding utf8 $result}
finally{if($browser){[BrowserInstallInput]::Top($browser.MainWindowHandle,$false)}}
