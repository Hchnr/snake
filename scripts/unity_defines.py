"""Edit an isolated Unity ProjectSettings copy without touching the workspace."""
import re


def enable_webgl_define(text, symbol):
    if not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*', symbol):
        raise ValueError('Invalid scripting define')
    header = re.search(r'(?m)^  scriptingDefineSymbols:([ \t]*)(\{\})?[ \t]*$', text)
    if not header:
        raise ValueError('Unrecognized scriptingDefineSymbols setting')
    if header.group(2):
        return text[:header.start()] + '  scriptingDefineSymbols:\n    WebGL: ' + symbol + text[header.end():]
    next_setting = re.search(r'(?m)^  [^ \t\r\n]', text[header.end():])
    end = header.end() + next_setting.start() if next_setting else len(text)
    block = text[header.end():end]
    entry = re.search(r'(?m)^    WebGL:[ \t]*([^\r\n]*)$', block)
    if not entry:
        return text[:header.end()] + '\n    WebGL: ' + symbol + text[header.end():]
    raw = entry.group(1).strip()
    quote = raw[0] if raw and raw[0] in "'\"" else ''
    if quote:
        if len(raw) < 2 or raw[-1] != quote:
            raise ValueError('Unrecognized WebGL define list')
        raw = raw[1:-1]
    if not re.fullmatch(r'[A-Za-z0-9_; \t]*', raw):
        raise ValueError('Unrecognized WebGL define list')
    names = [name.strip() for name in raw.split(';') if name.strip()]
    if symbol not in names:
        names.append(symbol)
    value = quote + ';'.join(names) + quote
    replacement = '    WebGL: ' + value
    return text[:header.end()] + block[:entry.start()] + replacement + block[entry.end():] + text[end:]
