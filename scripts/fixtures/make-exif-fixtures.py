#!/usr/bin/env python3
"""
Creates three JPEG fixtures for T026/T028 testing (stdlib only).

outputs:
  tests/fixtures/orientation-6.jpg   400x200 JPEG, EXIF Orientation=6
  tests/fixtures/gps-camera.jpg      400x200 JPEG, EXIF Make/Model/GPS
  tests/fixtures/not-a-jpeg.jpg      64 bytes PNG header (not a JPEG)
  tests/fixtures/camera-subifd.jpg   400x200 JPEG, Make/Model/LensModel in the Exif SubIFD only
"""
import os
import struct
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FIXTURES = os.path.join(ROOT, "tests", "fixtures")
os.makedirs(FIXTURES, exist_ok=True)


# ---------------------------------------------------------------------------
# JPEG / TIFF helpers
# ---------------------------------------------------------------------------

def strip_app_segments(jpeg_bytes):
    """Return the JPEG with all APP0–APPf segments removed (keep SOI + rest)."""
    assert jpeg_bytes[:2] == b"\xff\xd8", "not a JPEG"
    out = bytearray(b"\xff\xd8")
    pos = 2
    while pos < len(jpeg_bytes):
        if jpeg_bytes[pos] != 0xff:
            # raw scan data or remainder - copy as-is
            out += jpeg_bytes[pos:]
            break
        marker = jpeg_bytes[pos + 1]
        if marker == 0xd9:  # EOI
            out += jpeg_bytes[pos:]
            break
        if marker == 0xda:  # SOS - start of scan, copy rest verbatim
            out += jpeg_bytes[pos:]
            break
        if 0xe0 <= marker <= 0xef:  # APP0..APPf - skip
            length = struct.unpack_from(">H", jpeg_bytes, pos + 2)[0]
            pos += 2 + length
            continue
        # any other segment: copy
        if pos + 3 >= len(jpeg_bytes):
            out += jpeg_bytes[pos:]
            break
        length = struct.unpack_from(">H", jpeg_bytes, pos + 2)[0]
        out += jpeg_bytes[pos: pos + 2 + length]
        pos += 2 + length
    return bytes(out)


def inject_app1(jpeg_bytes, tiff_data):
    """Insert APP1 EXIF right after SOI (before any other segment)."""
    assert jpeg_bytes[:2] == b"\xff\xd8", "not a JPEG"
    exif_payload = b"Exif\x00\x00" + tiff_data
    length = len(exif_payload) + 2  # includes the length field itself
    app1 = b"\xff\xe1" + struct.pack(">H", length) + exif_payload
    return b"\xff\xd8" + app1 + jpeg_bytes[2:]


# ---------------------------------------------------------------------------
# TIFF builder (big-endian)
# ---------------------------------------------------------------------------

def pack_be_ifd_entry(tag, type_, count, value_or_offset):
    """Pack a 12-byte IFD entry. value_or_offset is 4 raw bytes."""
    return struct.pack(">HHI", tag, type_, count) + value_or_offset


def build_tiff_orientation6():
    """Big-endian TIFF: IFD0 with one entry Orientation=6 (SHORT)."""
    # Layout:
    #   0: header  (8 bytes)
    #   8: IFD0: count=1, 1 entry (12 bytes), next=0 (4 bytes)

    header = b"MM\x00\x2a\x00\x00\x00\x08"

    # Orientation tag=0x0112, type=SHORT(3), count=1, value=6
    # SHORT value packed in the first 2 bytes of the 4-byte value field (big-endian)
    entry = pack_be_ifd_entry(0x0112, 3, 1, struct.pack(">HH", 6, 0))
    ifd0 = struct.pack(">H", 1) + entry + b"\x00\x00\x00\x00"

    return header + ifd0


