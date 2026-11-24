using System.ComponentModel;
using System.Runtime.InteropServices;
using fnaf_access.Win32;

namespace fnaf_access.Input;

public sealed class KeyboardHookEventArgs : EventArgs
{
    public int VirtualKeyCode { get; }
    public bool IsKeyDown { get; }
    public bool IsSystemKey { get; }
    public bool Handled { get; set; }

    public KeyboardHookEventArgs(int virtualKeyCode, bool isKeyDown, bool isSystemKey)
    {
        VirtualKeyCode = virtualKeyCode;
        IsKeyDown = isKeyDown;
        IsSystemKey = isSystemKey;
    }
}

/// <summary>
/// Global WH_KEYBOARD_LL hook running on a dedicated Win32 message-pump thread.
/// Calls the supplied predicate to decide whether interception is currently active
/// (e.g., only when FNaF 1 is the foreground window).
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly Func<bool> _shouldIntercept;
    private readonly Func<uint> _getTargetPid;
    private readonly Action? _onTargetActivated;
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
    private readonly NativeMethods.WinEventDelegate _winEventProc;
    private readonly ManualResetEventSlim _startedEvent = new(false);

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hookHandle;
    private IntPtr _fgEventHook;
    private IntPtr _minEndEventHook;
    private Exception? _startupError;
    private bool _disposed;

    public event EventHandler<KeyboardHookEventArgs>? KeyEvent;

    public KeyboardHook(Func<bool> shouldIntercept, Func<uint>? getTargetPid = null, Action? onTargetActivated = null)
    {
        _shouldIntercept = shouldIntercept;
        _getTargetPid = getTargetPid ?? (() => 0);
        _onTargetActivated = onTargetActivated;
        // Keep delegates rooted for the lifetime of the hooks.
        _hookProc = HookCallback;
        _winEventProc = WinEventCallback;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(RunMessageLoop)
        {
            Name = "FnafAccess-KeyboardHook",
            IsBackground = true
        };
        _thread.Start();
        _startedEvent.Wait();

        if (_startupError is not null)
        {
            throw _startupError;
        }
    }

    private void RunMessageLoop()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        IntPtr moduleHandle = NativeMethods.GetModuleHandleW(null);
        _hookHandle = NativeMethods.SetWindowsHookExW(NativeMethods.WH_KEYBOARD_LL, _hookProc, moduleHandle, 0);

        if (_hookHandle == IntPtr.Zero)
        {
            _startupError = new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install WH_KEYBOARD_LL hook.");
            _startedEvent.Set();
            return;
        }

        _fgEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventProc,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);

        _minEndEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_MINIMIZEEND,
            NativeMethods.EVENT_SYSTEM_MINIMIZEEND,
            IntPtr.Zero,
            _winEventProc,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);

        _startedEvent.Set();

        while (NativeMethods.GetMessageW(out NativeMethods.MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }

        if (_fgEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_fgEventHook);
            _fgEventHook = IntPtr.Zero;
        }

        if (_minEndEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_minEndEventHook);
            _minEndEventHook = IntPtr.Zero;
        }

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private void WinEventCallback(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        uint targetPid = _getTargetPid();
        if (targetPid == 0 || hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != targetPid)
        {
            return;
        }

        _onTargetActivated?.Invoke();

        // Work around a vanilla FNaF 1 / Clickteam Fusion quirk: restoring the game from the Windows
        // Taskbar with Enter (Win+T -> arrows -> Up -> Enter) leaves VK_RETURN held in the async
        // keyboard state when FNaF 1 wakes up, causing FNaF 1's own Title screen to immediately launch
        // the selected menu option. Injecting a synthetic VK_RETURN KeyUp clears that held state.
        if (_externalHeldKeys.Contains(0x0D) || IsKeyPressed(0x0D))
        {
            SendSyntheticKeyUp(0x0D);
        }
    }

    private static void SendSyntheticKeyUp(ushort vk)
    {
        var inputs = new NativeMethods.INPUT[1];
        inputs[0].type = NativeMethods.INPUT_KEYBOARD;
        inputs[0].U.ki = new NativeMethods.KEYBDINPUT
        {
            wVk = vk,
            dwFlags = NativeMethods.KEYEVENTF_KEYUP
        };

        NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && lParam != IntPtr.Zero)
        {
            int msg = wParam.ToInt32();
            bool isDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
            bool isUp = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;

            if (isDown || isUp)
            {
                var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

                // Track Alt releases so Alt+Enter (fullscreen/maximize) never leaks Enter to the game.
                if (info.vkCode is 0x12 or 0xA4 or 0xA5)
                {
                    Volatile.Write(ref _lastAltActivityTick, Environment.TickCount64);
                }

                // Never intercept injected input, system/Alt combos, or keys pressed with Ctrl/Alt/Win held.
                bool isInjected = (info.flags & NativeMethods.LLKHF_INJECTED) != 0;
                bool isAltDown = (info.flags & NativeMethods.LLKHF_ALTDOWN) != 0 || IsKeyPressed(NativeMethods.VK_MENU);
                bool isCtrlDown = IsKeyPressed(NativeMethods.VK_CONTROL);
                bool isWinDown = IsKeyPressed(NativeMethods.VK_LWIN) || IsKeyPressed(NativeMethods.VK_RWIN);
                bool isSys = msg == NativeMethods.WM_SYSKEYDOWN || msg == NativeMethods.WM_SYSKEYUP;

                // On KeyUp, always clear external-held tracking and still notify listeners if needed.
                if (isUp)
                {
                    bool wasExternal = _externalHeldKeys.Remove((int)info.vkCode);
                    if (wasExternal)
                    {
                        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                    }
                }

                // On KeyDown, ignore if modifier held, recent Alt+Enter, or FNaF 1 is not active.
                bool modifierBlocked = isDown && (isSys || isAltDown || isCtrlDown || isWinDown);

                // Suppress Enter briefly after Alt activity so Alt+Enter (maximize/fullscreen) never clicks menus.
                bool recentAltEnter = isDown && info.vkCode == 0x0D &&
                    (Environment.TickCount64 - Volatile.Read(ref _lastAltActivityTick)) < 450;

                if (isInjected)
                {
                    return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                }

                bool canIntercept = _shouldIntercept();
                if (isDown && (!canIntercept || modifierBlocked || recentAltEnter))
                {
                    // Remember keys pressed outside FNaF 1 (e.g., Enter on Taskbar or Alt+Space menu)
                    // so OS key-repeats never fire after FNaF 1 gains focus.
                    _externalHeldKeys.Add((int)info.vkCode);
                    return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                }

                if (isDown && _externalHeldKeys.Contains((int)info.vkCode))
                {
                    return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                }

                if (canIntercept)
                {
                    var args = new KeyboardHookEventArgs((int)info.vkCode, isDown, isSys);
                    KeyEvent?.Invoke(this, args);

                    if (args.Handled)
                    {
                        return new IntPtr(1);
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private long _lastAltActivityTick;
    private readonly HashSet<int> _externalHeldKeys = [];

    private static bool IsKeyPressed(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_threadId != 0)
        {
            NativeMethods.PostThreadMessageW(_threadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        }

        _thread?.Join(TimeSpan.FromSeconds(2));
        _startedEvent.Dispose();
    }
}
