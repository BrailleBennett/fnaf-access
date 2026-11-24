using System.Threading.Channels;
using fnaf_access.Config;
using fnaf_access.Game;
using fnaf_access.Narration;
using fnaf_access.Win32;

namespace fnaf_access.Input;

/// <summary>
/// Translates intercepted keyboard actions into FNaF 1 mouse actions and sighted-parity queries.
/// Runs all mouse actions on a dedicated background worker so the WH_KEYBOARD_LL callback never blocks.
/// Strictly enforces physical on-screen visibility before clicking any office control.
/// </summary>
public sealed class FnafController : IDisposable
{
    private readonly KeybindConfig _config;
    private readonly Dictionary<int, List<GameAction>> _vkMap;
    private readonly GameNarrator _narrator;
    private readonly Func<FnafGameState?> _getState;
    private readonly Func<IntPtr> _getTargetWindow;

    private readonly Channel<GameAction> _actionQueue = Channel.CreateUnbounded<GameAction>();
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _workerThread;

    private volatile bool _passthrough;
    private volatile bool _panLeftHeld;
    private volatile bool _panRightHeld;
    private volatile bool _clickingBusy;
    private long _suppressConfirmUntilTick;
    private int _customNightFocus;
    private bool _customNightClickToggle;
    private FnafScreen _lastActionScreen = FnafScreen.Unknown;
    private readonly HashSet<int> _heldKeys = [];
    private readonly Lock _keyLock = new();
    private bool _disposed;

    public bool IsPassthrough => _passthrough;

    public FnafController(
        KeybindConfig config,
        GameNarrator narrator,
        Func<FnafGameState?> getState,
        Func<IntPtr> getTargetWindow)
    {
        _config = config;
        _vkMap = config.BuildVirtualKeyMap();
        _narrator = narrator;
        _getState = getState;
        _getTargetWindow = getTargetWindow;

        _workerThread = new Thread(WorkerLoop)
        {
            Name = "FnafAccess-InputWorker",
            IsBackground = true
        };
        _workerThread.Start();
    }

    /// <summary>
    /// Fast, non-blocking hook handler invoked on the WH_KEYBOARD_LL thread.
    /// </summary>
    public void HandleKeyEvent(object? sender, KeyboardHookEventArgs e)
    {
        // Always release held keys on KeyUp BEFORE checking passthrough/state/screen so no key
        // can ever get stuck across screen transitions or loading frames.
        if (!e.IsKeyDown)
        {
            bool wasSwallowed = UnregisterKeyDown(e.VirtualKeyCode);

            if (_vkMap.TryGetValue(e.VirtualKeyCode, out List<GameAction>? upActions))
            {
                if (upActions.Contains(GameAction.PanLeft) && _panLeftHeld)
                {
                    _panLeftHeld = false;
                    if (!_panRightHeld)
                    {
                        _actionQueue.Writer.TryWrite(GameAction.PanLeft);
                    }

                    e.Handled = true;
                    return;
                }

                if (upActions.Contains(GameAction.PanRight) && _panRightHeld)
                {
                    _panRightHeld = false;
                    if (!_panLeftHeld)
                    {
                        _actionQueue.Writer.TryWrite(GameAction.PanRight);
                    }

                    e.Handled = true;
                    return;
                }
            }

            if (wasSwallowed)
            {
                e.Handled = true;
            }

            return;
        }

        if (!_vkMap.TryGetValue(e.VirtualKeyCode, out List<GameAction>? mappedActions) || mappedActions.Count == 0)
        {
            return;
        }

        // Passthrough toggle always works while FNaF 1 is focused.
        if (mappedActions.Contains(GameAction.TogglePassthrough))
        {
            e.Handled = true;
            if (RegisterKeyDown(e.VirtualKeyCode))
            {
                _passthrough = !_passthrough;
                if (_passthrough)
                {
                    _panLeftHeld = false;
                    _panRightHeld = false;
                }

                _actionQueue.Writer.TryWrite(GameAction.TogglePassthrough);
            }

            return;
        }

        if (_passthrough)
        {
            return;
        }

        FnafGameState? state = _getState();
        if (state is null)
        {
            return;
        }

        // Handle held office panning keys (Left/Right in Office when Monitor is down)
        if (state.Screen == FnafScreen.Office && !state.MonitorUp)
        {
            if (mappedActions.Contains(GameAction.PanLeft))
            {
                e.Handled = true;
                _panLeftHeld = true;
                return;
            }

            if (mappedActions.Contains(GameAction.PanRight))
            {
                e.Handled = true;
                _panRightHeld = true;
                return;
            }
        }
        else
        {
            _panLeftHeld = false;
            _panRightHeld = false;
        }

        GameAction? resolved = ResolveContextualAction(state, mappedActions);
        if (resolved is null)
        {
            return;
        }

        e.Handled = true;

        // Ignore OS key-repeat for discrete toggle/click actions
        if (RegisterKeyDown(e.VirtualKeyCode))
        {
            // Drop MenuConfirm immediately during focus-gain / maximize / menu-close grace window.
            if (resolved.Value == GameAction.MenuConfirm &&
                Environment.TickCount64 < Volatile.Read(ref _suppressConfirmUntilTick))
            {
                return;
            }

            _actionQueue.Writer.TryWrite(resolved.Value);
        }
    }

