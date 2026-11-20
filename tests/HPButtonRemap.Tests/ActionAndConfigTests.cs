using System.Text.Json;
using Xunit;

namespace HPButtonRemap.Tests;

public class ActionAndConfigTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --flag", "C:\\Program Files\\App\\app.exe", "--flag")]
    [InlineData("notepad.exe notes.txt", "notepad.exe", "notes.txt")]
    [InlineData("calc.exe", "calc.exe", "")]
    public void TrySplitCommand_SplitsFileAndArguments(string command, string file, string arguments)
    {
        Assert.True(ActionExecutor.TrySplitCommand(command, out string actualFile, out string actualArguments));
        Assert.Equal(file, actualFile);
        Assert.Equal(arguments, actualArguments);
    }

    [Fact]
    public void TrySplitCommand_KeepsUnquotedExistingPathWithSpaces()
    {
        string directory = Directory.CreateTempSubdirectory("hp remap ").FullName;
        string file = Path.Combine(directory, "my app.exe");
        File.WriteAllText(file, "");
        try
        {
            Assert.True(ActionExecutor.TrySplitCommand(file, out string actualFile, out string arguments));
            Assert.Equal(file, actualFile);
            Assert.Equal("", arguments);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("   ")]
    public void TrySplitCommand_RejectsEmptyCommands(string command)
    {
        Assert.False(ActionExecutor.TrySplitCommand(command, out _, out _));
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com/path?q=1", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("https://user@example.com", false)]
    [InlineData("example.com", false)]
    [InlineData(null, false)]
    public void TryGetWebsite_AcceptsOnlyHttpAddresses(string? value, bool expected)
    {
        Assert.Equal(expected, ButtonAction.TryGetWebsite(value, out _));
    }

    [Fact]
    public void ConfigStore_CreatesDefaultWhenMissing()
    {
        string path = TempConfigPath();
        try
        {
            var config = new ConfigStore(path).LoadOrCreateDefault();
            Assert.True(File.Exists(path));
            Assert.Equal(29, config.EffectiveEventID);
            Assert.Equal(8616, config.EffectiveEventData);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Theory]
    [InlineData("""{ "EventID": -1 }""")]
    [InlineData("""{ "Action": { "Type": "OpenWebsite", "OpenWebsite": "ftp://example.com" } }""")]
    [InlineData("""{ "Action": { "Type": "OpenFile", "FilePath": "relative.txt" } }""")]
    [InlineData("""{ "Action": { "Type": "SendShortcut", "Shortcut": "Ctrl+" } }""")]
    [InlineData("""{ "Action": { "Type": "RunCommand", "Command": " " } }""")]
    [InlineData("""{ "Action": { "Type": "Teleport" } }""")]
    [InlineData("null")]
    public void ConfigStore_RejectsInvalidConfigurations(string json)
    {
        string path = TempConfigPath();
        File.WriteAllText(path, json);
        try
        {
            Assert.ThrowsAny<JsonException>(() => new ConfigStore(path).LoadOrCreateDefault());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void ConfigStore_RoundTripsSavedAction()
    {
        string path = TempConfigPath();
        try
        {
            var store = new ConfigStore(path);
            var config = store.LoadOrCreateDefault();
            config.Action = new ButtonAction { Type = ActionType.SendShortcut, Shortcut = "Win+Shift+S" };
            store.Save(config);

            var reloaded = store.LoadOrCreateDefault();
            Assert.Equal(ActionType.SendShortcut, reloaded.Action!.Type);
            Assert.Equal("Win+Shift+S", reloaded.Action.Shortcut);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private static string TempConfigPath() =>
        Path.Combine(Directory.CreateTempSubdirectory("hpremap").FullName, "config.json");
}
