using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HPButtonRemap;

internal static class AppWindowToggle
{
    internal static bool TryToggle(string target)
    {
        string? executable = GetExecutable(target);
        if (executable is null)
            return false;

        var matchingProcesses = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == CurrentSession &&
                        string.Equals(GetProcessPath(process.Id), executable, StringComparison.OrdinalIgnoreCase))
                        matchingProcesses.Add((uint)process.Id);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // An exited or inaccessible process cannot be verified as the target.
                }
            }
        }

        if (matchingProcesses.Count == 0)
            return false;

        IntPtr foreground = GetForegroundWindow();
        IntPtr foregroundOwner = GetAncestor(foreground, 3); // GA_ROOTOWNER
        IntPtr visible = IntPtr.Zero;
        IntPtr hidden = IntPtr.Zero;
        IntPtr focused = IntPtr.Zero;

        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint pid);
            if (!matchingProcesses.Contains(pid) || !IsAppWindow(window))
                return true;

            if (IsWindowVisible(window))
            {
                if ((window == foreground || window == foregroundOwner) && !IsIconic(window))
                {
                    focused = window;
                    return false;
                }
                if (visible == IntPtr.Zero)
                    visible = window;
            }
            else if (hidden == IntPtr.Zero && (GetWindowLong(window, -16) & 0x00020000) != 0) // WS_MINIMIZEBOX
            {
                hidden = window;
            }
            return true;
        }, IntPtr.Zero);

        if (focused != IntPtr.Zero)
        {
            // Ask the window to minimize itself, as the taskbar does. Windows blocks ShowWindow on apps
            // running as administrator (such as WireGuard), but this request still reaches them.
            PostMessage(focused, WM_SYSCOMMAND, SC_MINIMIZE, IntPtr.Zero);
            if (!WaitUntil(() => IsIconic(focused) || !IsWindowVisible(focused)))
            {
                ShowWindowAsync(focused, 6); // SW_MINIMIZE
                if (!WaitUntil(() => IsIconic(focused) || !IsWindowVisible(focused)))
                    throw new InvalidOperationException("The app did not minimize. It may ignore minimize requests.");
            }
            return true;
        }

        IntPtr selected = visible != IntPtr.Zero ? visible : hidden;
        if (selected == IntPtr.Zero)
            throw new InvalidOperationException("The app is already running, but it has no window Windows can restore.");

        // Become the last input sender so Windows allows this background app to change the foreground window.
        KeyboardShortcut.SendEmptyMouseInput();

        // Restore minimized windows without resizing visible ones. Like minimizing, restoring is requested
        // through WM_SYSCOMMAND first so it also works for apps running as administrator.
        if (IsIconic(selected))
        {
            PostMessage(selected, WM_SYSCOMMAND, SC_RESTORE, IntPtr.Zero);
            if (!WaitUntil(() => !IsIconic(selected)))
            {
                ShowWindowAsync(selected, 9); // SW_RESTORE
                if (!WaitUntil(() => !IsIconic(selected)))
                    throw new InvalidOperationException("Windows could not restore the app window.");
            }
        }
        else if (selected == hidden)
        {
            // A window hidden in the tray. If Windows will not show it (for example because the app runs as
            // administrator), launch the app instead: single-instance apps then show their own window.
            if (!ShowWindowAsync(selected, 5) || !WaitUntil(() => IsWindowVisible(selected))) // SW_SHOW
                return false;
        }

        if (!SetForegroundWindow(selected) && GetForegroundWindow() != selected)
        {
            FlashWindow(selected, true);
            throw new InvalidOperationException("Windows blocked focusing the app. Select it from the taskbar.");
        }
        return true;
    }

    private const uint WM_SYSCOMMAND = 0x0112;
    private static readonly IntPtr SC_MINIMIZE = 0xF020;
    private static readonly IntPtr SC_RESTORE = 0xF120;

    private static bool WaitUntil(Func<bool> condition) => SpinWait.SpinUntil(condition, 500);

    private static readonly int CurrentSession = GetCurrentSession();

    private static int GetCurrentSession()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }

    // QueryFullProcessImageName needs only limited access, so unlike Process.MainModule it is fast
    // and also works for elevated processes.
    private static string? GetProcessPath(int processId)
    {
        IntPtr handle = OpenProcess(0x1000, false, (uint)processId); // PROCESS_QUERY_LIMITED_INFORMATION
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var buffer = new char[1024];
            uint size = (uint)buffer.Length;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string? GetExecutable(string target)
    {
        string extension = Path.GetExtension(target);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(target);
        if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return null;

        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return null;
            shell = Activator.CreateInstance(shellType);
            shortcut = ((dynamic)shell!).CreateShortcut(target);
            string path = ((dynamic)shortcut!).TargetPath;
            return Path.IsPathFullyQualified(path) && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(path)
                : null;
        }
        catch (COMException)
        {
            // Let Windows open links that do not expose a regular executable target.
            return null;
        }
        finally
        {
            if (shortcut is not null)
                Marshal.ReleaseComObject(shortcut);
            if (shell is not null)
                Marshal.ReleaseComObject(shell);
        }
    }

    private static bool IsAppWindow(IntPtr window)
    {
        const int ToolOrNoActivate = 0x00000080 | 0x08000000;
        return GetWindowTextLength(window) > 0 && GetWindow(window, 4) == IntPtr.Zero && // GW_OWNER
            (GetWindowLong(window, -20) & ToolOrNoActivate) == 0 && // GWL_EXSTYLE
            (DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) != 0 || cloaked == 0);
    }

    private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool FlashWindow(IntPtr window, bool invert);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, [Out] char[] buffer, ref uint size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, int size);
}