    public void SuppressMenuConfirmBriefly(int milliseconds = 500)
    {
        long until = Environment.TickCount64 + milliseconds;
        long prev = Volatile.Read(ref _suppressConfirmUntilTick);
        if (until > prev)
        {
            Volatile.Write(ref _suppressConfirmUntilTick, until);
        }
    }

    public void ClearHeldInputs()
    {
        _panLeftHeld = false;
        _panRightHeld = false;
        lock (_keyLock)
        {
            _heldKeys.Clear();
        }
    }

    private bool RegisterKeyDown(int vk)
    {
        lock (_keyLock)
        {
            return _heldKeys.Add(vk);
        }
    }

    private bool UnregisterKeyDown(int vk)
    {
        lock (_keyLock)
        {
            return _heldKeys.Remove(vk);
        }
    }

    private static GameAction? ResolveContextualAction(FnafGameState state, List<GameAction> candidates)
    {
        // Always-available status/help actions
        foreach (GameAction a in candidates)
        {
            if (a is GameAction.ReadPowerAndUsage or GameAction.ReadTimeAndNight or GameAction.ReadDoorStatus
                or GameAction.ReadCurrentView or GameAction.RepeatLastOrDescribe or GameAction.SpeakHelp)
            {
                return a;
            }
        }

        if (state.Screen == FnafScreen.Title)
        {
            foreach (GameAction a in candidates)
            {
                if (a is GameAction.MenuUp or GameAction.MenuDown or GameAction.MenuConfirm or GameAction.ToggleMonitor)
                {
                    return a == GameAction.ToggleMonitor ? GameAction.MenuConfirm : a;
                }
            }

            return null;
        }

        if (state.Screen == FnafScreen.CustomNight)
        {
            foreach (GameAction a in candidates)
            {
                if (a is GameAction.MenuUp or GameAction.MenuDown or GameAction.PanLeft or GameAction.PanRight
                    or GameAction.MenuConfirm or GameAction.ToggleMonitor)
                {
                    return a == GameAction.ToggleMonitor ? GameAction.MenuConfirm : a;
                }
            }

            return null;
        }

        if (state.Screen is FnafScreen.Warning or FnafScreen.Newspaper or FnafScreen.GameOver or FnafScreen.Ending)
        {
            foreach (GameAction a in candidates)
            {
                if (a is GameAction.MenuConfirm or GameAction.ToggleMonitor)
                {
                    return GameAction.MenuConfirm;
                }
            }

            return null;
        }

        if (state.Screen == FnafScreen.Office)
        {
            if (state.MonitorUp)
            {
                foreach (GameAction a in candidates)
                {
                    if (a is GameAction.ToggleMonitor or GameAction.CamPrevious or GameAction.CamNext
                        or GameAction.Cam1A or GameAction.Cam1B or GameAction.Cam1C or GameAction.Cam2A
                        or GameAction.Cam2B or GameAction.Cam3 or GameAction.Cam4A or GameAction.Cam4B
                        or GameAction.Cam5 or GameAction.Cam6 or GameAction.Cam7 or GameAction.MuteCall)
                    {
                        return a;
                    }
                }

                // If player presses a door/light key while monitor is up, still route it so we can speak "Monitor is up".
                foreach (GameAction a in candidates)
                {
                    if (a is GameAction.ToggleLeftDoor or GameAction.ToggleLeftLight
                        or GameAction.ToggleRightDoor or GameAction.ToggleRightLight)
                    {
                        return a;
                    }
                }

                return null;
            }

            // Office with monitor down
            foreach (GameAction a in candidates)
            {
                if (a is GameAction.ToggleLeftDoor or GameAction.ToggleLeftLight
                    or GameAction.ToggleRightDoor or GameAction.ToggleRightLight
                    or GameAction.ToggleMonitor or GameAction.MuteCall or GameAction.HonkNose)
                {
                    return a;
                }
            }
        }

        return null;
    }

