import re, html
s = open("/tmp/atlas.html", encoding="utf-8").read()
rows = re.findall(r"<tr[^>]*>(.*?)</tr>", s, re.S)
for r in rows:
    cells = re.findall(r"<td[^>]*>(.*?)</td>", r, re.S)
    if not cells:
        continue
    def txt(c):
        t = re.sub(r"<svg.*?</svg>", lambda m: "[icon:" + ("check" if "heck" in m.group(0) else "x") + "]", c, flags=re.S)
        t = re.sub(r"<[^>]+>", "", t)
        return html.unescape(t).strip()
    vals = [txt(c) for c in cells]
    if vals and vals[0] in ("westus", "westus2", "westus3", "eastus2", "centralus", "eastus"):
        print(vals[:7])
        # show raw of free-support cell for icon inspection
        print("   raw free cell:", re.sub(r"\s+", " ", cells[3])[:300] if len(cells) > 3 else "")
