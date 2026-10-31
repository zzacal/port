"""Hover-then-click cache check over raw CDP (feature 006 owner report).

Launches a throwaway headless Chrome profile, opens a gallery page, fires pointerenter on a tile,
waits for the hover fetch to finish, clicks through to the photo page, and records every
img-dev request with whether the browser served it from its HTTP cache. PASS when the photo page's
main image (the <picture> currentSrc) was fetched during hover and NOT fetched again on the network
after the click. Also checks that the neighbour images the photo page warms are the ones the
neighbour pages then use.

Run: uv run --with websocket-client python specs/006-public-site-local/evidence/hover-cache-check.py [base]
"""
import json, subprocess, sys, tempfile, time, urllib.request
import websocket

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5090"
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
PORT = 9333

profile = tempfile.mkdtemp(prefix="hover-cache-")
chrome = subprocess.Popen([CHROME, "--headless=new", f"--remote-debugging-port={PORT}", f"--user-data-dir={profile}",
                           "--no-first-run", "--window-size=1440,900", "about:blank"],
                          stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
try:
    for _ in range(50):
        try:
            tabs = json.load(urllib.request.urlopen(f"http://127.0.0.1:{PORT}/json"))
            page = next(t for t in tabs if t["type"] == "page"); break
        except Exception:
            time.sleep(0.2)
    ws = websocket.create_connection(page["webSocketDebuggerUrl"], timeout=30)
    seq = [0]; events = []

    def call(method, **params):
        seq[0] += 1; my = seq[0]
        ws.send(json.dumps({"id": my, "method": method, "params": params}))
        while True:
            m = json.loads(ws.recv())
            if m.get("id") == my: return m.get("result", {})
            events.append(m)

    def pump(seconds):
        end = time.time() + seconds; ws.settimeout(0.2)
        while time.time() < end:
            try: events.append(json.loads(ws.recv()))
            except websocket.WebSocketTimeoutException: pass
        ws.settimeout(30)

    def ev(expr):
        return call("Runtime.evaluate", expression=expr, awaitPromise=True, returnByValue=True)["result"].get("value")

    def phase():
        reqs, cached, done = {}, set(), set()
        for m in events:
            p = m.get("params", {})
            if m.get("method") == "Network.requestWillBeSent" and "img-dev" in p["request"]["url"]:
                reqs[p["requestId"]] = p["request"]["url"].rsplit("/", 1)[-1]
            elif m.get("method") == "Network.requestServedFromCache": cached.add(p["requestId"])
            elif m.get("method") == "Network.responseReceived" and p["response"].get("fromDiskCache"): cached.add(p["requestId"])
            elif m.get("method") == "Network.loadingFinished": done.add(p["requestId"])
        events.clear()
        return [(f, "cache" if r in cached else "network", "done" if r in done else "pending") for r, f in reqs.items()]

    def go(url):
        call("Page.navigate", url=url); pump(4)

    call("Network.enable"); call("Page.enable")
    go(BASE + "/")
    gallery = ev("document.querySelector('a.tile[href^=\"/g/\"]').getAttribute('href')")
    go(BASE + gallery); phase()

    results = []
    for idx in (7, 12):
        go(BASE + gallery); phase()
        href = ev(f"(()=>{{const t=document.querySelectorAll('a.tile[data-preload-srcset]')[{idx}]; t.dispatchEvent(new PointerEvent('pointerenter',{{bubbles:true}})); return t.getAttribute('href')}})()")
        pump(4); hover = phase()
        ev(f"document.querySelector('a.tile[href=\"{href}\"]').click()"); pump(4)
        after = phase()
        main = ev("document.querySelector('article.solo picture img').currentSrc").rsplit("/", 1)[-1]
        hovered = any(f == main and how == "network" for f, how, _ in hover)
        refetched = [x for x in after if x[0] == main and x[1] == "network"]
        results.append((idx, main, hovered, refetched, hover, after))
        print(f"tile {idx}: main={main}  fetched-on-hover={hovered}  network-refetch-after-click={len(refetched)}")
        print(f"   hover: {hover}")
        print(f"   click: {after}")

    # Neighbour warm-up: photo page warms prev/next; the next page's main must not hit the network again.
    warmed = [f for f, how, _ in after if how == "network"]
    nxt = ev("[...document.querySelectorAll('a')].find(a=>/next/i.test(a.textContent))?.getAttribute('href')")
    ev(f"document.querySelector('a[href=\"{nxt}\"]').click()"); pump(4)
    after_next = phase()
    nmain = ev("document.querySelector('article.solo picture img').currentSrc").rsplit("/", 1)[-1]
    nrefetch = [x for x in after_next if x[0] == nmain and x[1] == "network"]
    print(f"next: main={nmain} warmed-before-click={nmain in warmed} network-refetch={len(nrefetch)}")
    print(f"   click: {after_next}")

    ok = all(h and not r for _, _, h, r, _, _ in results) and nmain in warmed and not nrefetch
    print("PASS hover/neighbour warm-ups are reused, no second download" if ok else "FAIL")
    sys.exit(0 if ok else 1)
finally:
    chrome.terminate()
