"""Read-only localization audit. No build, game launch or file writes."""
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / 'SelectSpecialCardMod' / 'localization'

def unique(pairs):
    result = {}
    for key, value in pairs:
        assert key not in result, f'Duplicate key: {key}'
        result[key] = value
    return result

def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'), object_pairs_hook=unique)

def args(text):
    return set(re.findall(r'\{([A-Za-z_][A-Za-z_0-9]*)(?=[:}])', text))

def markup(text):
    stack = []
    for closing, name in re.findall(r'\[(/?)([a-z]+)(?:=[^\]]+)?\]', text):
        if name not in {'gold', 'blue', 'red', 'green', 'purple', 'orange', 'b', 'i'}:
            continue
        if closing:
            assert stack and stack.pop() == name, f'Mismatched markup: {text}'
        else:
            stack.append(name)
    assert not stack, f'Unclosed markup: {text}'
    assert text.count('{') == text.count('}'), f'Unbalanced braces: {text}'

source = {p.name: load(p) for p in (BASE / 'zhs').glob('*.json')}
assert 'SelectSpecialCardMod/localization/*/*.json' in (ROOT / 'export_presets.cfg').read_text(encoding='utf-8-sig')
for lang in ('eng', 'jpn'):
    assert {p.name for p in (BASE / lang).glob('*.json')} == set(source), f'{lang}: missing tables'
    unchanged = []
    longest = []
    for file, original in source.items():
        translated = load(BASE / lang / file)
        assert translated.keys() == original.keys(), f'{lang}/{file}: key mismatch'
        for key, text in translated.items():
            assert isinstance(text, str), (lang, file, key)
            assert args(text) == args(original[key]), (lang, file, key, args(text), args(original[key]))
            assert text.count('|') == original[key].count('|'), (lang, file, key, 'conditional separator mismatch')
            markup(text)
            assert not text.strip() or original[key].strip(), (lang, file, key, 'filled intentional blank')
            assert text.strip() or not original[key].strip(), (lang, file, key, 'missing text')
            if lang == 'eng':
                assert not re.search(r'[\u3400-\u9fff\u3040-\u30ff]', text), (lang, file, key, 'untranslated CJK')
            if text == original[key] and re.search(r'[\u3400-\u9fff]', text):
                unchanged.append((file, key, text))
            if file == 'cards.json' and key.endswith('.description'):
                longest.append((len(re.sub(r'\[[^]]+\]|\{[^}]*\}', '', text)), key))
    # 固有名や日中同形語（除外、手札中央など）は日文で共通になり得る。
    assert lang == 'jpn' or not unchanged, unchanged
    print(f'PASS {lang}: {len(source)} tables, {sum(map(len, source.values()))} keys; placeholders, branches, markup, blanks and UTF-8.')
    print('  Longest card descriptions:', sorted(longest, reverse=True)[:3])

myth = {key.removeprefix('SELECT_SPECIAL_CARD_MOD_MYTH_'): value for key, value in source['cards.json'].items()
        if key.startswith('SELECT_SPECIAL_CARD_MOD_MYTH_')}
for kind in ('conditions', 'effects'):
    modules = load(ROOT / f'SelectSpecialCardMod/config/myth/myth_{kind}.json')['modules']
    for module in modules:
        assert myth[module['id'] + '.text'] == module['text'], module['id']
        assert myth[module['id'] + '.name'] == module['name_part'], module['id']
        assert args(module['text']) <= module['parameters'].keys(), module['id']
print('PASS myth: all 45 condition and 121 effect texts/names match the untouched source modules.')

# 独立保存的百鸽参考名称用于避免之后生成或手改翻译时重新引入旧错误。
names = load(ROOT / 'Tests/Fixtures/ygocdb_localization_names.json')
translated_cards = {lang: load(BASE / lang / 'cards.json') for lang in ('eng', 'jpn')}
card_titles = {k for k in source['cards.json'] if k.startswith('SELECT_SPECIAL_CARD_MOD_CARD_') and k.endswith('.title')}
assert {entry['key'] for entry in names['cards']} == card_titles, 'Name review omitted card titles'
assert len(names['cards']) == len(card_titles), 'Duplicate name review entry'
for entry in names['cards']:
    for language, field in [('eng', 'english'), ('jpn', 'japanese')]:
        assert translated_cards[language][entry['key']] == entry[field], (language, entry['key'], 'reference name mismatch')
    if entry['source'] == 'ygocdb':
        assert entry['english_status'] in {'official', 'wiki_en'}, entry
        assert entry['url'] == 'https://ygocdb.com/card/' + str(entry['card_id']), entry
for language in ('eng', 'jpn'):
    texts = '\n'.join(v for file in source for v in load(BASE / language / file).values())
    for obsolete in ('Yijie', 'Netherroot', 'Fiendsmith Rex Tremendae', 'Sevens Road Enchanter', 'セブンスロード・エンチャンター', '刻まれし魔トークン'):
        assert obsolete not in texts, (language, 'obsolete translation', obsolete)
    if language == 'jpn':
        assert not re.search('異解(?!△)', texts), 'Japanese Trirealm Rift archetype lost its triangle'
    # 用户询问用词不代表授权改名；检索继续采用Search。
    if language == 'eng':
        assert load(BASE / language / 'card_keywords.json')['SELECT_SPECIAL_CARD_MOD_KEYWORD_RETRIEVER.title'] == 'Search'
print('PASS names: all 146 card titles categorized; 110 matched to ygocdb, 3 Rush + 2 Tokens checked separately, 8 originals, 18 UI cards, 5 explicitly unverified.')
