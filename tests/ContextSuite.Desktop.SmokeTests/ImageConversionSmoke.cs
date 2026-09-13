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
using System.Threading;
using FlaUI.Core.AutomationElements;
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
        File.WriteAllText(Path.Combine(root, "settings.json"), "{\"SchemaVersion\":2,\"Convert\":{\"ReplaceOriginals\":false},\"Optimize\":{\"ReplaceOriginals\":false},\"PlayCompletionSound\":false}");
        var owned = new List<Process>();
        var requests = new List<string>();
        using var automation = new UIA3Automation { ConnectionTimeout = TimeSpan.FromSeconds(3), TransactionTimeout = TimeSpan.FromSeconds(3) };
        Window? conversion = null;
        Window? main = null;
        var checks = 0;
        try
        {
            // Analyze keeps the compact report open while forwarded commands run.
            Activate("analyze", "open-details", [files[0]]);
            main = WaitWindow("ContextSuiteWindow");
            Wait(() => IsIdle(main), "read-only analysis completion");
            Check(!File.Exists(trial), "Analyze does not start a trial");
            Check(main.FindFirstDescendant(cf => cf.ByAutomationId("MoreTools")) is null,
                "compact report has no manual workspace");

            Activate("convert", "dds", [files[0]]);
            conversion = WaitWindow("ConversionWindow");
            Wait(() => Control(conversion, "RefreshConversionPreview").IsEnabled, "DDS source loaded");
            Check(!Control(conversion, "ConfirmImageConversion").IsEnabled,
                "DDS requires explicit source interpretation");
            var target = conversion.FindFirstDescendant(cf => cf.ByAutomationId("ConversionTarget"));
            Check(target is null || target.IsOffscreen, "direct DDS prompt does not expose an unrelated target selector");
            Control(conversion, "DdsInterpretation").AsComboBox().Select(2);
            Control(conversion, "DdsMetadataPolicy").AsComboBox().Select(1);
            Wait(() => Control(conversion, "ConversionPlanStatus").Name.Contains("1 file(s) selected", StringComparison.Ordinal), "explicit DDS plan");
            Toggle(conversion, "AcknowledgeConversionWarnings");
            Wait(() => Control(conversion, "ConfirmImageConversion").IsEnabled, "automatic actual DDS preview");
            Check(!File.Exists(trial) && Control(conversion, "ConversionPreviewStatus").Name.StartsWith("DDS after", StringComparison.Ordinal),
                "DDS preview does not start trial or publish output");
            Capture(conversion, "dds-decision");
            Control(conversion, "CancelImageConversion").AsButton().Invoke();
            Wait(() => IsIdle(main), "DDS cancellation");

            Activate("convert", "jpeg", [files[0]]);
            conversion = WaitWindow("ConversionWindow");
            Wait(() => Control(conversion, "ConversionMatte").IsEnabled, "transparent JPEG prompt");
            Check(!Control(conversion, "ConfirmImageConversion").IsEnabled, "transparent JPEG requires a background");
            Check(conversion.FindFirstDescendant(cf => cf.ByAutomationId("ConversionQuality")) is null,
                "fixed conversion defaults have no quality editor");
            Control(conversion, "ConversionMatte").AsComboBox().Select(1);
            Toggle(conversion, "AcknowledgeConversionWarnings");
            Wait(() => Control(conversion, "ConfirmImageConversion").IsEnabled, "automatic matte preview");
            Check(!File.Exists(trial) && Directory.GetFiles(inputs).Length == 3, "decision previews create no output or trial");
            conversion.SetForeground();
            Control(conversion, "ConversionMatte").Focus();
            Wait(() => Control(conversion, "ConversionMatte").Properties.HasKeyboardFocus.ValueOrDefault, "matte keyboard focus");
            Keyboard.Press(VirtualKeyShort.TAB); Keyboard.Release(VirtualKeyShort.TAB);
            Wait(() => conversion.FindAllDescendants().Any(e => e.Properties.HasKeyboardFocus.ValueOrDefault), "decision keyboard traversal");
            Capture(conversion, "matte-decision");
            Keyboard.Press(VirtualKeyShort.ESCAPE); Keyboard.Release(VirtualKeyShort.ESCAPE);
            Wait(() => IsIdle(main), "Escape cancels the decision");
            Check(!File.Exists(trial), "Escape before confirmation leaves trial unstarted");

            Activate("convert", "webp", files);
            Activate("convert", "jpeg", [files[1]]);
            var jpeg = Path.Combine(inputs, "opaque - Converted.jpg");
            Wait(() => File.Exists(jpeg) && IsIdle(main), "direct queued conversion completion");
            Check(File.Exists(Path.Combine(inputs, "alpha - Converted.webp")) && File.Exists(Path.Combine(inputs, "opaque - Converted.webp")),
                "direct WebP publishes both valid copies despite damaged input");
            Check(automation.GetDesktop().FindFirstDescendant(cf => cf.ByProcessId(owned[0].Id).And(cf.ByAutomationId("ConversionWindow"))) is null,
                "routine direct actions leave no conversion dialog");
            Check(owned.Skip(1).All(process => process.HasExited), "forwarded activations retain one owner");
            using (var decoded = new Bitmap(jpeg)) Check(decoded.Width == 64 && decoded.Height == 48, "fixed defaults preserve original dimensions");
            Check(File.Exists(trial), "first direct conversion starts isolated trial");
            Check(Control(main, "BatchSummary").Name.Contains("3 completed", StringComparison.Ordinal), "compact status reports the three successful outputs");
            Capture(main, "direct-results");
            var tryAgain = main.FindFirstDescendant(cf => cf.ByAutomationId("RetrySelectedFiles"));
            Check(tryAgain is null || tryAgain.IsOffscreen,
                "unsupported input does not offer an ineffective Try again action");

            File.WriteAllText(trial, JsonSerializer.Serialize(new { SchemaVersion = 1,
                StartedUtc = DateTimeOffset.UtcNow.AddDays(-8), LastObservedUtc = DateTimeOffset.UtcNow }));
            Activate("convert", "tga", [files[1]]);
            Wait(() => IsIdle(main), "expired direct conversion completion");
            Check(!File.Exists(Path.Combine(inputs, "opaque - Converted.tga")) && Control(main, "OpenLicense").IsEnabled,
                "expired direct conversion keeps files and exposes License");
            Control(main, "RetrySelectedFiles").AsButton().Invoke();
            Wait(() => IsIdle(main), "expired command retry completes");
            Check(!File.Exists(Path.Combine(inputs, "opaque - Converted.tga")) &&
                !Directory.EnumerateFiles(inputs, "*Converted (2)*").Any(),
                "Try again respects expired access and does not repeat successful files");
            Control(main, "OpenSettings").AsButton().Invoke();
            var settings = WaitWindow("SettingsWindow");
            Check(Control(settings, "SaveSettings").IsEnabled, "settings remain accessible after expiry");
            Check(Control(settings, "CreateCopies").AsRadioButton().IsChecked, "Create copies is the saved default");
            var preferencesBefore = File.ReadAllBytes(Path.Combine(root, "settings.json"));
            var replacement = Control(settings, "AllowReplacement");
            if (replacement.IsEnabled) replacement.Patterns.SelectionItem.Pattern.Select();
            Capture(settings, "output-settings");
            Control(settings, "CancelSettings").AsButton().Invoke();
            Check(File.ReadAllBytes(Path.Combine(root, "settings.json")).SequenceEqual(preferencesBefore), "cancelled Settings does not change output consent");
            main.SetForeground();
            Control(main, "CloseResults").Focus();
            Wait(() => Control(main, "CloseResults").Properties.HasKeyboardFocus.ValueOrDefault, "result close focus");
            Keyboard.Press(VirtualKeyShort.ESCAPE); Keyboard.Release(VirtualKeyShort.ESCAPE);
            Wait(() => owned[0].HasExited, "Escape closes idle results and worker");
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

        void Activate(string operation, string action, string[] selection)
        {
            var directory = Path.Combine(root, "ActivationCleanup");
            Directory.CreateDirectory(directory);
            var id = Guid.NewGuid();
            var path = Path.Combine(directory, id.ToString("D") + ".request");
            requests.Add(path);
            File.WriteAllText(path, $"ContextSuiteActivation/1\nrequestId={id:D}\noperation={operation}\naction={action}\npathCount={selection.Length}\n" +
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
            var window = found!.AsWindow();
            return window;
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

    private static bool IsIdle(Window window)
    {
        var cancel = window.FindFirstDescendant(cf => cf.ByAutomationId("CancelPending"));
        return cancel is null || cancel.IsOffscreen || !cancel.IsEnabled;
    }

    private static AutomationElement Control(AutomationElement window, string id)
    {
        return window.FindFirstDescendant(cf => cf.ByAutomationId(id)) ?? throw new PendingControlException("Missing UI control: " + id);
    }

    private sealed class PendingControlException(string message) : Exception(message);

    private static void Toggle(Window window, string id) { Control(window, id).AsCheckBox().Toggle(); }

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