    private void WorkerLoop()
    {
        bool wasPanning = false;
        int lastWorkerFrame = -1;

        while (!_cts.IsCancellationRequested)
        {
            // Drain all queued discrete actions first
            while (_actionQueue.Reader.TryRead(out GameAction action))
            {
                ExecuteAction(action);
            }

            // Handle continuous office panning while Left or Right is held
            FnafGameState? state = _getState();
            IntPtr hwnd = _getTargetWindow();

            // Snap the mouse to the center deadzone on night loading screens ("what day" / "wait")
            // and upon entering the Office so the cursor left over from Title / Custom Night buttons
            // doesn't immediately start panning left or right as soon as the night begins.
            if (state is not null && state.FrameIndex != lastWorkerFrame)
            {
                lastWorkerFrame = state.FrameIndex;
                if (!_passthrough && !_clickingBusy && !_panLeftHeld && !_panRightHeld &&
                    hwnd != IntPtr.Zero &&
                    IsSameForegroundProcess(hwnd) &&
                    state.Screen is FnafScreen.NightIntro or FnafScreen.Office)
                {
                    WindowMapper.MoveMouseLogical(hwnd, 640, 360);
                }
            }

            if (!_passthrough && !_clickingBusy && hwnd != IntPtr.Zero &&
                state is { Screen: FnafScreen.Office, MonitorUp: false, PowerOut: false })
            {
                bool left = _panLeftHeld;
                bool right = _panRightHeld;

                if (left && !right)
                {
                    // Inside FNaF 1's "fast left" pan zone (x: -9..155)
                    WindowMapper.MoveMouseLogical(hwnd, 90, 360);
                    wasPanning = true;
                }
                else if (right && !left)
                {
                    // Inside FNaF 1's "fast right" pan zone (x: 1145..1309)
                    WindowMapper.MoveMouseLogical(hwnd, 1190, 360);
                    wasPanning = true;
                }
                else if (wasPanning)
                {
                    // Stop panning immediately by returning mouse to center deadzone
                    WindowMapper.MoveMouseLogical(hwnd, 640, 360);
                    wasPanning = false;
                }
            }
            else if (wasPanning)
            {
                if (hwnd != IntPtr.Zero)
                {
                    WindowMapper.MoveMouseLogical(hwnd, 640, 360);
                }

                wasPanning = false;
            }

            Thread.Sleep(16);
        }
    }

