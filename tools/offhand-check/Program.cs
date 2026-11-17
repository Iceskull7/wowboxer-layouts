using System.Diagnostics;
using WoWBoxer.Layouts.Core;
using WoWBoxer.Layouts.Win32;

// Run on a Windows desktop: dotnet run --project tools/offhand-check -c Release.
// Two stand-ins have the same exe and title. Only the second should span and restore.
Dpi.EnsurePerMonitorV2();
var area = Monitors.Primary(MonitorReader.GetMonitors()).WorkArea;
if (WindowFinder.FindGameWindows().Count != 0)
    throw new InvalidOperationException("Close the existing WoW clients before running this check.");

var temp = Directory.CreateTempSubdirectory("wowboxer-offhand-");
var children = new List<Process>();
try
{
    var install = temp.CreateSubdirectory("_forever_");
    var exe = Path.Combine(install.FullName, "Wow.exe");
    var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
    using var build = Start(compiler, "/nologo", "/target:winexe", "/out:" + exe, Path.Combine(AppContext.BaseDirectory, "FakeWow.cs"));
    await build.WaitForExitAsync();
    if (build.ExitCode != 0) throw new InvalidOperationException("Could not compile the stand-in WoW client.");

    var first = LaunchClient(20);
    var untouched = await WaitForClient(first);
    var second = LaunchClient(120);
    var selected = await WaitForClient(second);

    var firstState = ReadState(untouched.Handle);
    var secondState = ReadState(selected.Handle);
    var service = new OffhandWindow();
    // A single test monitor suffices to verify native placement. The layout tests cover monitor selection.
    await service.ExpandAsync(selected, area);
    Check(service.ActiveWindow?.Handle == selected.Handle, "the selected handle owns the span");
    Check(WindowFinder.RectOf(selected.Handle) == area, "the selected window fills the target rectangle");
    Check(WindowFinder.ClientSizeOf(selected.Handle) == (area.Width, area.Height), "the game area fills the borderless window");
    Check(ReadState(untouched.Handle) == firstState, "the first window keeps its bounds, style and topmost state");

    await service.RestoreAsync();
    Check(service.ActiveWindow == null, "the span ends");
    Check(ReadState(selected.Handle) == secondState, "the second window regains its original bounds, style and topmost state");
    Check(ReadState(untouched.Handle) == firstState, "restoring leaves the first window alone");

    Process LaunchClient(int offset)
    {
        var p = Start(exe, (area.X + offset).ToString(), (area.Y + 40).ToString(), "460", "320", "WoW Forever test");
        children.Add(p);
        return p;
    }
}
finally
{
    foreach (var p in children)
    {
        using (p)
        {
            if (!p.HasExited) p.Kill();
            await p.WaitForExitAsync();
        }
    }
    temp.Delete(recursive: true);
}

static Process Start(string file, params string[] arguments)
{
    var info = new ProcessStartInfo(file) { UseShellExecute = false };
    foreach (var argument in arguments) info.ArgumentList.Add(argument);
    return Process.Start(info) ?? throw new InvalidOperationException($"Could not start {file}.");
}

static async Task<WindowInfo> WaitForClient(Process process)
{
    var timer = Stopwatch.StartNew();
    while (timer.Elapsed < TimeSpan.FromSeconds(10) && !process.HasExited)
    {
        var client = WindowFinder.FindGameWindows(WowGame.Forever).SingleOrDefault(w => w.Pid == process.Id);
        if (client != null) return client;
        await Task.Delay(100);
    }
    throw new InvalidOperationException($"No visible stand-in WoW window for PID {process.Id}.");
}

static (PixelRect Rect, int Style, bool Topmost) ReadState(nint handle) =>
    (WindowFinder.RectOf(handle), WindowFinder.Describe(handle).Style, WindowPlacer.IsTopmost(handle));

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    Console.WriteLine($"PASS  {description}");
}
