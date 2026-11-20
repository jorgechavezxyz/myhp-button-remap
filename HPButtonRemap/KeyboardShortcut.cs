using System.Runtime.InteropServices;

namespace HPButtonRemap;

internal static class KeyboardShortcut
{
    private static readonly Dictionary<string, Keys> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = Keys.ControlKey,
        ["Alt"] = Keys.Menu,
        ["Shift"] = Keys.ShiftKey,
        ["Win"] = Keys.LWin
    };

    internal const string FormatHelp = "Use Ctrl+Alt+N or Win+Shift+S. Keys can be letters, digits, F1–F24, or named keys such as Enter and Space.";

    internal static bool TryParse(string? text, out Keys[] keys)
    {
        keys = [];
        var parts = text?.Split('+', StringSplitOptions.TrimEntries);
        if (parts is null || parts.Length is < 1 or > 5)
            return false;

        var parsed = new List<Keys>(parts.Length);
        foreach (string modifier in parts[..^1])
        {
            if (!Modifiers.TryGetValue(modifier, out var key) || parsed.Contains(key))
                return false;
            parsed.Add(key);
        }

        string last = parts[^1];
        if (last.Length == 1 && char.IsAsciiDigit(last[0]))
            last = "D" + last;

        if (!Enum.TryParse<Keys>(last, ignoreCase: true, out var primary) ||
            !Enum.GetNames<Keys>().Contains(last, StringComparer.OrdinalIgnoreCase) ||
            !(primary is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9 or >= Keys.F1 and <= Keys.F24
                or Keys.Enter or Keys.Space or Keys.Tab or Keys.Escape or Keys.Back or Keys.Delete
                or Keys.Insert or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown
                or Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.PrintScreen))
            return false;

        parsed.Add(primary);
        keys = [.. parsed];
        return true;
    }

    internal static void Send(string? text)
    {
        if (!TryParse(text, out var keys))
            throw new InvalidOperationException(FormatHelp);

        if (Modifiers.Values.Append(Keys.RWin).Append(keys[^1]).Any(key => GetAsyncKeyState((int)key) < 0))
            throw new InvalidOperationException("Release the shortcut keys and try again.");

        var inputs = new Input[keys.Length * 2];
        for (int i = 0; i < keys.Length; i++)
        {
            inputs[i] = CreateInput(keys[i], release: false);
            inputs[^(i + 1)] = CreateInput(keys[i], release: true);
        }

        int inputSize = Marshal.SizeOf<Input>();
        uint sent = SendInput((uint)inputs.Length, inputs, inputSize);
        if (sent == inputs.Length)
            return;

        int stillPressed = (int)Math.Min(sent, (uint)keys.Length) - Math.Max(0, (int)sent - keys.Length);
        if (stillPressed > 0)
        {
            int end = (int)Math.Min(sent, (uint)keys.Length);
            Input[] releases = keys[..end].Reverse().Take(stillPressed).Select(key => CreateInput(key, release: true)).ToArray();
            SendInput((uint)releases.Length, releases, inputSize);
        }

        throw new InvalidOperationException("Windows could not send the shortcut. Elevated apps may block simulated keys.");
    }

    private static Input CreateInput(Keys key, bool release)
    {
        const uint ExtendedKey = 1;
        const uint KeyUp = 2;
        bool extended = key is Keys.LWin or Keys.Insert or Keys.Delete or Keys.Home or Keys.End
            or Keys.PageUp or Keys.PageDown or Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.PrintScreen;

        return new Input
        {
            Type = 1,
            Data = new InputData
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = (ushort)key,
                    Flags = (extended ? ExtendedKey : 0) | (release ? KeyUp : 0)
                }
            }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public UIntPtr ExtraInfo;
    }

    // INPUT's native union is sized by its largest member.
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
