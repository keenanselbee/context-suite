using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Core.ContractTests;

internal static class WorkerLifetimeContracts
{
    private sealed record ProcessIdentity(int Id, long StartUtcTicks);
    private sealed record Receipt(WorkerLifetimeIdentity Job, ProcessIdentity Worker, ProcessIdentity Leaf);

    internal static async Task HoldAsync(string stage, string mode)
    {
        stage = Path.GetFullPath(stage);
        if (!stage.Contains("\\.codex-temp\\", StringComparison.OrdinalIgnoreCase) || mode is not ("owner" or "worker" or "leaf"))
            throw new ArgumentException("Use an owned worker lifetime fixture.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        if (mode == "owner")
        {
            using var job = WorkerProcessJob.CreateRecoverable(Guid.NewGuid());
            using var worker = Start(stage, "worker");
            job.Assign(worker);
            File.WriteAllText(Path.Combine(stage, "assigned"), "assigned");
            await WaitFileAsync(Path.Combine(stage, "leaf.json"), deadline.Token);
            var leaf = JsonSerializer.Deserialize<ProcessIdentity>(File.ReadAllText(Path.Combine(stage, "leaf.json")))!;
            var receipt = new Receipt(job.RecoveryIdentity!, Identity(worker), leaf);
            Publish(Path.Combine(stage, "receipt.json"), JsonSerializer.Serialize(receipt));
            await Task.Delay(Timeout.Infinite);
        }
        else if (mode == "worker")
        {
            await WaitFileAsync(Path.Combine(stage, "assigned"), deadline.Token);
            using var leaf = Start(stage, "leaf");
            Publish(Path.Combine(stage, "leaf.json"), JsonSerializer.Serialize(Identity(leaf)));
            await Task.Delay(Timeout.Infinite);
        }
        else await Task.Delay(Timeout.Infinite);
    }

    internal static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var stage = Path.GetFullPath(Path.Combine(scratch, "worker-lifetime-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(stage);
        WorkerLifetimeIdentity? departed = null;
        var id = Guid.NewGuid();
        using (var job = WorkerProcessJob.CreateRecoverable(id))
        {
            var identity = job.RecoveryIdentity!;
            identity.Validate();
            check(identity.OwnerProcessId == Environment.ProcessId, "worker lifetime identifies its actual creator");
            await RefusesAsync(() => WorkerProcessJob.StopRecordedAsync(identity), "worker lifetime refuses recovery while its creator is live");
            var collided = false;
            try { using var collision = WorkerProcessJob.CreateRecoverable(id); }
            catch (Win32Exception error) when (error.NativeErrorCode == 183) { collided = true; }
            check(collided, "worker lifetime creation refuses an existing job without adoption");
            using var probe = Open(identity.Name, 0x20004);
            check(ReadFlags(probe) == 0x2000, "worker lifetime collision preserves the original kill-on-close limits");
            CheckSecurity(probe);
            check(true, "named worker lifetime grants access only to its user and SYSTEM");
            foreach (var invalid in new[]
            {
                identity with { Name = "unrelated" }, identity with { Name = identity.Name.Replace("Local\\", "Global\\", StringComparison.Ordinal) },
                identity with { OwnerProcessId = 0 }, identity with { OwnerStartUtcTicks = 0 }, identity with { SessionId = -1 },
                identity with { SessionId = identity.SessionId + 1 }
            })
                await RefusesAsync(() => WorkerProcessJob.StopRecordedAsync(invalid), "worker lifetime refuses an invalid identity or another session");
        }
        var eventId = Guid.NewGuid();
        using (var existing = new EventWaitHandle(false, EventResetMode.ManualReset, WorkerLifetimeIdentity.Create(eventId).Name))
        {
            var refused = false;
            try { using var collision = WorkerProcessJob.CreateRecoverable(eventId); }
            catch (Win32Exception error) when (error.NativeErrorCode == 6) { refused = true; }
            check(refused, "worker lifetime refuses a name occupied by another kernel object type");
            existing.Set();
            check(existing.WaitOne(0), "worker lifetime collision preserves the existing event");
        }
        foreach (var retained in new[] { false, true })
        {
            var folder = Path.Combine(stage, retained ? "retained-handle" : "last-handle"); Directory.CreateDirectory(folder);
            using var owner = Start(folder, "owner");
            Process? worker = null, leaf = null;
            SafeFileHandle? observer = null;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await WaitFileAsync(Path.Combine(folder, "receipt.json"), deadline.Token);
                var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(Path.Combine(folder, "receipt.json")))!;
                check(receipt.Job.OwnerProcessId == owner.Id && receipt.Job.OwnerStartUtcTicks == owner.StartTime.ToUniversalTime().Ticks,
                    "worker lifetime receipt matches the live disposable owner: " + retained);
                worker = OpenProcess(receipt.Worker); leaf = OpenProcess(receipt.Leaf);
                check(!owner.HasExited && !worker.HasExited && !leaf.HasExited, "worker lifetime tree is live before owner-only crash: " + retained);
                observer = Open(receipt.Job.Name, 0x000c);
                check(IsMember(worker, observer) && IsMember(leaf, observer), "worker and descendant belong to the named lifetime job: " + retained);
                if (!retained) { observer.Dispose(); observer = null; }
                owner.Kill(); await owner.WaitForExitAsync(deadline.Token);
                check(owner.HasExited, "worker lifetime test confirms owner death before recovery: " + retained);
                if (retained)
                {
                    check(!worker.HasExited && !leaf.HasExited, "an extra job handle keeps both processes live after owner death");
                    check(await WorkerProcessJob.StopRecordedAsync(receipt.Job), "recorded lifetime recovery opens and explicitly stops the retained job");
                    check(worker.HasExited && leaf.HasExited, "recorded recovery observes both process exits before returning");
                    observer!.Dispose(); observer = null;
                }
                else
                {
                    await Task.WhenAll(worker.WaitForExitAsync(deadline.Token), leaf.WaitForExitAsync(deadline.Token));
                    check(worker.HasExited && leaf.HasExited, "last-handle closure stops the worker and its descendant");
                }
                check(!await WorkerProcessJob.StopRecordedAsync(receipt.Job), "recorded job is absent after its final handle and processes exit: " + retained);
                departed = receipt.Job;
            }
            finally
            {
                if (observer is not null) { TerminateJobObject(observer, 1); observer.Dispose(); }
                foreach (var process in new[] { owner, worker, leaf })
                {
                    if (process is null) continue;
                    if (!process.HasExited) process.Kill(true);
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await process.WaitForExitAsync(cleanup.Token);
                }
                worker?.Dispose(); leaf?.Dispose();
            }
        }
        var altered = departed! with { Name = WorkerLifetimeIdentity.Create(Guid.NewGuid()).Name };
        using (var unconfigured = CreateJobObject(IntPtr.Zero, altered.Name))
        using (var child = Start(stage, "leaf"))
        {
            try
            {
                if (unconfigured.IsInvalid || !AssignProcessToJobObject(unconfigured, child.SafeHandle))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                await RefusesAsync(() => WorkerProcessJob.StopRecordedAsync(altered), "worker lifetime refuses recovery of a job with unexpected limits");
                check(!child.HasExited && ReadFlags(unconfigured) == 0, "unexpected-limit refusal preserves the process and job configuration");
            }
            finally
            {
                if (!child.HasExited) child.Kill();
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await child.WaitForExitAsync(cleanup.Token);
            }
        }

        async Task RefusesAsync(Func<Task<bool>> action, string message)
        {
            var refused = false;
            try { await action(); } catch (Exception error) when (error is InvalidDataException or IOException or Win32Exception) { refused = true; }
            check(refused, message);
        }
    }

