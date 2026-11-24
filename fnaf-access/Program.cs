using fnaf_access.Config;
using fnaf_access.Game;
using fnaf_access.Input;
using fnaf_access.Narration;
using fnaf_access.Speech;
#if DEBUG
using fnaf_access.Tools;
#endif
using fnaf_access.Win32;

namespace fnaf_access;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            Console.Title = "FNaF Access - Five Nights at Freddy's 1 Accessibility Companion";
        }
        catch
        {
            // Ignore when running without an attached console window.
        }

        string configPath = Path.Combine(AppContext.BaseDirectory, "keybinds.json");

        if (args.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a == "-h"))
        {
            PrintUsage();
            return 0;
        }

        if (args.Any(a => a.Equals("--generate-config", StringComparison.OrdinalIgnoreCase)))
        {
            KeybindConfig cfg = KeybindConfig.LoadOrCreate(configPath);
            Console.WriteLine($"Keybinds config ready at: {configPath}");
            foreach ((GameAction action, string key) in cfg.Bindings)
            {
                Console.WriteLine($"  {action,-22} = {key}");
            }

            return 0;
        }

        if (args.Any(a => a.Equals("--test-speech", StringComparison.OrdinalIgnoreCase)))
        {
            return RunSpeechTest();
        }

#if DEBUG
        if (args.Any(a => a.Equals("--inspect", StringComparison.OrdinalIgnoreCase)))
        {
            string reportPath = Path.Combine(Directory.GetCurrentDirectory(), "inspect-report.txt");
            bool wait = args.Any(a => a.Equals("--wait", StringComparison.OrdinalIgnoreCase));
            bool watch = args.Any(a => a.Equals("--watch", StringComparison.OrdinalIgnoreCase));
            return FnafInspector.Run(wait, reportPath, watch);
        }
