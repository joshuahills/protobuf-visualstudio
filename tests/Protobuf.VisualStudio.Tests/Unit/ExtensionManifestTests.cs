using System.Text.Json;
using Protobuf.VisualStudio.Tests.Infrastructure;

namespace Protobuf.VisualStudio.Tests.Unit;

/// <summary>Checks the command placements in the extension.json the extensibility SDK generates at build time.</summary>
public sealed class ExtensionManifestTests
{
    private const string ShellMainMenu = "d309f791-903f-11d0-9efc-00a0c911004f";

    // Menus, not groups, in guidSHLMainMenu (vsshlids.h). A command parented straight to one is never shown.
    private static readonly int[] ContextMenus = [0x0402 /* PROJNODE */, 0x0430 /* ITEMNODE */, 0x0431 /* FOLDERNODE */];

    [Fact]
    public void Solution_Explorer_placements_use_the_shells_menu_guid()
    {
        // The SDK generates the Tools menu placement itself, so its guid is the real guidSHLMainMenu. A typo here
        // (it happened: ...00a0c90f57e3) registers the commands but shows them nowhere.
        var toolsMenu = Assert.Single(LegacyPlacements(), p => p.Control.EndsWith(".BufMenu", StringComparison.Ordinal));

        Assert.Equal(toolsMenu.Guid, ShellMainMenu);
        Assert.All(
            LegacyPlacements().Where(p => p.Control.EndsWith("FileCommand", StringComparison.Ordinal) || p.Control.EndsWith("FolderCommand", StringComparison.Ordinal)),
            p => Assert.Equal(ShellMainMenu, p.Guid));
    }

    [Fact]
    public void Solution_Explorer_commands_are_parented_to_groups_not_menus()
    {
        var placements = LegacyPlacements().Where(p => p.Guid == ShellMainMenu).ToList();

        Assert.NotEmpty(placements);
        Assert.All(placements, p => Assert.DoesNotContain(p.Id, ContextMenus));
    }

    [Theory]
    [InlineData("LintFileCommand", 0x0209)]
    [InlineData("FormatFileCommand", 0x0209)]
    [InlineData("LintFolderCommand", 0x0267)]
    [InlineData("LintFolderCommand", 0x0206)]
    public void Each_Solution_Explorer_command_is_in_its_group(string command, int group)
    {
        Assert.Contains(LegacyPlacements(), p => p.Control.EndsWith("." + command, StringComparison.Ordinal) && p.Guid == ShellMainMenu && p.Id == group);
    }

    [Fact]
    public void Only_Debug_builds_use_the_Dev_identity()
    {
        // Release builds are what goes to the Marketplace, so they must keep the published identity.
        var manifest = File.ReadAllText(GeneratedFile("extension.vsixmanifest"));
        var expected = Configuration == "Release"
            ? "Id=\"Protobuf.VisualStudio.4f6c2a9e-8d1b-4e57-9c3a-2b7e6f0d5a14\""
            : "Id=\"Protobuf.VisualStudio.Dev.4f6c2a9e-8d1b-4e57-9c3a-2b7e6f0d5a14\"";

        Assert.Contains(expected, manifest, StringComparison.Ordinal);
    }

    // The test build always builds the extension too, with the same configuration.
    private static string Configuration => new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;

    private static string GeneratedFile(string name) =>
        Path.Combine(RealBuf.RepoRoot(), "src", "Protobuf.VisualStudio", "obj", Configuration, "net10.0-windows", name);

    private static IEnumerable<(string Control, string Guid, int Id)> LegacyPlacements()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(GeneratedFile("extension.json")));

        var results = new List<(string, string, int)>();
        Walk(json.RootElement, results);
        return results;
    }

    private static void Walk(JsonElement element, List<(string, string, int)> results)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("controlName", out var name) &&
                element.TryGetProperty("parent", out var parent) &&
                parent.TryGetProperty("legacyParentId", out var legacy))
            {
                results.Add((name.GetString()!, legacy.GetProperty("guid").GetString()!, legacy.GetProperty("id").GetInt32()));
            }

            foreach (var property in element.EnumerateObject())
            {
                Walk(property.Value, results);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Walk(item, results);
            }
        }
    }
}
