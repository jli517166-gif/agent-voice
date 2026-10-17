using System;
using System.IO;
using System.Windows.Forms;
using System.Linq;
using System.Threading.Tasks;

namespace AgentVoice {
    public sealed partial class MainForm {
        void BuildTraining(){
            var stack=Stack(NewPage("training"));
            var choose=Card(stack,"选择训练引擎");
            Add(choose,Hint("先选择引擎，再按对应教程操作。这里只准备和检查素材，不会自动启动显卡训练。GPT 与 CosyVoice 的训练清单和权重不能互换。"));
            var trainingChoice=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Name="trainingEngineChoice"};trainingChoice.Items.AddRange(new object[]{"CosyVoice 3","GPT-SoVITS · v5 Turbo","GPT-SoVITS · ProPlus"});Add(choose,trainingChoice);
            var intro=Card(stack,"基础训练");
            intro.Name="cosyTrainingGuide";
            Add(intro,Hint("这里负责指导、准备目录、校验结果和带你回到导入入口。训练本身使用 CosyVoice 官方脚本，耗时、显存和数据量因数据集与训练轮数而异；程序不会把一段参考音频冒充为已训练模型。"));
            Add(intro,Hint("本机可以先检查素材和生成清单。官方权重训练在单独的 Linux／WSL2 环境执行；只有推理文件的安装包不含完整训练脚本。不要覆盖已认可的推理环境。"));
            Add(intro,Row(Primary(AsyncBtn("检查训练素材",async()=>await CheckTraining(false))),AsyncBtn("生成训练清单",async()=>await CheckTraining(true)),EntryBtn("从零开始训练","包含素材表格、Windows/WSL 路径、命令、失败处理和输出验收。",()=>ServiceClient.Open(Store.Resolve("音色训练从零开始.md"))),EntryBtn("交给 AI 的训练说明","AI 应先检查环境和素材，不得把参考克隆称为训练，也不应覆盖认可声音。",()=>ServiceClient.Open(Store.Resolve("AI训练操作约定.md")))));
            Add(intro,Row(AsyncBtn("检查训练脚本",async()=>{var r=Json.Object(await Service.RunTool("prepare_training.py","inspect-repo --input "+ServiceClient.Quote(Store.Resolve(Config.engines.cosyRepo))+" --report-only"));MessageBox.Show(this,Json.Text(r,"message"),"训练环境检查");})));
            Add(intro,Row(EntryBtn("打开训练目录","整理音频、台词和训练结果。",OpenTrainingWorkspace),EntryBtn("打开 CosyVoice 源码","查看本机安装的 CosyVoice。",()=>ServiceClient.Open(Store.Resolve(Config.engines.cosyRepo))),EntryBtn("打开官方训练脚本","查看官方 CosyVoice3 训练流程。",()=>ServiceClient.Open("https://github.com/FunAudioLLM/CosyVoice/blob/main/examples/libritts/cosyvoice3/run.sh")),EntryBtn("高级训练","查看高配置电脑训练专属权重的完整流程。",()=>ShowPage("advancedTraining"))));

            var prepare=Card(stack,"第 1 步 · 准备训练数据");
            Add(prepare,Hint("收集同一说话人的干净单声道录音，切成自然短句；每段配一条完全一致的台词。去掉背景音乐、混响、重叠人声、爆音和长静音。训练数据可以有很多段，首页的推理参考音频仍是另一件事。"));
            Add(prepare,Hint("按官方 recipe 准备 wav.scp、text、utt2spk、spk2utt，再生成 parquet 数据列表。建议先留出独立验证集，用没有参加训练的句子试听。"));

            var train=Card(stack,"第 2 步 · 训练与导出");
            Add(train,Hint("使用 CosyVoice3 官方 examples/libritts/cosyvoice3/run.sh：先准备数据，再提取说话人特征／语音 token（CosyVoice3 可在线提取，但会影响训练速度），随后训练并平均 checkpoint。官方脚本当前标注主要支持 LLM 训练；不要直接把单个 checkpoint 当成完整可推理模型包。"));
            Add(train,Hint("训练完成后，把选定权重放回兼容的完整 CosyVoice3 模型目录。该目录至少应包含 cosyvoice3.yaml、llm.pt、flow.pt、hift.pt、campplus.onnx、speech_tokenizer_v3.onnx 和 CosyVoice-BlankEN。"));

            var use=Card(stack,"第 3 步 · 校验并在 agent语音中使用");
            Add(use,Hint("训练结果统一放在音色包库的“训练输出”目录。校验完整模型后，到首页设置参考声音并点击“使用当前声音”试听，满意后进入“音色包”导出。"));
            Add(use,Row(EntryBtn("选择训练模型","选择并校验完整 CosyVoice3 模型目录。",SelectTrainedModel),EntryBtn("打开音色包","导出训练结果或导入已有音色包。",()=>ShowPage("configuration")),EntryBtn("返回首页","回到首页设置参考声音并试听。",()=>ShowPage("home"))));