#endif

        return RunCompanion(configPath);
    }

    private static int RunSpeechTest()
    {
        using var speech = new SpeechService();
        Console.WriteLine($"Prism backend:       {speech.ActiveBackendName}");
        Console.WriteLine($"Screen reader mode:  {speech.IsScreenReader}");
        Console.WriteLine($"Speech+Braille API:  {speech.SupportsCombinedOutput}");
        Console.WriteLine($"Raw features:        {speech.ActiveFeatures}");

        const string message = "FNaF Access connected to Prism. Speech and braille output test.";
        Console.WriteLine($"Outputting: \"{message}\"");
        speech.Output(message, interrupt: true);

        if (!speech.IsScreenReader)
        {
            Thread.Sleep(2500);
        }

        return 0;
    }

    private static int RunCompanion(string configPath)
    {
        KeybindConfig config = KeybindConfig.LoadOrCreate(configPath);
        using var speech = new SpeechService();
        var narrator = new GameNarrator(speech, config);

        Console.WriteLine("fnaf-access companion started.");
        Console.WriteLine($"Prism backend: {speech.ActiveBackendName} (ScreenReader={speech.IsScreenReader}, Speech+Braille={speech.SupportsCombinedOutput})");
        Console.WriteLine($"Keybinds:      {configPath} ({config.Bindings.Count} actions)");
        Console.WriteLine("Press F1 in-game for keybind help, F8 to toggle passthrough, Ctrl+C here to exit.");

        narrator.Say("FNaF Access ready. Waiting for Five Nights at Freddy's.");

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        uint attachedPid = 0;
        IntPtr locatedHwnd = IntPtr.Zero;
        FnafGameState? latestState = null;
        FnafController? controllerRef = null;
        int syncFgState = 0; // 0 = not foreground, 1 = foreground (updated synchronously on hook + poll threads)
        int syncClientW = 0;
        int syncClientH = 0;

        bool IsFnafForeground()
        {
            bool active = CheckFnafForegroundRaw(out IntPtr fgHwnd);
            int prev = Interlocked.Exchange(ref syncFgState, active ? 1 : 0);

            if (!active)
            {
                if (prev == 1)
                {
                    controllerRef?.ClearHeldInputs();
                }

                return false;
            }

            // Synchronously arm MenuConfirm grace period the EXACT millisecond focus returns
            // (eliminating any race between WH_KEYBOARD_LL and the 33ms polling loop).
            if (prev == 0)
            {
                controllerRef?.SuppressMenuConfirmBriefly(600);
            }

            if (fgHwnd != IntPtr.Zero && NativeMethods.GetClientRect(fgHwnd, out NativeMethods.RECT crc))
            {
                if (crc.Width <= 0 || crc.Height <= 0)
                {
                    Interlocked.Exchange(ref syncFgState, 0);
                    return false;
                }

                int prevW = Interlocked.Exchange(ref syncClientW, crc.Width);
                int prevH = Interlocked.Exchange(ref syncClientH, crc.Height);
                if ((prevW != 0 || prevH != 0) && (prevW != crc.Width || prevH != crc.Height))
                {
                    controllerRef?.SuppressMenuConfirmBriefly(600);
                }
            }

            return true;
        }

        bool CheckFnafForegroundRaw(out IntPtr fgHwnd)
        {
            fgHwnd = IntPtr.Zero;
            uint pid = Volatile.Read(ref attachedPid);
            if (pid == 0)
            {
                return false;
            }

            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero || NativeMethods.IsIconic(fg))
            {
                return false;
            }

            uint fgThread = NativeMethods.GetWindowThreadProcessId(fg, out uint fgPid);
            if (fgPid != pid)
            {
                return false;
            }

            // Do not intercept keys while the user is inside the Windows system menu (Alt+Space -> Maximize/Close).
            var gui = new NativeMethods.GUITHREADINFO
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>()
            };
            if (NativeMethods.GetGUIThreadInfo(fgThread, ref gui) &&
                (gui.flags & (NativeMethods.GUI_INMENUMODE | NativeMethods.GUI_SYSTEMMENUMODE | NativeMethods.GUI_POPUPMENUMODE)) != 0)
            {
                return false;
            }

            fgHwnd = fg;
            return true;
        }

        IntPtr GetTargetWindow()
        {
            FnafGameState? s = Volatile.Read(ref latestState);
            if (s is not null && s.MainWindowHandle != IntPtr.Zero && NativeMethods.IsWindow(s.MainWindowHandle))
            {
                return s.MainWindowHandle;
            }

            if (CheckFnafForegroundRaw(out IntPtr fg) && fg != IntPtr.Zero)
            {
                return fg;
            }

            return locatedHwnd;
        }

        using var controller = new FnafController(config, narrator, () => Volatile.Read(ref latestState), GetTargetWindow);
        controllerRef = controller;
        using var hook = new KeyboardHook(
            IsFnafForeground,
            () => Volatile.Read(ref attachedPid),
            () => controller.SuppressMenuConfirmBriefly(600));
        hook.KeyEvent += controller.HandleKeyEvent;
        hook.Start();

        ClickteamRuntime? runtime = null;
        int lastLoggedFrame = -1;

        try
        {
            while (!cancel.IsCancellationRequested)
            {
                if (runtime is null || !runtime.IsAttached)
                {
                    FnafWindowInfo? found = FnafProcessLocator.FindRunningGame();
                    if (found is not null)
                    {
                        runtime = ClickteamRuntime.TryAttach(found.Value.ProcessId);
                        if (runtime is not null)
                        {
                            Volatile.Write(ref attachedPid, found.Value.ProcessId);
                            locatedHwnd = found.Value.WindowHandle;
                            lastLoggedFrame = -1;
                            Console.WriteLine($"Attached to Five Nights at Freddy's (PID {found.Value.ProcessId}).");
                        }
                    }

                    Thread.Sleep(250);
                    continue;
                }

                FrameReadResult read = runtime.ReadFrame();
                if (read.ProcessExited)
                {
                    FnafGameState? prevBeforeExit = Volatile.Read(ref latestState);
                    bool crashedFromJumpscare = narrator.HasRecentJumpscare ||
                                                prevBeforeExit?.Screen == FnafScreen.Jumpscare ||
                                                prevBeforeExit?.Jumpscare == FnafJumpscareCharacter.GoldenFreddy;
                    Console.WriteLine("FNaF 1 process closed. Waiting for game...");
                    narrator.Say("Five Nights at Freddy's closed.", interrupt: !crashedFromJumpscare);
                    runtime.Dispose();
                    runtime = null;
                    Volatile.Write(ref attachedPid, 0);
                    Volatile.Write(ref latestState, null);
                    locatedHwnd = IntPtr.Zero;
                    lastLoggedFrame = -1;
                    controller.ClearHeldInputs();
                    Thread.Sleep(500);
                    continue;
                }

                if (read.IsTransitioning || read.Snapshot is null)
                {
                    // Clickteam is mid-frame transition (e.g., Night Loading -> Office); wait cleanly.
                    Thread.Sleep(33);
                    continue;
                }

                // Refresh foreground / resize state on every poll tick too (shared atomically with hook thread).
                IsFnafForeground();

                FnafGameState state = FnafGameState.FromFrameSnapshot(read.Snapshot);
                if (state.FrameIndex != lastLoggedFrame)
                {
                    lastLoggedFrame = state.FrameIndex;
                    Console.WriteLine($"[Frame {state.FrameIndex}: \"{state.FrameName}\" -> {state.Screen}]");
                }

                Volatile.Write(ref latestState, state);
                narrator.Update(state);

                Thread.Sleep(33); // ~30Hz live state polling
            }
        }
        finally
        {
            runtime?.Dispose();
        }

        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("fnaf-access — Accessibility companion for Five Nights at Freddy's 1");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  fnaf-access                       Start the accessibility companion");
        Console.WriteLine("  fnaf-access --test-speech         Test Prism speech + braille output");
#if DEBUG
        Console.WriteLine("  fnaf-access --inspect [--watch]   Inspect live FNaF 1 Clickteam state");
#endif
        Console.WriteLine("  fnaf-access --generate-config     Create or verify keybinds.json");
    }
}
