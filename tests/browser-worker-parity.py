"""Actual signed-browser collector/pills/cover → Windows Vault → production Swift.

Run through the canonical customBlocker CDP driver on mini1. The routed page
uses real YouTube cards and installed content scripts; chrome APIs, native
transport and tag state remain real. The launcher stages a verified worker,
existing model and fresh private profile. Provider research stays disabled.
"""
import base64
import html
import json
import os
from pathlib import Path
import queue
import subprocess
import threading
import time
import uuid


def encoded(script):
    return base64.b64encode(script.encode("utf-16le")).decode()


def run(context, service_worker):
    program = os.environ["WINDOWS_BROWSER_PROGRAM"]
    guest = os.path.expanduser("~/winvm/gssh.sh")
    fixture_path = r"C:\vault-porting-host\test-browser-fixture"
    checks, proxy, sequence, page = [], None, 0, None
    evidence = Path.home() / "winvm" / ("evidence-browser-" + program + "-" + uuid.uuid4().hex[:8])
    evidence.mkdir()

    def guest_command(script):
        return subprocess.run([guest, "powershell -NoProfile -EncodedCommand " + encoded(script)],
                              text=True, encoding="utf-8", capture_output=True, check=True, timeout=60).stdout.strip()

    fixture = json.loads(guest_command("Get-Content '" + fixture_path + r"\state.json' -Raw"))

    def check(ok, label):
        if not ok:
            raise AssertionError(label)
        checks.append(label)
        print("PASS " + label, flush=True)

    def start_proxy():
        nonlocal proxy
        # Bearer credential remains in the guest, absent from args/stdout.
        script = "$env:VAULT_ENVIRONMENT='development';$env:VAULT_STORAGE_ROOT='" + fixture["storage"] + r"';& 'C:\vault-porting-host\src\VaultNativeHost\bin\Release\net8.0-windows\VaultNativeHost.exe' --mcp-proxy development"
        proxy = subprocess.Popen([guest, "powershell -NoProfile -EncodedCommand " + encoded(script)],
                                 stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                 text=True, encoding="utf-8", bufsize=1)
        answers = queue.Queue()
        def read():
            for line in proxy.stdout:
                answers.put(line)
        threading.Thread(target=read, daemon=True).start()
        return answers

    def stop_proxy():
        nonlocal proxy
        if proxy is None:
            return
        proxy.stdin.close()
        try:
            proxy.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proxy.terminate()
            proxy.wait(timeout=10)
        proxy = None

    replies = start_proxy()

    def tool(name, arguments):
        nonlocal sequence
        sequence += 1
        proxy.stdin.write(json.dumps({"jsonrpc":"2.0", "id":sequence, "method":"tools/call",
                                      "params":{"name":name,"arguments":arguments}}, ensure_ascii=False) + "\n")
        proxy.stdin.flush()
        answer = json.loads(replies.get(timeout=130))
        if answer.get("id") != sequence or "error" in answer:
            raise AssertionError("Bundled proxy failed " + name + ": " + repr(answer))
        result = answer["result"]
        if result.get("isError"):
            raise AssertionError("Tool refused " + name + ": " + repr(result))
        return json.loads(result["content"][0]["text"])

    def action(name, data):
        result = tool("classifier_action", {"action":name,"data":data})
        check(not result.get("issue"), "Shared Swift action accepted " + name)
        return result

    def hub(operation, body):
        return service_worker.evaluate("async ({operation,body}) => await CBClassifierHub.request(operation,body)",
                                       {"operation":operation,"body":body})

    def wait(predicate, seconds=30):
        deadline, answer = time.monotonic() + seconds, None
        while time.monotonic() < deadline:
            answer = predicate()
            if answer:
                return answer
            time.sleep(0.25)
        raise AssertionError("Timed out waiting for actual browser outcome; last=" + repr(answer))

    def set_app_paused(paused):
        marker = "pause-app" if paused else "resume-app"
        guest_command("New-Item -ItemType File -Force '" + fixture_path + "\\" + marker + "'|Out-Null")
        deadline = time.monotonic() + 45
        while time.monotonic() < deadline:
            state = json.loads(guest_command("Get-Content '" + fixture_path + r"\state.json' -Raw"))
            if state["paused"] is paused:
                return state
            time.sleep(0.5)
        raise AssertionError("Owned app did not " + ("close normally" if paused else "restart"))

    try:
        initial = tool("classifier_state", {"section":"all"})
        check(not initial.get("issue") and not initial["settings"]["research"]["enabled"],
              "Production worker is available with provider research disabled")
        name = "Browser bridge 中文 " + uuid.uuid4().hex[:8]
        action("createClassifierType", {"name":name,"platformIDs":["youtube"]})
        state = tool("classifier_state", {"section":"all"})
        group = next(g for g in state["assets"]["classifierTypes"] if g["name"] == name)
        action("saveClassifierTypeLocalModel", {"typeID":group["id"],"speedQuality":"fast","strictness":3,
                                               "houseRules":"","minimumTagsOverride":1,"maximumTagsOverride":1})
        action("setCollectionEnabled", {"platformID":"youtube","enabled":True})
        action("saveClassificationSettings", {"classificationEnabled":True})
        action("setClassifierTypePaused", {"typeID":group["id"],"paused":False})
        for tag, description in [("Gaming","Video games, including Minecraft survival gameplay."),
                                 ("Politics","Government, elections and political policy.")]:
            action("addTag", {"treeID":group["treeID"],"name":tag,"description":description,"positionX":0,"positionY":0})
        taxonomy = hub("classifier-taxonomy", {"platformID":"youtube"})
        tags = next(t["tags"] for t in taxonomy["types"] if t["typeID"] == group["id"])
        check({t["name"] for t in tags} == {"Gaming","Politics"}, "Signed browser receives shared worker taxonomy")
        politics = next(tag for tag in tags if tag["name"] == "Politics")
        block = tool("extension_create_group", {"browser":program,"groupType":"youtube","patch":{
            "name":"Owned pending cover fixture","scopes":[{"surface":"items","platform":"youtube","form":"all",
                "sourceMode":"all","sources":[],"action":"dim","tagFilter":{"mode":"include",
                "tags":[{"name":"Politics","confidence":1}],"defaultConfidence":1,"blockUntagged":False,"coverUntilTagged":True}}]}})
        check(block["group"]["scopes"][0]["tagFilter"]["coverUntilTagged"] is True,
              "Native MCP creates current browser cover-until-tagged policy")
        service_worker.evaluate("() => {self.__windowsProductionPushes=[];const old=self.CBClassifierBroadcastReceive;self.CBClassifierBroadcastReceive=frame=>{self.__windowsProductionPushes.push(JSON.parse(JSON.stringify(frame)));return old(frame);};}")

        page = context.new_page()
        page.route("https://www.youtube.com/**", lambda route: route.fulfill(status=200, content_type="text/html", body='''<!doctype html><html><head><title>Owned Windows browser fixture</title><style>body{font:16px Arial;padding:40px;background:white;color:black}ytd-video-renderer{display:block;width:600px;margin:20px}ytd-thumbnail{display:block;width:400px;height:225px;background:#5577aa}a{display:block;color:#123}h2{margin:8px}span{display:inline}</style></head><body><h1>Owned Windows browser fixture</h1><div id="cards"></div></body></html>'''))
        cdp = context.new_cdp_session(page)
        contexts = {}
        cdp.on("Runtime.executionContextCreated", lambda event: contexts.update({event["context"]["id"]:event["context"]}))
        cdp.on("Runtime.executionContextDestroyed", lambda event: contexts.pop(event["executionContextId"], None))
        cdp.on("Runtime.executionContextsCleared", lambda _: contexts.clear())
        cdp.send("Runtime.enable"); cdp.send("DOM.enable")
        page.goto("https://www.youtube.com/feed/subscriptions", wait_until="domcontentloaded")

        def isolated(expression):
            for context_id, description in list(contexts.items()):
                if description.get("auxData", {}).get("isDefault") is not False:
                    continue
                try:
                    ready = cdp.send("Runtime.evaluate", {"contextId":context_id,"expression":"typeof vaultTagsForCard==='function' && typeof getFeedCardElements==='function' && window.__vaultClassifierYouTube===true","returnByValue":True})
                    if ready.get("result", {}).get("value") is True:
                        answer = cdp.send("Runtime.evaluate", {"contextId":context_id,"expression":expression,"returnByValue":True})
                        if "exceptionDetails" in answer:
                            raise AssertionError(answer["exceptionDetails"])
                        return answer["result"].get("value")
                except Exception as error:
                    if "Cannot find context" not in str(error):
                        raise
            return None

        def card_state(card_id):
            return isolated("(()=>{const card=document.getElementById(" + json.dumps(card_id) + ");return card?{cards:getFeedCardElements('youtube').length,tags:vaultTagsForCard(card),settled:vaultTagsSettledForCard(card),covered:card.getAttribute('data-cb-content-blocked')==='true',panels:card.querySelectorAll('.cb-block-panel').length}:null})()")

        def rendered_chips():
            document = cdp.send("DOM.getDocument", {"depth":-1,"pierce":True})["root"]
            chips = []
            def visit(node):
                attributes = node.get("attributes", [])
                attrs = dict(zip(attributes[::2], attributes[1::2]))
                if "chip" in attrs.get("class", "").split():
                    def text(n):
                        return n.get("nodeValue", "") + "".join(text(child) for child in n.get("children", []))
                    chips.append(text(node))
                for child in node.get("children", []) + node.get("shadowRoots", []):
                    visit(child)
            visit(document)
            return chips

        def add_card(card_id, video_id, creator):
            title = "Minecraft survival gameplay: building a stone castle"
            markup = f'<ytd-video-renderer id="{card_id}"><ytd-thumbnail><a id="thumbnail" href="/watch?v={video_id}">Owned video thumbnail</a></ytd-thumbnail><h2><a id="video-title" href="/watch?v={video_id}">{html.escape(title)}</a></h2><a href="/@{creator}">{creator}</a></ytd-video-renderer>'
            page.locator("#cards").evaluate("(container,markup)=>container.insertAdjacentHTML('beforeend',markup)", markup)
            return {"platformID":"youtube","entryID":"youtube:video:" + video_id,"creatorID":"youtube:handle:@" + creator.lower(),"title":title}

        wait(lambda: isolated("true"))
        started = time.monotonic()
        video = add_card("initial-card", uuid.uuid4().hex[:11], "WindowsFixture" + uuid.uuid4().hex[:8])
        pending = wait(lambda: (s := card_state("initial-card")) and not s["settled"] and s["covered"] and s["panels"] > 0 and s)
        check(pending["cards"] == 1, "Actual collector discovers card; pending thumbnail is covered until tagged")
        final = wait(lambda: (s := card_state("initial-card")) and s["settled"] and [t["name"] for t in s["tags"]] == ["Gaming"] and not s["covered"] and s, 600)
        check(final["panels"] == 0 and "Gaming" in rendered_chips(), "Real Gaming result renders closed-shadow pill and lifts pending cover")
        cached = hub("video-tags", video)
        check(not cached.get("pending") and not cached.get("predicted") and [t["name"] for t in cached["tags"]] == ["Gaming"],
              "Browser receives authoritative cache after actual collector/model path")
        check(any(frame.get("operation") == "video-tags-updated" and any(item.get("entryID") == video["entryID"] for item in frame.get("body", {}).get("items", []))
                  for frame in service_worker.evaluate("() => self.__windowsProductionPushes")), "Production completion broadcasts through authenticated native tunnel")
        corrected = hub("submit-correction", dict(video,typeID=group["id"],correctTagIDs=[politics["id"]]))
        check([t["name"] for t in corrected["tags"]] == ["Politics"], "Browser submits authoritative human correction")
        wait(lambda: (s := card_state("initial-card")) and s["settled"] and s["covered"] and [t["name"] for t in s["tags"]] == ["Politics"] and s)
        check("Politics" in rendered_chips(), "Politics pill updates actual content-block verdict")
        page.screenshot(path=str(evidence / "corrected-card.png"))

        stop_proxy()
        paused = set_app_paused(True)
        check(paused["appPid"] is None, "Owned Windows Vault closes normally for actual native outage")
        wait(lambda: service_worker.evaluate("() => cbConnection.statusForTarget('windowsapp').state !== 'connected'"))
        add_card("outage-card", uuid.uuid4().hex[:11], "OfflineFixture" + uuid.uuid4().hex[:8])
        outage = wait(lambda: (s := card_state("outage-card")) and not s["settled"] and s["covered"] and s)
        check(outage["panels"] > 0, "New card stays covered and unknown while native app is unavailable")
        resumed = set_app_paused(False)
        check(resumed["appPid"] != fixture["appPid"], "Same private Vault state reopens in a fresh owned process")
        wait(lambda: service_worker.evaluate("() => cbConnection.statusForTarget('windowsapp').state === 'connected'"), 60)
        replies = start_proxy()
        recovered = wait(lambda: (s := card_state("outage-card")) and s["settled"] and [t["name"] for t in s["tags"]] == ["Gaming"] and not s["covered"] and s, 600)
        check(recovered["panels"] == 0 and "Gaming" in rendered_chips(), "Actual collector/pills recover after outage and authenticated reconnect")
        page.screenshot(path=str(evidence / "reconnected-card.png"))

        settings = hub("activity-settings", {"settings":{"byCategory":{"web-visit":{"enabled":True},"app-usage":{"enabled":False}}}})
        check(settings["settings"]["byCategory"]["web-visit"]["enabled"], "Browser enables shared Activity category")
        record = {"id":uuid.uuid4().hex,"category":"web-visit","startedAtMs":int(time.time()*1000),"seconds":12,
                  "key":"windows-fixture.example","label":"Windows browser 中文"}
        recorded = hub("activity-record", {"records":[record],"icons":{record["key"]:"data:image/png;base64,iVBORw0KGgo="}})
        check(recorded["stored"] == 1 and hub("activity-record", {"records":[record]})["stored"] == 0, "Actual Activity record stored once across replay")
        forged = dict(record,id=uuid.uuid4().hex,category="app-usage",key=r"C:\Windows\fake.exe")
        check(hub("activity-record", {"records":[forged]})["stored"] == 0, "Browser cannot forge native app usage")
        disabled = hub("activity-settings", {"settings":{"byCategory":{"web-visit":{"enabled":False}}}})
        check(not disabled["settings"]["byCategory"]["web-visit"]["enabled"] and hub("activity-record", {"records":[dict(record,id=uuid.uuid4().hex)]})["stored"] == 0,
              "Disabled Activity category refuses records at native privacy boundary")
        result = {"ok":True,"program":program,"checks":checks,"elapsedSeconds":round(time.monotonic()-started,2)}
        (evidence / "result.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
        print(json.dumps(result,ensure_ascii=False), flush=True)
        print("Evidence: " + str(evidence), flush=True)
    finally:
        if page is not None:
            page.close()
        stop_proxy()
