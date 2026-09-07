using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

internal static class ImageConversionSmoke
{
    public static int Run(string host, string worker, string scratch)
    {
        var root = Path.Combine(Path.GetFullPath(scratch), "images-" + Guid.NewGuid().ToString("N"));
        var inputs = Path.Combine(root, "inputs");
        Directory.CreateDirectory(inputs);
        var files = new[] { "alpha.png", "opaque.png", "damaged.jpg" }.Select(name => Path.Combine(inputs, name)).ToArray();
        for (var index = 0; index < 2; index++)
        {
            // GDI+ is independent of the production Magick.NET encoder.
            using var bitmap = new Bitmap(64, 48, PixelFormat.Format32bppArgb);
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                bitmap.SetPixel(x, y, Color.FromArgb(index == 0 && x < 32 ? (y < 24 ? 0 : 128) : 255,
                    x < 32 ? 230 : 35, y < 24 ? 60 : 200, 90));
            bitmap.Save(files[index], ImageFormat.Png);
        }
        File.WriteAllText(files[2], "Deliberately damaged media fixture. Never convert or delete this source.");
        var originals = files.Select(file => SHA256.HashData(File.ReadAllBytes(file))).ToArray();
        var trial = Path.Combine(root, "Access", "trial.json");
        var owned = new List<Process>();
        var requests = new List<string>();
        using var automation = new UIA3Automation { ConnectionTimeout = TimeSpan.FromSeconds(3), TransactionTimeout = TimeSpan.FromSeconds(3) };
        Window? conversion = null;
        Window? main = null;
        var checks = 0;
        try
        {
            Activate([files[0]]);
            main = WaitWindow("ContextSuiteWindow");
            conversion = WaitWindow("ConversionWindow");
            Wait(() => Control(conversion, "RefreshConversionPreview").IsEnabled, "initial source preview");
            conversion.SetForeground();
            Control(conversion, "ConversionTarget").Focus();
            Wait(() => conversion.FindAllDescendants().Any(e => e.Properties.HasKeyboardFocus.ValueOrDefault), "planner keyboard focus before input");
            Keyboard.Press(VirtualKeyShort.TAB); Keyboard.Release(VirtualKeyShort.TAB);
            Wait(() => conversion.FindAllDescendants().Any(e => e.Properties.HasKeyboardFocus.ValueOrDefault), "planner keyboard navigation");
            Check(true, "keyboard navigation retains focus within the conversion window");
            Check(!Control(conversion, "ConfirmImageConversion").IsEnabled && !File.Exists(trial), "explicit target required; opening planner/preview does not start trial");
            Control(conversion, "ConversionTarget").AsComboBox().Select(1);
            Wait(() => Control(conversion, "ConversionPlanStatus").Name.Contains("0 ready", StringComparison.Ordinal), "transparent JPEG matte gate");
            Check(!Control(conversion, "ConfirmImageConversion").IsEnabled, "transparent JPEG cannot convert without a background choice");
            Control(conversion, "ConversionMatte").AsComboBox().Select(1);
            RemoveMetadata(conversion);
            Wait(() => Control(conversion, "ConversionPlanStatus").Name.Contains("1 ready", StringComparison.Ordinal), "explicit unsupported metadata removal");
            Check(!Control(conversion, "ConfirmImageConversion").IsEnabled, "matte consequence requires acknowledgement");
            Toggle(conversion, "AcknowledgeConversionWarnings");
            Check(!Control(conversion, "ConfirmImageConversion").IsEnabled, "transparent JPEG needs a current matte preview even after consent");
            Control(conversion, "RefreshConversionPreview").AsButton().Invoke();
            Wait(() => Control(conversion, "ConfirmImageConversion").IsEnabled, "acknowledged JPEG plan");
            Control(conversion, "ConversionTarget").AsComboBox().Select(3);
            Check(!Control(conversion, "ConfirmImageConversion").IsEnabled, "BMP requires fresh matte-preview consent after switching targets");
            Toggle(conversion, "AcknowledgeConversionWarnings");
            Control(conversion, "RefreshConversionPreview").AsButton().Invoke();
            Wait(() => Control(conversion, "ConfirmImageConversion").IsEnabled, "BMP matte preview");
            Check(true, "BMP is selectable and its real encoded matte preview enables the plan");
            Capture(conversion, "bmp-planner");
            Control(conversion, "ConversionTarget").AsComboBox().Select(4);
            Toggle(conversion, "AcknowledgeConversionWarnings");
            Control(conversion, "RefreshConversionPreview").AsButton().Invoke();
            Wait(() => Control(conversion, "ConversionPreviewStatus").Name.StartsWith("Before / after", StringComparison.Ordinal), "TGA alpha preview");
            Check(Control(conversion, "ConfirmImageConversion").IsEnabled && !File.Exists(trial), "TGA alpha plan and preview are available without starting a trial");
            Capture(conversion, "tga-planner");
            Control(conversion, "ConversionTarget").AsComboBox().Select(1);
            Toggle(conversion, "AcknowledgeConversionWarnings");
            Control(conversion, "RefreshConversionPreview").AsButton().Invoke();
            Wait(() => Control(conversion, "ConfirmImageConversion").IsEnabled, "return to JPEG preview");
            Control(conversion, "ConversionQuality").AsTextBox().Text = "85";
            Wait(() => !Control(conversion, "ConfirmImageConversion").IsEnabled, "changed settings invalidate confirmation");
            Check(!Control(conversion, "AcknowledgeConversionWarnings").AsCheckBox().IsChecked.GetValueOrDefault(), "quality change clears prior consent");
            Control(conversion, "RefreshConversionPreview").AsButton().Invoke();
            Wait(() => Control(conversion, "ConversionPreviewStatus").Name.StartsWith("Before / after", StringComparison.Ordinal), "encoded matte preview");
            Check(!File.Exists(trial) && Directory.GetFiles(inputs).Length == 3, "encoded preview creates neither published files nor trial record");
            Control(conversion, "ConversionAdvanced").Patterns.ExpandCollapse.Pattern.Collapse();
            Wait(() =>
            {
                Control(conversion, "ConversionBody").Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);
                var bounds = Control(conversion, "BeforeImagePreview").BoundingRectangle;
                return bounds.Height > 0 && bounds.Bottom < conversion.BoundingRectangle.Bottom - 70 &&
                    bounds.Top > Control(conversion, "ConversionBody").BoundingRectangle.Top;
            }, "visible before/after panels after layout settles");
            Capture(conversion, "jpeg-matte-planner");
            Control(conversion, "CancelImageConversion").AsButton().Invoke();
            Wait(() => Control(main, "BatchSummary").Name.Contains("0 pending", StringComparison.Ordinal), "cancelled batch result");
            Check(!File.Exists(trial), "cancelling before confirmation leaves the trial unstarted");

            Activate(files);
            conversion = WaitWindow("ConversionWindow");
            Wait(() => Control(conversion, "RefreshConversionPreview").IsEnabled, "mixed-batch source preview");
            Control(conversion, "ConversionTarget").AsComboBox().Select(2);
            Toggle(conversion, "WebPLossless");
            RemoveMetadata(conversion);
            Wait(() => Control(conversion, "ConversionPlanStatus").Name.Contains("2 ready", StringComparison.Ordinal), "mixed batch applicability");
            Check(Control(conversion, "ConversionPlanStatus").Name.Contains("1 will be skipped", StringComparison.Ordinal), "damaged input remains a visible skipped member of the batch");
            Toggle(conversion, "AcknowledgeConversionWarnings");
            Wait(() => Control(conversion, "ConfirmImageConversion").IsEnabled, "mixed batch confirmation");
            Activate([files[1]]); // Forward while the previous immutable batch is still awaiting confirmation.
            Check(owned.Count == 3 && owned.Skip(1).All(p => p.HasExited), "repeated activations forward to one owner while planning");
            Control(conversion, "ConfirmImageConversion").AsButton().Invoke();
            Wait(() => File.Exists(Path.Combine(inputs, "alpha - Converted.webp")) && File.Exists(Path.Combine(inputs, "opaque - Converted.webp")), "two validated WebP copies");
            Check(File.Exists(trial), "first confirmed conversion creates only the isolated trial record");
            var initialTrial = JsonDocument.Parse(File.ReadAllText(trial));
            var started = initialTrial.RootElement.GetProperty("StartedUtc").GetString();
            initialTrial.Dispose();

            conversion = WaitWindow("ConversionWindow");
            Wait(() => Control(conversion, "RefreshConversionPreview").IsEnabled, "queued batch planner");
            Control(conversion, "ConversionTarget").AsComboBox().Select(1);
            Control(conversion, "ConversionMaximumDimension").AsTextBox().Text = "24";
            RemoveMetadata(conversion);
            Toggle(conversion, "AcknowledgeConversionWarnings");
            Wait(() => Control(conversion, "ConfirmImageConversion").IsEnabled, "opaque resized JPEG plan");
            Capture(conversion, "queued-resize-planner");
            Control(conversion, "ConfirmImageConversion").AsButton().Invoke();
            var jpeg = Path.Combine(inputs, "opaque - Converted.jpg");
            Wait(() => File.Exists(jpeg) && Control(main, "BatchSummary").Name.Contains("0 pending", StringComparison.Ordinal), "queued JPEG publication");
            using (var decoded = new Bitmap(jpeg)) Check(decoded.Width == 24 && decoded.Height == 18, "independent decoder verifies resize dimensions");
            using (var persisted = JsonDocument.Parse(File.ReadAllText(trial)))
                Check(persisted.RootElement.GetProperty("StartedUtc").GetString() == started, "later batch does not restart trial");
            Check(Control(main, "BatchSummary").Name.Contains("3 completed", StringComparison.Ordinal), "aggregate results report three completed outputs across queued batches");
            Capture(main, "conversion-results");

            var completedRows = Control(main, "BatchResults").FindAllChildren(cf => cf.ByControlType(ControlType.DataItem));
            completedRows.Last().Patterns.SelectionItem.Pattern.Select();
            Control(main, "RetrySelectedFiles").AsButton().Invoke();
            conversion = WaitWindow("ConversionWindow");
            Wait(() => Control(conversion, "RefreshConversionPreview").IsEnabled, "retry source preview");
            Check(!Control(conversion, "ConfirmImageConversion").IsEnabled &&
                conversion.FindAllDescendants().Any(e => e.Name?.Contains("opaque.png", StringComparison.Ordinal) == true),
                "retry opens a fresh explicit plan for the original source, not the converted file");
            Control(conversion, "CancelImageConversion").AsButton().Invoke();
            Wait(() => Control(main, "BatchSummary").Name.Contains("0 pending", StringComparison.Ordinal), "retry cancellation");

            main.SetForeground();
            var chooseFiles = Control(main, "ChooseConversionFiles");
            chooseFiles.Focus();
            Wait(() => chooseFiles.Properties.HasKeyboardFocus.ValueOrDefault, "file-picker button focus");
            Keyboard.Press(VirtualKeyShort.SPACE); Keyboard.Release(VirtualKeyShort.SPACE);
            AutomationElement? picker = null;
            Wait(() => (picker = automation.GetDesktop().FindFirstDescendant(cf => cf.ByProcessId(owned[0].Id)
                .And(cf.ByName("Choose images to convert")))) is not null, "native file picker");
            Capture(picker, "file-picker");
            var filename = picker!.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit).And(cf.ByName("File name:")))
                ?? throw new InvalidOperationException("Native file picker has no filename edit control.");
            // ValuePattern and the dialog's Open button do not require global
            // keyboard focus, which unrelated desktop windows can steal.
            filename.AsTextBox().Text = files[1];
            Wait(() => filename.AsTextBox().Text == files[1], "native filename value");
            Capture(picker, "file-picker-selected");
            var open = picker.FindFirstDescendant(cf => cf.ByAutomationId("1").And(cf.ByName("Open")))
                ?? throw new InvalidOperationException("Native picker has no Open action.");
            open.Patterns.Invoke.Pattern.Invoke(); // Windows exposes this action as a split button.
            conversion = WaitWindow("ConversionWindow");
            Wait(() => Control(conversion, "RefreshConversionPreview").IsEnabled, "file-picked source preview");
            Check(conversion.FindAllDescendants().Any(e => e.Name?.Contains("opaque.png", StringComparison.Ordinal) == true) &&
                !Control(conversion, "ConfirmImageConversion").IsEnabled, "native file selection opens a fresh conversion plan");
            Control(conversion, "CancelImageConversion").AsButton().Invoke();
            Wait(() => Control(main, "BatchSummary").Name.Contains("0 pending", StringComparison.Ordinal), "file-picked batch cancellation");
            using (var persisted = JsonDocument.Parse(File.ReadAllText(trial)))
                Check(persisted.RootElement.GetProperty("StartedUtc").GetString() == started && Directory.GetFiles(inputs).Length == 6,
                    "cancelled retry and file selection neither reset trial nor publish outputs");

            main.SetForeground();
            using (var dragSource = new FileDropSource(files[1], main.BoundingRectangle))
            {
                var source = automation.GetDesktop().FindFirstChild(cf => cf.ByName(FileDropSource.Title))
                    ?? throw new InvalidOperationException("Owned OLE drag source was not visible.");
                source.AsWindow().SetForeground();
                var dragButton = source.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Drag test file")))!;
                dragButton.Focus();
                Wait(() => dragButton.Properties.HasKeyboardFocus.ValueOrDefault, "owned drag button focus");
                Capture(source, "ole-source");
                var bounds = dragButton.BoundingRectangle;
                Mouse.MoveTo(new Point((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2)));
                Mouse.Down(MouseButton.Left);
                try
                {
                    Wait(() => dragSource.Started, "owned OLE drag start");
                    Mouse.MoveTo(new Point((int)main.BoundingRectangle.Left + 50, (int)main.BoundingRectangle.Top + 90));
                    Thread.Sleep(200); // Let the external OLE target process DragEnter/DragOver before releasing.
                }
                finally { Mouse.Up(MouseButton.Left); }
                Wait(() => dragSource.Completed, "owned OLE drag completion");
                Check(dragSource.Result == System.Windows.Forms.DragDropEffects.Copy, "main-window whitespace accepts a real OLE copy drop");
                conversion = WaitWindow("ConversionWindow");
                Wait(() => Control(conversion, "RefreshConversionPreview").IsEnabled, "OLE file-drop preview");
                Check(conversion.FindAllDescendants().Any(e => e.Name?.Contains("opaque.png", StringComparison.Ordinal) == true) &&
                    !Control(conversion, "ConfirmImageConversion").IsEnabled, "real cross-window OLE file drop creates a fresh explicit conversion plan");
                Control(conversion, "CancelImageConversion").AsButton().Invoke();
                Wait(() => Control(main, "BatchSummary").Name.Contains("0 pending", StringComparison.Ordinal), "dropped batch cancellation");
            }

            // Expire only this test host's isolated record; this is not a production access override.
            File.WriteAllText(trial, JsonSerializer.Serialize(new { SchemaVersion = 1,
                StartedUtc = DateTimeOffset.UtcNow.AddDays(-4), LastObservedUtc = DateTimeOffset.UtcNow }));
            Activate([files[1]]);
            conversion = WaitWindow("ConversionWindow");
            Wait(() => Control(conversion, "ConversionTrialStatus").Name.Contains("expired", StringComparison.OrdinalIgnoreCase), "expired local trial banner");
            Control(conversion, "ConversionTarget").AsComboBox().Select(2);
            RemoveMetadata(conversion);
            Toggle(conversion, "AcknowledgeConversionWarnings");
            Check(!Control(conversion, "ConfirmImageConversion").IsEnabled, "expiry blocks a new otherwise valid conversion");
            Capture(conversion, "expired-planner");
            Control(conversion, "CancelImageConversion").AsButton().Invoke();
            Control(main, "OpenSettings").AsButton().Invoke();
            var settings = WaitWindow("SettingsWindow");
            Check(Control(settings, "SaveSettings").IsEnabled, "settings remain accessible after expiry");
            Control(settings, "CancelSettings").AsButton().Invoke();
            main.SetForeground();
            completedRows.Last().Patterns.SelectionItem.Pattern.Select();
            Control(main, "OpenOutputFolder").AsButton().Invoke();
            Wait(() => IsFolderOpen(inputs), "actual completed output folder in Explorer");
            Check(true, "opening a completed output folder remains available after trial expiry");
            Wait(() => Control(main, "BatchSummary").Name.Contains("0 pending", StringComparison.Ordinal), "final queue idle");
            main.Close();
            Wait(() => owned[0].HasExited, "test application shutdown");
            for (var index = 0; index < files.Length; index++)
                Check(SHA256.HashData(File.ReadAllBytes(files[index])).SequenceEqual(originals[index]), "original bytes unchanged: " + Path.GetFileName(files[index]));
            Check(Directory.GetFiles(inputs).Length == 6, "only the three confirmed outputs were published");
            File.WriteAllText(Path.Combine(root, "result.txt"), $"PASS: {checks} conversion UI checks; actual app sources, isolated trial/settings, real worker.\n");
            Console.WriteLine($"PASS: {checks} conversion UI checks. Evidence: {root}");
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(root, "failure.txt"), error.ToString());
            Capture(conversion, "failure-planner"); Capture(main, "failure-main");
            Console.Error.WriteLine($"FAIL: {error.Message}\nEvidence: {root}");
            return 1;
        }
        finally
        {
            foreach (var process in owned)
            {
                try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } }
                catch (InvalidOperationException) { }
                process.Dispose();
            }
            foreach (var path in requests) File.Delete(path);
        }

        void Activate(string[] selection)
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextSuite", "Prototype", "Activations");
            Directory.CreateDirectory(directory);
            var id = Guid.NewGuid();
            var path = Path.Combine(directory, id.ToString("D") + ".request");
            requests.Add(path);
            File.WriteAllText(path, $"ContextSuiteActivation/1\nrequestId={id:D}\noperation=convert\naction=choose-format\npathCount={selection.Length}\n" +
                string.Concat(selection.Select(file => "path=" + file + "\n")), new UTF8Encoding(false));
            var start = new ProcessStartInfo(host) { UseShellExecute = false, RedirectStandardError = true };
            start.Environment["CONTEXTSUITE_TEST_ROOT"] = root;
            start.Environment["CONTEXTSUITE_TEST_WORKER"] = worker;
            start.ArgumentList.Add("--activation-file"); start.ArgumentList.Add(path);
            var process = Process.Start(start) ?? throw new IOException("Test host did not launch.");
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
            process.BeginErrorReadLine();
            owned.Add(process);
            Wait(() => !File.Exists(path), "normal activation consumption");
            if (owned.Count > 1) Wait(() => process.HasExited, "forwarding host exit");
        }

        Window WaitWindow(string id)
        {
            AutomationElement? found = null;
            Wait(() => (found = automation.GetDesktop().FindFirstDescendant(cf => cf.ByProcessId(owned[0].Id).And(cf.ByAutomationId(id)))) is not null, id);
            return found!.AsWindow();
        }
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks++; Console.WriteLine("PASS: UI: " + name);
        }
        void Capture(AutomationElement? element, string name)
        {
            if (element is null) return;
            try
            {
                using var capture = element.Capture();
                capture.Save(Path.Combine(root, name + ".png"));
                File.WriteAllLines(Path.Combine(root, name + "-uia.txt"), element.FindAllDescendants().Take(500).Select(e =>
                    $"{e.Properties.ControlType.ValueOrDefault} | {e.Properties.AutomationId.ValueOrDefault} | {e.Properties.Name.ValueOrDefault}"));
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(root, name + "-capture-error.txt"), error.Message); }
        }
    }

    private static AutomationElement Control(AutomationElement window, string id) =>
        window.FindFirstDescendant(cf => cf.ByAutomationId(id)) ?? throw new PendingControlException("Missing UI control: " + id);

    private sealed class PendingControlException(string message) : Exception(message);

    private static bool IsFolderOpen(string path)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", true)!)!;
        dynamic windows = shell.Windows();
        try
        {
            for (var index = 0; index < (int)windows.Count; index++)
            {
                dynamic window = windows.Item(index);
                try
                {
                    if (Uri.TryCreate((string)window.LocationURL, UriKind.Absolute, out var uri) && uri.IsFile &&
                        string.Equals(uri.LocalPath.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
                    // Modern Explorer's URL can lag navigation. Check the actual
                    // folder object too, never merely its displayed caption.
                    dynamic document = window.Document;
                    try
                    {
                        dynamic folder = document.Folder;
                        try
                        {
                            dynamic item = folder.Self;
                            try { if (string.Equals(((string)item.Path).TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true; }
                            finally { Marshal.FinalReleaseComObject(item); }
                        }
                        finally { Marshal.FinalReleaseComObject(folder); }
                    }
                    finally { Marshal.FinalReleaseComObject(document); }
                }
                catch (Exception error) when (error is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                { /* A closing/non-folder window must not hide a later matching folder. */ }
                finally { if (window is not null) Marshal.FinalReleaseComObject(window); }
            }
            return false;
        }
        finally { Marshal.FinalReleaseComObject(windows); Marshal.FinalReleaseComObject(shell); }
        // Leave the verified test tab open: Explorer may reuse a window containing user tabs.
    }

    private sealed class FileDropSource : IDisposable
    {
        public const string Title = "Context Suite test drag source";
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new();
        private System.Windows.Forms.Form? _form;
        public volatile bool Completed;
        public volatile bool Started;
        public System.Windows.Forms.DragDropEffects Result;
        public FileDropSource(string path, Rectangle bounds)
        {
            _thread = new Thread(() =>
            {
                using var form = new System.Windows.Forms.Form { Text = Title, TopMost = true, Width = 180, Height = 110,
                    StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new Point(bounds.Left + 200, bounds.Top + 200) };
                _form = form;
                var button = new System.Windows.Forms.Button { Text = "Drag test file", Dock = System.Windows.Forms.DockStyle.Fill };
                button.MouseDown += (_, _) =>
                {
                    Started = true;
                    Result = button.DoDragDrop(new System.Windows.Forms.DataObject(System.Windows.Forms.DataFormats.FileDrop,
                        new[] { path }), System.Windows.Forms.DragDropEffects.Copy);
                    Completed = true;
                };
                form.Controls.Add(button);
                form.Shown += (_, _) => { form.Activate(); form.BringToFront(); _ready.Set(); };
                System.Windows.Forms.Application.Run(form);
            }) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            if (!_ready.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Owned OLE source did not start.");
        }
        public void Dispose()
        {
            if (_form is { IsDisposed: false }) _form.BeginInvoke(() => _form.Close());
            _thread.Join(TimeSpan.FromSeconds(5));
            _ready.Dispose();
        }
    }

    private static void Toggle(Window window, string id) { Control(window, id).AsCheckBox().Toggle(); }

    private static void RemoveMetadata(Window window)
    {
        Control(window, "ConversionAdvanced").Patterns.ExpandCollapse.Pattern.Expand();
        Toggle(window, "RemoveImageMetadata");
    }

    private static void Wait(Func<bool> condition, string name)
    {
        var clock = Stopwatch.StartNew();
        Exception? lastLookupFailure = null;
        while (clock.Elapsed < TimeSpan.FromSeconds(40))
        {
            try { if (condition()) return; }
            catch (PendingControlException error) { lastLookupFailure = error; }
            Thread.Sleep(100);
        }
        throw new TimeoutException("Timed out waiting for " + name, lastLookupFailure);
    }
}
