from pathlib import Path
import re
import hashlib
import json
root=Path(__file__).resolve().parent
html=(root/'base-template.html').read_text(encoding='utf-8')
# Keep icon definitions distinct from interactive controls (back/replay).
html=re.sub(r'(<symbol\s+id=")([^"]+)',lambda m:m[1]+'av-symbol-'+m[2],html)
html=re.sub(r'(<use\s+href="#)([^"]+)',lambda m:m[1]+'av-symbol-'+m[2],html)
html=html.replace('<title>Agent Voice · 声音终端 · 设计审核</title>','<title>Agent Voice · 黑白声场设计审核</title>')
html=html.replace('</head>','<link rel="preload" as="image" href="assets/headphone.webp"><link rel="preload" as="image" href="assets/music.webp"><link rel="stylesheet" href="art-direction.css"><link rel="stylesheet" href="workspace.css"><link rel="stylesheet" href="reference-glass.css"><link rel="stylesheet" href="page-compositions.css"><link rel="stylesheet" href="scene-materials.css"><link rel="stylesheet" href="render-lifecycle.css"></head>')
html=html.replace("history.replaceState(null,'',`#setup-${step}`)}","history.replaceState(null,'',`#setup-${step}`);window.dispatchEvent(new Event('design-route'))}")
html=html.replace("$('#jump').value='desk';history.replaceState","$('#jump').value='desk';window.dispatchEvent(new Event('design-route'));history.replaceState")
html=html.replace('function clearTransient(){',"function clearTransient(){window.dispatchEvent(new Event('design-dispose'));")
html=html.replace("if(action==='engine'){engine=value;", "if(action==='engine'){if(engine!==value)voice='尚未选择';engine=value;")
html=html.replace("if(mode==='setup')show(step,false)","if(mode==='setup'&&!window.reviewTasks?.isActive())show(step,false)")
html=html.replace('<button id="references">参考与设计说明</button>','<button id="airiConcept">AIRI 构图草图</button><button id="references">素材与设计说明</button>')
start=html.index('function deskContent()')
end=html.index("$('#jump').innerHTML=",start)
html=html[:start]+'''function showDesk(page=deskPage){clearTransient();mode='desk';deskPage=page;$('#window').classList.add('workspace');$('#showDesk').classList.add('active');$('#showSetup').classList.remove('active');$('#screens').innerHTML='';$('#jump').value='desk';history.replaceState(null,'',`#desk-${page}`);window.workspaceDesign?.render(page);window.dispatchEvent(new Event('design-route'))}
'''+html[end:]
bridge="""window.reviewBridge={getState:()=>({step,mode,deskPage,engine,source,environment,voice,airi,sound,clients:[...clients]}),isReduced:()=>reduced||matchMedia('(prefers-reduced-motion:reduce)').matches,showDesk,showSetup:show,refresh:()=>mode==='desk'?showDesk(deskPage):show(step,false),openModal,closeModal,setVoice:(n,e)=>{voice=n;if(e)engine=e},setAiri:v=>{airi=v},setSource:v=>{source=v;show(step,false)},cue:k=>tone(k),toggleSound:()=>$('#soundToggle').click()};"""
html=html.replace('const initial=location.hash;',bridge+'\nconst initial=location.hash;')
html=html.replace('</script></body>','</script><script type="module" src="art-direction.js"></script><script type="module" src="workflow.js"></script><script type="module" src="workspace.js"></script></body>')
html=html.replace('href="设计说明.md"','href="设计与素材说明.md"')
assert '<body>' in html, 'The first-paint gate must be attached to the page body'
html=html.replace('<body>','<body class="ui-booting">',1)
html=html.replace('</body>','<script type="module" src="ui-readiness.js"></script><script type="module" src="render-diagnostics.js"></script></body>')
# One module identity per document, including dependencies imported by readiness.
# Content versions prevent a refreshed layout from running a cached old task renderer.
modules=['art-direction.js','page-compositions.js','scene-materials.js','workflow.js','workspace.js','ui-readiness.js','render-diagnostics.js']
# Normalize checkout line endings so Git's Windows conversion does not stale versions.
version=lambda name: hashlib.sha256((root/name).read_text(encoding='utf-8').encode('utf-8')).hexdigest()[:12]
imports={'./'+name:'./'+name+'?v='+version(name) for name in modules}
html=html.replace('</head>','<script type="importmap">'+json.dumps({'imports':imports})+'</script></head>')
html=re.sub(r'(<script type="module" src=")([^"?]+)(")',lambda m:m[1]+m[2]+'?v='+version(m[2])+m[3],html)
html=re.sub(r'(href=")([^"?]+\.css)(")',lambda m:m[1]+m[2]+'?v='+version(m[2])+m[3],html)
(root/'index.html').write_text(html,encoding='utf-8')
