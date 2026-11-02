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
         "rule-core.js", "rule-records.js", "rule-ai-ui.js", "storage-schema.js", "vault-ui.css", "vault-ui.js", "vault-info.js", "vault-info.css", "chrome-shim.js",
         "scenes.js", "scenes.css", "activity.js", "activity.css", "activity-time-bins.js"]
for name in files:
    # Browser assets stay canonical even if the Mac mirror has not been synced.
    source = ext / name if (ext / name).is_file() and name != "popup.html" else editor / name
    shutil.copy2(source, dest / name)
for name in ["popup.html", "rule-core.js"]:
    page = dest / name
    page.write_text(page.read_text(encoding="utf-8").replace("Vault on Mac", "Vault on Windows"), encoding="utf-8")
# Windows update strings are maintained in the checked-in desktop catalogs;
# preserve them when refreshing the shared browser and Mac strings.
windows_native = {}
for catalog in (dest / "translation").glob("*.json"):
    current = json.loads(catalog.read_text(encoding="utf-8"))
    windows_native[catalog.name] = {key: value for key, value in current.items() if key.startswith("windows.")}
for name in ["translation", "code-manual", "manual"]:
    target = dest / name
    if target.exists(): shutil.rmtree(target)
    if name in ["manual", "code-manual"]:
        source = root / "localization" / name
        target.mkdir(parents=True)
        for page in source.glob("*.md"):
            if page.name != "package-info.md": shutil.copy2(page, target / page.name)
        assert len(list(target.glob("*.md"))) == 20, f"{name} must cover all supported languages"
    else:
        shutil.copytree(ext / name, target)
# The browser editor and the native Classifier scene have separate English
# catalogs. Windows hosts both in one WebView, so it needs their union in every
# locale. Keep browser wording for shared keys and add native-only scene keys.
for catalog in (dest / "translation").glob("*.json"):
    browser_strings = json.loads(catalog.read_text(encoding="utf-8"))
    native_strings = json.loads((editor / "translation" / catalog.name).read_text(encoding="utf-8"))
    for key, value in native_strings.items():
        browser_strings.setdefault(key, value)
    browser_strings.update(windows_native.get(catalog.name, {}))
    catalog.write_text(json.dumps(browser_strings, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
# Windows editor branding comes solely from the selected Windows master.
# Browser-only master previews must not leak into the desktop payload.
icons = dest / "icons"
if icons.exists(): shutil.rmtree(icons)
icons.mkdir()
for icon in (root / "src/WindowsBlocker/Assets/BrowserIcons").iterdir():
    if icon.suffix not in (".png", ".svg") and icon.name != "package-info.md": continue
    shutil.copy2(icon, icons / icon.name)
# Windows guides, including English and native API differences, are maintained
# in localization/ and copied above.
target = dest / "classifier"
if target.exists(): shutil.rmtree(target)
shutil.copytree(mac / "classifier/Sources/VaultClassifierApp/WebAssets", target)
shutil.copy2(mac / "Sources/MacBlockerCore/Resources/custom-rule-runtime.js", dest / "custom-rule-runtime.js")
version = ET.parse(root / "src/WindowsBlocker/WindowsBlocker.csproj").findtext("PropertyGroup/Version")
config = 'window.__CB_DESKTOP_PROGRAM_ID="windowsapp";\nwindow.__CB_DESKTOP_MANIFEST=' + json.dumps({"version": version, "name": "Vault"}) + ';\n'
(dest / "desktop-config.js").write_text(config, encoding="utf-8")
html = (dest / "popup.html").read_text(encoding="utf-8")
marker = '    <script src="chrome-shim.js"></script>'
assert html.count(marker) == 1, "Mac popup template must load the native shim once"
config_tag = '    <script src="desktop-config.js"></script>\n'
if config_tag not in html:
    html = html.replace(marker, config_tag + marker)
(dest / "popup.html").write_text(html, encoding="utf-8")
# tools/mcp-tools.mac.json is exported from the tested Swift tools/list
# registry by PortableMCPCatalogTests, with handlers deliberately untouched.
catalog = json.loads((root / "tools/mcp-tools.mac.json").read_text(encoding="utf-8"))
assert len({tool["name"] for tool in catalog}) == len(catalog)
def windows_metadata(value):
    if isinstance(value, list): return [windows_metadata(item) for item in value]
    if isinstance(value, dict): return {key: windows_metadata(item) for key, item in value.items()}
    if not isinstance(value, str): return value
    return (value.replace("Vault on Mac", "Vault on Windows").replace("Mac Vault", "Vault on Windows").replace("macapp", "windowsapp")
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
