# -*- coding: utf-8 -*-
"""从 yuno-mod 源码 + zhs 本地化生成卡牌效果表（卡名 | 效果），按类名 A-Z 排序。"""
import json, re, sys
from pathlib import Path

ROOT = Path(r"D:/myProgram/GodotProgram/yuno-mod")
CARDS_DIR = ROOT / "Scripts" / "Cards"
LOC = json.load(open(ROOT / "YunoMod" / "localization" / "zhs" / "cards.json", encoding="utf-8-sig"))
NEW_ROOT = Path(r"D:/myProgram/GodotProgram/SelectSpecialCardMod")
NEW_LOC_KEY_PREFIX = "SELECT_SPECIAL_CARD_MOD_CARD_"

def added_markers() -> tuple[set, set]:
    """已在 SelectSpecialCardMod 中添加的卡：返回 (类名集合, 本地化键集合)"""
    classes, loc_keys = set(), set()
    scripts = NEW_ROOT / "Scripts"
    if scripts.exists():
        for p in scripts.rglob("*.cs"):
            for m in re.finditer(r"class\s+(\w+)", p.read_text(encoding="utf-8-sig")):
                classes.add(m.group(1))
    loc_new = NEW_ROOT / "SelectSpecialCardMod" / "localization" / "zhs" / "cards.json"
    if loc_new.exists():
        for k in json.load(open(loc_new, encoding="utf-8-sig")):
            if k.endswith(".title"):
                loc_keys.add(k[: -len(".title")])
    return classes, loc_keys

ADDED_CLASSES, ADDED_LOC_KEYS = added_markers()

# 新 mod 的本地化：已添加的卡优先显示新 mod 里的实际文本（效果可能与原版不同）
NEW_LOC = {}
_new_loc_file = NEW_ROOT / "SelectSpecialCardMod" / "localization" / "zhs" / "cards.json"
if _new_loc_file.exists():
    NEW_LOC = json.load(open(_new_loc_file, encoding="utf-8-sig"))

# 新 mod 里的卡类源码（已添加的卡以新 mod 源码为准：变量、关键字都可能和原版不同）
new_files = {}
for p in (NEW_ROOT / "Scripts").rglob("*.cs"):
    text = p.read_text(encoding="utf-8-sig")
    for m in re.finditer(r"class\s+(\w+)\s*:", text):
        new_files.setdefault(m.group(1), text)

# 关键字中文名：自定义关键字查 mod 本地化，原版关键字用固定对照
def load_kw_titles(path, prefix):
    try:
        d = json.load(open(path, encoding="utf-8-sig"))
    except FileNotFoundError:
        return {}
    return {k[len(prefix):-len(".title")]: v for k, v in d.items() if k.startswith(prefix) and k.endswith(".title")}

KW_TITLES = {
    **load_kw_titles(ROOT / "YunoMod" / "localization" / "zhs" / "card_keywords.json", "YUNO_MOD_KEYWORD_"),
    **load_kw_titles(NEW_ROOT / "SelectSpecialCardMod" / "localization" / "zhs" / "card_keywords.json", "SELECT_SPECIAL_CARD_MOD_KEYWORD_"),
}
VANILLA_KEYWORDS = {"Exhaust": "消耗", "Retain": "保留", "Ethereal": "虚无", "Unplayable": "无法打出", "Innate": "先天"}

def extract_keywords(text: str) -> list:
    m = re.search(r"CanonicalKeywords\s*=>", text)
    if not m:
        return []
    block = capture_block(text, m.end())
    names = []
    for mm in re.finditer(r"(?:CardKeyword|YunoKeywords|SelectKeywords)\.(\w+)", block):
        n = mm.group(1)
        names.append(KW_TITLES.get(n.upper()) or VANILLA_KEYWORDS.get(n, n))
    return names

def snake(name: str) -> str:
    return re.sub(r"(?<!^)(?=[A-Z])", "_", name).upper()

