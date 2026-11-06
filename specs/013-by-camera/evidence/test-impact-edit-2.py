#!/usr/bin/env python3
"""013 parent stated change after Batch D, test-impact (C): append the three camera page types to
the IX.4 measured layout test. The existing five rows stay; every page is compared on every
measure at every width, with no exception. Count-asserting; writes nothing on a mismatch.
    python3 specs/013-by-camera/evidence/test-impact-edit-2.py | tee specs/013-by-camera/evidence/test-impact-edit-2.txt
"""
import os, pathlib, sys

F = pathlib.Path("tests/Gallery.Web.Tests/LayoutConsistencyBrowserTests.cs")
OLD = '''            ("404", "/g/zzzzzzzzzzzzzzzzzzzzzzzzzz/none"),
        };'''
NEW = '''            ("404", "/g/zzzzzzzzzzzzzzzzzzzzzzzzzz/none"),
            // 013 FR-013 (test-impact C): the camera pages share the same chrome.
            ("cameras", "/c"),
            ("camera", await site.CameraPathAsync()),
            ("camera-photo", await site.CameraPhotoPathAsync(0)),
        };'''

text = F.read_text(encoding="utf-8")
n = text.count(OLD)
if n != 1:
    print(f"FAIL {F}: anchor occurs {n}x, expected 1")
    sys.exit(1)
print(f"OK C {F.name} x1: page list + cameras, camera, camera-photo")
if os.environ.get("DRYRUN"):
    print("dry run: nothing written"); sys.exit(0)
F.write_text(text.replace(OLD, NEW), encoding="utf-8")
print("wrote 1 file")