    private void ExecuteAction(GameAction action)
    {
        if (action == GameAction.TogglePassthrough)
        {
            _narrator.Say(_passthrough ? "Keyboard passthrough on." : "Keyboard controls active.");
            return;
        }

        FnafGameState? state = _getState();
        if (state is null)
        {
            return;
        }

        switch (action)
        {
            case GameAction.ReadPowerAndUsage:
                _narrator.SpeakPowerAndUsage(state);
                return;
            case GameAction.ReadTimeAndNight:
                _narrator.SpeakTimeAndNight(state);
                return;
            case GameAction.ReadDoorStatus:
                _narrator.SpeakVisibleDoorStatus(state);
                return;
            case GameAction.ReadCurrentView:
                _narrator.SpeakCurrentView(state);
                return;
            case GameAction.RepeatLastOrDescribe:
                _narrator.RepeatOrDescribe(state);
                return;
            case GameAction.SpeakHelp:
                _narrator.SpeakHelp(state);
                return;
        }

        IntPtr hwnd = _getTargetWindow();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // Release-to-center events from PanLeft / PanRight (Office only; Custom Night uses Left/Right for AI sliders)
        if (state.Screen == FnafScreen.Office && action is GameAction.PanLeft or GameAction.PanRight)
        {
            if (!_panLeftHeld && !_panRightHeld && !state.MonitorUp)
            {
                WindowMapper.MoveMouseLogical(hwnd, 640, 360);
            }

            return;
        }

        if (state.Screen != _lastActionScreen)
        {
            _lastActionScreen = state.Screen;
            if (state.Screen == FnafScreen.CustomNight)
            {
                _customNightFocus = 0;
            }
        }

        _clickingBusy = true;
        try
        {
            if (state.Screen == FnafScreen.Title)
            {
                HandleTitleAction(state, hwnd, action);
            }
            else if (state.Screen == FnafScreen.CustomNight)
            {
                HandleCustomNightAction(state, hwnd, action);
            }
            else if (state.Screen is FnafScreen.Warning or FnafScreen.Newspaper or FnafScreen.GameOver or FnafScreen.Ending)
            {
                if (action == GameAction.MenuConfirm)
                {
                    if (Environment.TickCount64 < Volatile.Read(ref _suppressConfirmUntilTick))
                    {
                        return;
                    }

                    WindowMapper.ClickLogical(hwnd, 640, 360);
                }
            }
            else if (state.Screen == FnafScreen.Office)
            {
                HandleOfficeAction(state, hwnd, action);
            }
        }
        finally
        {
            _clickingBusy = false;
        }
    }

    private void HandleTitleAction(FnafGameState state, IntPtr hwnd, GameAction action)
    {
        List<int> available = state.TitleButtonRects.Keys.OrderBy(k => k).ToList();
        if (available.Count == 0)
        {
            return;
        }

        int curIdx = available.IndexOf(state.TitleOptionSelected);
        if (curIdx < 0) curIdx = 0;

        if (action == GameAction.MenuUp)
        {
            int nextIdx = (curIdx - 1 + available.Count) % available.Count;
            ClickteamRect rect = state.TitleButtonRects[available[nextIdx]];
            WindowMapper.MoveMouseLogical(hwnd, rect.CenterX, rect.CenterY);
        }
        else if (action == GameAction.MenuDown)
        {
            int nextIdx = (curIdx + 1) % available.Count;
            ClickteamRect rect = state.TitleButtonRects[available[nextIdx]];
            WindowMapper.MoveMouseLogical(hwnd, rect.CenterX, rect.CenterY);
        }
        else if (action == GameAction.MenuConfirm)
        {
            if (Environment.TickCount64 < Volatile.Read(ref _suppressConfirmUntilTick))
            {
                return;
            }

            ClickteamRect rect = state.TitleButtonRects[available[curIdx]];
            WindowMapper.ClickLogical(hwnd, rect.CenterX, rect.CenterY, holdMs: 40);
        }
    }

