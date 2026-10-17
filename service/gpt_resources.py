"""Offline text resources must live inside the selected GPT engine, not a user profile."""
from pathlib import Path

RESOURCES = ('cmudict', 'averaged_perceptron_tagger', 'averaged_perceptron_tagger_eng')

def data_directory(source):
    return Path(source).resolve().parent / 'nltk_data'

def configure(source):
    import nltk
    directory = data_directory(source)
    nltk.data.path.insert(0, str(directory))
    missing = []
    for name in RESOURCES:
        section = 'corpora' if name == 'cmudict' else 'taggers'
        try: nltk.data.find(section + '/' + name,paths=[str(directory)])
        except LookupError:
            try: nltk.data.find(section + '/' + name + '.zip',paths=[str(directory)])
            except LookupError: missing.append(name)
    if missing:
        raise ValueError('GPT 英文发音资源缺失：' + '、'.join(missing) +
                         '；请运行 tools/prepare_gpt_resources.py，将资源安装到 ' + str(directory))
    # Exercise lazy resources too: importing the inference module alone misses these failures.
    from nltk.corpus import cmudict
    if not cmudict.dict(): raise ValueError('GPT 英文发音词典为空')
    nltk.pos_tag(['agent', 'voice'])
    return directory
