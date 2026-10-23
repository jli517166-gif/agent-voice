using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        static readonly string[] WindowLayoutPages = {
            "home", "modules", "voices", "connections", "settings", "training",
            "voiceEditor", "harness", "airi", "connectionGuide", "advancedTraining"
        };

        static object Geometry(Control control) {
            return new {
                name = control.Name, type = control.GetType().Name, bounds = control.Bounds,
                client = control.ClientSize, display = control.DisplayRectangle,
                dock = control.Dock.ToString(), font = control.Font.Size,
                autoSize = control.AutoSize, minimum = control.MinimumSize,
                maximum = control.MaximumSize,
                scrollMinimum = control is ScrollableControl?((ScrollableControl)control).AutoScrollMinSize:Size.Empty
            };
        }

        static string LayoutFingerprint(Control control) {
            var result = new StringBuilder();
            result.Append(control.GetType().Name).Append(control.Bounds)
                .Append(control.Font.Size.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
            // Hidden native helpers may acquire their preferred height when a handle
            // is created. Compare the visible layout users can actually interact with.
            foreach (Control child in control.Controls)
                if (child.Visible) result.Append(LayoutFingerprint(child));
            return result.ToString();
        }

        static void CheckWindowContent(Control parent, string tag, List<string> errors) {
            foreach (Control control in parent.Controls) {
                if (!control.Visible) continue;
                var viewport=parent as ScrollableControl;
                Rectangle document=viewport!=null&&viewport.AutoScroll?viewport.DisplayRectangle:parent.ClientRectangle;
                if(control.Right>document.Right+1||control.Bottom>document.Bottom+1)
                    errors.Add(tag+": child exceeds its container: "+control.Text+" "+control.Bounds+" vs "+document);
                if ((control is Label || control is AnimeButton || control is CheckBox || control is System.Windows.Forms.Button || control is TextBoxBase || control is ComboBox)
                    && control.Font.Size < 10.4F)
                    errors.Add(tag + ": text below readable size: " + control.Text + " " + control.Font.Size);
                var label = control as Label;
                if (label != null) {
                    if (label.Font.Size > 26.1F)
                        errors.Add(tag + ": viewport enlarged font beyond design size: " + label.Text);
                    foreach (Control other in parent.Controls) {
                        if (!other.Visible || other == control ||
                            !(other is ComboBox || other is TextBoxBase || other is AnimeButton || other is System.Windows.Forms.Button)) continue;
                        var overlap = Rectangle.Intersect(control.Bounds, other.Bounds);
                        if (overlap.Width > 2 && overlap.Height > 2)
                            errors.Add(tag + ": text covers input/action: " + label.Text + " " +
                                label.Bounds + " / " + other.Text + " " + other.Bounds);
                    }
                }
                if (control.Controls.Count > 0) CheckWindowContent(control, tag, errors);
            }
        }

        void RecordWindowLayout(string id, string mode, List<object> layouts,
            List<string> errors, string directory, Dictionary<string, string> defaults) {
            ShowPage(id);
            PerformLayout();
            Application.DoEvents();
            var current = pages[id];
            string tag = mode + "/" + id;
            if (shell.Bounds != ClientRectangle)
                errors.Add(tag + ": shell does not fill client: " + shell.Bounds + " vs " + ClientRectangle);
            if (outer.Bounds != shell.ClientRectangle)
                errors.Add(tag + ": outer does not fill shell: " + outer.Bounds + " vs " + shell.ClientRectangle);

            var outside = new List<object>();
            foreach (Control control in current.Controls) {
                if (!control.Visible) continue;
                // A short viewport may scroll, but every control must remain in
                // the scrollable document (never shrink its font to make it fit).
                var document=current.AutoScroll?current.DisplayRectangle:current.ClientRectangle;
                if (control.Right <= document.Right + 1 && control.Bottom <= document.Bottom + 1) continue;
                outside.Add(new {text = control.Text, type = control.GetType().Name, bounds = control.Bounds});
                errors.Add(tag + ": control outside page: " + control.Text + " " + control.Bounds + " vs " + current.ClientSize);
            }
            CheckWindowContent(current, tag, errors);
            var viewportScreen=content.RectangleToScreen(content.ClientRectangle);
            var statusScreen=footer.RectangleToScreen(footer.ClientRectangle);
            if(Rectangle.Intersect(viewportScreen,statusScreen).Height>0||statusScreen.Top-viewportScreen.Bottom<UiPx(8))
                errors.Add(tag+": status row overlaps the page or has no separation");
            if(id=="connections"){
                var last=current.Controls.Cast<Control>().SelectMany(c=>c.Controls.Cast<Control>()).OfType<AnimeButton>().First(b=>b.Text=="开发其他客户端连接器");
                current.AutoScrollPosition=new Point(0,current.DisplayRectangle.Height);Application.DoEvents();
                var actionScreen=last.RectangleToScreen(last.ClientRectangle);
                if(!viewportScreen.Contains(actionScreen)||Rectangle.Intersect(actionScreen,statusScreen).Height>0)
                    errors.Add(tag+": final connection action cannot be fully reached above status row");
                if(mode=="default"||mode=="minimum"||mode=="maximized")WebSubpageShot(id,directory,mode+"-connection-bottom");
                current.AutoScrollPosition=Point.Empty;Application.DoEvents();
            }
            if (sidebar.Width < 180 * InterfaceScale)
                errors.Add(tag + ": navigation too narrow for display scaling");
            var navigation = navigationButtons["home"];
            var center = sidebar.PointToClient(navigation.PointToScreen(new Point(navigation.Width / 2, navigation.Height / 2)));
            var front = sidebar.GetChildAtPoint(center);
            if (front != null && !front.Contains(navigation))
                errors.Add(tag + ": navigation is covered by a sibling background");

            if (mode == "default") defaults[id] = LayoutFingerprint(current);
            if (mode == "restore" && defaults[id] != LayoutFingerprint(current))
                {
                File.WriteAllText(Path.Combine(directory, "restore-"+id+"-before.txt"), defaults[id]);
                File.WriteAllText(Path.Combine(directory, "restore-"+id+"-after.txt"), LayoutFingerprint(current));
                errors.Add(tag + ": resizing accumulated layout or font changes");
            }

            layouts.Add(new {
                mode = mode, page = id, windowState = WindowState.ToString(), client = ClientSize,
                scale = AutoScaleDimensions, currentScale = CurrentAutoScaleDimensions,
                canvasReference = WebFit.CanvasFor(current), shell = Geometry(shell),
                outer = Geometry(outer), sidebar = Geometry(sidebar), workspace = Geometry(workspace),
                content = Geometry(content), pageGeometry = Geometry(current),
                elements = current.Controls.Cast<Control>().Where(c => c.Visible)
                    .Select(c => new {text = c.Text, geometry = Geometry(c)}).ToArray(), outside = outside
            });
            // PrintWindow preserves actual child z-order. DrawToBitmap reverses it.
            if (id == "home" || id == "settings") WebSubpageShot(id, directory, mode + "-" + id);
        }

        internal object WindowLayoutReport(string directory) {
            Directory.CreateDirectory(directory);
            var layouts = new List<object>();
            var errors = new List<string>();
            var defaults = new Dictionary<string, string>();
            foreach (string mode in new[] {"initial", "default", "large-normal", "maximized", "restore", "minimum", "maximize-again"}) {
                WindowState = FormWindowState.Normal;
                if (mode == "default" || mode == "restore") ClientSize = new Size(UiPx(1180), UiPx(780));
                if (mode == "large-normal") ClientSize = new Size(UiPx(1440), UiPx(860));
                if (mode == "minimum") Size = MinimumSize;
                if (mode == "maximized" || mode == "maximize-again") WindowState = FormWindowState.Maximized;
                PerformLayout();
                Application.DoEvents();
                foreach (string id in WindowLayoutPages)
                    RecordWindowLayout(id, mode, layouts, errors, directory, defaults);
            }

            // A successful MCP refresh changes the text after initial layout.
            // Exercise populated statuses, not just the short offline placeholder.
            foreach(string source in new[]{"codex","deepseek"})mcpPassed[source]=true;
            UpdateHarnessIndicators(null,true);
            foreach(bool maximize in new[]{false,true}){
                WindowState=FormWindowState.Normal;ClientSize=new Size(UiPx(1180),UiPx(780));
                if(maximize)WindowState=FormWindowState.Maximized;
                RecordWindowLayout("home",maximize?"mcp-connected-maximized":"mcp-connected-normal",layouts,errors,directory,defaults);
            }

            string originalName = homeVoiceSummary.Text;
            try {
                homeVoiceSummary.Text = "CosyVoice · " + string.Concat(Enumerable.Repeat("很长的自定义音色名称", 20));
                foreach (bool maximized in new[] {false, true}) {
                    WindowState = FormWindowState.Normal;
                    ClientSize = new Size(UiPx(1180), UiPx(780));
                    if (maximized) WindowState = FormWindowState.Maximized;
                    RecordWindowLayout("home", maximized ? "long-name-maximized" : "long-name-normal",
                        layouts, errors, directory, defaults);
                }
            } finally {homeVoiceSummary.Text = originalName;}

            // Exercise a generated background fixture, never personal appearance files.
            string originalBackground = Config.appearance.background;
            string fixture = Path.Combine(Path.GetFullPath(directory), "background-fixture.png");
            Color expected = Color.FromArgb(31, 44, 65);
            using (var bitmap = new Bitmap(32, 32)) {
                using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(expected);
                bitmap.Save(fixture);
            }
            try {
                Config.appearance.background = fixture;
                ApplyTheme();
                foreach (bool maximized in new[] {false, true}) {
                    WindowState = FormWindowState.Normal;
                    ClientSize = new Size(UiPx(1180), UiPx(780));
                    if (maximized) WindowState = FormWindowState.Maximized;
                    foreach (string id in new[] {"home", "settings"})
                        RecordWindowLayout(id, maximized ? "background-maximized" : "background-normal",
                            layouts, errors, directory, defaults);
                    using (var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height)) {
                        using (var graphics = Graphics.FromImage(bitmap)) PaintScene(graphics, shell);
                        foreach (Point point in new[] {Point.Empty, new Point(bitmap.Width - 1, 0),
                            new Point(0, bitmap.Height - 1), new Point(bitmap.Width - 1, bitmap.Height - 1)})
                            if (Math.Abs(bitmap.GetPixel(point.X,point.Y).R-(expected.R*35+BackColor.R*220)/255)>1 || Math.Abs(bitmap.GetPixel(point.X,point.Y).G-(expected.G*35+BackColor.G*220)/255)>1 || Math.Abs(bitmap.GetPixel(point.X,point.Y).B-(expected.B*35+BackColor.B*220)/255)>1)
                                errors.Add("custom background left an unpainted client corner: " + point+" actual="+bitmap.GetPixel(point.X,point.Y)+" expected="+expected);
                    }
                }
            } finally {Config.appearance.background = originalBackground; ApplyTheme();}

            var result = new {ok = errors.Count == 0, checks = layouts.Count, errors = errors, layouts = layouts};
            File.WriteAllText(Path.Combine(directory, "window-layout.json"), Json.Encode(result));
            return result;
        }
    }

    static class WindowLayoutRunner {
        internal static int Run(SettingsStore store, string[] args) {
            string directory = args.Length > 1 ? args[1] : store.Resolve("acceptance/window-layout");
            Directory.CreateDirectory(directory);
            try {
                using (var form = new MainForm(store, true)) {
                    form.ShowInTaskbar = false;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-32000, -32000);
                    form.Show();
                    Application.DoEvents();
                    string json = Json.Encode(form.WindowLayoutReport(directory));
                    return json.Contains("\"ok\":true") ? 0 : 1;
                }
            } catch (Exception error) {
                File.WriteAllText(Path.Combine(directory, "error.txt"), error.ToString());
                return 1;
            }
        }
    }
}
