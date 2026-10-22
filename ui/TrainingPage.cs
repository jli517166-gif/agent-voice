using Button = AgentVoice.AnimeButton;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Linq;
using System.Threading.Tasks;

namespace AgentVoice {
    public sealed partial class MainForm {
        Label gptVariantNote;
        void BuildTraining(){
            var page=WebPage("training");
            WebTitle(page,"训练指南","先确认素材授权，再按引擎选择训练路线。");
            page.Controls.Add(WebButton("训练须知","训练、声音克隆、发布及权重分发须分别核对授权与许可。",()=>ServiceClient.Open(Store.Resolve("docs/使用须知与TTS许可.md")),784,46,150,39));
            var cosyGuide=new ScenePanel{Host=this,Location=new Point(38,160),Size=new Size(902,516),Margin=new Padding(0),Name="cosyTrainingGuide"};
            var gptGuide=new ScenePanel{Host=this,Location=new Point(38,160),Size=new Size(902,516),Margin=new Padding(0),Name="gptTrainingGuide"};
            page.Controls.Add(cosyGuide);page.Controls.Add(gptGuide);
            var cosyIntro=WebCard(cosyGuide,0,21,902,114);
            cosyIntro.Controls.Add(WebText("从一段干净的参考录音开始。",15F,FontStyle.Bold,16,12,740,30,ContentAlignment.MiddleLeft));
            cosyIntro.Controls.Add(WebText("先完成参考克隆，再决定是否需要训练权重。",9.5F,FontStyle.Regular,16,46,740,22,ContentAlignment.MiddleLeft));
            cosyIntro.Controls.Add(WebText("仅使用有权训练与克隆的素材；发布和商用另核查授权。",8.5F,FontStyle.Regular,16,78,740,22,ContentAlignment.MiddleLeft));
            var cosyArt=WebCard(cosyIntro,770,16,112,78);
            cosyArt.Controls.Add(WebText("MIC",16F,FontStyle.Bold,0,0,112,78,ContentAlignment.MiddleCenter));
            var cosySteps=WebCard(cosyGuide,0,151,902,312);
            TrainingStep(cosySteps,0,"准备声音","先确认录音、台词、声音使用授权，再检查素材。","检查素材",()=>{var ignored=Guard(async()=>await CheckTraining(false),"检查训练素材");},590,110,"生成清单",()=>{var ignored=Guard(async()=>await CheckTraining(true),"生成训练清单");},706,110);
            TrainingStep(cosySteps,1,"选择训练路线","参考克隆用录音与台词；硬件较好时按高级指南。","从零开始",()=>ServiceClient.Open(Store.Resolve("音色训练从零开始.md")),560,110,"AI 训练约定",()=>ServiceClient.Open(Store.Resolve("AI训练操作约定.md")),678,150);
            TrainingStep(cosySteps,2,"本地试听并验证","确认引擎、模型与台词一致，再用长短句验证。","选择训练模型",SelectTrainedModel,550,155,"管理可选模块",()=>ShowPage("modules"),713,125);
            TrainingStep(cosySteps,3,"导出训练结果","按内容选择轻量、完整、训练增量或训练完整包。","高级训练",()=>ShowPage("advancedTraining"),540,105,"检查脚本",()=>{var ignored=Guard(InspectTrainingScript,"检查训练脚本");},653,105);
            cosySteps.Controls.Add(WebButton("打开目录","打开本地训练结果目录。",OpenTrainingWorkspace,770,255,110,36));
            var cosyFooter=WebCard(cosyGuide,0,468,902,48);
            cosyFooter.Controls.Add(WebButton("查看详细操作手册","包含素材表格、Windows/WSL 路径、命令、失败处理和输出验收。",()=>ServiceClient.Open(Store.Resolve("音色训练从零开始.md")),0,4,185,39));
            cosyFooter.Controls.Add(WebButton("打开 CosyVoice 源码","查看本机安装的 CosyVoice。",()=>ServiceClient.Open(Store.Resolve(Config.engines.cosyRepo)),191,4,230,39));
            cosyFooter.Controls.Add(WebButton("打开官方训练脚本","查看官方 CosyVoice3 训练流程。",()=>ServiceClient.Open("https://github.com/FunAudioLLM/CosyVoice/blob/main/examples/libritts/cosyvoice3/run.sh"),427,4,185,39));
            cosyFooter.Controls.Add(WebButton("打开训练界面","整理音频、台词和训练结果。",OpenTrainingWorkspace,618,4,145,39));
            cosyFooter.Controls.Add(WebButton("返回首页","回到首页设置参考声音并试听。",()=>ShowPage("home"),769,4,105,39));

            var gptIntro=WebCard(gptGuide,0,21,902,114);
            gptIntro.Controls.Add(WebText("GPT-SoVITS 训练与声音准备",15F,FontStyle.Bold,16,12,740,30,ContentAlignment.MiddleLeft));
            gptVariantNote=WebText("",8.5F,FontStyle.Regular,16,46,740,40,ContentAlignment.TopLeft);
            gptIntro.Controls.Add(gptVariantNote);
            gptIntro.Controls.Add(WebText("素材须获相应用途授权；代码 MIT 不代表全部权重可商用。",8.5F,FontStyle.Regular,16,90,740,20,ContentAlignment.MiddleLeft));
            var gptArt=WebCard(gptIntro,770,16,112,78);
            gptArt.Controls.Add(WebText("GPT",16F,FontStyle.Bold,0,0,112,78,ContentAlignment.MiddleCenter));
            var gptSteps=WebCard(gptGuide,0,151,902,312);
            TrainingStep(gptSteps,0,"准备素材","干净短句、官方切片与标注；保留试听句。","训练手册",()=>ServiceClient.Open(Store.Resolve("GPT-SoVITS训练指南.md")),700,100);
            TrainingStep(gptSteps,1,"训练特征","独立环境运行官方脚本，完成特征提取。","打开源码",()=>ServiceClient.Open(Store.Resolve(Config.engines.gptRepo)),700,100);
            TrainingStep(gptSteps,2,"验证模型","先在官方推理页测试；不支持自动合并 LoRA。","官方说明",()=>ServiceClient.Open("https://github.com/RVC-Boss/GPT-SoVITS/blob/main/docs/cn/README.md"),700,100);
            TrainingStep(gptSteps,3,"验证结果","参考音频 3～10 秒，选择训练结果并试听。","打开目录",OpenTrainingWorkspace,700,100);
            var gptFooter=WebCard(gptGuide,0,468,902,48);
            gptFooter.Controls.Add(WebButton("查看详细操作手册","分别说明 ProPlus 与 v5 Turbo 的流程、权重格式和当前接入限制。",()=>ServiceClient.Open(Store.Resolve("GPT-SoVITS训练指南.md")),0,4,185,39));
            gptFooter.Controls.Add(WebButton("打开训练目录","保存独立训练结果和验收记录。",OpenTrainingWorkspace,191,4,140,39));
            gptFooter.Controls.Add(WebButton("管理可选模块","打开独立的可选功能模块。",()=>ShowPage("modules"),337,4,120,39));
            gptFooter.Controls.Add(WebButton("高级训练","查看高配置电脑训练专属权重的完整流程。",()=>ShowPage("advancedTraining"),463,4,110,39));
            gptFooter.Controls.Add(WebButton("返回首页","回到首页设置参考声音并试听。",()=>ShowPage("home"),579,4,105,39));

            var trainingChoice=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Name="trainingEngineChoice",Visible=false,Location=new Point(0,0),Size=new Size(1,1)};
            trainingChoice.Items.AddRange(new object[]{"CosyVoice 3","GPT-SoVITS · v5 Turbo","GPT-SoVITS · ProPlus"});
            page.Controls.Add(trainingChoice);
            var tabs=new Button[3];
            var names=new[]{"CosyVoice","GPT-SoVITS · v5 Turbo","GPT-SoVITS · ProPlus"};
            var widths=new[]{140,260,250};
            var xs=new[]{38,186,454};
            for(int i=0;i<3;i++){
                int index=i;
                tabs[i]=WebButton(names[i],null,()=>trainingChoice.SelectedIndex=index,xs[i],126,widths[i],30,false);
                page.Controls.Add(tabs[i]);
            }
            trainingChoice.SelectedIndexChanged+=(s,e)=>{
                int index=trainingChoice.SelectedIndex;if(index<0)return;
                cosyGuide.Visible=index==0;gptGuide.Visible=index>0;
                for(int i=0;i<3;i++){tabs[i].Tag=i==index?"primary":"secondary";if(!Accent.IsEmpty)StyleButton(tabs[i],false);}
                gptVariantNote.Text=index==1?"v5 Turbo：优先验证参考克隆。\r\n官方微调可能输出 LoRA，需先合并验证。":"ProPlus：分别训练语义 GPT 与声学 SoVITS。\r\n只导入架构兼容的完整推理权重。";
            };
            trainingChoice.SelectedIndex=Config.voice.engine=="gpt-sovits"?(Config.voice.gptVariant=="v5turbo"?1:2):0;
            int initial=trainingChoice.SelectedIndex;
            cosyGuide.Visible=initial==0;gptGuide.Visible=initial!=0;
            for(int i=0;i<3;i++)tabs[i].Tag=i==initial?"primary":"secondary";
            gptVariantNote.Text=initial==2?"ProPlus：分别训练语义 GPT 与声学 SoVITS。\r\n只导入架构兼容的完整推理权重。":"v5 Turbo：优先验证参考克隆。\r\n官方微调可能输出 LoRA，需先合并验证。";
        }
        void TrainingStep(ScenePanel card,int index,string title,string detail,string first,Action firstAction,int firstX,int firstWidth,string second,Action secondAction,int secondX,int secondWidth){
            int y=index*78;
            card.Controls.Add(WebText((index+1).ToString(),12F,FontStyle.Bold,16,y+18,28,24,ContentAlignment.MiddleLeft));
            card.Controls.Add(WebText(title,10.5F,FontStyle.Bold,48,y+14,460,24,ContentAlignment.MiddleLeft));
            card.Controls.Add(WebText(detail,8.5F,FontStyle.Regular,48,y+38,460,20,ContentAlignment.MiddleLeft));
            card.Controls.Add(WebButton(first,null,firstAction,firstX,y+21,firstWidth,36));
            if(second!=null)card.Controls.Add(WebButton(second,null,secondAction,secondX,y+21,secondWidth,36));
            if(index>0)WebDivider(card,16,y,870);
        }
        void TrainingStep(ScenePanel card,int index,string title,string detail,string action,Action handler,int x,int width){
            TrainingStep(card,index,title,detail,action,handler,x,width,null,null,0,0);
        }
        async Task InspectTrainingScript(){
            var r=Json.Object(await Service.RunTool("prepare_training.py","inspect-repo --input "+ServiceClient.Quote(Store.Resolve(Config.engines.cosyRepo))+" --report-only"));
            MessageBox.Show(this,Json.Text(r,"message"),"训练环境检查");
        }
        void BuildAdvancedTraining(){
            var page=WebPage("advancedTraining");
            WebTitle(page,"高级训练","高配置电脑训练专属权重的完整流程。");
            page.Controls.Add(WebButton("返回基础训练","回到引擎选择与基础流程。",()=>ShowPage("training"),784,46,150,39));
            var intro=WebCard(page,38,126,902,142);
            WebCardTitle(intro,"高级 CosyVoice 训练");
            intro.Controls.Add(WebText("适合显存和算力较强的电脑。目标是使用多段带准确台词的数据训练专属权重，并用未见文本验收；训练仍由 CosyVoice 官方脚本完成。",8F,FontStyle.Regular,16,44,870,42,ContentAlignment.TopLeft));
            intro.Controls.Add(WebButton("打开完整手册","查看硬件、数据、训练、验收和导出标准。",()=>ServiceClient.Open(Store.Resolve("高级CosyVoice训练手册.md")),16,92,150,38));
            intro.Controls.Add(WebButton("打开训练目录","整理数据和训练结果。",OpenTrainingWorkspace,174,92,150,38));
            intro.Controls.Add(WebButton("打开官方训练脚本","查看当前 CosyVoice3 recipe。",()=>ServiceClient.Open("https://github.com/FunAudioLLM/CosyVoice/blob/main/examples/libritts/cosyvoice3/run.sh"),332,92,190,38));

            var flow=WebCard(page,38,276,902,158);
            WebCardTitle(flow,"四个阶段");
            flow.Controls.Add(WebText("1. 固定 CosyVoice、基础模型、Python、PyTorch 和 CUDA 版本。\r\n2. 清洗多段录音，准备逐字台词、训练集、验证集和试听集。\r\n3. 按官方 recipe 训练、选择或平均 checkpoint，并整理完整模型目录。\r\n4. 用未见文本验收，再导出高级训练包或高级完整包。",8F,FontStyle.Regular,16,44,870,102,ContentAlignment.TopLeft));

            var packages=WebCard(page,38,442,902,218);
            WebCardTitle(packages,"训练结果验收");
            packages.Controls.Add(WebText("保留完整训练结果、基础模型版本和训练参数。模型可以直接在声音设置中选择；核心朗读与声音方案不依赖功能模块。",8F,FontStyle.Regular,16,44,870,42,ContentAlignment.TopLeft));
            packages.Controls.Add(WebText("验收清单",9F,FontStyle.Bold,16,94,300,24,ContentAlignment.MiddleLeft));
            packages.Controls.Add(WebText("确认基础模型与训练环境一致；验证集与试听句不参与训练；完整模型目录包含全部权重文件；用未见文本试听通过后再应用。",7.5F,FontStyle.Regular,16,118,870,40,ContentAlignment.TopLeft));
            packages.Controls.Add(WebButton("校验训练模型","选择完整训练结果并填到首页。",SelectTrainedModel,16,168,150,38));
            packages.Controls.Add(WebButton("管理可选模块","按需安装独立功能模块。",()=>ShowPage("modules"),174,168,140,38));
        }
        string TrainingWorkspace(){string directory=Path.Combine(TrainedModelDirectory(),"训练输出");Directory.CreateDirectory(directory);return directory;}
        async Task CheckTraining(bool prepare){
            using(var dialog=new FolderBrowserDialog{Description="选择包含 metadata.csv 和单声道 WAV 录音的素材目录",ShowNewFolderButton=false}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                string output=Path.Combine(TrainingWorkspace(),"数据清单-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
                if(prepare)using(var target=new FolderBrowserDialog{Description="选择训练清单的存放位置（完整路径不能含空格）；程序会新建 dataset 子目录",ShowNewFolderButton=true}){if(target.ShowDialog(this)!=DialogResult.OK)return;output=Path.Combine(target.SelectedPath,"dataset_"+Guid.NewGuid().ToString("N").Substring(0,10));}
                string args=(prepare?"prepare":"validate")+" --input "+ServiceClient.Quote(dialog.SelectedPath)+" --report-only"+(prepare?" --output "+ServiceClient.Quote(output):"");
                var r=Json.Object(await Service.RunTool("prepare_training.py",args));bool ok=Convert.ToBoolean(r["ok"]);object raw;string message=ok?"素材结构通过。台词是否准确仍需人工核对；此操作没有启动训练。":"素材检查未通过：";
                foreach(string key in new[]{"errors","warnings"})if(r.TryGetValue(key,out raw)&&raw is object[])message+="\r\n"+string.Join("\r\n",((object[])raw).Select(Convert.ToString).ToArray());
                if(ok&&prepare)message+="\r\n清单目录："+output+"\r\nWindows 生成的路径供 WSL 使用；训练前请阅读从零开始手册。";
                Status(message);MessageBox.Show(this,message,"训练素材检查",MessageBoxButtons.OK,ok?MessageBoxIcon.Information:MessageBoxIcon.Warning);
            }
        }
        void OpenTrainingWorkspace(){string directory=TrainingWorkspace();ServiceClient.Open(directory);Status("已打开音色模型包库内的训练输出目录；训练好的完整模型请保存在这里。");}
        void SelectTrainedModel(){
            using(var dialog=new FolderBrowserDialog{Description="从音色模型包库的训练输出中选择完整 CosyVoice3 模型目录",ShowNewFolderButton=false,SelectedPath=TrainingWorkspace()}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                string library=Path.GetFullPath(TrainedModelDirectory()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
                string selected=Path.GetFullPath(dialog.SelectedPath).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(!selected.StartsWith(library,StringComparison.OrdinalIgnoreCase)){Status("训练完成的模型必须放在音色模型包文件夹中；已为你打开对应目录。");ServiceClient.Open(TrainingWorkspace());return;}
                var probe=Config.voice.Clone();probe.engine="cosyvoice";probe.baseModel=Store.Portable(dialog.SelectedPath);ValidatePaths(probe);baseModelPath.Text=probe.baseModel;SelectEngineVoice(probe);ShowPage("home");Status("模型目录校验通过，已切回 CosyVoice 并填入模型；请补全参考声音后试听。");
            }
        }
    }
}
