using System.Diagnostics;
using fnaf_access.Win32;

namespace fnaf_access.Game;

public readonly record struct FnafWindowInfo(
    uint ProcessId,
    string ProcessName,
    IntPtr WindowHandle,
    string WindowTitle,
    string ClassName,
    bool IsVisible,
    int ClientWidth,
    int ClientHeight);

/// <summary>
/// Locates the running Five Nights at Freddy's 1 process and main game window,
/// including when fullscreen or temporarily minimized/alt-tabbed.
/// </summary>
public static class FnafProcessLocator
{
    private static readonly string[] KnownProcessNames =
    [
        "FiveNightsatFreddys",
        "Five Nights at Freddy's",
        "fnaf",
        "fnaf1"
    ];

    public static FnafWindowInfo? FindRunningGame()
    {
        // 1. Direct process-name lookup (works even when the game is fullscreen-minimized).
        foreach (string name in KnownProcessNames)
        {
            Process[] procs = Process.GetProcessesByName(name);
            try
            {
                if (procs.Length > 0)
                {
                    uint pid = (uint)procs[0].Id;
                    string procName = procs[0].ProcessName;
                    return PickBestWindowForProcess(pid, procName);
                }
            }
            finally
            {
                foreach (Process p in procs)
                {
                    p.Dispose();
                }
            }
        }

        // 2. Fallback: scan top-level windows by title.
        FnafWindowInfo? found = null;
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            string title = NativeMethods.GetWindowText(hWnd);
            if (!title.Contains("Five Nights at Freddy", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0)
            {
                return true;
            }

            string procName = TryGetProcessName(pid);
            if (IsIgnoredHostProcess(procName))
            {
                return true;
            }

            found = PickBestWindowForProcess(pid, procName);
            return false;
        }, IntPtr.Zero);

        return found;
    }

    public static List<FnafWindowInfo> EnumerateWindowsForProcess(uint pid, string procName)
    {
        var list = new List<FnafWindowInfo>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint wPid);
            if (wPid == pid)
            {
                string title = NativeMethods.GetWindowText(hWnd);
                string cls = NativeMethods.GetWindowClassName(hWnd);
                bool visible = NativeMethods.IsWindowVisible(hWnd);
                NativeMethods.GetClientRect(hWnd, out NativeMethods.RECT rc);
                list.Add(new FnafWindowInfo(pid, procName, hWnd, title, cls, visible, rc.Width, rc.Height));
            }

            return true;
        }, IntPtr.Zero);

        return list;
    }

    private static FnafWindowInfo PickBestWindowForProcess(uint pid, string procName)
    {
        List<FnafWindowInfo> windows = EnumerateWindowsForProcess(pid, procName);
        if (windows.Count == 0)
        {
            return new FnafWindowInfo(pid, procName, IntPtr.Zero, string.Empty, string.Empty, false, 0, 0);
        }

        // Prefer Clickteam main window class ("MainHighGUI" / "CNRuntime") or largest/visible window.
        FnafWindowInfo best = windows
            .OrderByDescending(w =>
                w.ClassName.Contains("MainHighGUI", StringComparison.OrdinalIgnoreCase) ||
                w.ClassName.Contains("CNRuntime", StringComparison.OrdinalIgnoreCase) ||
                w.ClassName.Contains("MMF", StringComparison.OrdinalIgnoreCase) ? 1000 : 0)
            .ThenByDescending(w => w.IsVisible ? 100 : 0)
            .ThenByDescending(w => (long)w.ClientWidth * w.ClientHeight)
            .ThenByDescending(w => !string.IsNullOrEmpty(w.WindowTitle) ? 10 : 0)
            .First();

        return best;
    }

    private static bool IsIgnoredHostProcess(string procName) =>
        procName.Equals("steamwebhelper", StringComparison.OrdinalIgnoreCase) ||
        procName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
        procName.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
        procName.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
        procName.Equals("firefox", StringComparison.OrdinalIgnoreCase) ||
        procName.Equals("fnaf-access", StringComparison.OrdinalIgnoreCase);

    private static string TryGetProcessName(uint pid)
    {
        try
        {
            using Process proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
