"""Run through customBlocker's canonical driver, attached to an owned guest browser."""
import base64
import json
import os
import shlex
import subprocess
import time


def run(context, service_worker):
    program = os.environ["WINDOWS_BROWSER_PROGRAM"]
    checks = []
    def check(ok, description):
        if not ok:
            raise AssertionError(description)
        checks.append(description)
        print("PASS " + description, flush=True)
    host = "com.adamancia.vault.local_hub.development"
    def request(body):
        return service_worker.evaluate("async ({host,body}) => await chrome.runtime.sendNativeMessage(host,body)", {"host":host,"body":body})
    challenge = {"kind":"local-hub-challenge","v":4,"program":program,"challenge":"A"*43}
    valid = request(challenge)
    check(valid.get("ok") is True and len(valid.get("proof", "")) == 43, "Genuine signed " + program + " receives a protocol4 native proof")
    invalid = request(dict(challenge, program="chrome" if program == "edge" else "edge"))
    check(invalid.get("ok") is False and "proof" not in invalid, "Proof request is bound to the actual browser's program")
    try:
        request(dict(challenge, padding="x"*65_536))
        raise AssertionError("Oversized browser challenge was accepted")
    except Exception as error:
        check("Oversized browser challenge was accepted" not in str(error), "Actual native browser connection rejects frames above 64KiB")
    check(request(challenge).get("ok") is True, "Fresh native connection recovers after an oversized frame")
    # Keep a real native connection alive while inspecting its actual ancestry.
    service_worker.evaluate("host => { self.__vaultOwnedNativePort = chrome.runtime.connectNative(host); }", host)
    time.sleep(0.5)
    script = r"""$ProgressPreference='SilentlyContinue';$processes=Get-CimInstance Win32_Process;$native=$processes|Where-Object {$_.Name -eq 'vault-local-hub-native-host-development.exe'};foreach($p in $native){$chain=@();for($i=0;$i -lt 3 -and $p;$i++){$chain+=@{name=$p.Name;pid=$p.ProcessId;parent=$p.ParentProcessId;path=$p.ExecutablePath};$next=$p.ParentProcessId;$p=$processes|Where-Object {$_.ProcessId -eq $next}|Select-Object -First 1};$chain|ConvertTo-Json -Depth 4 -Compress}"""
    encoded = base64.b64encode(script.encode("utf-16le")).decode()
    result = subprocess.run([str(os.path.expanduser("~/winvm/gssh.sh")), "powershell -NoProfile -EncodedCommand " + encoded], capture_output=True, text=True, check=True)
    chains = [json.loads(line) for line in result.stdout.splitlines() if line.startswith("[")]
    expected = "msedge.exe" if program == "edge" else "chrome.exe"
    check(any(chain[0]["name"].endswith("-development.exe") and (chain[1]["name"] == expected or chain[1]["name"] == "cmd.exe" and chain[2]["name"] == expected) for chain in chains), "Actual native host ancestry is a browser or its single system command intermediary")
    print("Observed native ancestry: " + json.dumps(chains), flush=True)
    service_worker.evaluate("() => {self.__vaultOwnedNativePort.disconnect();delete self.__vaultOwnedNativePort;}")
    deadline = time.monotonic() + 15
    status = None
    while time.monotonic() < deadline:
        status = service_worker.evaluate("() => cbConnection.statusForTarget('windowsapp')")
        if status.get("state") == "connected":
            break
        time.sleep(0.2)
    check(status.get("state") == "connected" and status.get("hubProgram") == "windowsapp", "Canonical extension authenticates its Windows Vault tunnel")
    print(json.dumps({"ok":True,"program":program,"checks":checks}), flush=True)
