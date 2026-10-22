using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AgentVoice {
    // Shared connection operations; both onboarding and the workspace show their own UI.
    internal sealed class ConnectionSetup {
        readonly SetupWorkflow flow;
        internal Action<string> Report=delegate{};
        internal Action<string> ManualPrompt=delegate{};
        internal ConnectionSetup(SetupWorkflow value){flow=value;}
        internal async Task<Dictionary<string,object>> Harness(string location){
            Report("1/3 · 识别目录并配置连接……");
            var r=Json.Object(await flow.Service.RunTool("connect_harness.py","--source auto --root "+ServiceClient.Quote(flow.Store.Root)+" --location "+ServiceClient.Quote(location)));
            string source=Json.Text(r,"source");if(source!="codex"&&source!="deepseek")throw new InvalidOperationException("仅支持实测的 Codex 与 DeepSeek Harness。其他客户端请按二次开发指南接入。");
            flow.Mark("mcpSource",source);
            Report("2/3 · 安装推荐的 MCP 控制并回读配置……");
            await flow.Service.RunTool("install_mcp.py","--source "+source+" --root "+ServiceClient.Quote(flow.Store.Root));
            Report("3/3 · 检查启用状态、协议和客户端工具回执……");
            return await CheckMcp(source);
        }
        internal async Task<Dictionary<string,object>> CheckMcp(string source){
            if(source!="codex"&&source!="deepseek")throw new InvalidOperationException("仅支持 Codex 与 DeepSeek Harness 的 MCP 自检。");
            var result=Json.Object(await flow.Service.RunTool("install_mcp.py","--source "+source+" --root "+ServiceClient.Quote(flow.Store.Root)+" --self-test"));
            bool passed=result.ContainsKey("passed")&&Convert.ToBoolean(result["passed"]);
            flow.Mark("mcp:"+source,passed?"verified":"pending");Report(Json.Text(result,"message"));return result;
        }
        internal async Task Airi(string directory){
            if(!File.Exists(Path.Combine(directory,"airi.exe")))throw new InvalidOperationException("请选择包含 airi.exe 的原 AIRI 安装目录。");
            Report("1/3 · 安装原 AIRI 目录的桥接，保留 app.asar 备份……");
            await flow.Service.RunTool("prepare_airi.py","--source "+ServiceClient.Quote(directory)+" --root "+ServiceClient.Quote(flow.Store.Root));
            var c=flow.Config;c.airi.directory=flow.Store.Portable(directory);c.airi.enabled=true;c.airi.output="airi";await flow.Save(c,new{airi=c.airi});
            string registry=flow.Store.Resolve("data/airi-installation.json");Directory.CreateDirectory(Path.GetDirectoryName(registry));string temp=registry+".tmp";File.WriteAllText(temp,Json.Encode(new{directory=directory}));if(File.Exists(registry))File.Replace(temp,registry,null);else File.Move(temp,registry);
            OpenAiri(directory);Report("2/3 · 等待所选 AIRI 的播放桥连通……");bool online=false;
            for(int i=0;i<120;i++){var h=await flow.Service.Request("/health");if(Convert.ToBoolean(h["airiConnected"])){online=true;break;}await Task.Delay(500);}
            if(!online)throw new InvalidOperationException("AIRI 桥接未在一分钟内连通。请退出其他版本，打开所选的已登录实例，再重新验证。桥接文件已安装不代表连接成功。");
            await flow.Service.Request("/api/verify/airi",new{});Report("3/3 · 等待桌宠实际播放完成与嘴型回执……");await Wait("airi");flow.Mark("airi","verified");Report("AIRI 实际播放验证通过。AIRI 更新可能覆盖桥接；失效后重新接入验证。");
        }
        async Task Wait(string source){for(int i=0;i<240;i++){var r=await flow.Service.Request("/api/verify/"+source,null,3000);string state=Json.Text(r,"state");if(state=="verified")return;if(state=="failed")throw new InvalidOperationException("真实播放验证失败："+Json.Text(r,"error"));if(state=="playing")Report("已收到验证正文，正在生成并播放……");await Task.Delay(500);}throw new InvalidOperationException("两分钟内未收到实际播放完成回执。请检查登录、连接器重载与音色环境后重试；当前未标记成功。");}
        [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr h,int state);
        [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
        internal static void OpenAiri(string directory){string exe=Path.Combine(directory,"airi.exe");foreach(var p in Process.GetProcessesByName("airi"))try{if(p.MainModule.FileName.Equals(exe,StringComparison.OrdinalIgnoreCase)){if(p.MainWindowHandle!=IntPtr.Zero){ShowWindow(p.MainWindowHandle,9);SetForegroundWindow(p.MainWindowHandle);}return;}}catch(System.ComponentModel.Win32Exception){}if(!File.Exists(exe))throw new InvalidOperationException("所选目录中没有 airi.exe。");Process.Start(new ProcessStartInfo(exe){WorkingDirectory=directory,UseShellExecute=true});}
    }
}
