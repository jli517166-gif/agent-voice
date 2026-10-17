"""Shared text sanitizer: bodies only, never reference prompts or tool output."""
import re

def sanitize(text):
    if not isinstance(text,str):
        raise ValueError("朗读正文必须是文字")
    out=re.sub(r"(?s)(`{3,}|~{3,}).*?\1", " ", text)
    out=re.sub(r"(?s)(`{3,}|~{3,}).*\Z", " ", out)
    out=re.sub(r"`([^`\n]+)`",lambda m:m.group(1) if re.search(r"[\u4e00-\u9fff]",m.group(1)) and not re.search(r"[(){};=]",m.group(1)) else " ",out)
    out=re.sub(r"(?<![A-Za-z0-9_])[A-Za-z_][A-Za-z0-9_]*\([^()\n]*\)"," ",out)
    out=re.sub(r"!?\[([^\]]*)\]\((?:<[^>]*>|[^)]*)\)",lambda m:m.group(1),out)
    out=re.sub(r"[（(]\s*(?:[a-z][a-z0-9+.\-]*://|www\.)[^）)]*[）)]"," ",out,flags=re.I)
    out=re.sub(r"(?:[a-z][a-z0-9+.\-]*://|www\.)[^\s，。！？；）)]+"," ",out,flags=re.I)
    out=re.sub(r"\b(?:localhost|0\.0\.0\.0|(?:\d{1,3}\.){3}\d{1,3})(?::\d+)?(?:/[^\s，。！？；）)]*)?"," ",out,flags=re.I)
    out=re.sub(r"\b[\w\-]+(?:\.[\w\-]+)*\.(?:com|cn|net|org|io|dev|ai|app|me|co|top|xyz|info|edu|gov|tv|cc|site|online|store|link|wiki|tech|space|live|pro|club|vip|shop|work|art|blog|page|fun)\b(?:/[^\s，。！？；）)]*)?"," ",out,flags=re.I)
    out=re.sub(r"[A-Za-z][：:][\\/][^\s，。；：、）)\]】\"'|<>]+|\\\\[^\s，。；：、）)】\"'|]+"," ",out)
    out=re.sub(r"<[^>\n]*>"," ",out)
    out=re.sub(r"[\w\u4e00-\u9fff\-]+\.(?:png|jpe?g|gif|webp|bmp|ico|zip|rar|7z|exe|msi|bat|cmd|ps1|py|pyc|js|mjs|ts|json|ya?ml|md|txt|log|wav|mp3|flac|m4a|ogg|pth|index|dll|so|bin|lnk)\b"," ",out,flags=re.I)
    out=re.sub(r"\S*\\\S*|(?<![A-Za-z0-9_])[a-z][a-z0-9]*(?:_[a-z0-9]+)+(?![A-Za-z0-9_])|\b[A-Fa-f0-9]{8,}\b|\b[A-Za-z0-9]{18,}\b|\b[a-z]+[A-Z][A-Za-z0-9]{4,}\b"," ",out)
    out=re.sub(r"(?m)^\s*(?:#{1,6}\s*|[-*+]\s+|>\s*)","",out)
    out=re.sub(r"[ \t]{2,}"," ",out)
    out=re.sub(r"\n{2,}","\n",out).strip(" \t，、；:：-—\n")
    return out if re.search(r"[^\W_]",out,re.UNICODE) else ""

def playback_chunks(text):
    """Split only after complete sentences so progressive speech stays coherent."""
    if not text:
        return []
    chunks=[];start=0;index=0
    while index<len(text):
        if text[index] in "。！？!?\n":
            end=index+1
            while end<len(text) and text[end] in " \t\r\n":end+=1
            chunks.append(text[start:end]);start=end;index=end
        else:index+=1
    if start<len(text):chunks.append(text[start:])
    return [chunk for chunk in chunks if chunk]

def generation_chunks(text,limit=220):
    """Preserve sentence boundaries normally; bound pathological unpunctuated input."""
    result=[]
    for sentence in playback_chunks(text):
        while len(sentence)>limit:
            end=max(sentence.rfind(c,0,limit) for c in '，,；;、 ')+1
            if end<limit//2:end=limit
            result.append(sentence[:end]);sentence=sentence[end:]
        if sentence:result.append(sentence)
    return result