    private void HandleCustomNightAction(FnafGameState state, IntPtr hwnd, GameAction action)
    {
        if (action == GameAction.PanLeft)
        {
            _customNightFocus = (_customNightFocus - 1 + 4) % 4;
            SpeakCustomNightFocus(state);
            return;
        }

        if (action == GameAction.PanRight)
        {
            _customNightFocus = (_customNightFocus + 1) % 4;
            SpeakCustomNightFocus(state);
            return;
        }

        if (action == GameAction.MenuUp)
        {
            (int currentAi, _, ClickteamRect? rightRect) = GetCustomNightSlot(state, _customNightFocus);
            if (currentAi >= 20)
            {
                _narrator.Say("20");
                return;
            }

            if (rightRect is ClickteamRect r)
            {
                ClickCustomNightArrow(hwnd, r);
            }

            return;
        }

        if (action == GameAction.MenuDown)
        {
            (int currentAi, ClickteamRect? leftRect, _) = GetCustomNightSlot(state, _customNightFocus);
            if (currentAi <= 0)
            {
                _narrator.Say("0");
                return;
            }

            if (leftRect is ClickteamRect r)
            {
                ClickCustomNightArrow(hwnd, r);
            }

            return;
        }

        if (action == GameAction.MenuConfirm)
        {
            if (Environment.TickCount64 < Volatile.Read(ref _suppressConfirmUntilTick))
            {
                return;
            }

            if (state.CustomNightReadyRect is ClickteamRect ready)
            {
                WindowMapper.ClickLogical(hwnd, ready.CenterX, ready.CenterY, holdMs: 40);
            }
        }
    }

    private void ClickCustomNightArrow(IntPtr hwnd, ClickteamRect rect)
    {
        // Alternate between two points inside the arrow hitbox separated by more than
        // Windows' double-click rectangle (SM_CXDOUBLECLK x SM_CYDOUBLECLK = 4x4 px) so
        // rapid Up/Down taps never coalesce into WM_LBUTTONDBLCLK and register immediately.
        _customNightClickToggle = !_customNightClickToggle;
        double maxDx = Math.Max(0.0, rect.Width / 2.0 - 3.0);
        double maxDy = Math.Max(0.0, rect.Height / 2.0 - 3.0);
        double dx = Math.Min(Math.Max(rect.Width / 4.0, 5.0), maxDx);
        double dy = Math.Min(Math.Max(rect.Height / 4.0, 5.0), maxDy);

        double x = _customNightClickToggle ? rect.CenterX + dx : rect.CenterX - dx;
        double y = _customNightClickToggle ? rect.CenterY + dy : rect.CenterY - dy;
        WindowMapper.ClickLogical(hwnd, x, y, holdMs: 18);
    }

    private void SpeakCustomNightFocus(FnafGameState state)
    {
        string text = _customNightFocus switch
        {
            0 => $"Freddy, {state.FreddyAi}",
            1 => $"Bonnie, {state.BonnieAi}",
            2 => $"Chica, {state.ChicaAi}",
            _ => $"Foxy, {state.FoxyAi}"
        };
        _narrator.Say(text);
    }

    private static (int Ai, ClickteamRect? LeftRect, ClickteamRect? RightRect) GetCustomNightSlot(FnafGameState state, int slot) =>
        slot switch
        {
            0 => (state.FreddyAi, state.FreddyLeftRect, state.FreddyRightRect),
            1 => (state.BonnieAi, state.BonnieLeftRect, state.BonnieRightRect),
            2 => (state.ChicaAi, state.ChicaLeftRect, state.ChicaRightRect),
            3 => (state.FoxyAi, state.FoxyLeftRect, state.FoxyRightRect),
            _ => (0, null, null)
        };

