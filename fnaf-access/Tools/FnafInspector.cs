using System.Text;
using fnaf_access.Game;

namespace fnaf_access.Tools;

/// <summary>
/// Live FNaF 1 frame &amp; object dumper powered by ClickteamRuntime.
/// </summary>
public static class FnafInspector
{
    public static int Run(bool waitForGame, string reportPath, bool watch = false)
    {
        FnafWindowInfo? info = FnafProcessLocator.FindRunningGame();
        while (info is null && waitForGame)
        {
            Thread.Sleep(500);
            info = FnafProcessLocator.FindRunningGame();
        }

        if (info is null)
        {
            Console.WriteLine("FNaF 1 does not appear to be running.");
            return 1;
        }

        using ClickteamRuntime? runtime = ClickteamRuntime.TryAttach(info.Value.ProcessId);
        if (runtime is null)
        {
            Console.WriteLine($"Failed to attach ClickteamRuntime to PID {info.Value.ProcessId}.");
            return 1;
        }

        var sb = new StringBuilder();
        void DumpCurrent(ClickteamFrameSnapshot snap)
        {
            sb.Clear();
            void Log(string line)
            {
                Console.WriteLine(line);
                sb.AppendLine(line);
            }

            FnafGameState state = FnafGameState.FromFrameSnapshot(snap);
            Log($"=== Frame {snap.FrameIndex}: \"{snap.FrameName}\" -> Screen={state.Screen} ({snap.LevelWidth}x{snap.LevelHeight}) Win=({snap.WindowX},{snap.WindowY}) Timer={snap.FrameTimerMs}ms ===");
            if (state.Screen == FnafScreen.Office)
            {
                Log($"[STATE] Night={state.Night} Hour={state.Hour} Power={state.PowerPercent}% Usage={state.UsageLevel} Side={state.ViewSide} PowerOut={state.PowerOut}");
                Log($"[DOORS] Left=(closed={state.LeftDoorClosed}, light={state.LeftLightOn}, reachDoor={state.CanReachLeftDoor}, reachLight={state.CanReachLeftLight}, bonnie={state.BonnieVisibleAtLeftDoor})");
                Log($"[DOORS] Right=(closed={state.RightDoorClosed}, light={state.RightLightOn}, reachDoor={state.CanReachRightDoor}, reachLight={state.CanReachRightLight}, chica={state.ChicaVisibleAtRightWindow})");
                Log($"[CAM]   MonitorUp={state.MonitorUp} Current={state.CurrentCamera} Static={state.CameraStatic} Desc=\"{state.ActiveCameraDescription}\" MuteVis={state.CanMuteCall} GF={state.GoldenFreddyInOffice}");
            }
            else if (state.Screen == FnafScreen.Title)
            {
                Log($"[TITLE] Option={state.TitleOptionSelected} SavedNight={state.SavedNight} Stars={state.StarsUnlocked} 6th={state.SixthNightUnlocked} Custom={state.CustomNightUnlocked}");
            }

            Log($"Live objects: {snap.Objects.Count}");
            Log("");
            Log("--- Counters ---");
            foreach (ClickteamObjectSnapshot o in snap.Objects.Where(o => o.Type == (short)ClickteamObjectType.Counter))
            {
                Log($"  [{o.SlotIndex,3}] \"{o.Name,-24}\" = {o.CounterIntValue?.ToString() ?? "?",-10} vis={o.IsVisible,-5} pos=({o.X},{o.Y})");
            }

            Log("");
            Log("--- Strings ---");
            foreach (ClickteamObjectSnapshot o in snap.Objects.Where(o => o.Type == (short)ClickteamObjectType.String))
            {
                Log($"  [{o.SlotIndex,3}] \"{o.Name,-24}\" text=\"{o.StringText}\" vis={o.IsVisible}");
            }

            Log("");
            Log("--- Actives & Extensions ---");
            foreach (ClickteamObjectSnapshot o in snap.Objects.Where(o =>
                o.Type != (short)ClickteamObjectType.Counter &&
                o.Type != (short)ClickteamObjectType.String))
            {
                bool onScreen = o.IsVisible && o.Bounds.IsPhysicallyOnScreen(snap.WindowX, snap.WindowY);
                Log($"  [{o.SlotIndex,3}] t={o.Type,2} \"{o.Name,-24}\" vis={o.IsVisible,-5} onScr={onScreen,-5} img={o.ImageHandle,4} anim=({o.AnimSequence},{o.AnimDirection},{o.AnimFrame}) rect=({o.Bounds.Left,4},{o.Bounds.Top,4},{o.Bounds.Right,4},{o.Bounds.Bottom,4})");
                if (o.Type == (short)ClickteamObjectType.Active)
                {
                    List<string> anims = runtime.DescribeObjectAnimations(o.LphoAddress);
                    if (anims.Count > 1 || (anims.Count == 1 && !anims[0].Contains(":1f[")))
                    {
                        Log($"        anims: {string.Join(" | ", anims)}");
                    }
                }
            }

            File.WriteAllText(reportPath, sb.ToString());
            Console.WriteLine($"Saved report to: {Path.GetFullPath(reportPath)}");
        }

        if (!watch)
        {
            ClickteamFrameSnapshot? snap = runtime.ReadFrameSnapshot();
            if (snap is null)
            {
                Console.WriteLine("Could not read frame snapshot.");
                return 1;
            }

            DumpCurrent(snap);
            return 0;
        }

        Console.WriteLine("Watching live state changes (Ctrl+C to stop)...");
        int lastFrame = -1;
        string lastLine = string.Empty;
        while (true)
        {
            ClickteamFrameSnapshot? snap = runtime.ReadFrameSnapshot();
            if (snap is null)
            {
                Thread.Sleep(250);
                continue;
            }

            if (snap.FrameIndex != lastFrame)
            {
                lastFrame = snap.FrameIndex;
                string frameReportPath = Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(reportPath)) ?? ".",
                    $"inspect-frame-{snap.FrameIndex}.txt");
                DumpCurrent(snap);
                File.WriteAllText(frameReportPath, sb.ToString());
                Console.WriteLine($"Saved frame report to: {frameReportPath}");
                lastLine = string.Empty;
            }
            else
            {
                FnafGameState st = FnafGameState.FromFrameSnapshot(snap);
                string curLine;
                if (st.Screen == FnafScreen.Office)
                {
                    ClickteamObjectSnapshot? officeObj = snap.FindByName("office") ?? snap.Objects.FirstOrDefault(o => o.Type == 2 && o.Bounds.Width >= 1500);
                    ClickteamObjectSnapshot? camObj = snap.Objects.FirstOrDefault(o => o.Type == 2 && o.IsVisible && o.Bounds.Width >= 1200 && o.Bounds.Height >= 700 && o != officeObj && !o.Name.Contains("static", StringComparison.OrdinalIgnoreCase));
                    curLine = $"WinX={st.WindowX} Mon={st.MonitorUp} Cam={st.CurrentCamera} Desc=\"{st.ActiveCameraDescription}\" Office=({officeObj?.Name}:{officeObj?.AnimSequence}/{officeObj?.AnimFrame}/img{officeObj?.ImageHandle}) CamObj=({camObj?.Name}:{camObj?.AnimSequence}/{camObj?.AnimFrame}/img{camObj?.ImageHandle})";
                }
                else if (st.Screen == FnafScreen.Title)
                {
                    ClickteamObjectSnapshot? nn = snap.FindByName("night number");
                    ClickteamObjectSnapshot? nn2 = snap.FindByName("night number 2");
                    ClickteamObjectSnapshot? nw = snap.FindByName("night word");
                    curLine = $"Title Opt={st.TitleOptionSelected} nightNum={nn?.CounterIntValue}(vis={nn?.IsVisible}) nightNum2={nn2?.CounterIntValue} nightWordVis={nw?.IsVisible}";
                }
                else
                {
                    curLine = $"Screen={st.Screen} Frame={snap.FrameIndex} \"{snap.FrameName}\"";
                }

                if (curLine != lastLine)
                {
                    Console.WriteLine($"  [LIVE] {curLine}");
                    lastLine = curLine;
                }
            }

            Thread.Sleep(50);
        }
    }
}
