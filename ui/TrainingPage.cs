using System;
using System.IO;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        void BuildTraining(){
            var stack=Stack(NewPage("training"));
            var intro=Card(stack,"CosyVoice 音色训练指导");
            Add(intro,Hint("这里负责指导、准备目录、校验结果和带你回到导入入口。训练本身使用 CosyVoice 官方脚本，耗时、显存和数据量因数据集与训练轮数而异；程序不会把一段参考音频冒充为已训练模型。"));
            Add(intro,Row(EntryBtn("打开训练工作目录","打开音色模型包库内的“训练输出”目录，用于整理音频、台词和训练结果。",OpenTrainingWorkspace),EntryBtn("打开本机 CosyVoice 源码","打开设置中配置的 CosyVoice 源码目录。",()=>ServiceClient.Open(Store.Resolve(Config.engines.cosyRepo))),EntryBtn("打开官方 CosyVoice3 训练脚本","查看官方数据准备、特征提取、训练、模型平均和导出步骤。",()=>ServiceClient.Open("https://github.com/FunAudioLLM/CosyVoice/blob/main/examples/libritts/cosyvoice3/run.sh"))));

            var prepare=Card(stack,"第 1 步 · 准备训练数据");
            Add(prepare,Hint("收集同一说话人的干净单声道录音，切成自然短句；每段配一条完全一致的台词。去掉背景音乐、混响、重叠人声、爆音和长静音。训练数据可以有很多段，首页的推理参考音频仍是另一件事。"));
            Add(prepare,Hint("按官方 recipe 准备 wav.scp、text、utt2spk、spk2utt，再生成 parquet 数据列表。建议先留出独立验证集，用没有参加训练的句子试听。"));

            var train=Card(stack,"第 2 步 · 训练与导出");
            Add(train,Hint("使用 CosyVoice3 官方 examples/libritts/cosyvoice3/run.sh：先准备数据，再提取说话人特征／语音 token（CosyVoice3 可在线提取，但会影响训练速度），随后训练并平均 checkpoint。官方脚本当前标注主要支持 LLM 训练；不要直接把单个 checkpoint 当成完整可推理模型包。"));
            Add(train,Hint("训练完成后，把选定权重放回兼容的完整 CosyVoice3 模型目录。该目录至少应包含 cosyvoice3.yaml、llm.pt、flow.pt、hift.pt、campplus.onnx、speech_tokenizer_v3.onnx 和 CosyVoice-BlankEN。"));

            var use=Card(stack,"第 3 步 · 校验并在 agent语音中使用");
            Add(use,Hint("训练结果统一放在音色模型包库的“训练输出”目录。选择整理好的完整模型目录并校验，通过后到首页的“CosyVoice 训练相关参考”设置推理参考音频和准确台词，再回首页点击“应用当前设置的声音配置”试听。满意后从顶部“模型导入导出”打包为 .cosyvoice.ttsmodel。"));
            Add(use,Row(EntryBtn("选择训练完成的模型目录","选择并校验完整 CosyVoice3 模型目录，通过后填入首页。",SelectTrainedModel),EntryBtn("模型导入导出","导出当前训练音色模型包，或导入已有 .cosyvoice.ttsmodel。",()=>ShowPage("configuration")),EntryBtn("返回本地 TTS","回到首页应用模型包与语速；参考音频和台词在独立参考入口设置。",()=>ShowPage("home"))));

            var explain=Card(stack,"参考克隆与训练模型的区别");
            Add(explain,Hint("首页当前使用零样本参考克隆：一段参考音频 + 它的逐字台词作为条件。真正训练会用多段带台词的数据更新模型权重。当前程序仍以零样本接口调用训练后模型，因此首页的一段参考音频和逐字台词仍需填写；未来若加入 SFT 说话人 ID 推理，才可以对兼容的专属模型取消这项要求。"));
        }
        string TrainingWorkspace(){string directory=Path.Combine(VoicePackageDirectory(),"训练输出");Directory.CreateDirectory(directory);return directory;}
        void OpenTrainingWorkspace(){string directory=TrainingWorkspace();ServiceClient.Open(directory);Status("已打开音色模型包库内的训练输出目录；训练好的完整模型请保存在这里。");}
        void SelectTrainedModel(){
            using(var dialog=new FolderBrowserDialog{Description="从音色模型包库的训练输出中选择完整 CosyVoice3 模型目录",ShowNewFolderButton=false,SelectedPath=TrainingWorkspace()}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                string library=Path.GetFullPath(VoicePackageDirectory()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
                string selected=Path.GetFullPath(dialog.SelectedPath).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(!selected.StartsWith(library,StringComparison.OrdinalIgnoreCase)){Status("训练完成的模型必须放在音色模型包文件夹中；已为你打开对应目录。");ServiceClient.Open(TrainingWorkspace());return;}
                var probe=Config.voice.Clone();probe.engine="cosyvoice";probe.baseModel=Store.Portable(dialog.SelectedPath);ValidatePaths(probe);baseModelPath.Text=probe.baseModel;ShowPage("home");Status("模型目录校验通过，已填入“本地训练音色模型包”；请补全参考音频和台词后应用并试听。");
            }
        }
    }
}
