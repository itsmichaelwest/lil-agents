using System.Runtime.InteropServices;
using LilAgents.Interop;

namespace LilAgents.Core;

/// <summary>
/// Detects Windows taskbar position and size, exposing the walking area
/// where characters should animate. Supports multi-monitor setups and
/// auto-hide taskbars.
/// </summary>
public sealed class TaskbarGeometry
{
    /// <summary>
    /// Describes the taskbar on a particular monitor.
    /// </summary>
    public readonly record struct TaskbarInfo(
        int Left, int Top, int Right, int Bottom,
        TaskbarEdge Edge, bool AutoHide);

    /// <summary>
    /// Which edge of the screen the taskbar is docked to.
    /// </summary>
    public enum TaskbarEdge { Left, Top, Right, Bottom }

    /// <summary>
    /// Describes a rectangular walking area in screen coordinates.
    /// </summary>
    public readonly record struct WalkingArea(
        double X, double Y, double Width, double Height);

    /// <summary>
    /// Gets the primary taskbar position using SHAppBarMessage.
    /// Returns null if the call fails.
    /// </summary>
    public static TaskbarInfo? GetPrimaryTaskbar()
    {
        var abd = new NativeMethods.APPBARDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>()
        };

        var result = NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref abd);
        if (result == 0)
            return null;

        var edge = abd.uEdge switch
        {
            NativeMethods.ABE_LEFT => TaskbarEdge.Left,
            NativeMethods.ABE_TOP => TaskbarEdge.Top,
            NativeMethods.ABE_RIGHT => TaskbarEdge.Right,
            NativeMethods.ABE_BOTTOM => TaskbarEdge.Bottom,
            _ => TaskbarEdge.Bottom,
        };

        // Check auto-hide state
        var stateAbd = new NativeMethods.APPBARDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>()
        };
        var state = NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref stateAbd);
        bool autoHide = ((int)state & NativeMethods.ABS_AUTOHIDE) != 0;

        return new TaskbarInfo(
            abd.rc.Left, abd.rc.Top, abd.rc.Right, abd.rc.Bottom,
            edge, autoHide);
    }

    /// <summary>
    /// Gets the walking area for the specified monitor (or the primary monitor if null).
    /// The walking area is the strip along the desktop edge adjacent to the taskbar
    /// where characters walk. The height of the walking area accommodates the
    /// character sprite.
    /// </summary>
    /// <param name="characterHeight">Height of the character sprite in screen pixels.</param>
    /// <param name="hMonitor">Monitor handle, or null for the primary monitor.</param>
    public static WalkingArea GetWalkingArea(int characterHeight, nint? hMonitor = null)
    {
        var monitor = hMonitor ?? WindowInterop.GetPrimaryMonitor();

        // Get the work area (excludes taskbar) and full monitor bounds
        var mi = new NativeMethods.MONITORINFOEX
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>()
        };

        if (!NativeMethods.GetMonitorInfoW(monitor, ref mi))
        {
            // Fallback: return a reasonable default
            return new WalkingArea(0, 800 - characterHeight, 1920, characterHeight);
        }

        var workArea = mi.rcWork;
        var fullBounds = mi.rcMonitor;

        // Determine taskbar edge by comparing work area to full bounds
        var taskbar = GetPrimaryTaskbar();
        var edge = taskbar?.Edge ?? InferTaskbarEdge(fullBounds, workArea);

        return edge switch
        {
            TaskbarEdge.Bottom => new WalkingArea(
                workArea.Left,
                workArea.Bottom - characterHeight,
                workArea.Right - workArea.Left,
                characterHeight),

            TaskbarEdge.Top => new WalkingArea(
                workArea.Left,
                workArea.Top,
                workArea.Right - workArea.Left,
                characterHeight),

            TaskbarEdge.Left => new WalkingArea(
                workArea.Left,
                workArea.Bottom - characterHeight,
                workArea.Right - workArea.Left,
                characterHeight),

            TaskbarEdge.Right => new WalkingArea(
                workArea.Left,
                workArea.Bottom - characterHeight,
                workArea.Right - workArea.Left,
                characterHeight),

            _ => new WalkingArea(
                workArea.Left,
                workArea.Bottom - characterHeight,
                workArea.Right - workArea.Left,
                characterHeight),
        };
    }

    /// <summary>
    /// Enumerates all monitors and returns their handles.
    /// </summary>
    public static List<nint> GetAllMonitors()
    {
        var monitors = new List<nint>();
        NativeMethods.EnumDisplayMonitors(0, 0, (hMon, _, ref _, _) =>
        {
            monitors.Add(hMon);
            return true;
        }, 0);
        return monitors;
    }

    /// <summary>
    /// Gets the monitor handle for the given screen-coordinate point.
    /// </summary>
    public static nint GetMonitorForPoint(int x, int y)
    {
        var pt = new NativeMethods.POINT { X = x, Y = y };
        return NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
    }

    /// <summary>
    /// Infers which edge the taskbar is on by comparing the work area to the full
    /// monitor bounds. The edge where the work area is inset is where the taskbar sits.
    /// </summary>
    private static TaskbarEdge InferTaskbarEdge(NativeMethods.RECT full, NativeMethods.RECT work)
    {
        int diffBottom = full.Bottom - work.Bottom;
        int diffTop = work.Top - full.Top;
        int diffLeft = work.Left - full.Left;
        int diffRight = full.Right - work.Right;

        int max = Math.Max(Math.Max(diffBottom, diffTop), Math.Max(diffLeft, diffRight));

        if (max <= 0) return TaskbarEdge.Bottom; // no difference, assume bottom

        if (diffBottom == max) return TaskbarEdge.Bottom;
        if (diffTop == max) return TaskbarEdge.Top;
        if (diffLeft == max) return TaskbarEdge.Left;
        return TaskbarEdge.Right;
    }
}
