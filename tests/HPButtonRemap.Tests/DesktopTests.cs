using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace HPButtonRemap.Tests;

/// <summary>
/// Drives real windows, so these run only in the CI step with a desktop (Category=Desktop).
/// </summary>
[Trait("Category", "Desktop")]
public class DesktopTests
{
    private static readonly string Notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");

    [Fact]
    public void TryToggle_MinimizesFocusedWindowAndRestoresIt()
    {
        using var process = Process.Start(Notepad)!;
        try
        {
            IntPtr window = WaitForWindow(process);

            // Press until the window is minimized: the first press may only bring it to the front.
            for (int i = 0; i < 3 && !IsIconic(window); i++)
            {
                Assert.True(AppWindowToggle.TryToggle(Notepad));
                Thread.Sleep(500);
            }
            Assert.True(IsIconic(window), "The focused window was not minimized.");

            Assert.True(AppWindowToggle.TryToggle(Notepad));
            Assert.True(SpinWait.SpinUntil(() => !IsIconic(window), 2000), "The minimized window was not restored.");
            Assert.True(SpinWait.SpinUntil(() => GetForegroundWindow() == window, 2000), "The restored window was not focused.");
        }
        finally
        {
            process.Kill();
        }
    }

    [Fact]
    public void ExecuteAction_LaunchesAppThatIsNotRunning()
    {
        foreach (var running in Process.GetProcessesByName("notepad"))
            using (running) running.Kill();

        var action = new ButtonAction { Type = ActionType.OpenFile, FilePath = Notepad };
        Assert.Null(new ActionExecutor().ExecuteAction(action));

        Process? launched = null;
        Assert.True(SpinWait.SpinUntil(() => (launched = Process.GetProcessesByName("notepad").FirstOrDefault()) is not null, 5000));
        using (launched!)
            launched.Kill();
    }

    private static IntPtr WaitForWindow(Process process)
    {
        IntPtr window = IntPtr.Zero;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            process.Refresh();
            window = process.MainWindowHandle;
            return window != IntPtr.Zero;
        }, 10_000), "Notepad did not open a window.");
        return window;
    }

    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