            var explain=Card(stack,"参考克隆与训练模型的区别");
            Add(explain,Hint("首页当前使用零样本参考克隆：一段参考音频 + 它的逐字台词作为条件。真正训练会用多段带台词的数据更新模型权重。当前程序仍以零样本接口调用训练后模型，因此首页的一段参考音频和逐字台词仍需填写；未来若加入 SFT 说话人 ID 推理，才可以对兼容的专属模型取消这项要求。"));
            var cosyCards=stack.Controls.Cast<Control>().Where(c=>c!=choose).ToArray();
            var gpt=Card(stack,"GPT-SoVITS 训练与声音准备");
            gpt.Name="gptTrainingGuide";
            var gptVariantNote=Hint("");Add(gpt,gptVariantNote);
            Add(gpt,Hint("第 1 步：收集同一人的干净短句，使用 GPT 官方切片、ASR 和标注工具校对。标注格式为 音频路径|说话人|语言|逐字台词；不要使用 CosyVoice 的 metadata.csv／Kaldi 清单。保留未参加训练的试听句。"));
            Add(gpt,Hint("第 2 步：在独立训练环境启动官方 webui.py，选择准确版本。完成文本、SSL 和语义特征提取，再按官方页面训练 SoVITS／GPT；不要覆盖本程序正在使用的推理环境或已认可模型。"));
            Add(gpt,Hint("第 3 步：先在官方推理页测试训练结果，再检查本程序适配边界。当前适配完整权重目录和参考克隆，不会把 LoRA／adapter 单文件自动合并，也不能把改名当作格式转换。"));
            Add(gpt,Hint("第 4 步：准备 3～10 秒参考音频与准确台词，在首页选对应 GPT 版本和完整模型目录，点击“使用当前声音”。等待新声音就绪，再用未见文本试听。满意后在音色包页面导出 .gpt-sovits.ttsmodel。"));
            Add(gpt,Row(EntryBtn("GPT 训练手册","分别说明 ProPlus 与 v5 Turbo 的流程、权重格式和当前接入限制。",()=>ServiceClient.Open(Store.Resolve("GPT-SoVITS训练指南.md"))),EntryBtn("打开 GPT 源码","在独立环境运行官方 webui.py；不会自动启动训练。",()=>ServiceClient.Open(Store.Resolve(Config.engines.gptRepo))),EntryBtn("打开 GPT 官方说明","以所安装版本的训练页面和说明为准。",()=>ServiceClient.Open("https://github.com/RVC-Boss/GPT-SoVITS/blob/main/docs/cn/README.md")),EntryBtn("打开训练目录","保存独立训练结果和验收记录。",OpenTrainingWorkspace)));
            trainingChoice.SelectedIndexChanged+=(s,e)=>{bool cosy=trainingChoice.SelectedIndex==0;foreach(var c in cosyCards)c.Visible=cosy;gpt.Visible=!cosy;gptVariantNote.Text=trainingChoice.SelectedIndex==1?"v5 Turbo：优先验证参考克隆。官方微调可能输出 LoRA；当前软件没有自动合并 LoRA 的功能，未经合并验证的权重不能直接导入。":"ProPlus：在官方页面选择 v2ProPlus，分别训练语义 GPT 与声学 SoVITS。只有架构兼容的完整推理权重才能按手册整理为模型目录。";};
            trainingChoice.SelectedIndex=Config.voice.engine=="gpt-sovits"?(Config.voice.gptVariant=="v5turbo"?1:2):0;
        }
        void BuildAdvancedTraining(){
            var stack=Stack(NewPage("advancedTraining"));
            var intro=Card(stack,"高级 CosyVoice 训练");
            Add(intro,Hint("适合显存和算力较强的电脑。目标是使用多段带准确台词的数据训练专属权重，并用未见文本验收；训练仍由 CosyVoice 官方脚本完成。"));
            Add(intro,Row(EntryBtn("打开完整手册","查看硬件、数据、训练、验收和导出标准。",()=>ServiceClient.Open(Store.Resolve("高级CosyVoice训练手册.md"))),EntryBtn("打开训练目录","整理数据和训练结果。",OpenTrainingWorkspace),EntryBtn("打开官方训练脚本","查看当前 CosyVoice3 recipe。",()=>ServiceClient.Open("https://github.com/FunAudioLLM/CosyVoice/blob/main/examples/libritts/cosyvoice3/run.sh"))));
            var flow=Card(stack,"四个阶段");
            Add(flow,Hint("1. 固定 CosyVoice、基础模型、Python、PyTorch 和 CUDA 版本。\r\n2. 清洗多段录音，准备逐字台词、训练集、验证集和试听集。\r\n3. 按官方 recipe 训练、选择或平均 checkpoint，并整理完整模型目录。\r\n4. 用未见文本验收，再导出高级训练包或高级完整包。"));
            var packages=Card(stack,"导出选择");
            Add(packages,Hint("高级训练包只保存相对基础模型变化的权重，体积较小，但导入电脑必须有完全一致的基础模型。高级完整包保存完整训练模型，迁移更方便；两者都不包含 CosyVoice 源码、Python 或 CUDA。"));
            Add(packages,Row(EntryBtn("校验训练模型","选择完整训练结果并填到首页。",SelectTrainedModel),EntryBtn("打开音色包","选择高级训练包或高级完整包。",()=>ShowPage("configuration")),EntryBtn("返回基础训练","查看基础流程。",()=>ShowPage("training"))));
        }
        string TrainingWorkspace(){string directory=Path.Combine(VoicePackageDirectory(),"训练输出");Directory.CreateDirectory(directory);return directory;}
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
                string library=Path.GetFullPath(VoicePackageDirectory()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
                string selected=Path.GetFullPath(dialog.SelectedPath).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(!selected.StartsWith(library,StringComparison.OrdinalIgnoreCase)){Status("训练完成的模型必须放在音色模型包文件夹中；已为你打开对应目录。");ServiceClient.Open(TrainingWorkspace());return;}
                var probe=Config.voice.Clone();probe.engine="cosyvoice";probe.baseModel=Store.Portable(dialog.SelectedPath);ValidatePaths(probe);baseModelPath.Text=probe.baseModel;SelectEngineVoice(probe);ShowPage("home");Status("模型目录校验通过，已切回 CosyVoice 并填入模型；请补全参考声音后试听。");
            }
        }
    }
}
