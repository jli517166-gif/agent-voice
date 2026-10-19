from pathlib import Path
import shutil
root=Path(__file__).resolve().parent
html=(root/'base-template.html').read_text(encoding='utf-8')
html=html.replace('<title>Agent Voice · 声音终端 · 设计审核</title>','<title>Agent Voice · 黑白声场设计审核</title>')
html=html.replace('</head>','<link rel="stylesheet" href="art-direction.css"></head>')
html=html.replace("history.replaceState(null,'',`#setup-${step}`)}","history.replaceState(null,'',`#setup-${step}`);window.dispatchEvent(new Event('design-route'))}")
html=html.replace("$('#jump').value='desk';history.replaceState","$('#jump').value='desk';window.dispatchEvent(new Event('design-route'));history.replaceState")
html=html.replace('function clearTransient(){',"function clearTransient(){window.dispatchEvent(new Event('design-dispose'));")
html=html.replace('<button id="references">参考与设计说明</button>','<button id="airiConcept">AIRI 构图草图</button><button id="references">素材与设计说明</button>')
html=html.replace('</script></body>','</script><script type="module" src="art-direction.js"></script></body>')
html=html.replace('href="设计说明.md"','href="设计与素材说明.md"')
(root/'index.html').write_text(html,encoding='utf-8')
