import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"service"))
from state import Store

class McpControlTests(unittest.TestCase):
    def test_codex_chat_entry_plugin_is_packaged(self):
        root=Path(__file__).resolve().parents[1]
        market=json.loads((root/'.agents/plugins/marketplace.json').read_text(encoding='utf-8'))
        self.assertEqual(market['plugins'][0]['name'],'agent-voice-control')
        plugin=root/'.agents/plugins/agent-voice-control'
        manifest=json.loads((plugin/'plugin.json').read_text(encoding='utf-8'))
        self.assertEqual(manifest['extensions']['com.openai']['interface']['brandColor'],'#0B9FD8')
        skill=(plugin/'skills/agent-voice-control/SKILL.md').read_text(encoding='utf-8')
        self.assertIn('agent_voice',skill)

    def test_stdio_initialization_tools_and_profile_listing(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary);store=Store(root)
            voice=store.get()["voice"];store.update({"profiles":[{"id":"voice-one","name":"声音一","voice":voice}]})
            server=Path(__file__).resolve().parents[1]/"integrations/agent_voice_mcp.py"
            process=subprocess.Popen([sys.executable,str(server),"--root",str(root),"--host","codex"],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding="utf-8")
            def request(ident,method,params=None):
                process.stdin.write(json.dumps({"jsonrpc":"2.0","id":ident,"method":method,"params":params or {}})+"\n");process.stdin.flush()
                return json.loads(process.stdout.readline())
            initialized=request(1,"initialize",{"protocolVersion":"2025-06-18","clientInfo":{"name":"test","version":"1"},"capabilities":{}})
            self.assertEqual(initialized["result"]["serverInfo"]["name"],"agent-voice-control")
            listing=request(2,"tools/list")["result"]["tools"]
            self.assertIn("stop_speech",[tool["name"] for tool in listing])
            result=request(3,"tools/call",{"name":"list_voice_profiles","arguments":{}})["result"]["structuredContent"]
            self.assertEqual(result["profiles"][0]["id"],"voice-one")
            process.stdin.close();process.wait(timeout=5);process.stdout.close();process.stderr.close()

    def test_old_unverified_harness_switches_are_migrated_out(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary);store=Store(root);saved=store.get();saved["harness"].update({"claude":True,"pi":True,"opencode":True})
            store.path.write_text(json.dumps(saved),encoding="utf-8")
            self.assertEqual(set(Store(root).get()["harness"]),{"codex","deepseek"})

if __name__=="__main__":unittest.main()
