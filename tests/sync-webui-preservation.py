"""Run on Mini2: the ordinary generator must preserve Windows platform resources."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

SOURCE = Path(__file__).resolve().parents[1] / "sync-webui.sh"
KEYS = ["link.browserUpgradeRequired", "link.browserIdentitySyncPending",
        "link.browserIdentityPendingEditsInvalid", "link.browserConnectRequired"]
with tempfile.TemporaryDirectory(prefix="vault-windows-browser-sync-") as temporary:
    base = Path(temporary)
    windows = base / "windowsBlocker"
    assets = windows / "src/WindowsBlocker/WebAssets"
    browser = base / "customBlocker"
    assets.mkdir(parents=True)
    (assets / "translation").mkdir()
    (browser / "translation").mkdir(parents=True)
    shutil.copyfile(SOURCE, windows / "sync-webui.sh")
    preserved = ["activity.js", "activity.css", "chrome-shim.js", "popup.html",
                 "manual/en.md", "code-manual/en.md", "classifier/native.js", "mcp-tools.json"]
    for name in preserved:
        target = assets / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text("Windows platform implementation: " + name)
        other = base / "macosBlocker/Sources/MacBlockerWebUI/WebAssets" / name
        other.parent.mkdir(parents=True, exist_ok=True)
        other.write_text("Different Mac platform implementation: " + name)
    before = {name: hashlib.sha256((assets / name).read_bytes()).hexdigest() for name in preserved}
    for name in ["popup.js", "rule-core.js"]:
        (assets / name).write_text("old Windows browser mirror")
        (browser / name).write_text("canonical browser " + name)
    original = {"apps.allowlistHelp": "Windows protects system apps and browsers", "windows.only": "preserved"}
    (assets / "translation/en.json").write_text(json.dumps(original, indent=2) + "\n")
    (assets / "translation/fr.json").write_bytes(b'{"platform":"Windows"}\n')
    incoming = dict(original, **{"apps.allowlistHelp": "Mac platform wording", "unrelated.new": "not imported"})
    incoming.update({key: "canonical " + key for key in KEYS})
    (browser / "translation/en.json").write_text(json.dumps(incoming))
    (browser / "translation/fr.json").write_text('{"unrelated.new":"not imported"}')
    for arguments in [[], ["--browser-only"]]:
        subprocess.run(["bash", str(windows / "sync-webui.sh"), *arguments], check=True)
        assert before == {name: hashlib.sha256((assets / name).read_bytes()).hexdigest() for name in preserved}
        for name in ["popup.js", "rule-core.js"]:
            assert (assets / name).read_bytes() == (browser / name).read_bytes()
        assert json.loads((assets / "translation/en.json").read_text()) == dict(original, **{key: incoming[key] for key in KEYS})
        assert (assets / "translation/fr.json").read_bytes() == b'{"platform":"Windows"}\n'
    print("PASS ordinary and explicit browser refresh preserve Windows platform resources and copy canonical selected assets/messages")
