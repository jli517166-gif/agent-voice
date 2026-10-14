using System;
using System.IO;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        void BuildTraining(){
            var stack=Stack(NewPage("training"));
            var intro=Card(stack,"基础训练");
            Add(intro,Hint("这里负责指导、准备目录、校验结果和带你回到导入入口。训练本身使用 CosyVoice 官方脚本，耗时、显存和数据量因数据集与训练轮数而异；程序不会把一段参考音频冒充为已训练模型。"));
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
        }
        void BuildAdvancedTraining(){
            var stack=Stack(NewPage("advancedTraining"));
            var intro=Card(stack,"高级 CosyVoice 训练");
            Add(intro,Hint("适合显存和算力较强的电脑。目标是使用多段带准确台词的数据训练专属权重，并用未见文本验收；训练仍由 CosyVoice 官方脚本完成。"));
            Add(intro,Row(EntryBtn("打开完整手册","查看硬件、数据、训练、验收和导出标准。",()=>ServiceClient.Open(Store.Resolve("高级CosyVoice训练手册.md"))),EntryBtn("打开训练目录","整理数据和训练结果。",OpenTrainingWorkspace),EntryBtn("打开官方训练脚本","查看当前 CosyVoice3 recipe。",()=>ServiceClient.Open("https://github.com/FunAudioLLM/CosyVoice/blob/main/examples/libritts/cosyvoice3/run.sh"))));
            var flow=Card(stack,"四个阶段");
            Add(flow,Hint("1. 固定 CosyVoice、基础模型、Python、PyTorch 和 CUDA 版本。\r\n2. 清洗多段录音，准备逐字台词、训练集、验证集和试听集。\r\n3. 按官方 recipe 训练、选择或平均 checkpoint，并整理完整模型目录。\r\n4. 用未见文本验收，再导出高级训练包或高级完整包。"));
            var packages=Card(stack,"导出选择");
            Add(packages,Hint("高级训练包只保存相对基础模型变化的权重，体积较小，但导入电脑必须有完全一致的基础模型。高级完整包保存完整训练模型，迁移更方便；两者都不包含 CosyVoice 源码、Python、CUDA 或 RVC。"));
            Add(packages,Row(EntryBtn("校验训练模型","选择完整训练结果并填到首页。",SelectTrainedModel),EntryBtn("打开音色包","选择高级训练包或高级完整包。",()=>ShowPage("configuration")),EntryBtn("返回基础训练","查看基础流程。",()=>ShowPage("training"))));
        }
        string TrainingWorkspace(){string directory=Path.Combine(VoicePackageDirectory(),"训练输出");Directory.CreateDirectory(directory);return directory;}
        void OpenTrainingWorkspace(){string directory=TrainingWorkspace();ServiceClient.Open(directory);Status("已打开音色模型包库内的训练输出目录；训练好的完整模型请保存在这里。");}
        void SelectTrainedModel(){
            using(var dialog=new FolderBrowserDialog{Description="从音色模型包库的训练输出中选择完整 CosyVoice3 模型目录",ShowNewFolderButton=false,SelectedPath=TrainingWorkspace()}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                string library=Path.GetFullPath(VoicePackageDirectory()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
                string selected=Path.GetFullPath(dialog.SelectedPath).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(!selected.StartsWith(library,StringComparison.OrdinalIgnoreCase)){Status("训练完成的模型必须放在音色模型包文件夹中；已为你打开对应目录。");ServiceClient.Open(TrainingWorkspace());return;}
                var probe=Config.voice.Clone();probe.engine="cosyvoice";probe.baseModel=Store.Portable(dialog.SelectedPath);ValidatePaths(probe);baseModelPath.Text=probe.baseModel;ShowPage("home");Status("模型目录校验通过，已填入当前模型；请补全参考声音后试听。");
            }
        }
    }
}