    private static Process Start(string stage, string mode)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var value in new[] { "--worker-lifetime-hold", stage, mode }) start.ArgumentList.Add(value);
        return Process.Start(start) ?? throw new IOException("Worker lifetime test child did not start.");
    }
    private static ProcessIdentity Identity(Process process) => new(process.Id, process.StartTime.ToUniversalTime().Ticks);
    private static Process OpenProcess(ProcessIdentity identity)
    {
        var process = Process.GetProcessById(identity.Id);
        if (Identity(process) == identity) { _ = process.Handle; return process; }
        process.Dispose(); throw new IOException("The disposable process identity changed.");
    }
    private static async Task WaitFileAsync(string path, CancellationToken token)
    {
        while (!File.Exists(path)) await Task.Delay(10, token);
    }
    private static void Publish(string path, string value)
    {
        File.WriteAllText(path + ".pending", value);
        File.Move(path + ".pending", path);
    }
    private static SafeFileHandle Open(string name, uint access)
    {
        var handle = OpenJobObject(access, false, name);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error);
    }
    private static uint ReadFlags(SafeFileHandle job)
    {
        if (!QueryInformationJobObject(job, 9, out var limits, 144, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return limits.Flags;
    }
    private static bool IsMember(Process process, SafeFileHandle job)
    {
        if (!IsProcessInJob(process.SafeHandle, job, out var member)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return member;
    }
    private static void CheckSecurity(SafeFileHandle job)
    {
        var error = GetSecurityInfo(job, 6, 4, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var descriptor);
        if (error != 0) throw new Win32Exception((int)error);
        try
        {
            var bytes = new byte[GetSecurityDescriptorLength(descriptor)]; Marshal.Copy(descriptor, bytes, 0, bytes.Length);
            var acl = new RawSecurityDescriptor(bytes, 0).DiscretionaryAcl ?? throw new IOException("Missing lifetime DACL.");
            using var user = WindowsIdentity.GetCurrent();
            var expected = new[] { user.User!.Value, "S-1-5-18" }.Order().ToArray();
            var allowed = acl.Cast<GenericAce>().Select(ace => ace is CommonAce common && common.AceQualifier == AceQualifier.AccessAllowed &&
                common.AceFlags == AceFlags.None && !common.IsCallback ? common.SecurityIdentifier.Value : "unexpected").Order().ToArray();
            if (!allowed.SequenceEqual(expected)) throw new IOException("Unexpected named job permissions.");
        }
        finally { LocalFree(descriptor); }
    }

    [StructLayout(LayoutKind.Explicit, Size = 144)] private struct Limits { [FieldOffset(16)] public uint Flags; }
    [DllImport("kernel32.dll", EntryPoint = "OpenJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle OpenJobObject(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);
    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, out Limits limits, uint size, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(SafeProcessHandle process, SafeFileHandle job, [MarshalAs(UnmanagedType.Bool)] out bool member);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    [DllImport("advapi32.dll")] private static extern uint GetSecurityInfo(SafeFileHandle handle, int type, uint information,
        IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl, out IntPtr descriptor);
    [DllImport("advapi32.dll")] private static extern uint GetSecurityDescriptorLength(IntPtr descriptor);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
