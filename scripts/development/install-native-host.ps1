param([Parameter(Mandatory=$true)][string]$HostDirectory,[ValidateSet('development','production')][string]$Environment='development')
$ErrorActionPreference='Stop'
$directory=(Resolve-Path $HostDirectory).Path
$development=$Environment -eq 'development'
$name=if($development){'com.adamancia.vault.local_hub.development'}else{'com.adamancia.vault.local_hub'}
$exe=if($development){'vault-local-hub-native-host-development.exe'}else{'vault-local-hub-native-host.exe'}
Copy-Item "$directory\VaultNativeHost.exe" "$directory\$exe" -Force
Copy-Item "$directory\VaultNativeHost.deps.json" "$directory\$([IO.Path]::GetFileNameWithoutExtension($exe)).deps.json" -Force
Copy-Item "$directory\VaultNativeHost.runtimeconfig.json" "$directory\$([IO.Path]::GetFileNameWithoutExtension($exe)).runtimeconfig.json" -Force
$id=if($development){'fjichnkbaoilbfbjcjkggllmbicmeegk'}else{'mcbmcmephdaapjepopobikobjmfdeamm'}
$manifest=@{name=$name;description='Adamancia Vault authenticated local connection';path="$directory\$exe";type='stdio';allowed_origins=@("chrome-extension://$id/")}
$path="$directory\$name.json"
$manifest|ConvertTo-Json -Depth 4|Set-Content -Encoding utf8 $path
foreach($browser in @('Google\Chrome','Microsoft\Edge')){
  $key="HKCU:\Software\$browser\NativeMessagingHosts\$name"
  New-Item $key -Force|Out-Null
  Set-Item $key $path
}
Write-Output "Registered $name for the current user."