def build_tiff_gps_camera():
    """
    Big-endian TIFF:
      IFD0:  Make="TESTCAM", Model="T1", GPSInfo -> GPS IFD
      GPS IFD: GPSLatitudeRef="N", GPSLatitude=37/1 46/1 0/1,
               GPSLongitudeRef="W", GPSLongitude=122/1 25/1 0/1
    """
    # We compute offsets top-down:
    #   0..7:   header (8 bytes)
    #   8..9:   IFD0 entry count (2 bytes)
    #  10..45:  IFD0 entries 3 × 12 = 36 bytes
    #  46..49:  IFD0 next-IFD (4 bytes)
    #  50..57:  Make data "TESTCAM\0" (8 bytes)
    #  58..59:  GPS IFD count (2 bytes)
    #  60..107: GPS IFD entries 4 × 12 = 48 bytes
    # 108..111: GPS IFD next-IFD (4 bytes)
    # 112..135: GPSLatitude data (3 rationals × 8 = 24 bytes)
    # 136..159: GPSLongitude data (24 bytes)

    make_data_offset = 50
    gps_ifd_offset   = 58
    gps_lat_offset   = 112   # 58 + 2 + 48 + 4 = 112
    gps_lon_offset   = 136   # 112 + 24

    header = b"MM\x00\x2a\x00\x00\x00\x08"

    # IFD0 entries
    # Make: tag=0x010F, ASCII(2), count=8, offset=make_data_offset
    e_make  = pack_be_ifd_entry(0x010F, 2, 8, struct.pack(">I", make_data_offset))
    # Model: tag=0x0110, ASCII(2), count=3, in-place "T1\0\0"
    e_model = pack_be_ifd_entry(0x0110, 2, 3, b"T1\x00\x00")
    # GPSInfo: tag=0x8825, LONG(4), count=1, offset=gps_ifd_offset
    e_gpsi  = pack_be_ifd_entry(0x8825, 4, 1, struct.pack(">I", gps_ifd_offset))

    ifd0 = (struct.pack(">H", 3)
            + e_make + e_model + e_gpsi
            + b"\x00\x00\x00\x00")

    make_data = b"TESTCAM\x00"

    # GPS IFD entries
    # GPSLatitudeRef:  tag=0x0001, ASCII, count=2, in-place "N\0\0\0"
    g_latref = pack_be_ifd_entry(0x0001, 2, 2, b"N\x00\x00\x00")
    # GPSLatitude:     tag=0x0002, RATIONAL(5), count=3, offset
    g_lat    = pack_be_ifd_entry(0x0002, 5, 3, struct.pack(">I", gps_lat_offset))
    # GPSLongitudeRef: tag=0x0003, ASCII, count=2, in-place "W\0\0\0"
    g_lonref = pack_be_ifd_entry(0x0003, 2, 2, b"W\x00\x00\x00")
    # GPSLongitude:    tag=0x0004, RATIONAL(5), count=3, offset
    g_lon    = pack_be_ifd_entry(0x0004, 5, 3, struct.pack(">I", gps_lon_offset))

    gps_ifd = (struct.pack(">H", 4)
               + g_latref + g_lat + g_lonref + g_lon
               + b"\x00\x00\x00\x00")

    def rational(num, den):
        return struct.pack(">II", num, den)

    lat_data = rational(37, 1) + rational(46, 1) + rational(0, 1)
    lon_data = rational(122, 1) + rational(25, 1) + rational(0, 1)

    return header + ifd0 + make_data + gps_ifd + lat_data + lon_data


