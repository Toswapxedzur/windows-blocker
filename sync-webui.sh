#!/usr/bin/env bash
# Generate Windows' shared editor/scenes from their canonical browser/Mac sources.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
MAC="$ROOT/../macosBlocker"
EXT="$ROOT/../customBlocker"
python3 - "$ROOT" "$MAC" "$EXT" <<'PY'
import json
import shutil
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
root, mac, ext = map(Path, sys.argv[1:])
dest = root / "src/WindowsBlocker/WebAssets"
editor = mac / "Sources/MacBlockerWebUI/WebAssets"
files = ["popup.js", "popup.css", "popup.html", "group-scopes.js", "parental-pin.js", "group-actions.js",
         "platform-profiles.js", "translations.js", "popup-markdown.js", "bridge-protocol.js", "browser-compat.js",
         "rule-core.js", "vault-ui.css", "vault-ui.js", "vault-info.js", "vault-info.css", "chrome-shim.js",
         "scenes.js", "scenes.css", "activity.js", "activity.css", "activity-time-bins.js"]
for name in files:
    # Browser assets stay canonical even if the Mac mirror has not been synced.
    source = ext / name if (ext / name).is_file() and name != "popup.html" else editor / name
    shutil.copy2(source, dest / name)
for name in ["translation", "icons", "code-manual", "manual"]:
    target = dest / name
    if target.exists(): shutil.rmtree(target)
    source = editor / name if name in ["code-manual", "manual"] else ext / name
    shutil.copytree(source, target)
# Native API differences are kept explicit in English; locale catalogs remain
# the same canonical assets for the separate translation batch.
for folder in ["manual", "code-manual"]:
    page = dest / folder / "en.md"
    text = page.read_text(encoding="utf-8").replace("Mac Vault", "Windows Vault").replace("this Mac", "this PC").replace("the Mac", "the PC")
    text = text.replace("asks macOS to open", "asks Windows to open")
    text = text.replace("App IDs are bundle identifiers (for example `com.valvesoftware.steam`), available in events. Block/Quit ignore `com.apple.*`, browsers, Vault and its helpers, and empty IDs.",
        "App IDs are full executable paths or application user model IDs, available in events and the app picker. Block/Quit ignore Windows system processes, browsers, Vault and its helpers, and empty IDs.")
    text = text.replace("Running includes foreground, menu-bar and background `.app` processes with bundle IDs; not every Unix process.", "Running lists identified Windows application processes.")
    text = text.replace("displayed as plain text on Mac", "displayed as plain text on Windows")
    text = text.replace('"com.valvesoftware.steam"', json.dumps(r"C:\Program Files (x86)\Steam\steam.exe"))
    text = text.replace("Safari rules use the browser code manual, even when Windows Vault hosts their engine.", "Browser rules use the browser extension's code manual.")
    text = text.replace("not the system Keychain", "with access restricted to the current Windows user")
    page.write_text(text, encoding="utf-8")
target = dest / "classifier"
if target.exists(): shutil.rmtree(target)
shutil.copytree(mac / "classifier/Sources/VaultClassifierApp/WebAssets", target)
shutil.copy2(mac / "Sources/MacBlockerCore/Resources/custom-rule-runtime.js", dest / "custom-rule-runtime.js")
version = ET.parse(root / "src/WindowsBlocker/WindowsBlocker.csproj").findtext("PropertyGroup/Version")
config = 'window.__CB_DESKTOP_PROGRAM_ID="windowsapp";\nwindow.__CB_DESKTOP_MANIFEST=' + json.dumps({"version": version, "name": "Windows Vault"}) + ';\n'
(dest / "desktop-config.js").write_text(config, encoding="utf-8")
html = (dest / "popup.html").read_text(encoding="utf-8")
marker = '    <script src="chrome-shim.js"></script>'
assert html.count(marker) == 1, "Mac popup template must load the native shim once"
html = html.replace(marker, '    <script src="desktop-config.js"></script>\n' + marker)
(dest / "popup.html").write_text(html, encoding="utf-8")
# tools/mcp-tools.mac.json is exported from the tested Swift tools/list
# registry by PortableMCPCatalogTests, with handlers deliberately untouched.
catalog = json.loads((root / "tools/mcp-tools.mac.json").read_text(encoding="utf-8"))
assert len({tool["name"] for tool in catalog}) == len(catalog)
def windows_metadata(value):
    if isinstance(value, list): return [windows_metadata(item) for item in value]
    if isinstance(value, dict): return {key: windows_metadata(item) for key, item in value.items()}
    if not isinstance(value, str): return value
    return (value.replace("Mac Vault", "Windows Vault").replace("macapp", "windowsapp")
        .replace("chrome, edge, safari", "chrome, edge")
        .replace("macOS application (by bundle identifier)", "Windows application (by full executable path or application user model ID)")
        .replace("by bundle identifier", "by full executable path or application user model ID")
        .replace("The application's bundle identifier.", "The application's full executable path or application user model ID.")
        .replace("<bundle id>", "<app id>"))
catalog = windows_metadata(catalog)
for tool in catalog:
    if tool["name"] in ["add_application", "remove_application"]:
        schema = tool["inputSchema"]
        schema["properties"]["appId"] = schema["properties"].pop("bundleId")
        schema["required"] = ["appId" if key == "bundleId" else key for key in schema["required"]]
    if tool["name"] == "run_custom_rule":
        # Use the already generated platform-specific native code reference.
        prefix = tool["description"].split("CUSTOM RULE API", 1)[0]
        tool["description"] = prefix + (dest / "code-manual/en.md").read_text(encoding="utf-8")
(dest / "mcp-tools.json").write_text(json.dumps(catalog, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print("[sync-webui] generated Windows editor, Classifier, Activity, languages and native rule contract")
PY
