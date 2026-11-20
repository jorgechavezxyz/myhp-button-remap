using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace HPButtonRemap;

/// <summary>
/// Executes actions based on button configuration
/// </summary>
public class ActionExecutor
{
    /// <summary>
    /// Execute an action based on its configuration
    /// </summary>
    public string? ExecuteAction(ButtonAction action)
    {
        try
        {
            Debug.WriteLine($"Executing action type: {action.Type}");

            if (action.EffectiveDelayMs > 0)
            {
                Thread.Sleep(action.EffectiveDelayMs);
            }

            switch (action.Type)
            {
                case ActionType.RunCommand:
                    RunCommand(action);
                    break;
                case ActionType.OpenWebsite:
                    OpenWebsite(action);
                    break;
                case ActionType.OpenFile:
                    if (action.FilePath is not { } file || !Path.IsPathFullyQualified(file))
                        return "Choose an app or file in Settings.";
                    if (!File.Exists(file))
                        return $"File not found or inaccessible: {Path.GetFileName(file)}";
                    if (!AppWindowToggle.TryToggle(file))
                        OpenTarget(file);
                    break;
                case ActionType.SendShortcut:
                    KeyboardShortcut.Send(action.Shortcut);
                    break;
                default:
                    return "This action type is not supported.";
            }
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            return $"Could not run the action. {ex.Message}";
        }
    }

    private static void RunCommand(ButtonAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Command))
        {
            throw new InvalidOperationException("No command is configured.");
        }

        if (action.EffectiveCreateNewWindow && TryRunCommandDirect(action.Command, action.WorkingDirectory))
        {
            Debug.WriteLine($"Executed command directly: {action.Command}");
            return;
        }

        string arguments = action.EffectiveCreateNewWindow
            ? $"/d /c start \"\" {action.Command}"
            : $"/d /c {action.Command}";

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        if (!string.IsNullOrWhiteSpace(action.WorkingDirectory))
        {
            startInfo.WorkingDirectory = action.WorkingDirectory;
        }

        using var process = Process.Start(startInfo);
        Debug.WriteLine($"Executed command: {action.Command}");
    }

    private static void OpenWebsite(ButtonAction action)
    {
        if (!ButtonAction.TryGetWebsite(action.OpenWebsite, out var website))
            throw new InvalidOperationException("Enter an HTTP or HTTPS address in Settings.");

        OpenTarget(website!.AbsoluteUri);
    }

    private static void OpenTarget(string target)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        });
    }

    private static bool TryRunCommandDirect(string command, string? workingDirectory)
    {
        if (ContainsCmdSyntax(command))
            return false;

        if (!TrySplitCommand(command, out string fileName, out string arguments))
            return false;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            };

            if (!string.IsNullOrWhiteSpace(workingDirectory))
                startInfo.WorkingDirectory = workingDirectory;

            using var process = Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static bool ContainsCmdSyntax(string command)
    {
        foreach (char c in command)
        {
            if (c is '|' or '&' or '<' or '>' or '^' or '%' or '!')
                return true;
        }
        return false;
    }

    private static bool TrySplitCommand(string command, out string fileName, out string arguments)
    {
        fileName = string.Empty;
        arguments = string.Empty;

        string trimmed = command.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return false;

        if (trimmed.StartsWith('"'))
        {
            int closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote <= 1)
                return false;

            fileName = trimmed[1..closingQuote];
            arguments = trimmed[(closingQuote + 1)..].TrimStart();
            return true;
        }

        int firstSpace = trimmed.IndexOf(' ');
        if (firstSpace < 0)
        {
            fileName = trimmed;
            return true;
        }

        fileName = trimmed[..firstSpace];
        arguments = trimmed[(firstSpace + 1)..].TrimStart();
        return true;
    }
}
