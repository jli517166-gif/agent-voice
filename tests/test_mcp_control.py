import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch,Mock
import importlib.util

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"service"))
from state import Store

class McpControlTests(unittest.TestCase):
    def mcp_module(self):
        file=Path(__file__).resolve().parents[1]/'integrations/agent_voice_mcp.py'
        spec=importlib.util.spec_from_file_location('agent_voice_mcp',file)
        module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
        return module

    def test_reading_tool_uses_exact_text_and_never_starts_on_open(self):
        module=self.mcp_module()
        with tempfile.TemporaryDirectory() as temporary:
            voice=module.AgentVoice(temporary,'codex');voice.running=Mock(return_value=None)
            voice.start=Mock();voice.request=Mock(return_value={'ok':True,'jobId':'job'})
            voice.settings=Mock(return_value={'harness':{'codex':True}})
            result=voice.call('open_voice_player',{'text':'只读这一段。'})
            self.assertEqual(result['text'],'只读这一段。');voice.start.assert_not_called();voice.request.assert_not_called()
            result=voice.call('read_text',{'text':'工作好久啦，起来动一动、喝口水吧～'})
            voice.start.assert_called_once()
            voice.request.assert_called_once_with('/speak',{'text':'工作好久啦，起来动一动、喝口水吧～','source':'mcp-read-aloud','play':True},timeout=30)
            self.assertEqual(result['jobId'],'job')

    def test_invalid_text_never_starts_service(self):
        module=self.mcp_module();voice=module.AgentVoice('.','codex');voice.start=Mock()
        for text in ('',None,' '*3,'x'*4001,123):
            with self.assertRaises(ValueError):voice.call('read_text',{'text':text})
        voice.start.assert_not_called()

    def test_deepseek_mcp_install_refreshes_actual_composer_bundle(self):
        from tools import install_mcp
        with tempfile.TemporaryDirectory() as temporary:
            base=Path(temporary);root=base/'voice';store=Store(root);home=base/'home';location=base/'harness';location.mkdir()
            profile=home/'.dsh/profiles/desktop';profile.mkdir(parents=True);(profile/'cordis.patch.yml').write_text('[]',encoding='utf-8')
            source=root/'integrations/deepseek-control-client.js';source.parent.mkdir();source.write_text('// dsh-agent-voice-public\nconst version=1;\n',encoding='utf-8')
            (root/'data/harness-installations.json').write_text(json.dumps({'deepseek':{'directory':str(location),'deepseekProfiles':[str(profile)]}}),encoding='utf-8')
            with patch.object(install_mcp,'find_deepseek',return_value=(location/'dsh.cmd',profile)),patch.object(install_mcp,'run',return_value='0.2.0-rc.2'),patch.object(Path,'home',return_value=home):
                install_mcp.install_deepseek(store,base/'python.exe',root/'integrations/agent_voice_mcp.py')
                source.write_text('// dsh-agent-voice-public\nconst version=2;\n',encoding='utf-8')
                result=install_mcp.install_deepseek(store,base/'python.exe',root/'integrations/agent_voice_mcp.py')
            actual=profile/'node_modules/dsh-agent-voice-public/client.js'
            self.assertEqual(actual.read_text(encoding='utf-8'),source.read_text(encoding='utf-8'))
            self.assertTrue(result['clientSha256']);self.assertTrue(result['connector']['requiresHarnessReload'])

    def test_codex_chat_entry_plugin_is_packaged(self):
        root=Path(__file__).resolve().parents[1]
        market=json.loads((root/'.agents/plugins/marketplace.json').read_text(encoding='utf-8'))
        self.assertEqual(market['plugins'][0]['name'],'agent-voice-control')
        plugin=root/'.agents/plugins/agent-voice-control'
        manifest=json.loads((plugin/'plugin.json').read_text(encoding='utf-8'))
        self.assertEqual(manifest['extensions']['com.openai']['interface']['brandColor'],'#0B9FD8')
        interface=manifest['extensions']['com.openai']['interface']
        for name in ('composerIcon','logo'):
            self.assertTrue((plugin/interface[name]).is_file(),name+' must resolve to a bundled image')
        skill=(plugin/'skills/agent-voice-control/SKILL.md').read_text(encoding='utf-8')
        self.assertIn('agent_voice',skill)

    def test_stdio_initialization_tools_and_profile_listing(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary);store=Store(root)
            voice=store.get()["voice"];store.update({"profiles":[{"id":"voice-one","name":"声音一","voice":voice}]})
            server=Path(__file__).resolve().parents[1]/"integrations/agent_voice_mcp.py"
            process=subprocess.Popen([sys.executable,str(server),"--root",str(root),"--host","codex"],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding="utf-8")
            def cleanup():
                if not process.stdin.closed:process.stdin.close()
                try:process.wait(timeout=5)
                except subprocess.TimeoutExpired:process.kill();process.wait(timeout=5)
                process.stdout.close();process.stderr.close()
            self.addCleanup(cleanup)
            def request(ident,method,params=None):
                process.stdin.write(json.dumps({"jsonrpc":"2.0","id":ident,"method":method,"params":params or {}})+"\n");process.stdin.flush()
                return json.loads(process.stdout.readline())
            initialized=request(1,"initialize",{"protocolVersion":"2025-06-18","clientInfo":{"name":"test","version":"1"},"capabilities":{}})
            self.assertEqual(initialized["result"]["serverInfo"]["name"],"agent-voice-control")
            listing=request(2,"tools/list")["result"]["tools"]
            self.assertIn("stop_speech",[tool["name"] for tool in listing])
            result=request(3,"tools/call",{"name":"list_voice_profiles","arguments":{}})["result"]["structuredContent"]
            self.assertEqual(result["profiles"][0]["id"],"voice-one")
            panel=next(tool for tool in listing if tool['name']=='open_voice_player')
            uri=panel['_meta']['ui']['resourceUri']
            resources=request(4,'resources/list')['result']['resources']
            self.assertEqual(resources[0]['uri'],uri)
            resource=request(5,'resources/read',{'uri':uri})['result']['contents'][0]
            self.assertEqual(resource['mimeType'],'text/html;profile=mcp-app')
            self.assertIn('ui/initialize',resource['text']);self.assertNotIn(store.get()['token'],resource['text'])
            self.assertIn('error',request(6,'resources/read',{'uri':'file:///data/settings.json'}))
            observation=json.loads((root/'data/logs/mcp-ui-client-codex.json').read_text(encoding='utf-8'))
            self.assertEqual(observation['declaration'],'not-declared');self.assertFalse(observation['renderVerified'])
            process.stdin.close();process.wait(timeout=5);process.stdout.close();process.stderr.close()

    def test_old_unverified_harness_switches_are_migrated_out(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary);store=Store(root);saved=store.get();saved["harness"].update({"claude":True,"pi":True,"opencode":True})
            store.path.write_text(json.dumps(saved),encoding="utf-8")
            self.assertEqual(set(Store(root).get()["harness"]),{"codex","deepseek"})

if __name__=="__main__":unittest.main()
