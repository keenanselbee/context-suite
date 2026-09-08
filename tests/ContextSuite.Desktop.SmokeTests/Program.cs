using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

internal static class Program
{
    private static readonly List<Process> Owned = [];
    private static readonly List<string> Requests = [];
    private static Window? _window;
    private static AutomationElement? _explorer;
    private static dynamic? _folderWindow;

    [DllImport("user32.dll")]
    private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")]
    private static extern bool SwitchDesktop(IntPtr desktop);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 3 && !(args.Length == 4 && args[2] == "images")) { Console.Error.WriteLine("Expected executable, scratch directory, wpf|explorer|images, optional image worker."); return 1; }
        var desktop = OpenInputDesktop(0, false, 0x0100);
        if (desktop == IntPtr.Zero) { Console.Error.WriteLine("NOT RUN: no accessible input desktop."); return 2; }
        var interactive = SwitchDesktop(desktop);
        CloseDesktop(desktop);
        if (!interactive) { Console.Error.WriteLine("NOT RUN: unlock the desktop first."); return 2; }
        var existing = Process.GetProcessesByName("ContextSuite.Application");
        foreach (var process in existing) process.Dispose();
        if (existing.Length != 0) { Console.Error.WriteLine("NOT RUN: close Context Suite first."); return 2; }
        if (args[2] == "images") return ImageConversionSmoke.Run(args[0], args[3], args[1]);

        var run = Path.Combine(Path.GetFullPath(args[1]), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var fixtureFolder = Path.Combine(run, "ContextSuite-Smoke-" + Path.GetFileName(run)[..8]);
        Directory.CreateDirectory(fixtureFolder);
        var files = new[] { "alpha.png", "beta.jpg", "gamma.webp" }.Select(n => Path.Combine(fixtureFolder, n)).ToArray();
        foreach (var file in files) File.WriteAllBytes(file, []);
        var hashes = files.Select(f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))).ToArray();
        using var automation = new UIA3Automation();
        // Bound UIA provider calls as well as our condition polling.
        automation.ConnectionTimeout = TimeSpan.FromSeconds(3);
        automation.TransactionTimeout = TimeSpan.FromSeconds(3);
        var explorerMode = args[2] == "explorer";
        try
        {
            if (explorerMode) OpenExplorer(automation, fixtureFolder);
            var operations = new[] { "analyze", "convert", "optimize" };
            foreach (var operation in operations)
            {
                if (explorerMode) InvokeExplorer(automation, operation);
                else Activate(args[0], operation, files);
                _window = WaitForWindow(automation, args[0]);
                var batches = Array.IndexOf(operations, operation) + 1;
                WaitFor(() => VerifyRows(_window, files, operations.Take(batches).ToArray()), $"{batches * 3} correct rows and completed statuses");
                var owners = Process.GetProcessesByName("ContextSuite.Application");
                try { if (owners.Length != 1) throw new InvalidOperationException("Expected one application owner after forwarding."); }
                finally { foreach (var owner in owners) owner.Dispose(); }
                Console.WriteLine($"PASS: {operation}: {batches * 3} rows in one window.");
            }
            _window!.SetForeground();
            var keyboardStart = _window.FindFirstDescendant(cf => cf.ByAutomationId("OpenSettings"))
                ?? throw new InvalidOperationException("Missing settings button.");
            keyboardStart.Focus();
            WaitFor(() => keyboardStart.Properties.HasKeyboardFocus.ValueOrDefault, "initial keyboard focus within window");
            Keyboard.Press(VirtualKeyShort.TAB);
            Keyboard.Release(VirtualKeyShort.TAB);
            WaitFor(() => _window.FindAllDescendants().Any(e => e.Properties.HasKeyboardFocus.ValueOrDefault), "keyboard focus within window");
            _window.Patterns.Transform.Pattern.Resize(700, 440);
            WaitFor(() => _window.BoundingRectangle.Width <= 750, "window resize");
            _window.Patterns.Transform.Pattern.Resize(1000, 700);
            Console.WriteLine("PASS: keyboard focus and resize (not a visual/accessibility certification).");
            CaptureFailure(run, _window, "main-window");
            if (!explorerMode) VerifySettings(automation, args[0], run, files);
            CloseApp();
            if (explorerMode) InvokeExplorer(automation, "analyze");
            else Activate(args[0], "analyze", files);
            _window = WaitForWindow(automation, args[0]);
            WaitFor(() => VerifyRows(_window, files, ["analyze"]), "fresh three-row batch after reopening");
            CloseApp();
            for (var i = 0; i < files.Length; i++)
                if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(files[i]))) != hashes[i])
                    throw new InvalidOperationException("Source fixture changed.");
            if (Directory.GetFiles(fixtureFolder).Length != 3) throw new InvalidOperationException("Unexpected output in fixture folder.");
            File.WriteAllText(Path.Combine(run, "result.txt"), $"PASS: {args[2]} smoke; row content, instance reuse, reopen, source preservation.\n");
            Console.WriteLine($"PASS: close/reopen and unchanged sources. Evidence: {run}");
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(run, "failure.txt"), error.ToString());
            CaptureFailure(run, _window, "application");
            CaptureFailure(run, _explorer, "explorer");
            Console.Error.WriteLine($"FAIL: {error.Message}\nEvidence: {run}");
            return 1;
        }
        finally
        {
            // Only processes launched by this run (or the exact unique Explorer window).
            foreach (var process in Owned)
            {
                try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } }
                catch (InvalidOperationException) { }
                finally { process.Dispose(); }
            }
            if (_folderWindow is not null)
            {
                // Explorer may reuse a user's tabbed window. Never Quit that window.
                // Leave our tab open rather than risk closing unrelated user tabs.
                Marshal.FinalReleaseComObject(_folderWindow);
            }
            foreach (var request in Requests) File.Delete(request);
            // Keep fixtures and diagnostics in this unique ignored run directory for inspection.
        }
    }

    private static void Activate(string executable, string operation, string[] files, string? actionOverride = null)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextSuite", "Prototype", "Activations");
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid();
        var path = Path.Combine(directory, $"{id:D}.request");
        Requests.Add(path);
        var action = actionOverride ?? (operation switch { "analyze" => "open-details", "convert" => "choose-format", _ => "choose-preset" });
        File.WriteAllText(path, $"ContextSuiteActivation/1\nrequestId={id:D}\noperation={operation}\naction={action}\npathCount={files.Length}\n" +
            string.Concat(files.Select(f => $"path={f}\n")), new UTF8Encoding(false));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        start.ArgumentList.Add("--activation-file");
        start.ArgumentList.Add(path);
        Owned.Add(Process.Start(start) ?? throw new IOException("Application launch failed."));
        WaitFor(() => !File.Exists(path), "activation request consumption");
        if (Owned.Count > 1 && !Owned[0].HasExited)
            WaitFor(() => Owned[^1].HasExited, "forwarding process exit");
    }

    private static void VerifySettings(UIA3Automation automation, string executable, string run, string[] files)
    {
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextSuite", "settings.json");
        var previous = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        foreach (var operation in new[] { "convert", "optimize" })
        {
            Activate(executable, operation, [], "settings");
            Window? settings = null;
            WaitFor(() =>
            {
                settings = _window!.FindFirstDescendant(cf => cf.ByAutomationId("SettingsWindow"))?.AsWindow();
                return settings is not null;
            }, "settings window");
            var selectedId = operation == "convert" ? "ConvertSettings" : "OptimizeSettings";
            WaitFor(() => settings!.FindFirstDescendant(cf => cf.ByAutomationId(selectedId))!
                .Patterns.SelectionItem.Pattern.IsSelected.Value, "correct settings section");
            if (!VerifyRows(_window!, files, ["analyze", "convert", "optimize"]))
                throw new InvalidOperationException("Settings activation changed the media queue.");
            var field = settings!.FindFirstDescendant(cf => cf.ByAutomationId("OutputFolder"))!.AsTextBox();
            if (field.IsEnabled)
            {
                field.Focus();
                field.Text = run;
                if (!field.Properties.HasKeyboardFocus.Value) throw new InvalidOperationException("Settings field did not receive focus.");
            }
            settings.Focus();
            WaitFor(() => settings.FindAllDescendants().Any(e => e.Properties.HasKeyboardFocus.ValueOrDefault),
                "keyboard focus within settings before Escape");
            CaptureFailure(run, settings, "settings-" + operation);
            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Keyboard.Release(VirtualKeyShort.ESCAPE);
            WaitFor(() => _window!.FindFirstDescendant(cf => cf.ByAutomationId("SettingsWindow")) is null,
                "Escape dismisses settings");
        }
        var after = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        if ((previous is null) != (after is null) || (previous is not null && !previous.SequenceEqual(after!)))
            throw new InvalidOperationException("Cancelling settings changed persisted preferences.");
        Console.WriteLine("PASS: settings sections, pathless forwarding, keyboard cancellation, unchanged queue and preferences.");
    }

    private static Window WaitForWindow(UIA3Automation automation, string executable)
    {
        Window? result = null;
        WaitFor(() =>
        {
            foreach (var process in Process.GetProcessesByName("ContextSuite.Application"))
            {
                var retain = false;
                try
                {
                    if (!string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Shell launched a different build; explicitly register the intended production configuration.");
                    if (process.MainWindowHandle == IntPtr.Zero) continue;
                    result = automation.FromHandle(process.MainWindowHandle).AsWindow();
                    if (Owned.All(p => p.Id != process.Id)) { Owned.Add(process); retain = true; }
                    return result.AutomationId == "ContextSuiteWindow";
                }
                finally { if (!retain) process.Dispose(); }
            }
            return false;
        }, "production WPF window");
        return result!;
    }

    private static bool VerifyRows(Window window, string[] files, string[] operations)
    {
        var element = window.FindFirstDescendant(cf => cf.ByAutomationId("BatchResults"));
        if (element is null) return false;
        var grid = element.Patterns.Grid.Pattern;
        if (grid.RowCount.Value != files.Length * operations.Length) return false;
        for (var batch = 0; batch < operations.Length; batch++)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < files.Length; index++)
            {
                var row = batch * files.Length + index;
                var cells = Enumerable.Range(0, 4).Select(col => CellText(grid.GetItem(row, col))).ToArray();
                if (cells[0] != (batch + 1).ToString() || cells[1] != operations[batch] ||
                    !files.Contains(cells[2], StringComparer.OrdinalIgnoreCase) || !seen.Add(cells[2]) ||
                    cells[3] != (operations[batch] == "convert" ? "The image is damaged or invalid. Check the source file and try another copy." :
                        operations[batch] == "analyze" ? "Not a supported DDS header. No files changed." : "Unsupported \u2014 not implemented")) return false;
            }
        }
        return true;
    }

    private static string CellText(AutomationElement cell)
    {
        if (cell.Patterns.Value.IsSupported) return cell.Patterns.Value.Pattern.Value.Value;
        return cell.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text))?.Name ?? cell.Name;
    }

    private static void CloseApp()
    {
        var process = Owned.Single(p => !p.HasExited && p.Id == _window!.Properties.ProcessId.Value);
        _window!.Close();
        WaitFor(() => process.HasExited, "graceful application close");
        _window = null;
    }

    private static void OpenExplorer(UIA3Automation automation, string directory)
    {
        using var launcher = Process.Start(new ProcessStartInfo("explorer.exe", $"/n,\"{directory}\"") { UseShellExecute = true });
        var shellType = Type.GetTypeFromProgID("Shell.Application", true)!;
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            WaitFor(() =>
            {
                dynamic windows = shell.Windows();
                try
                {
                    for (var i = 0; i < (int)windows.Count; i++)
                    {
                        dynamic candidate = windows.Item(i);
                        var retain = false;
                        try
                        {
                            if (!Uri.TryCreate((string)candidate.LocationURL, UriKind.Absolute, out var uri) ||
                                !uri.IsFile || !string.Equals(uri.LocalPath.TrimEnd('\\'), directory, StringComparison.OrdinalIgnoreCase)) continue;
                            _folderWindow = candidate;
                            retain = true;
                            // Tabbed Explorer can report a legacy/hidden automation
                            // HWND through ShellWindows. Resolve the visible frame
                            // by this run's unique folder title instead.
                            _explorer = automation.GetDesktop().FindAllChildren(cf => cf.ByClassName("CabinetWClass"))
                                .FirstOrDefault(e => e.Name.StartsWith(Path.GetFileName(directory), StringComparison.Ordinal));
                            if (_explorer is null) { _folderWindow = null; retain = false; continue; }
                            return true;
                        }
                        finally { if (!retain) Marshal.FinalReleaseComObject(candidate); }
                    }
                    return false;
                }
                finally { Marshal.FinalReleaseComObject(windows); }
            }, "unique Explorer test folder");
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    private static void InvokeExplorer(UIA3Automation automation, string operation)
    {
        _explorer!.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
        _explorer!.AsWindow().Focus();
        // Windows can reuse a tabbed frame; select only the uniquely named test tab.
        var folderName = Path.GetFileName(new Uri((string)_folderWindow!.LocationURL).LocalPath.TrimEnd('\\'));
        var tab = _explorer!.FindAllDescendants(cf => cf.ByControlType(ControlType.TabItem))
            .FirstOrDefault(e => e.Name.StartsWith(folderName, StringComparison.Ordinal));
        if (tab is not null && tab.Patterns.SelectionItem.IsSupported) tab.Patterns.SelectionItem.Pattern.Select();
        AutomationElement? first = null;
        WaitFor(() => (first = _explorer!.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .FirstOrDefault(e => e.Name is "alpha.png" or "alpha")) is not null, "Explorer fixture list item");
        first!.Focus();
        dynamic document = _folderWindow!.Document;
        dynamic items = document.Folder.Items();
        try
        {
            for (var i = 0; i < (int)items.Count; i++)
            {
                dynamic item = items.Item(i);
                try { document.SelectItem(item, i == 0 ? 1 | 4 | 8 | 16 : 1); }
                finally { Marshal.FinalReleaseComObject(item); }
            }
        }
        finally { Marshal.FinalReleaseComObject(items); Marshal.FinalReleaseComObject(document); }
        // Explicitly exercise the keyboard/classic context menu, not a claim about
        // the modern Windows 11 menu presentation. Avoid pointer-coordinate races.
        Keyboard.Press(VirtualKeyShort.SHIFT);
        try { Keyboard.Press(VirtualKeyShort.F10); Keyboard.Release(VirtualKeyShort.F10); }
        finally { Keyboard.Release(VirtualKeyShort.SHIFT); }
        var title = char.ToUpperInvariant(operation[0]) + operation[1..];
        AutomationElement? command = null;
        WaitFor(() => (command = automation.GetDesktop().FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(title)))
            .FirstOrDefault(e => e.Properties.ProcessId.ValueOrDefault == _explorer.Properties.ProcessId.Value)) is not null,
            $"Explorer {title} menu item (English Windows required)");
        command!.Click();
        if (operation != "analyze")
        {
            // This foundation has exactly one child per submenu (also covered
            // by native contracts). Custom shell flyouts do not always expose
            // child elements through UIA; traverse the real menu with keys.
            Keyboard.Press(VirtualKeyShort.RIGHT);
            Keyboard.Release(VirtualKeyShort.RIGHT);
            Keyboard.Press(VirtualKeyShort.RETURN);
            Keyboard.Release(VirtualKeyShort.RETURN);
        }
    }

    private static void WaitFor(Func<bool> condition, string description)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (condition()) return;
            Thread.Sleep(100);
        }
        throw new TimeoutException($"Timed out waiting for {description}.");
    }

    private static void CaptureFailure(string directory, AutomationElement? element, string name)
    {
        if (element is null) return;
        try
        {
            using var capture = element.Capture();
            capture.Save(Path.Combine(directory, name + ".png"));
            File.WriteAllLines(Path.Combine(directory, name + "-uia.txt"),
                element.FindAllDescendants().Take(400).Select(e => $"{e.Properties.ControlType.ValueOrDefault} | {e.Properties.AutomationId.ValueOrDefault} | {e.Properties.Name.ValueOrDefault}"));
        }
        catch (Exception captureError) { File.WriteAllText(Path.Combine(directory, name + "-capture-error.txt"), captureError.Message); }
    }
}
