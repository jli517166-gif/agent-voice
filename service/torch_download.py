"""Resolve exact Windows wheels from the configured official PyTorch index."""
from html.parser import HTMLParser
from pathlib import PurePosixPath
import re
import urllib.parse
import urllib.request


class Links(HTMLParser):
    def __init__(self):
        super().__init__(); self.urls = []
    def handle_starttag(self, tag, attributes):
        if tag == 'a':
            self.urls.extend(v for k,v in attributes if k=='href' and v)


def wheel(index, requirement, python_tag):
    if not re.fullmatch(r'https://download\.pytorch\.org/whl/cu\d+', index):
        raise ValueError('不是已配置的 PyTorch 官方索引')
    match = re.fullmatch(r'(torch|torchaudio|torchvision)==([0-9.]+)', requirement)
    if not match or not re.fullmatch(r'cp3\d+', python_tag):
        raise ValueError('PyTorch 下载要求或 Python 版本无效')
    package, version = match.groups()
    url = index+'/'+package+'/'
    request = urllib.request.Request(url, headers={'User-Agent':'AgentVoice-TorchResolver/1'})
    with urllib.request.urlopen(request, timeout=30) as response:
        text = response.read(8*1024**2).decode('utf-8')
    parser = Links(); parser.feed(text)
    cuda = index.rsplit('/', 1)[1]
    expected = re.compile(re.escape(package+'-'+version+'+'+cuda)+'-'+python_tag+'-'+python_tag+r'-win_amd64\.whl$')
    for link in parser.urls:
        parsed = urllib.parse.urlsplit(urllib.parse.urljoin(url,link))
        filename = urllib.parse.unquote(PurePosixPath(parsed.path).name)
        hash_match = re.fullmatch('sha256=([a-f0-9]{64})',parsed.fragment)
        if expected.fullmatch(filename) and hash_match and parsed.scheme=='https' and (parsed.hostname=='download.pytorch.org' or (parsed.hostname or '').endswith('.pytorch.org')):
            return {'url':urllib.parse.urlunsplit(parsed._replace(fragment='')), 'filename':filename, 'sha256':hash_match[1]}
    raise ValueError('官方索引没有匹配的 Windows x64 轮子：'+requirement+' / '+python_tag)
