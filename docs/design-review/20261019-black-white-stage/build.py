from pathlib import Path
root=Path(__file__).resolve().parent
html=(root/'base-template.html').read_text(encoding='utf-8')
html=html.replace('<title>Agent Voice · 声音终端 · 设计审核</title>','<title>Agent Voice · 黑白声场设计审核</title>')
html=html.replace('</head>','<link rel="stylesheet" href="art-direction.css"><link rel="stylesheet" href="workspace.css"></head>')
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
(root/'index.html').write_text(html,encoding='utf-8')
