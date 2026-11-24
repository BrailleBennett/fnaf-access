using System.Runtime.InteropServices;

namespace fnaf_access.Win32;

/// <summary>
/// Maps FNaF 1's logical 1280x720 canvas coordinates onto live screen coordinates
/// in both windowed and fullscreen modes, and sends mouse input.
/// </summary>
public static class WindowMapper
{
    public const double LogicalWidth = 1280.0;
    public const double LogicalHeight = 720.0;
    private const double LogicalAspect = LogicalWidth / LogicalHeight;

    public readonly record struct ViewportRect(
        int Left,
        int Top,
        int Width,
        int Height,
        int ClientWidth,
        int ClientHeight);

    /// <summary>
    /// Computes the active 16:9 game viewport in screen coordinates for the target window.
    /// If the window's client aspect ratio differs from 16:9 (e.g., ultrawide fullscreen with pillarbox),
    /// centers the 16:9 content region unless stretchToClient is enabled.
    /// </summary>
    public static bool TryGetViewport(IntPtr hWnd, out ViewportRect viewport, bool preserveAspect = true)
    {
        viewport = default;
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd))
        {
            return false;
        }

        if (!NativeMethods.GetClientRect(hWnd, out NativeMethods.RECT clientRect) ||
            clientRect.Width <= 0 ||
            clientRect.Height <= 0)
        {
            return false;
        }

        var origin = new NativeMethods.POINT { X = 0, Y = 0 };
        if (!NativeMethods.ClientToScreen(hWnd, ref origin))
        {
            return false;
        }

        int cw = clientRect.Width;
        int ch = clientRect.Height;

        if (!preserveAspect)
        {
            viewport = new ViewportRect(origin.X, origin.Y, cw, ch, cw, ch);
            return true;
        }

        double clientAspect = (double)cw / ch;
        int vw, vh, vx, vy;

        if (Math.Abs(clientAspect - LogicalAspect) < 0.01)
        {
            vx = origin.X;
            vy = origin.Y;
            vw = cw;
            vh = ch;
        }
        else if (clientAspect > LogicalAspect)
        {
            // Pillarboxed (wider than 16:9)
            vh = ch;
            vw = (int)Math.Round(ch * LogicalAspect);
            vx = origin.X + (cw - vw) / 2;
            vy = origin.Y;
        }
        else
        {
            // Letterboxed (taller than 16:9)
            vw = cw;
            vh = (int)Math.Round(cw / LogicalAspect);
            vx = origin.X;
            vy = origin.Y + (ch - vh) / 2;
        }

        viewport = new ViewportRect(vx, vy, vw, vh, cw, ch);
        return true;
    }

    public static bool TryLogicalToScreen(
        IntPtr hWnd,
        double logicalX,
        double logicalY,
        out int screenX,
        out int screenY,
        bool preserveAspect = true)
    {
        screenX = 0;
        screenY = 0;

        if (!TryGetViewport(hWnd, out ViewportRect vp, preserveAspect))
        {
            return false;
        }

        double nx = Math.Clamp(logicalX / LogicalWidth, 0.0, 1.0);
        double ny = Math.Clamp(logicalY / LogicalHeight, 0.0, 1.0);

        screenX = vp.Left + (int)Math.Round(nx * vp.Width);
        screenY = vp.Top + (int)Math.Round(ny * vp.Height);
        return true;
    }

    public static bool MoveMouseLogical(IntPtr hWnd, double logicalX, double logicalY, bool preserveAspect = true)
    {
        if (!TryLogicalToScreen(hWnd, logicalX, logicalY, out int sx, out int sy, preserveAspect))
        {
            return false;
        }

        SendAbsoluteMouse(sx, sy, NativeMethods.MOUSEEVENTF_MOVE);
        return true;
    }

    public static bool ClickLogical(IntPtr hWnd, double logicalX, double logicalY, int holdMs = 25, bool preserveAspect = true)
    {
        if (!TryLogicalToScreen(hWnd, logicalX, logicalY, out int sx, out int sy, preserveAspect))
        {
            return false;
        }

        SendAbsoluteMouse(sx, sy, NativeMethods.MOUSEEVENTF_MOVE);
        Thread.Sleep(15);
        SendAbsoluteMouse(sx, sy, NativeMethods.MOUSEEVENTF_LEFTDOWN);
        if (holdMs > 0)
        {
            Thread.Sleep(holdMs);
        }

        SendAbsoluteMouse(sx, sy, NativeMethods.MOUSEEVENTF_LEFTUP);
        return true;
    }

    private static void SendAbsoluteMouse(int screenX, int screenY, uint eventFlags)
    {
        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN));
        int vh = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));

        int absX = (int)Math.Round((screenX - vx) * 65535.0 / Math.Max(1, vw - 1));
        int absY = (int)Math.Round((screenY - vy) * 65535.0 / Math.Max(1, vh - 1));

        var inputs = new NativeMethods.INPUT[1];
        inputs[0].type = NativeMethods.INPUT_MOUSE;
        inputs[0].U.mi = new NativeMethods.MOUSEINPUT
        {
            dx = absX,
            dy = absY,
            dwFlags = eventFlags | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK
        };

        NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }
}
