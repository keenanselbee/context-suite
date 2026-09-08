using System.Runtime.InteropServices;

namespace ContextSuite.Application.Infrastructure;

internal static class CompletionSound
{
    public static Task PlayAsync() => Task.Run(() =>
    {
        // Play synchronously on a background thread so quiet shutdown cannot truncate the chime.
        // SND_NODEFAULT prevents an unexpected system beep when the installed asset is missing.
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "chimes.wav");
        if (File.Exists(path)) PlaySound(path, IntPtr.Zero, 0x00020000 | 0x00000002);
    });

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(string sound, IntPtr module, uint flags);
}
