using Xunit;

namespace HPButtonRemap.Tests;

public class KeyboardShortcutTests
{
    [Theory]
    [InlineData("Ctrl+Alt+N", new[] { Keys.ControlKey, Keys.Menu, Keys.N })]
    [InlineData("win+shift+s", new[] { Keys.LWin, Keys.ShiftKey, Keys.S })]
    [InlineData("Ctrl+1", new[] { Keys.ControlKey, Keys.D1 })]
    [InlineData("F13", new[] { Keys.F13 })]
    [InlineData("Alt + Enter", new[] { Keys.Menu, Keys.Enter })]
    public void TryParse_AcceptsSupportedShortcuts(string text, Keys[] expected)
    {
        Assert.True(KeyboardShortcut.TryParse(text, out var keys));
        Assert.Equal(expected, keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Hyper+A")]
    [InlineData("Ctrl+65")]
    [InlineData("Ctrl+LButton")]
    [InlineData("Ctrl+Alt+Shift+Win+Ctrl+A")]
    public void TryParse_RejectsInvalidShortcuts(string? text)
    {
        Assert.False(KeyboardShortcut.TryParse(text, out _));
    }

    // Inputs are sent as: Ctrl down, Shift down, S down, S up, Shift up, Ctrl up.
    [Theory]
    [InlineData(0u, new Keys[0])]
    [InlineData(1u, new[] { Keys.ControlKey })]
    [InlineData(2u, new[] { Keys.ShiftKey, Keys.ControlKey })]
    [InlineData(3u, new[] { Keys.S, Keys.ShiftKey, Keys.ControlKey })]
    [InlineData(4u, new[] { Keys.ShiftKey, Keys.ControlKey })]
    [InlineData(5u, new[] { Keys.ControlKey })]
    [InlineData(6u, new Keys[0])]
    public void KeysStillPressed_ReleasesOnlyKeysThatAreDown(uint sent, Keys[] expected)
    {
        Keys[] shortcut = [Keys.ControlKey, Keys.ShiftKey, Keys.S];
        Assert.Equal(expected, KeyboardShortcut.KeysStillPressed(shortcut, sent));
    }
}
