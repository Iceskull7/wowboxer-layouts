using System.ComponentModel;
using System.Runtime.InteropServices;
using WoWBoxer.Layouts.Core;
using static WoWBoxer.Layouts.Win32.NativeMethods;

namespace WoWBoxer.Layouts.Win32;

/// <summary>One explicitly selected game window temporarily uses a borderless, multi-monitor client area.</summary>
public sealed class OffhandWindow
{
    private sealed record Snapshot(WindowInfo Window, PixelRect Bounds, int Style, int ExStyle);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Snapshot? _original;

    public WindowInfo? ActiveWindow => _original is { } s && IsCurrent(s.Window) ? s.Window : null;

    public bool IsManaging(nint handle) =>
        _original is { } s && s.Window.Handle == handle && IsCurrent(s.Window);

    public Task ExpandAsync(WindowInfo selected, PixelRect target) => ExpandCoreAsync(selected, target, null);

    /// <summary>Expands the chosen client and publishes its verified dimensions. Returns false in demo mode
    /// or when the selected installation has no Offhand addon, leaving the window expanded.</summary>
    public Task<bool> ExpandWithAddonAsync(WindowInfo selected, MonitorInfo main, MonitorInfo workspace, PixelRect span)
    {
        var lua = OffhandTopology.ToLua(main, workspace, span);
        return ExpandCoreAsync(selected, span, lua);
    }

    private async Task<bool> ExpandCoreAsync(WindowInfo selected, PixelRect target, string? lua)
    {
        if (target.Width <= 0 || target.Height <= 0) throw new InvalidOperationException("Choose a non-empty Offhand span.");
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            RequireCurrent(selected);
            var saved = _original;
            if (saved is not null && !IsCurrent(saved.Window)) saved = null;
            if (saved is not null && saved.Window.Handle != selected.Handle)
                throw new InvalidOperationException("Restore the expanded client before choosing another one.");

            RequireNormalWindow(selected.Handle);
            saved ??= Capture(selected);
            _original = saved;   // reserve the slot before a normal placement step can run
            try
            {
                var (style, exStyle) = WindowChrome.ToBorderless(saved.Style, saved.ExStyle);
                SetChrome(saved, style, exStyle);
                await ResizeAsync(saved.Window, target, checkClient: true).ConfigureAwait(false);
                if (Sandbox.Active) return false;
                await VerifySpanAsync(saved.Window, target).ConfigureAwait(false);
                if (lua is null) return false;
                try
                {
                    OffhandBridgeStore.Write(saved.Window.ExePath, lua);
                    return true;   // Also when an identical topology was already present.
                }
                catch (OffhandAddonNotInstalledException) { return false; }
            }
            catch (InvalidOperationException error)
            {
                try { await RestoreOriginalAsync(saved).ConfigureAwait(false); }
                catch (InvalidOperationException restoreError)
                {
                    throw new InvalidOperationException($"{error.Message} Restore also failed: {restoreError.Message}", error);
                }
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task RestoreAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_original is { } saved) await RestoreOriginalAsync(saved).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task RestoreOriginalAsync(Snapshot saved)
    {
        if (IsCurrent(saved.Window))
        {
            await ReturnToNormalAsync(saved.Window).ConfigureAwait(false);
            SetChrome(saved, saved.Style, saved.ExStyle);
            await ResizeAsync(saved.Window, saved.Bounds, checkClient: false).ConfigureAwait(false);
        }
        _original = null;
    }

    private static Snapshot Capture(WindowInfo window)
    {
        RequireCurrent(window);
        var bounds = WindowFinder.RectOf(window.Handle);
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidOperationException("Could not read the selected window's size.");
        return new(window, bounds, Sandbox.Active ? 0 : GetWindowLong(window.Handle, GWL_STYLE),
            Sandbox.Active ? 0 : GetWindowLong(window.Handle, GWL_EXSTYLE));
    }

    private static bool IsCurrent(WindowInfo window) => Sandbox.Active ? Sandbox.IsSameWindow(window) :
        window.ProcessStartedUtc is { } start && WindowFinder.PidOf(window.Handle) == window.Pid &&
        ProcessInfo.StartTime(window.Pid) == start && WindowFinder.ClassOf(window.Handle) == window.ClassName;

    private static void RequireCurrent(WindowInfo window)
    {
        if (!IsCurrent(window)) throw new InvalidOperationException("The selected WoW window could not be identified. Refresh the client list and choose it again.");
    }

    private static void RequireNormalWindow(nint handle)
    {
        if (!Sandbox.Active && (IsZoomed(handle) || IsIconic(handle)))
            throw new InvalidOperationException("Return the selected WoW client to a normal, non-maximized window in Windowed mode, then try again.");
    }

    // The user might minimize the expanded client while playing another one. Undo returns it to its
    // original normal window state, including on app exit; SW_SHOWNOACTIVATE leaves keyboard focus alone.
    private static async Task ReturnToNormalAsync(WindowInfo window)
    {
        var h = window.Handle;
        if (Sandbox.Active || (!IsZoomed(h) && !IsIconic(h))) return;
        RequireCurrent(window);
        if (!ShowWindowAsync(h, SW_SHOWNOACTIVATE))
            throw new InvalidOperationException("Windows could not return the selected client to Windowed mode.");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(75).ConfigureAwait(false);
            RequireCurrent(window);
            if (!IsZoomed(h) && !IsIconic(h)) return;
        }
        RequireNormalWindow(h);
    }

