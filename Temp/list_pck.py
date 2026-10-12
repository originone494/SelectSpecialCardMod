import struct, sys

PCK = r"D:\steam\steamapps\common\Slay the Spire 2\SlayTheSpire2.pck"

f = open(PCK, "rb")
head = f.read(64)
magic, pver, vmaj, vmin, vpat = struct.unpack_from("<5i", head, 0)
flags, = struct.unpack_from("<i", head, 20)
file_base, = struct.unpack_from("<q", head, 24)
dir_off, = struct.unpack_from("<q", head, 32)
print(f"pck v{pver} engine {vmaj}.{vmin}.{vpat} flags={flags} file_base={file_base} dir_off={dir_off}")

f.seek(dir_off)
count, = struct.unpack_from("<i", f.read(4))
print("entry count:", count)

paths = []
for _ in range(count):
    plen, = struct.unpack_from("<i", f.read(4))
    path = f.read(plen).rstrip(b"\0").decode("utf-8", "replace")
    offset, size = struct.unpack_from("<qq", f.read(16))
    f.read(16)  # md5
    f.read(4)   # flags
    paths.append((path, offset, size))

print("parsed ok:", len(paths))
kw = [p for p in paths if "exhaust" in p[0].lower()]
print(f"--- {len(kw)} entries matching 'exhaust' ---")
for p, o, s in kw:
    print(f"{p}  (size={s})")