# ---------- 1. 收集卡牌类 ----------
card_files, card_bases = {}, {}
for p in sorted(CARDS_DIR.rglob("*.cs")):
    text = p.read_text(encoding="utf-8-sig")
    for m in re.finditer(r"class\s+(\w+)\s*:\s*([\w<>.,\s]+)", text):
        cls, base = m.group(1), m.group(2).split(",")[0].strip()
        if not cls.endswith("Card"):
            continue
        if re.search(rf"\babstract\s+class\s+{cls}\b", text):
            continue
        card_files[cls] = text
        card_bases[cls] = base

# 全部类声明（含 Base 目录）用于继承链判断
all_bases = {}
for p in (ROOT / "Scripts").rglob("*.cs"):
    for m in re.finditer(r"class\s+(\w+)\s*:\s*([\w<>.,\s]+)", p.read_text(encoding="utf-8-sig")):
        all_bases.setdefault(m.group(1), m.group(2).split(",")[0].strip())

def is_special_pool(cls: str) -> bool:
    """沿继承链判断是否属于 YunoSpecialCardPool（RegisterCard Inherit=true 挂在 YunoSpecialBaseCard 上）"""
    seen = set()
    while cls and cls not in seen:
        seen.add(cls)
        if cls == "YunoSpecialBaseCard":
            return True
        cls = all_bases.get(cls, "")
    return False

# ---------- 2. 解析 CanonicalVars ----------
def capture_block(text: str, start: int) -> str:
    """从 start 开始按括号配平截取表达式，遇到深度0的';'结束（lambda 内的分号不受影响）"""
    depth = 0
    for i in range(start, len(text)):
        c = text[i]
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        elif c == ";" and depth == 0:
            return text[start:i]
    return text[start:start + 2000]

def parse_num(s: str, consts: dict):
    s = s.strip()
    if s in consts:  # 数值常量
        s = consts[s]
    s = s.rstrip("mMfF")
    try:
        f = float(s)
        return int(f) if f == int(f) else f
    except ValueError:
        return None

def parse_vars(text: str):
    """返回 (vars dict, 是否声明了 CanonicalVars)"""
    m = re.search(r"CanonicalVars\s*=>", text)
    declared = bool(m)
    if not m:
        return {}, declared
    block = capture_block(text, m.end())
    consts_str = dict(re.findall(r"const\s+string\s+(\w+)\s*=\s*\"([^\"]+)\"", text))
    consts_num = dict(re.findall(r"const\s+(?:decimal|int|float|double)\s+(\w+)\s*=\s*([-\d.\w]+)", text))
    vars_ = {}
    for mm in re.finditer(r"new\s+(\w+)(?:<(\w+)>)?\(", block):
        typ, generic = mm.group(1), mm.group(2)
        if "Computed" in typ:  # 运行时计算变量，无静态数值
            continue
        args_start = mm.end()
        args, depth, found = "", 1, False  # 左括号已被正则消耗，深度从1开始
        for i in range(args_start, len(block)):
            if block[i] == "(":
                depth += 1
            elif block[i] == ")":
                depth -= 1
                if depth == 0:
                    args, found = block[args_start:i], True
                    break
        if not found:
            continue
        parts = [a.strip() for a in args.split(",") if a.strip()]
        if typ == "DynamicVar":
            first = parts[0].strip('"') if parts else ""
            key = consts_str.get(first, first)
            val = parse_num(parts[1], consts_num) if len(parts) > 1 else None
        else:
            key = generic if generic else (typ[:-3] if typ.endswith("Var") else typ)
            val = parse_num(parts[0], consts_num) if parts else None
        vars_[key] = val
    return vars_, declared

var_maps = {}
def vars_of(cls, seen=None):
    seen = seen or set()
    if cls in var_maps:
        return var_maps[cls]
    if cls in seen:
        return {}
    seen.add(cls)
    text = new_files.get(cls) or card_files.get(cls)  # 已添加的卡用新 mod 源码
    if text is None:
        return {}
    own, declared = parse_vars(text)
    if not own and not declared:  # 自己没声明 CanonicalVars 才回退到父类
        own = vars_of(card_bases.get(cls, ""), seen)
    var_maps[cls] = own
    return own

