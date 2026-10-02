#!/usr/bin/env python3
"""Export only source to the isolated mini1 Windows rig and run its tests there."""
import argparse
import base64
import os
from pathlib import Path
import shlex
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("--suite", choices=("build", "ui", "all"), default="all")
args = parser.parse_args()
repo = Path(__file__).resolve().parents[2]
archive = Path.home() / "Desktop/agentic/artifacts/windows-port-host.tar.gz"
archive.parent.mkdir(parents=True, exist_ok=True)
env = dict(os.environ, COPYFILE_DISABLE="1")
subprocess.run(["tar", "-czf", str(archive), "--exclude=.git", "--exclude=bin", "--exclude=obj", "-C", str(repo), "."], check=True, env=env)
subprocess.run(["scp", str(archive), "mini:winvm/windows-port-host.tar.gz"], check=True)
subprocess.run(["ssh", "mini", "scp -P2222 -oStrictHostKeyChecking=no -oUserKnownHostsFile=/dev/null -oLogLevel=ERROR ~/winvm/windows-port-host.tar.gz vault@127.0.0.1:C:/windows-port-host.tar.gz"], check=True)
scripts = ["build-and-test.ps1"]
scripts.append("test-folder-broker.ps1")
if args.suite == "all":
    scripts.append("test-native-live.ps1")
if args.suite in ("ui", "all"):
    scripts.append("test-windows-ui.ps1")
    scripts.append("test-external-links.ps1")
commands = [r"tar -xzf C:\windows-port-host.tar.gz -C C:\vault-porting-host"]
for script in scripts:
    commands += [rf"powershell -NoProfile -ExecutionPolicy Bypass -File C:\vault-porting-host\scripts\development\{script}", "if($LASTEXITCODE -ne 0){exit 1}"]
encoded = base64.b64encode("\n".join(commands).encode("utf-16le")).decode()
subprocess.run(["ssh", "mini", "~/winvm/gssh.sh " + shlex.quote("powershell -NoProfile -EncodedCommand " + encoded)], check=True)
