# 公共界面素材

这些是软件界面素材，和 TTS 音色、推理参考录音没有关系。

`keynote-voice.png` 是本轮通过内置 image_gen 工具新生成的玻璃／金属声浪。没有使用用户录音、肖像或私有音色素材。正式引用保存在本仓库，不依赖生成工具的缓存路径。

生成提示词：

> Create a premium abstract 3D hero artwork for Agent Voice, a local voice studio desktop app, in the visual language of an ambitious product launch keynote. NO TEXT, NO LETTERS, NO LOGO, NO UI. One large sculptural waveform made of luminous cyan-blue glass and brushed silver ribbons, shaped like sound acquiring a physical form. Refined realistic material reflections, black studio void, a restrained icy-blue pool of light under the floating object, cinematic side illumination, substantial negative space around the sculpture. Widescreen 16:9 composition, sculpture centered, clean silhouette that remains recognizable when used beside large typography. Color palette black, white, cyan and electric blue only. Beautiful product render, depth and precise edges, no small icon, no circuit board, no purple, no people, no bells, no watermarks. Intended as a reusable hero art layer; keep all typography in the app code.

`sfx/step-0.wav` 至 `sfx/step-8.wav` 为原创程序合成电音。可运行 `python tools/generate_ui_sounds.py` 重现；标准库合成，不下载第三方采样，不调用 TTS、不含人声，32 kHz、单声道、16 位 PCM、约 0.38 秒，峰值限制在 0.13。多个快速切页只保留最新一次提示音。

软件本体制作工具只额外允许这九个固定名称的 WAV，不因增加界面音效而允许任意用户 WAV 进入软件本体包。不要把用户背景、声音录音或音色包放进这个公共目录。