    // Only change the chrome bits this expansion owns. A topmost or other unrelated style set meanwhile is preserved.
    private static void SetChrome(Snapshot saved, int style, int exStyle)
    {
        if (Sandbox.Active) return;
        var expanded = WindowChrome.ToBorderless(saved.Style, saved.ExStyle);
        SetStyleBits(saved.Window, GWL_STYLE, saved.Style ^ expanded.Style, style);
        SetStyleBits(saved.Window, GWL_EXSTYLE, saved.ExStyle ^ expanded.ExStyle, exStyle);
    }

    private static void SetStyleBits(WindowInfo window, int index, int mask, int desired)
    {
        RequireCurrent(window);
        var current = GetWindowLong(window.Handle, index);
        var next = (current & ~mask) | (desired & mask);
        if (next == current) return;
        if (SetWindowLong(window.Handle, index, next) == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error != 0) throw WindowError(error);
        }
    }

    private static async Task ResizeAsync(WindowInfo window, PixelRect bounds, bool checkClient)
    {
        RequireCurrent(window);
        var h = window.Handle;
        if (Sandbox.Active)
            Sandbox.Move(h, bounds);
        else if (!SetWindowPos(h, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_FRAMECHANGED))
            throw WindowError(Marshal.GetLastPInvokeError());

        // SetWindowPos queues cross-thread moves; only report success after reading the result from the client.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (!Sandbox.Active) await Task.Delay(75).ConfigureAwait(false);
            RequireCurrent(window);
            if (WindowFinder.RectOf(h) == bounds &&
                (!checkClient || WindowFinder.ClientSizeOf(h) == (bounds.Width, bounds.Height))) return;
            if (Sandbox.Active) break;
        }
        var actual = WindowFinder.RectOf(h);
        var client = WindowFinder.ClientSizeOf(h);
        throw new InvalidOperationException($"The client did not accept {bounds}. The window is {actual} with a {client.Width}x{client.Height} game area. Check that WoW uses Windowed display mode.");
    }

    // A running client can accept the first WM_SIZE and reapply its own windowed size later.
    // Observe only: do not fight WoW by sending more resize requests during this interval.
    private static async Task VerifySpanAsync(WindowInfo window, PixelRect target)
    {
        bool previousMismatch = false;
        for (int sample = 1; sample <= 24; sample++)
        {
            await Task.Delay(250).ConfigureAwait(false);
            RequireCurrent(window);
            bool matches = WindowFinder.RectOf(window.Handle) == target &&
                WindowFinder.ClientSizeOf(window.Handle) == (target.Width, target.Height);
            if (!matches && (previousMismatch || sample == 24))
                throw new InvalidOperationException("WoW changed the requested span after the resize. Check Windowed mode, then extend the client again.");
            previousMismatch = !matches;
        }
    }

    private static InvalidOperationException WindowError(int code) => new(code == 5
        ? "Windows denied access to the game window. Run WoW and WoWBoxer at the same privilege level."
        : $"Windows could not change the selected window: {new Win32Exception(code).Message} ({code}).");
}
