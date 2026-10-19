from pathlib import Path

root=Path(__file__).resolve().parents[1]
p=root/'ui/MainForm.cs'
t=p.read_text(encoding='utf-8')
a=t.index('        void BuildHome(){')
b=t.index('        void BuildCosyReference()',a)
old=t[a:b]
voice=old[old.index('            var voice=Card(leftStack,'):old.index('            var speech=Card(rightStack,')]
voice=voice.replace('Card(leftStack,"当前声音")','Card(Stack(NewPage("voiceEditor")),"引擎、模型与参考声音")')
voice=voice.replace('保存首页当前选择','保存声音页当前选择')
voice=voice.replace('EntryBtn("基础训练","查看数据准备和模型校验步骤。",()=>ShowPage("training"))','EntryBtn("保存或切换声音方案","把当前声音保存为可复用方案。",()=>ShowPage("voices"))')
new='''        Label homeVoiceSummary;
        ComboBox quickVoice;
        bool speechOperation;
        void BuildHome(){
            var home=NewPage("home");var stack=Stack(home);
            serviceToggle=Primary(AsyncBtn("启动服务",async()=>{if(await Service.Running()){await Service.Stop();Status("服务已关闭。");}else{await SaveDraft();await Service.Start();Status("服务已启动。");}await Poll();}));
            var transport=Controls.Find("sidebarTransport",true).First() as TableLayoutPanel;
            serviceToggle.AutoSize=false;serviceToggle.Dock=DockStyle.Fill;serviceToggle.Margin=new Padding(0,3,0,3);transport.Controls.Add(serviceToggle,0,2);
            var stop=Danger(Btn("停止朗读",async()=>await StopSpeech()));stop.AutoSize=false;stop.Dock=DockStyle.Fill;stop.Margin=new Padding(0,3,0,3);transport.Controls.Add(stop,0,3);
            var voice=Card(stack,"正在使用");homeVoiceSummary=Hint("");homeVoiceSummary.Font=new Font(Font.FontFamily,12,FontStyle.Bold);Add(voice,homeVoiceSummary);
            quickVoice=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=330,AccessibleName="快速选择已保存的声音方案"};
            Add(voice,Row(quickVoice,AsyncBtn("切换声音",async()=>{var item=quickVoice.SelectedItem as SavedVoice;if(item==null)throw new InvalidOperationException("请先在声音方案中保存或选择一个声音。");await ProfileOperation("apply",item.id,null);BindVoice();Status("声音已切换："+item.name);}),EntryBtn("调整声音","选择引擎、模型和语速。",()=>ShowPage("voiceEditor"))));
            harnessSummary=Hint("Harness：服务关闭");harnessSummary.Font=new Font(Font.FontFamily,9);Add(voice,harnessSummary);
            var speech=Card(stack,"想说什么？");body=Input(true);body.Height=174;body.Text="工作好久啦，起来动一动、喝口水吧～";body.AccessibleName="朗读文本";Add(speech,body);
            generate=Primary(LiveSpeechButton("生成并播放",async()=>await Generate(body.Text,true)));playButton=LiveSpeechButton("重播上一条",PlayLast);
            Add(speech,Row(generate,LiveSpeechButton("只生成音频",async()=>await Generate(body.Text,false)),playButton));
            var phraseCard=Card(stack,"常用句");phrases=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=520,AccessibleName="常用句选择"};Add(phraseCard,phrases);
            Add(phraseCard,Row(LiveSpeechButton("朗读选中句",async()=>{if(phrases.SelectedItem!=null)await Generate(Convert.ToString(phrases.SelectedItem),true);}),Btn("放入输入框",()=>{if(phrases.SelectedItem!=null)body.Text=Convert.ToString(phrases.SelectedItem);}),AsyncBtn("保存输入为常用句",async()=>{string value=body.Text.Trim();if(value.Length==0)return;if(!Config.phrases.Contains(value))Config.phrases.Add(value);await Persist(Config,new{phrases=Config.phrases});RefreshPhrases();}),AsyncBtn("删除选中句",async()=>{if(phrases.SelectedItem==null)return;Config.phrases.Remove(Convert.ToString(phrases.SelectedItem));await Persist(Config,new{phrases=Config.phrases});RefreshPhrases();})));
            phraseCard.SizeChanged+=(s,e)=>phrases.Width=Math.Max(180,phraseCard.ClientSize.Width-44);
            BuildVoiceEditor();BuildCosyReference();
        }
        Button LiveSpeechButton(string text,Func<Task> action){return Btn(text,async()=>{if(speechOperation){Status("已有手动朗读任务进行中。可随时点击左侧停止朗读。");return;}speechOperation=true;try{await action();}catch(Exception error){StatusError(error.Message);}finally{speechOperation=false;}});}
        void BuildVoiceEditor(){
'''+voice+'''        }
'''
t=t[:a]+new+t[b:]
t=t.replace('new RowStyle(SizeType.Absolute,92)','new RowStyle(SizeType.Absolute,210)')
t=t.replace('var stateBox=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,','var stateBox=new BufferedTable{Name="sidebarTransport",Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,')
t=t.replace('stateBox.RowStyles.Add(new RowStyle(SizeType.Percent,100));stateBox.Controls.Add','stateBox.RowStyles.Add(new RowStyle(SizeType.Percent,100));stateBox.RowStyles.Add(new RowStyle(SizeType.Absolute,48));stateBox.RowStyles.Add(new RowStyle(SizeType.Absolute,48));stateBox.Controls.Add')
t=t.replace('void BindVoice(){var v=Config.voice;','void BindVoice(){var v=Config.voice;if(quickVoice!=null){quickVoice.DataSource=null;quickVoice.DataSource=Config.profiles.ToList();}if(homeVoiceSummary!=null)homeVoiceSummary.Text=EngineLabel(v)+"   ·   语速 "+v.speed.ToString("0.00")+"×   ·   "+(v.reference.Length>0?Path.GetFileNameWithoutExtension(v.reference):"待选择参考声音");')
p.write_text(t,encoding='utf-8')
p=root/'ui/UiChecks.cs';t=p.read_text(encoding='utf-8-sig');t=t.replace('if(id=="home"&&homeHero!=null)homeHero.Preview(KeynoteArt.Duration);','');t=t.replace('"home","voices","engine"','"home","voiceEditor","voices","engine"');p.write_text(t,encoding='utf-8')
p=root/'ui/Program.cs';t=p.read_text(encoding='utf-8-sig');t=t.replace('if(args.Contains("--welcome-preview")){using(var preview=new WelcomeForm(false,true,true))Application.Run(preview);return 0;}','if(args.Contains("--welcome-preview")||args.Contains("--setup-preview")){using(var preview=new SetupWizard(store))Application.Run(preview);return 0;}')
a=t.index('                    var welcomeState=')
b=t.index('                    using(var wake=',a)
t=t[:a]+'''                    var welcomeState=new WelcomeState(store);var preference=welcomeState.Read();
                    if(SetupWizard.NeedsSetup(preference)||args.Contains("--setup"))using(var setup=new SetupWizard(store)){
                        using(var setupWake=new EventWaitHandle(false,EventResetMode.AutoReset,wakeName)){
                            var registration=ThreadPool.RegisterWaitForSingleObject(setupWake,(s,t)=>{if(setup.IsHandleCreated&&!setup.IsDisposed)setup.BeginInvoke((Action)(()=>{setup.Show();setup.WindowState=FormWindowState.Normal;setup.Activate();}));},null,Timeout.Infinite,false);
                            try{if(setup.ShowDialog()!=DialogResult.OK)return 0;}finally{registration.Unregister(null);}
                        }
                    }
'''+t[b:]
t=t.replace('                        string initialChoice=choice;form.Shown+=(s,e)=>form.ApplyWelcomeChoice(initialChoice);\n','');p.write_text(t,encoding='utf-8')
