"""One-off source refactor helper; not included in runtime modules."""
from pathlib import Path

root = Path(__file__).resolve().parents[1]

def edit(name, old, new):
    path = root / name
    text = path.read_text(encoding='utf-8-sig')
    if old not in text:
        raise RuntimeError(f'Missing expected source in {name}: {old[:100]}')
    path.write_text(text.replace(old, new), encoding='utf-8')

p=root/'ui/MainForm.cs'
t=p.read_text(encoding='utf-8-sig')
t='\n'.join(line for line in t.split('\n') if 'public Image KeynoteArtwork' not in line and 'Bitmap keynoteArtwork;' not in line and 'SetupAnimation homeHero;' not in line)
t=t.replace('BuildSettings();BuildSetup();','BuildSettings();')
t=t.replace('            AddNav(navigation,"配置中心","setup","按步骤准备环境、引擎、声音和连接，可随时跳过。",true);\n','')
t=t.replace('AddNav(navigation,"首页","home","声音、服务和立即朗读。",true);','AddNav(navigation,"朗读","home","输入文字、常用句与播放控制。",true);\n            AddNav(navigation,"声音","voiceEditor","选择引擎、音色与语速，再应用到新任务。",true);')
t=t.replace('            navigation.Controls.Add(SideLabel("常用功能"));\n','').replace('            navigation.Controls.Add(SideLabel("声音工具"));\n','')
t=t.replace('            AddNav(navigation,"高级训练","advancedTraining","在高配置电脑上训练专属权重。",false);\n','')
t=t.replace('AddNav(navigation,"连接设备"','AddNav(navigation,"连接"').replace('AddNav(navigation,"基础训练"','AddNav(navigation,"训练"')
t=t.replace('ColumnStyle(SizeType.Absolute,198)','ColumnStyle(SizeType.Absolute,184)').replace('Width=150;button.Height=36','Width=146;button.Height=44')
t=t.replace('Host=this,Card=true,Dock=DockStyle.Fill,Padding=new Padding(12),Margin=new Padding(0,0,16,0)','Host=this,Dock=DockStyle.Fill,Padding=new Padding(6),Margin=new Padding(0,0,20,0)')
t=t.replace('if(id=="setup")id=setupIds[SetupStep];','if(id=="setup"){RunConfiguration();return;}')
t=t.replace('            if(IsSetupPage(id))SetupStep=Array.IndexOf(setupIds,id);\n','')
a=t.index('            try{foreach(var pair in pages)')
b=t.index('\n            finally{content.ResumeLayout',a)
t=t[:a]+'''            try{foreach(var pair in pages)pair.Value.Visible=pair.Key==id;pages[id].BringToFront();page=id;sidebar.Visible=true;outer.ColumnStyles[0].Width=184;workspace.RowStyles[0].Height=62;backButton.Visible=id!="home";pageTitle.Text=PageName(id);statusBar.Visible=true;workspace.RowStyles[1].Height=54;foreach(var pair in navigationButtons)StyleButton(pair.Value,pair.Key==id);}'''+t[b:]
t=t.replace('            if(changed)PlayInterfaceSound(IsSetupPage(id)?SetupStep:8);','').replace('            bool changed=page!=id;\n','')
t=t.replace('            if(!online)SetSetupConnections(null);\n','')
t=t.replace('            if(background==null&&(page=="home"||IsSetupPage(page))&&BackColor.ToArgb()==Color.FromArgb(5,16,29).ToArgb()){g.Clear(Color.Black);return;}\n','')
t=t.replace('||(IsSetupPage(page)&&c==navigationButtons["setup"])','').replace('if(keynoteArtwork!=null){keynoteArtwork.Dispose();keynoteArtwork=null;}','')
t=t.replace('{"home","首页"}','{"home","朗读工作台"},{"voiceEditor","声音设置"}')
t=t.replace('BackColor=ColorTranslator.FromHtml(a.backgroundColor);','BackColor=ColorTranslator.FromHtml(a.backgroundColor=="#05101D"?"#0B1017":a.backgroundColor);')
p.write_text(t,encoding='utf-8')
edit('ui/ConnectionPages.cs','            SetSetupConnections(new Dictionary<string,object>{{"verification",states}});\n','')
edit('ui/ScrollChecks.cs','                        if(IsSetupPage(id)){int step=Array.IndexOf(setupIds,id);GoSetup(step);viewport=(ScenePanel)pages[id].Controls.Find("setupScroller_"+step,true).Single();}\n','')
edit('ui/SettingsPages.cs','Add(common,Row(EntryBtn("配置中心","继续准备环境、声音与连接。",()=>ShowPage("setup")),EntryBtn("重播开场","重播开场并重新选择欢迎页入口；不重置声音设置。",ReplayWelcome)));','Add(common,Row(EntryBtn("重新运行首次配置","打开独立配置窗口；返回工作台后保留已有声音。",RunConfiguration)));')
edit('ui/SettingsPages.cs','界面电音音效（语音服务开启时暂停）','配置流程电音音效（语音服务运行时暂停）')
edit('ui/UiChecks.cs','if(homeHero!=null)homeHero.Preview(KeynoteArt.Duration);','')
