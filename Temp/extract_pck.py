import struct, sys, os

PCK = r"D:\steam\steamapps\common\Slay the Spire 2\SlayTheSpire2.pck"
OUT = r"D:\myProgram\GodotProgram\SelectSpecialCardMod\Temp\extracted"
os.makedirs(OUT, exist_ok=True)

WANT = {
    "scenes/combat/exhaust_pile.tscn",
    "images/packed/combat_ui/exhaust_pile.png.import",
    ".godot/imported/exhaust_pile.png-7b117804005b0ad0274f474ce4929104.ctex",
}

f = open(PCK, "rb")
head = f.read(64)
flags, = struct.unpack_from("<i", head, 20)
file_base, = struct.unpack_from("<q", head, 24)
dir_off, = struct.unpack_from("<q", head, 32)
f.seek(dir_off)
count, = struct.unpack_from("<i", f.read(4))
pos = f.tell()

for i in range(count):
    f.seek(pos)
    plen, = struct.unpack_from("<i", f.read(4))
    path = f.read(plen).rstrip(b"\0").decode("utf-8", "replace")
    offset, size = struct.unpack_from("<qq", f.read(16))
    f.read(16)
    f.read(4)
    pos = f.tell()
    if path in WANT:
        keep = f.tell()
        f.seek(file_base + offset)
        blob = f.read(size)
        name = path.replace("/", "__").replace(":", "_").replace("..", "")
        with open(os.path.join(OUT, name), "wb") as o:
            o.write(blob)
        print(f"extracted {path} ({size} bytes)")
        f.seek(keep)
