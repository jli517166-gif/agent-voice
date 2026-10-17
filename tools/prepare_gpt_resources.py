"""Run with the GPT Python environment; installs public NLTK resources locally."""
import argparse,json,sys,hashlib,io,urllib.request,xml.etree.ElementTree as ET,zipfile,os
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from gpt_resources import RESOURCES,data_directory,configure

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--source',required=True);parser.add_argument('--check',action='store_true');args=parser.parse_args()
    directory=data_directory(args.source)
    if not args.check:
        directory.mkdir(parents=True,exist_ok=True)
        base='https://raw.githubusercontent.com/nltk/nltk_data/gh-pages/'
        with urllib.request.urlopen(base+'index.xml',timeout=60) as response:
            packages={p.attrib['id']:p.attrib for p in ET.fromstring(response.read(4*1024**2)).iter('package')}
        for name in RESOURCES:
            section='corpora' if name=='cmudict' else 'taggers'
            with urllib.request.urlopen(base+'packages/'+section+'/'+name+'.zip',timeout=60) as response:
                data=response.read(16*1024**2+1)
            if len(data)>16*1024**2 or hashlib.sha256(data).hexdigest()!=packages[name]['sha256_checksum']:
                raise RuntimeError('资源校验失败：'+name)
            output=directory/section;output.mkdir(parents=True,exist_ok=True)
            with zipfile.ZipFile(io.BytesIO(data)) as archive:
                for member in archive.infolist():
                    path=(output/member.filename).resolve()
                    if output.resolve() not in path.parents or member.file_size>64*1024**2:
                        raise ValueError('不安全的资源目录')
                archive.extractall(output)
            (output/(name+'.zip')).write_bytes(data)
    configure(args.source)
    print(json.dumps({'ok':True,'directory':str(directory),'resources':RESOURCES},ensure_ascii=False))

if __name__=='__main__':main()