    private void HandleOfficeAction(FnafGameState state, IntPtr hwnd, GameAction action)
    {
        if (state.PowerOut)
        {
            _narrator.Say("Power out.");
            return;
        }

        // Mute call is fixed to the screen and clickable whenever visible (even with monitor up)
        if (action == GameAction.MuteCall)
        {
            if (!state.CanMuteCall || state.MuteCallRect is null)
            {
                _narrator.Say("Mute call not visible.");
                return;
            }

            int muteOffX = ResolveScreenPinnedOffsetX(state.MuteCallRect.Value.Left, baseScreenLeft: 27, state.WindowX);
            ClickOfficeRectAndRecenter(hwnd, state.MuteCallRect.Value, windowX: muteOffX, windowY: 0);
            _narrator.Say("Call muted.");
            return;
        }

        // Toggle camera monitor by sweeping cleanly through "flip down" (arm zone, Y ~ 561..643)
        // into "flip up" (trigger zone, Y ~ 653..735) and back through "flip down" to re-arm
        // FNaF 1's "flip it" counter. Staying at X = 640 keeps the cursor inside the office pan deadzone.
        if (action == GameAction.ToggleMonitor)
        {
            const double safeX = 640.0;
            double downY = state.FlipDownRect is ClickteamRect fd
                ? Math.Clamp(fd.CenterY - state.WindowY, 565, 640)
                : 602.0;
            double upY = state.FlipUpRect is ClickteamRect fu
                ? Math.Clamp(fu.CenterY - state.WindowY, 660, 708)
                : 685.0;

            WindowMapper.MoveMouseLogical(hwnd, safeX, downY);
            Thread.Sleep(45);
            WindowMapper.MoveMouseLogical(hwnd, safeX, upY);
            Thread.Sleep(60);
            WindowMapper.MoveMouseLogical(hwnd, safeX, downY);
            Thread.Sleep(45);
            WindowMapper.MoveMouseLogical(hwnd, safeX, 360);
            return;
        }

        // Camera selection while monitor is up
        if (state.MonitorUp)
        {
            if (action is GameAction.ToggleLeftDoor or GameAction.ToggleLeftLight
                or GameAction.ToggleRightDoor or GameAction.ToggleRightLight)
            {
                _narrator.Say("Monitor is up.");
                return;
            }

            FnafCamera targetCam = ResolveTargetCamera(state.CurrentCamera, action);
            if (targetCam != FnafCamera.None && state.CameraButtonRects.TryGetValue(targetCam, out ClickteamRect camRect))
            {
                // Auto-detect whether Clickteam shifted screen-pinned camera button bounds by WindowX
                int camOffX = state.CameraButtonRects.TryGetValue(FnafCamera.Cam1A, out ClickteamRect c1a)
                    ? ResolveScreenPinnedOffsetX(c1a.Left, baseScreenLeft: 954, state.WindowX)
                    : 0;
                double cx = Math.Clamp(camRect.CenterX - camOffX, 10, WindowMapper.LogicalWidth - 10);
                double cy = Math.Clamp(camRect.CenterY - state.WindowY, 10, WindowMapper.LogicalHeight - 10);
                WindowMapper.ClickLogical(hwnd, cx, cy, holdMs: 30);
                WindowMapper.MoveMouseLogical(hwnd, 640, 360);
            }

            return;
        }

        // Strictly sighted-parity office controls: NEVER toggle a control that is not physically on screen!
        switch (action)
        {
            case GameAction.ToggleLeftDoor:
                if (!state.CanReachLeftDoor || state.LeftDoorButtonRect is null)
                {
                    _narrator.Say("Left door not in view.");
                    return;
                }

                ClickOfficeRectAndRecenter(hwnd, state.LeftDoorButtonRect.Value, state.WindowX, state.WindowY);
                break;

            case GameAction.ToggleLeftLight:
                if (!state.CanReachLeftLight || state.LeftLightButtonRect is null)
                {
                    _narrator.Say("Left light not in view.");
                    return;
                }

                ClickOfficeRectAndRecenter(hwnd, state.LeftLightButtonRect.Value, state.WindowX, state.WindowY);
                break;

            case GameAction.ToggleRightDoor:
                if (!state.CanReachRightDoor || state.RightDoorButtonRect is null)
                {
                    _narrator.Say("Right door not in view.");
                    return;
                }

                ClickOfficeRectAndRecenter(hwnd, state.RightDoorButtonRect.Value, state.WindowX, state.WindowY);
                break;

            case GameAction.ToggleRightLight:
                if (!state.CanReachRightLight || state.RightLightButtonRect is null)
                {
                    _narrator.Say("Right light not in view.");
                    return;
                }

                ClickOfficeRectAndRecenter(hwnd, state.RightLightButtonRect.Value, state.WindowX, state.WindowY);
                break;

            case GameAction.HonkNose:
                if (!state.CanHonkNose || state.FreddyNoseRect is null)
                {
                    _narrator.Say("Poster not in view.");
                    return;
                }

                ClickOfficeRectAndRecenter(hwnd, state.FreddyNoseRect.Value, state.WindowX, state.WindowY);
                break;
        }
    }

