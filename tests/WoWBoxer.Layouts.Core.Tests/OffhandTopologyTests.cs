namespace WoWBoxer.Layouts.Core.Tests;

public class OffhandTopologyTests
{
    private static MonitorInfo Screen(int index, PixelRect bounds, int dpi = 96) =>
        new(index, $@"\\.\DISPLAY{index}", index == 1, bounds, bounds, dpi);

    private static object? Topology(MonitorInfo game, MonitorInfo workspace, int share)
    {
        Assert.True(OffhandSpan.TryCreate(game, workspace, [game, workspace], share, out var span, out var error), error);
        var assignments = LuaData.ParseFile(OffhandTopology.ToLua(game, workspace, span));
        var table = Assert.Single(assignments);
        Assert.Equal("OffhandCompanionTopology", table.Key);
        return table.Value;
    }

    private static void AssertRegion(object? table, string key, int x, int y, int width, int height)
    {
        Assert.Equal(x, LuaData.Number(table, key, "x"));
        Assert.Equal(y, LuaData.Number(table, key, "y"));
        Assert.Equal(width, LuaData.Number(table, key, "width"));
        Assert.Equal(height, LuaData.Number(table, key, "height"));
    }

    [Fact]
    public void Writes_a_single_workspace_with_the_two_selected_devices_and_their_scale()
    {
        var main = Screen(3, new(-1920, -40, 1920, 1080), 144);
        var workspace = Screen(4, new(0, -40, 1920, 1080), 120) with { DeviceName = "Desk\\\"\n7" };
        var table = Topology(main, workspace, 50);

        Assert.Equal(1, LuaData.Number(table, "schema"));
        Assert.Equal("DUAL_DISPLAY", LuaData.Text(table, "mode"));
        Assert.Equal(2880, LuaData.Number(table, "physicalWidth"));
        Assert.Equal(1080, LuaData.Number(table, "physicalHeight"));
        Assert.Equal(main.DeviceName, LuaData.Text(table, "mainhandDevice"));
        Assert.Equal(main.DeviceName, LuaData.Text(table, "selectedDevices", "1"));
        Assert.Equal(workspace.DeviceName, LuaData.Text(table, "selectedDevices", "2"));
        Assert.Equal(150, LuaData.Number(table, "panelScaleMetadata", "game", "percent"));
        Assert.Equal(workspace.DeviceName, LuaData.Text(table, "panelScaleMetadata", "workspaces", "workspace", "device"));
        Assert.Equal(125, LuaData.Number(table, "panelScaleMetadata", "workspaces", "workspace", "percent"));
        Assert.Null(LuaData.Get(table, "companionVersion"));
        AssertRegion(table, "game", 0, 0, 1920, 1080);
        AssertRegion(table, "workspace", 1920, 0, 960, 1080);
    }

    [Theory]
    // Workspace above or below the game. A different width clips the workspace on the horizontal axis.
    [InlineData(-300, -900, 1600, 900, 50, 0, 0, 0, 1080, 1300, 450)]
    [InlineData(300, 1080, 1200, 800, 75, 0, 600, 300, 0, 1200, 600)]
    // Left and right workspaces clipped on the vertical axis. y is measured from the window's bottom.
    [InlineData(-1600, 300, 1600, 600, 50, 800, 0, 0, 180, 800, 600)]
    [InlineData(1920, -400, 2560, 1440, 25, 0, 0, 1920, 40, 640, 1040)]
    // A one-percent display extension is valid exact geometry; only the manual wizard limits the seam.
    [InlineData(1920, 0, 1000, 1080, 1, 0, 0, 1920, 0, 10, 1080)]
    public void Exports_only_the_visible_workspace_in_bottom_left_canvas_coordinates(int x, int y,
        int width, int height, int share, int gameX, int gameY, int workX, int workY, int workW, int workH)
    {
        var table = Topology(Screen(1, new(0, 0, 1920, 1080)), Screen(2, new(x, y, width, height)), share);
        AssertRegion(table, "game", gameX, gameY, 1920, 1080);
        AssertRegion(table, "workspace", workX, workY, workW, workH);
    }

    [Fact]
    public void Rejects_stale_bounds_that_only_cover_the_game()
    {
        var main = Screen(1, new(0, 0, 1920, 1080));
        var workspace = Screen(2, new(-1920, 0, 1920, 1080));
        Assert.Throws<ArgumentException>(() => OffhandTopology.ToLua(main, workspace, main.Bounds));
    }
}
