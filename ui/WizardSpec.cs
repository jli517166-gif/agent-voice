using System.Collections.Generic;

namespace AgentVoice {
    internal sealed class WizardBox {
        public int x {get;set;}
        public int y {get;set;}
        public int w {get;set;}
        public int h {get;set;}
    }
    internal sealed class WizardItem {
        public string text {get;set;}
        public WizardBox box {get;set;}
    }
    internal sealed class WizardPageSpec {
        public int step {get;set;}
        public WizardItem title {get;set;}
        public WizardItem lead {get;set;}
        public List<WizardItem> buttons {get;set;}
        public WizardBox stage {get;set;}
    }
}
