using System.Diagnostics;
using WoWBoxer.Layouts.Tools;
using WoWBoxer.Layouts.Core;
using WoWBoxer.Layouts.Win32;

// Run on a Windows desktop: dotnet run --project tools/offhand-check -c Release.
// Two stand-ins have the same exe and title. Only the second should span and restore.
try
{
    await RunAsync();
    return 0;
}
catch (Exception e)
{
    SmokeFailure.Report("Offhand window check", e);
    return 1;
}

static async Task RunAsync()
{
    Dpi.EnsurePerMonitorV2();
    var area = Monitors.Primary(MonitorReader.GetMonitors()).WorkArea;
    if (WindowFinder.FindGameWindows().Count != 0)
        throw new InvalidOperationException("Close the existing WoW clients before running this check.");

    var temp = Directory.CreateTempSubdirectory("wowboxer-offhand-");
    var children = new List<Process>();
    bool verified = false;
    try
    {
        var install = temp.CreateSubdirectory("_forever_");
        var exe = Path.Combine(install.FullName, "Wow.exe");
        var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        var compile = new ProcessStartInfo(compiler) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var arg in new[] { "/nologo", "/target:winexe", "/reference:System.Drawing.dll", "/reference:System.Windows.Forms.dll",
            "/out:" + exe, Path.Combine(AppContext.BaseDirectory, "FakeWow.cs") }) compile.ArgumentList.Add(arg);
        using var build = Process.Start(compile) ?? throw new InvalidOperationException("Could not start the .NET Framework compiler.");
        var diagnostics = await build.StandardOutput.ReadToEndAsync();
        await build.WaitForExitAsync();
        if (build.ExitCode != 0) throw new InvalidOperationException($"FakeWow compilation failed ({build.ExitCode}): {diagnostics.Trim()}");

        var otherInstall = temp.CreateSubdirectory(Path.Combine("other-install", "_forever_"));
        var otherExe = Path.Combine(otherInstall.FullName, "Wow.exe");
        File.Copy(exe, otherExe);
        var otherBridge = InstallTestAddon(otherInstall.FullName);
        var first = LaunchClient(otherExe, 20);
        var untouched = await WaitForClient(first);
        var second = LaunchClient(exe, 120);
        var selected = await WaitForClient(second);

        Console.WriteLine($"Discovered both fake windows, PIDs {first.Id} and {second.Id}.");
        var firstState = ReadState(untouched.Handle);
        var secondState = ReadState(selected.Handle);
        var service = new OffhandWindow();
        // Two adjoining monitor fixtures inside the runner's real desktop exercise exact addon geometry.
        var gameArea = area with { Width = area.Width / 2 };
        var workspaceArea = new PixelRect(gameArea.Right, area.Y, area.Width - gameArea.Width, area.Height);
        var gameScreen = new MonitorInfo(1, "CI game", true, gameArea, gameArea);
        var workspaceScreen = new MonitorInfo(2, "CI workspace", false, workspaceArea, workspaceArea);
        Task<bool> Extend() => service.ExpandWithAddonAsync(selected, gameScreen, workspaceScreen, area);

        Check(!await Extend(), "a client without the addon can still expand in window-only mode");
        await service.RestoreAsync();
        var bridge = InstallTestAddon(install.FullName);
        var originalBytes = File.ReadAllBytes(bridge);
        try
        {
            Check(await Extend(), "the verified span publishes the addon layout");
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException($"Expand failed for PID {selected.Pid}: original {secondState}, requested {area}, " +
                $"after attempt {ReadState(selected.Handle)}, client size {WindowFinder.ClientSizeOf(selected.Handle)}.", error);
        }
        Check(service.ActiveWindow?.Handle == selected.Handle, "the selected handle owns the span");
        Check(WindowFinder.RectOf(selected.Handle) == area, "the selected window fills the target rectangle");
        Check(WindowFinder.ClientSizeOf(selected.Handle) == (area.Width, area.Height), "the game area fills the borderless window");
        Check(ReadState(untouched.Handle) == firstState, "the first window keeps its bounds, style and topmost state");

        Check(File.ReadAllText(bridge) == OffhandTopology.ToLua(gameScreen, workspaceScreen, area),
            "the bridge in the selected client's install contains the verified layout");
        Check(File.ReadAllBytes(otherBridge).SequenceEqual(originalBytes), "the sibling installation's bridge is untouched");
        await service.RestoreAsync();
        Check(service.ActiveWindow == null, "the span ends");
        Check(ReadState(selected.Handle) == secondState, "the second window regains its original bounds, style and topmost state");
        Check(ReadState(untouched.Handle) == firstState, "restoring leaves the first window alone");
        File.WriteAllBytes(bridge, originalBytes);
        var pending = Extend();
        var wait = Stopwatch.StartNew();
        while (WindowFinder.RectOf(selected.Handle) != area && !pending.IsCompleted && wait.Elapsed < TimeSpan.FromSeconds(2))
            await Task.Delay(50);
        Check(WindowFinder.RectOf(selected.Handle) == area, "the test client initially accepts the span");
        await Task.Delay(650);
        Check(!pending.IsCompleted, "the span is still being verified");
        Check(WindowPlacer.TryMove(selected.Handle, secondState.Rect) == 0, "simulate WoW reverting to its own size");
        await ExpectSpanFailure(pending, "a delayed reset rejects the span");
        Check(ReadState(selected.Handle) == secondState && service.ActiveWindow == null, "a reset rolls back the selected window");
        Check(File.ReadAllBytes(bridge).SequenceEqual(originalBytes), "a rejected size never publishes calibration data");

        using (File.Open(bridge, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await ExpectSpanFailure(Extend(), "a locked addon bridge rejects the span");
        Check(ReadState(selected.Handle) == secondState && service.ActiveWindow == null, "a bridge error restores the selected window");
        Check(File.ReadAllBytes(bridge).SequenceEqual(originalBytes), "a failed write keeps the previous bridge");
        Check(ReadState(untouched.Handle) == firstState && File.ReadAllBytes(otherBridge).SequenceEqual(originalBytes),
            "neither failure touches the sibling window or addon");
        verified = true;

        Process LaunchClient(string executable, int offset)
        {
            var p = Start(executable, (area.X + offset).ToString(), (area.Y + 40).ToString(), "460", "320", "WoW Forever test");
            children.Add(p);
            return p;
        }
    }
    finally
    {
        try
        {
            await CleanUpAsync(children, temp);
        }
        catch (Exception e) when (!verified)
        {
            // Preserve the placement failure already on its way to the caller.
            SmokeFailure.Report("Stand-in cleanup also failed", e);
        }
    }
}

static string InstallTestAddon(string install)
{
    var addon = Path.Combine(install, "Interface", "AddOns", "Offhand");
    Directory.CreateDirectory(Path.Combine(addon, "Core"));
    File.WriteAllText(Path.Combine(addon, "Offhand.toc"), @"Core\CompanionTopology.lua");
    var bridge = Path.Combine(addon, "Core", "CompanionTopology.lua");
    File.WriteAllText(bridge, "OffhandCompanionTopology = nil\n");
    return bridge;
}

static async Task ExpectSpanFailure(Task<bool> attempt, string description)
{
    try { await attempt; }
    catch (InvalidOperationException e)
    {
        Console.WriteLine($"PASS  {description}: {e.Message}");
        return;
    }
    throw new InvalidOperationException($"Expected the span to fail: {description}");
}

static async Task CleanUpAsync(List<Process> children, DirectoryInfo temp)
{
    List<Exception> failures = [];
    foreach (var child in children)
    {
        using (child)
        {
            try
            {
                if (!child.HasExited && child.CloseMainWindow())
                    child.WaitForExit(2000);
                if (!child.HasExited) child.Kill();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception e)
            {
                failures.Add(e);
            }
        }
    }

    // Windows can briefly hold the executable's image section after the process signals exit.
    var timer = Stopwatch.StartNew();
    while (true)
    {
        try
        {
            temp.Delete(recursive: true);
            break;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (timer.Elapsed >= TimeSpan.FromSeconds(10))
            {
                failures.Add(e);
                break;
            }
            await Task.Delay(250);
        }
    }
    if (failures.Count > 0)
        throw new AggregateException($"Could not fully clean up the stand-ins under {temp.FullName}.", failures);
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
    throw new InvalidOperationException(process.HasExited
        ? $"Stand-in PID {process.Id} exited with code {process.ExitCode} before its window could be found."
        : $"PID {process.Id} did not produce a visible Forever window within ten seconds.");
}

static (PixelRect Rect, int Style, bool Topmost) ReadState(nint handle) =>
    (WindowFinder.RectOf(handle), WindowFinder.Describe(handle).Style, WindowPlacer.IsTopmost(handle));

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    Console.WriteLine($"PASS  {description}");
}