# ---------- 3. 清洗描述 ----------
unresolved_report = {}

def clean(text: str, vars_: dict, cls: str) -> str:
    def sub_simple(m):
        key = m.group(1)
        if key in vars_ and vars_[key] is not None:
            return str(vars_[key])
        unresolved_report.setdefault(cls, []).append(key)
        return "X"  # 动态值，与 mod 文本惯例一致
    text = re.sub(r"\[/?[a-zA-Z]+[^\]]*\]", "", text)  # BBCode
    # 简单占位符（排除条件键，条件由 sub_cond 处理）
    text = re.sub(r"\{(?!(?:InCombat|IfUpgraded):)(\w+):[^{}]*\}", sub_simple, text)
    def sub_cond(m):
        key, content = m.group(1), m.group(2)
        a, _, b = content.partition("|")
        if key == "InCombat":  # 战斗中A｜非战斗B
            return a + (f"（非战斗时：{b}）" if b else "")
        # IfUpgraded:show:A|B → 未升级B（升级后A）
        if a.startswith("show:"):
            a = a[len("show:"):]
        return b + (f"（升级后：{a}）" if a else "")
    text = re.sub(r"\{(InCombat|IfUpgraded):((?:[^{}]|\{[^{}]*\})*)\}", sub_cond, text)
    return re.sub(r"\s+", " ", text).strip()

# ---------- 4. 汇总 ----------
only_special = "--special" in sys.argv
rows, missing_loc = [], []
for cls in sorted(card_files):
    if only_special and not is_special_pool(cls):
        continue
    key = "YUNO_MOD_CARD_" + snake(cls)
    new_key = NEW_LOC_KEY_PREFIX + snake(cls)
    title = NEW_LOC.get(new_key + ".title") or LOC.get(key + ".title")
    desc = NEW_LOC.get(new_key + ".description") or LOC.get(key + ".description")
    if title is None or desc is None:
        missing_loc.append(cls)
        continue
    kws = extract_keywords(new_files.get(cls) or card_files[cls])
    added = "✅" if cls in ADDED_CLASSES or (NEW_LOC_KEY_PREFIX + snake(cls)) in ADDED_LOC_KEYS else ""
    rows.append((cls, title, clean(desc, vars_of(cls), cls), "、".join(kws), added))

scope = "Special卡池" if only_special else "全部"
added_count = sum(1 for r in rows if r[4] == "✅")
# 未打勾（未添加）的排在上面，打勾的排在后面；各组内保持类名 A-Z（稳定排序）
rows.sort(key=lambda r: r[4] == "✅")
print(f"[{scope}] 卡牌: {len(rows)}，已添加到 SelectSpecialCardMod: {added_count}，缺本地化: {len(missing_loc)} {missing_loc}")
n = sum(len(v) for v in unresolved_report.values())
print(f"动态值(以X代替) {n} 处：")
for cls, keys in unresolved_report.items():
    print(f"  {cls}: {keys}")

if "--report" in sys.argv:
    sys.exit(0)

# ---------- 5. 输出 ----------
out = Path(r"D:/myProgram/GodotProgram/卡牌效果表.md")
suffix = "_special" if only_special else ""
lines = [f"# yuno-mod {scope}卡牌效果表（未添加在前，组内按类名 A-Z 排序，✅=已添加到 SelectSpecialCardMod）", "",
         "| 卡名 | 效果 | 关键字 | 已添加 |", "| --- | --- | --- | --- |"]
for cls, title, effect, kws, added in rows:
    lines.append(f"| {title} | {effect} | {kws} | {added} |")
out = out if not only_special else out.with_name(out.stem + suffix + out.suffix)
out.write_text("\n".join(lines), encoding="utf-8")
print("已写入:", out, f"（{len(rows)} 行）")