    private static bool IsSameForegroundProcess(IntPtr targetHwnd)
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero || NativeMethods.IsIconic(fg))
        {
            return false;
        }

        if (fg == targetHwnd)
        {
            return true;
        }

        NativeMethods.GetWindowThreadProcessId(fg, out uint fgPid);
        NativeMethods.GetWindowThreadProcessId(targetHwnd, out uint targetPid);
        return fgPid != 0 && fgPid == targetPid;
    }

    private static int ResolveScreenPinnedOffsetX(int actualLeft, int baseScreenLeft, int windowX)
    {
        if (windowX == 0)
        {
            return 0;
        }

        int distShifted = Math.Abs(actualLeft - (baseScreenLeft + windowX));
        int distUnshifted = Math.Abs(actualLeft - baseScreenLeft);
        return distShifted < distUnshifted ? windowX : 0;
    }

    private static void ClickOfficeRectAndRecenter(IntPtr hwnd, ClickteamRect rect, int windowX, int windowY)
    {
        if (!rect.TryGetVisibleClickPoint(windowX, windowY, out double lx, out double ly))
        {
            return;
        }

        WindowMapper.ClickLogical(hwnd, lx, ly, holdMs: 30);
        // Immediately return cursor to neutral center so hovering on a side panel doesn't keep panning
        WindowMapper.MoveMouseLogical(hwnd, 640, 360);
    }

    private static FnafCamera ResolveTargetCamera(FnafCamera current, GameAction action)
    {
        if (action == GameAction.CamPrevious || action == GameAction.CamNext)
        {
            int idx = -1;
            for (int i = 0; i < FnafGameState.AllCameras.Count; i++)
            {
                if (FnafGameState.AllCameras[i].Camera == current)
                {
                    idx = i;
                    break;
                }
            }

            if (idx < 0) idx = 0;
            int nextIdx = action == GameAction.CamNext
                ? (idx + 1) % FnafGameState.AllCameras.Count
                : (idx - 1 + FnafGameState.AllCameras.Count) % FnafGameState.AllCameras.Count;
            return FnafGameState.AllCameras[nextIdx].Camera;
        }

        return action switch
        {
            GameAction.Cam1A => FnafCamera.Cam1A,
            GameAction.Cam1B => FnafCamera.Cam1B,
            GameAction.Cam1C => FnafCamera.Cam1C,
            GameAction.Cam2A => FnafCamera.Cam2A,
            GameAction.Cam2B => FnafCamera.Cam2B,
            GameAction.Cam3 => FnafCamera.Cam3,
            GameAction.Cam4A => FnafCamera.Cam4A,
            GameAction.Cam4B => FnafCamera.Cam4B,
            GameAction.Cam5 => FnafCamera.Cam5,
            GameAction.Cam6 => FnafCamera.Cam6,
            GameAction.Cam7 => FnafCamera.Cam7,
            _ => FnafCamera.None
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        _actionQueue.Writer.TryComplete();
        _workerThread.Join(TimeSpan.FromSeconds(1));
        _cts.Dispose();
    }
}