def build_tiff_camera_subifd():
    """
    Big-endian TIFF shaped like the owner's Leica/Fujifilm exports: IFD0 holds ONLY the ExifOffset
    pointer; Make, Model and LensModel live in the Exif SubIFD.
      IFD0:    ExifOffset (0x8769) -> SubIFD
      SubIFD:  Make="SUBCAM", Model="S2 PRO", LensModel="SUBLENS 35"
    """
    #   0..7:   header
    #   8..9:   IFD0 count
    #  10..21:  IFD0 entry (ExifOffset)
    #  22..25:  IFD0 next
    #  26..27:  SubIFD count
    #  28..63:  SubIFD entries 3 x 12
    #  64..67:  SubIFD next
    #  68..74:  "SUBCAM\0"      (7 bytes)
    #  75..81:  "S2 PRO\0"      (7 bytes)
    #  82..92:  "SUBLENS 35\0"  (11 bytes)
    header = b"MM\x00\x2a\x00\x00\x00\x08"
    sub_off, make_off, model_off, lens_off = 26, 68, 75, 82
    ifd0 = (struct.pack(">H", 1)
            + pack_be_ifd_entry(0x8769, 4, 1, struct.pack(">I", sub_off))
            + b"\x00\x00\x00\x00")
    sub = (struct.pack(">H", 3)
           + pack_be_ifd_entry(0x010F, 2, 7, struct.pack(">I", make_off))
           + pack_be_ifd_entry(0x0110, 2, 7, struct.pack(">I", model_off))
           + pack_be_ifd_entry(0xA434, 2, 11, struct.pack(">I", lens_off))
           + b"\x00\x00\x00\x00")
    data = b"SUBCAM\x00" + b"S2 PRO\x00" + b"SUBLENS 35\x00"
    tiff = header + ifd0 + sub + data
    assert len(header + ifd0) == sub_off and len(header + ifd0 + sub) == make_off
    return tiff


# ---------------------------------------------------------------------------
# Build base JPEG (400×200, red with blue box in top-left)
# ---------------------------------------------------------------------------

def make_base_jpeg(tmp_path):
    cmd = [
        "ffmpeg", "-hide_banner", "-loglevel", "error",
        "-f", "lavfi",
        "-i", "color=c=red:s=400x200,drawbox=x=0:y=0:w=100:h=200:c=blue:t=fill",
        "-frames:v", "1",
        "-y", tmp_path,
    ]
    result = subprocess.run(cmd, capture_output=True)
    if result.returncode != 0:
        print("ffmpeg error:", result.stderr.decode(), file=sys.stderr)
        sys.exit(1)
    with open(tmp_path, "rb") as f:
        return f.read()


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main():
    import tempfile

    with tempfile.TemporaryDirectory() as tmp:
        base_path = os.path.join(tmp, "base.jpg")
        print("Building base JPEG (400×200 red+blue)...")
        base_jpeg = make_base_jpeg(base_path)
        base_clean = strip_app_segments(base_jpeg)

        # 1. orientation-6.jpg
        tiff_o6 = build_tiff_orientation6()
        out_o6 = inject_app1(base_clean, tiff_o6)
        path_o6 = os.path.join(FIXTURES, "orientation-6.jpg")
        with open(path_o6, "wb") as f:
            f.write(out_o6)
        size_o6 = len(out_o6)
        print(f"  orientation-6.jpg: {size_o6} bytes")
        assert size_o6 < 20 * 1024, f"too large: {size_o6}"

        # 2. gps-camera.jpg
        tiff_gps = build_tiff_gps_camera()
        out_gps = inject_app1(base_clean, tiff_gps)
        path_gps = os.path.join(FIXTURES, "gps-camera.jpg")
        with open(path_gps, "wb") as f:
            f.write(out_gps)
        size_gps = len(out_gps)
        print(f"  gps-camera.jpg:    {size_gps} bytes")
        assert size_gps < 20 * 1024, f"too large: {size_gps}"

        # 4. camera-subifd.jpg: Make/Model in the Exif SubIFD, as real camera exports do
        out_sub = inject_app1(base_clean, build_tiff_camera_subifd())
        with open(os.path.join(FIXTURES, "camera-subifd.jpg"), "wb") as f:
            f.write(out_sub)
        print(f"  camera-subifd.jpg: {len(out_sub)} bytes")
        assert len(out_sub) < 20 * 1024

        # 3. not-a-jpeg.jpg: 64-byte PNG signature + padding
        png_sig = b"\x89PNG\r\n\x1a\n"
        not_jpeg = png_sig + b"\x00" * (64 - len(png_sig))
        assert len(not_jpeg) == 64
        path_nj = os.path.join(FIXTURES, "not-a-jpeg.jpg")
        with open(path_nj, "wb") as f:
            f.write(not_jpeg)
        print(f"  not-a-jpeg.jpg:    {len(not_jpeg)} bytes")

    print(f"\nAll fixtures written to {FIXTURES}")


if __name__ == "__main__":
    main()
