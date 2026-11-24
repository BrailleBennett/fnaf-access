namespace fnaf_access.Game;

public enum FnafScreen
{
    Unknown,
    Warning,
    Title,
    Newspaper,
    NightIntro,
    Office,
    SixAm,
    GameOver,
    CustomNight,
    Ending,
    Jumpscare
}

public enum FnafJumpscareCharacter
{
    None = 0,
    Bonnie,
    Chica,
    Foxy,
    Freddy,
    PowerOutFreddy,
    GoldenFreddy
}

public enum FnafEndingType
{
    Unknown,
    Night5Paycheck,
    Night6Overtime,
    Night7PinkSlip
}

public enum OfficeViewSide
{
    Left,
    Center,
    Right
}

public enum FnafCamera
{
    None = 0,
    Cam1A,
    Cam1B,
    Cam1C,
    Cam2A,
    Cam2B,
    Cam3,
    Cam4A,
    Cam4B,
    Cam5,
    Cam6,
    Cam7
}

public readonly record struct CameraDefinition(
    FnafCamera Camera,
    string ObjectName,
    string Code,
    string DisplayName);

public sealed class FnafGameState
{
    public static readonly IReadOnlyList<CameraDefinition> AllCameras =
    [
        new(FnafCamera.Cam1A, "1A show stage", "Camera 1A", "Show Stage"),
        new(FnafCamera.Cam1B, "1B dining area", "Camera 1B", "Dining Area"),
        new(FnafCamera.Cam1C, "1C stage B", "Camera 1C", "Pirate Cove"),
        new(FnafCamera.Cam2A, "cam 2A", "Camera 2A", "West Hall"),
        new(FnafCamera.Cam2B, "cam 2B", "Camera 2B", "West Hall Corner"),
        new(FnafCamera.Cam3, "cam 3 closet", "Camera 3", "Supply Closet"),
        new(FnafCamera.Cam4A, "cam 4A", "Camera 4A", "East Hall"),
        new(FnafCamera.Cam4B, "cam 4B", "Camera 4B", "East Hall Corner"),
        new(FnafCamera.Cam5, "5 backstage", "Camera 5", "Backstage"),
        new(FnafCamera.Cam6, "cam 6 kitchen", "Camera 6", "Kitchen"),
        new(FnafCamera.Cam7, "cam 7 bathrooms", "Camera 7", "Restrooms")
    ];

    public required FnafScreen Screen { get; init; }
    public required int FrameIndex { get; init; }
    public required string FrameName { get; init; }
    public required IntPtr MainWindowHandle { get; init; }
    public required int WindowX { get; init; }
    public required int WindowY { get; init; }
    public FnafEndingType EndingType { get; init; }
    public FnafJumpscareCharacter Jumpscare { get; init; }

    // Title screen state
    public int TitleOptionSelected { get; init; }
    public int SavedNight { get; init; }
    public bool SixthNightUnlocked { get; init; }
    public bool CustomNightUnlocked { get; init; }
    public int StarsUnlocked { get; init; }
    public IReadOnlyDictionary<int, ClickteamRect> TitleButtonRects { get; init; } = new Dictionary<int, ClickteamRect>();

    // Custom Night state (Frame 12: "customize")
    public int FreddyAi { get; init; }
    public int BonnieAi { get; init; }
    public int ChicaAi { get; init; }
    public int FoxyAi { get; init; }
    public ClickteamRect? FreddyLeftRect { get; init; }
    public ClickteamRect? FreddyRightRect { get; init; }
    public ClickteamRect? BonnieLeftRect { get; init; }
    public ClickteamRect? BonnieRightRect { get; init; }
    public ClickteamRect? ChicaLeftRect { get; init; }
    public ClickteamRect? ChicaRightRect { get; init; }
    public ClickteamRect? FoxyLeftRect { get; init; }
    public ClickteamRect? FoxyRightRect { get; init; }
    public ClickteamRect? CustomNightReadyRect { get; init; }

    // Office HUD & gameplay state
    public int Night { get; init; }
    public int Hour { get; init; }
    public int PowerPercent { get; init; }
    public int UsageLevel { get; init; }
    public bool PowerOut { get; init; }
    public OfficeViewSide ViewSide { get; init; }

    // Door & light status
    public bool LeftDoorClosed { get; init; }
    public bool LeftLightOn { get; init; }
    public bool RightDoorClosed { get; init; }
    public bool RightLightOn { get; init; }

    // Sighted-parity physical reachability & live click hitboxes
    public bool CanReachLeftDoor { get; init; }
    public bool CanReachLeftLight { get; init; }
    public bool CanReachRightDoor { get; init; }
    public bool CanReachRightLight { get; init; }
    public bool CanMuteCall { get; init; }
    public bool CanHonkNose { get; init; }

    public ClickteamRect? LeftDoorButtonRect { get; init; }
    public ClickteamRect? LeftLightButtonRect { get; init; }
    public ClickteamRect? RightDoorButtonRect { get; init; }
    public ClickteamRect? RightLightButtonRect { get; init; }
    public ClickteamRect? MuteCallRect { get; init; }
    public ClickteamRect? FreddyNoseRect { get; init; }
    public ClickteamRect? FlipPanelRect { get; init; }
    public ClickteamRect? FlipUpRect { get; init; }
    public ClickteamRect? FlipDownRect { get; init; }

    // Sighted-parity observable office threats
    public bool BonnieVisibleAtLeftDoor { get; init; }
    public bool ChicaVisibleAtRightWindow { get; init; }
    public bool GoldenFreddyInOffice { get; init; }

    // Camera monitor state (only current camera is exposed for sighted parity)
    public bool MonitorUp { get; init; }
    public FnafCamera CurrentCamera { get; init; }
    public bool CameraStatic { get; init; }
    public bool CameraAudioOnly { get; init; }
    public string ActiveCameraDescription { get; init; } = string.Empty;
    public IReadOnlyDictionary<FnafCamera, ClickteamRect> CameraButtonRects { get; init; } = new Dictionary<FnafCamera, ClickteamRect>();

    public static CameraDefinition? GetCameraDefinition(FnafCamera cam) =>
        AllCameras.FirstOrDefault(c => c.Camera == cam);

    public static FnafGameState FromFrameSnapshot(ClickteamFrameSnapshot snap)
    {
        FnafScreen screen = ClassifyScreen(snap);
        IntPtr hwnd = snap.MainWindowHandle != 0 ? new IntPtr(snap.MainWindowHandle) : IntPtr.Zero;

        return screen switch
        {
            FnafScreen.Title => BuildTitleState(snap, hwnd),
            FnafScreen.Office => BuildOfficeState(snap, hwnd),
            FnafScreen.CustomNight => BuildCustomNightState(snap, hwnd),
            FnafScreen.Ending => BuildEndingState(snap, hwnd),
            FnafScreen.Jumpscare => BuildJumpscareState(snap, hwnd),
            _ => new FnafGameState
            {
                Screen = screen,
                FrameIndex = snap.FrameIndex,
                FrameName = snap.FrameName,
                MainWindowHandle = hwnd,
                WindowX = snap.WindowX,
                WindowY = snap.WindowY,
                Night = snap.GetCounter("night number") ?? snap.GetCounter("loaded level") ?? 0
            }
        };
    }

    private static FnafGameState BuildJumpscareState(ClickteamFrameSnapshot snap, IntPtr hwnd)
    {
        string name = snap.FrameName.Trim();
        FnafJumpscareCharacter ch = FnafJumpscareCharacter.None;

        if (name.Contains("creepy", StringComparison.OrdinalIgnoreCase))
        {
            ch = FnafJumpscareCharacter.GoldenFreddy;
        }
        else if (name.Contains("freddy", StringComparison.OrdinalIgnoreCase))
        {
            ch = FnafJumpscareCharacter.PowerOutFreddy;
        }

        return new FnafGameState
        {
            Screen = FnafScreen.Jumpscare,
            FrameIndex = snap.FrameIndex,
            FrameName = snap.FrameName,
            MainWindowHandle = hwnd,
            WindowX = snap.WindowX,
            WindowY = snap.WindowY,
            Jumpscare = ch
        };
    }

    private static FnafGameState BuildEndingState(ClickteamFrameSnapshot snap, IntPtr hwnd)
    {
        string name = snap.FrameName.Trim();
        int night = snap.GetCounter("night number") ?? snap.GetCounter("loaded level") ?? 0;

        // Determine ending from explicit frame name only; do NOT trust saved "night number" here
        // because FNaF 1's save Ini keeps level=5 during Night 6/7 endings. GameNarrator resolves
        // Unknown from _lastOfficeNight (the actual night just completed).
        FnafEndingType endingType = FnafEndingType.Unknown;
        if (name.Contains("3", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("fired", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("pink", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("slip", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("terminat", StringComparison.OrdinalIgnoreCase))
        {
            endingType = FnafEndingType.Night7PinkSlip;
        }
        else if (name.Contains("2", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("overtime", StringComparison.OrdinalIgnoreCase))
        {
            endingType = FnafEndingType.Night6Overtime;
        }
        else if (name.Contains("1", StringComparison.OrdinalIgnoreCase))
        {
            endingType = FnafEndingType.Night5Paycheck;
        }

        return new FnafGameState
        {
            Screen = FnafScreen.Ending,
            FrameIndex = snap.FrameIndex,
            FrameName = snap.FrameName,
            MainWindowHandle = hwnd,
            WindowX = snap.WindowX,
            WindowY = snap.WindowY,
            Night = night,
            EndingType = endingType
        };
    }

    private static FnafScreen ClassifyScreen(ClickteamFrameSnapshot snap)
    {
        string name = snap.FrameName.Trim();

        // Office frame is uniquely 1600x720 with "power left 2" / "usage meter"
        if (snap.LevelWidth >= 1500 || snap.FindByName("power left 2") is not null)
        {
            return FnafScreen.Office;
        }

        if (name.Equals("title", StringComparison.OrdinalIgnoreCase) || snap.FindByName("new game") is not null)
        {
            return FnafScreen.Title;
        }

        if (name.Contains("warning", StringComparison.OrdinalIgnoreCase))
        {
            return FnafScreen.Warning;
        }

        if (name.Equals("ad", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("news", StringComparison.OrdinalIgnoreCase))
        {
            return FnafScreen.Newspaper;
        }

        if (name.Contains("wait", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("what day", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("load", StringComparison.OrdinalIgnoreCase))
        {
            return FnafScreen.NightIntro;
        }

        if (name.Contains("next day", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("6am", StringComparison.OrdinalIgnoreCase))
        {
            return FnafScreen.SixAm;
        }

        if (name.Contains("gameover", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("game over", StringComparison.OrdinalIgnoreCase))
        {
            return FnafScreen.GameOver;
        }

        if (name.Contains("custom", StringComparison.OrdinalIgnoreCase) ||
            snap.FindByName("freddy left") is not null)
        {
            return FnafScreen.CustomNight;
        }

        // Check dedicated jumpscare frames BEFORE "end" so "creepy end" (Golden Freddy)
        // is never misclassified as an Ending paycheck screen.
        if (name.Contains("creepy end", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("creepyend", StringComparison.OrdinalIgnoreCase))
        {
            return FnafScreen.Jumpscare;
        }

        if (name.Equals("freddy", StringComparison.OrdinalIgnoreCase) ||
            (name.Contains("freddy", StringComparison.OrdinalIgnoreCase) &&
             !name.Contains("title", StringComparison.OrdinalIgnoreCase)))
        {
            return FnafScreen.Jumpscare;
        }

        if (name.Contains("creepy", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("demo", StringComparison.OrdinalIgnoreCase))
        {
            return FnafScreen.Unknown;
        }

        if (name.Contains("end", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("check", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("overtime", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("fired", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("pink", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("slip", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("terminat", StringComparison.OrdinalIgnoreCase))
        {
            return FnafScreen.Ending;
        }

        return snap.FrameIndex switch
        {
            0 => FnafScreen.Warning,
            1 => FnafScreen.Title,
            2 => FnafScreen.Newspaper,
            3 => FnafScreen.Office,
            8 => FnafScreen.GameOver,
            _ => FnafScreen.Unknown
        };
    }

    private static FnafGameState BuildTitleState(ClickteamFrameSnapshot snap, IntPtr hwnd)
    {
        int opt = snap.GetCounter("option selected") ?? 0;

        // FNaF 1's Title screen resets/updates the global "night number" counter when hovering
        // between New Game (1), Continue (Ini level), 6th Night (6), and Custom Night (7).
        // Polling right as "option selected" flips to 1 can catch "night number" still at 1
        // before Clickteam reloads it from %AppData%\MMFApplications\freddy.
        // Reading the authoritative saved level from Clickteam's freddy INI eliminates that race.
        int? iniLevel = ReadSavedNightFromIni();
        int? counterNight = snap.GetCounter("night number");
        int night = iniLevel is >= 1 and <= 5
            ? iniLevel.Value
            : (counterNight is >= 1 and <= 5 ? counterNight.Value : 1);

        ClickteamObjectSnapshot? newGame = snap.FindByName("new game");
        ClickteamObjectSnapshot? cont = snap.FindByName("continue word");
        ClickteamObjectSnapshot? sixth = snap.FindByName("6th night");
        ClickteamObjectSnapshot? custom = snap.FindByName("custom night");

        int stars = 0;
        if (snap.FindByName("star 1")?.IsVisible == true) stars++;
        if (snap.FindByName("star 2")?.IsVisible == true) stars++;
        if (snap.FindByName("star 3")?.IsVisible == true) stars++;

        var rects = new Dictionary<int, ClickteamRect>();
        if (newGame is not null) rects[0] = newGame.Bounds;
        if (cont is not null) rects[1] = cont.Bounds;
        if (sixth is not null && sixth.IsVisible) rects[2] = sixth.Bounds;
        if (custom is not null && custom.IsVisible) rects[3] = custom.Bounds;

        return new FnafGameState
        {
            Screen = FnafScreen.Title,
            FrameIndex = snap.FrameIndex,
            FrameName = snap.FrameName,
            MainWindowHandle = hwnd,
            WindowX = snap.WindowX,
            WindowY = snap.WindowY,
            TitleOptionSelected = opt,
            SavedNight = night,
            SixthNightUnlocked = sixth?.IsVisible == true,
            CustomNightUnlocked = custom?.IsVisible == true,
            StarsUnlocked = stars,
            TitleButtonRects = rects
        };
    }

    private static FnafGameState BuildCustomNightState(ClickteamFrameSnapshot snap, IntPtr hwnd)
    {
        return new FnafGameState
        {
            Screen = FnafScreen.CustomNight,
            FrameIndex = snap.FrameIndex,
            FrameName = snap.FrameName,
            MainWindowHandle = hwnd,
            WindowX = snap.WindowX,
            WindowY = snap.WindowY,
            Night = snap.GetCounter("night number") ?? 7,
            FreddyAi = snap.GetCounter("freddy AI") ?? 0,
            BonnieAi = snap.GetCounter("bonie AI") ?? 0,
            ChicaAi = snap.GetCounter("chica AI") ?? 0,
            FoxyAi = snap.GetCounter("foxy AI") ?? 0,
            FreddyLeftRect = snap.FindByName("freddy left")?.Bounds,
            FreddyRightRect = snap.FindByName("freddy right")?.Bounds,
            BonnieLeftRect = snap.FindByName("bonnie left")?.Bounds,
            BonnieRightRect = snap.FindByName("bonnie right")?.Bounds,
            ChicaLeftRect = snap.FindByName("chica left")?.Bounds,
            ChicaRightRect = snap.FindByName("chica right")?.Bounds,
            FoxyLeftRect = snap.FindByName("foxy left")?.Bounds,
            FoxyRightRect = snap.FindByName("foxy right")?.Bounds,
            CustomNightReadyRect = snap.FindByName("Active 2")?.Bounds
        };
    }

    private static FnafGameState BuildOfficeState(ClickteamFrameSnapshot snap, IntPtr hwnd)
    {
        int night = snap.GetCounter("night number") ?? 1;
        int hour = snap.GetCounter("time of day") ?? 12;
        int power = snap.GetCounter("power left 2") ?? 100;
        int usage = snap.GetCounter("usage meter") ?? 1;
        bool powerOut = (snap.GetCounter("power down") ?? 0) != 0;

        int winX = snap.WindowX;
        int winY = snap.WindowY;

        // Monitor state: "viewing" counter == 1 OR camera map ("Active 9") / camera buttons visible
        ClickteamObjectSnapshot? camMap = snap.FindByName("Active 9");
        ClickteamObjectSnapshot? cam1AObj = snap.FindByName("1A show stage");
        bool monitorUp = !powerOut &&
            ((snap.GetCounter("viewing") ?? 0) != 0 ||
             camMap?.IsVisible == true ||
             cam1AObj?.IsVisible == true);

        // Door & light states from verified Clickteam animations & sprite image IDs:
        // left door:  anim 0=open (img 104), 12=closing, 13=closed (img 103), 14=opening
        // right door: anim 0=open (img 120), 12=closing, 13=closed (img 119), 14=opening
        // Active 7 (left panel):  anim 0=both off (img 123), 12/13=door-only vs light-only (125/126), 14=both on (img 131)
        // panel 2 (right panel):  anim 0=both off (img 135), 12/13=door-only vs light-only (136/132), 14=both on (img 48)
        ClickteamObjectSnapshot? active3 = snap.FindByName("Active 3") ??
            snap.Objects.FirstOrDefault(o => o.Type == (short)ClickteamObjectType.Active && o.Bounds.Width >= 1500);
        ClickteamObjectSnapshot? leftDoor = snap.FindByName("left door");
        ClickteamObjectSnapshot? rightDoor = snap.FindByName("right door");
        ClickteamObjectSnapshot? leftPanel = snap.FindByName("Active 7");
        ClickteamObjectSnapshot? rightPanel = snap.FindByName("panel 2");

        bool leftDoorClosed = !powerOut && leftDoor is not null &&
            (leftDoor.AnimSequence is short lAnim ? lAnim is 12 or 13 : leftDoor.ImageHandle == 103);
        bool rightDoorClosed = !powerOut && rightDoor is not null &&
            (rightDoor.AnimSequence is short rAnim ? rAnim is 12 or 13 : rightDoor.ImageHandle == 119);

        // Learn which of panel seq 12 vs 13 is light-only vs door-only so light detection never
        // glitches when opening a door (seq 14) while the panel briefly stays on door-only.
        CalibratePanelLightSeq(leftDoor, leftPanel, active3, powerOut, monitorUp, ref _leftLightOnlyPanelSeq);
        CalibratePanelLightSeq(rightDoor, rightPanel, active3, powerOut, monitorUp, ref _rightLightOnlyPanelSeq);

        bool leftDoorShowsOnPanel = !powerOut && leftDoor is not null &&
            (leftDoor.AnimSequence is short lAnimP ? lAnimP is 12 or 13 or 14 : leftDoor.ImageHandle == 103);
        bool rightDoorShowsOnPanel = !powerOut && rightDoor is not null &&
            (rightDoor.AnimSequence is short rAnimP ? rAnimP is 12 or 13 or 14 : rightDoor.ImageHandle == 119);

        bool leftPanelBothOff = leftPanel is null || leftPanel.AnimSequence == 0 || leftPanel.ImageHandle == 123;
        bool leftPanelBothOn = leftPanel is not null && (leftPanel.AnimSequence == 14 || leftPanel.ImageHandle == 131);
        bool rightPanelBothOff = rightPanel is null || rightPanel.AnimSequence == 0 || rightPanel.ImageHandle == 135;
        bool rightPanelBothOn = rightPanel is not null && (rightPanel.AnimSequence == 14 || rightPanel.ImageHandle == 48);

        bool leftLightOn = !powerOut && !monitorUp && leftPanel is not null &&
            (_leftLightOnlyPanelSeq is short lLightSeq && leftPanel.AnimSequence is short lpCur
                ? (lpCur == 14 || lpCur == lLightSeq)
                : (leftDoorShowsOnPanel ? leftPanelBothOn : !leftPanelBothOff));
        bool rightLightOn = !powerOut && !monitorUp && rightPanel is not null &&
            (_rightLightOnlyPanelSeq is short rLightSeq && rightPanel.AnimSequence is short rpCur
                ? (rpCur == 14 || rpCur == rLightSeq)
                : (rightDoorShowsOnPanel ? rightPanelBothOn : !rightPanelBothOff));

        // Live hitboxes & STRICT physical on-screen visibility checks
        ClickteamObjectSnapshot? leftDoorBtn = snap.FindByName("door open left");
        ClickteamObjectSnapshot? leftLightBtn = snap.FindByName("light on left");
        ClickteamObjectSnapshot? rightDoorBtn = snap.FindByName("door open right");
        ClickteamObjectSnapshot? rightLightBtn = snap.FindByName("light on right");
        ClickteamObjectSnapshot? muteCall = snap.FindByName("mute call");
        ClickteamObjectSnapshot? freddyNose = snap.FindByName("Active 26");
        ClickteamObjectSnapshot? flipPanel = snap.FindByName("flip panel");
        ClickteamObjectSnapshot? flipUp = snap.FindByName("flip up");
        ClickteamObjectSnapshot? flipDown = snap.FindByName("flip down");

        bool leftDoorOnScreen = leftDoorBtn is not null &&
            leftDoorBtn.Bounds.IsPhysicallyOnScreen(winX, winY);
        bool leftLightOnScreen = leftLightBtn is not null &&
            leftLightBtn.Bounds.IsPhysicallyOnScreen(winX, winY);
        bool rightDoorOnScreen = rightDoorBtn is not null &&
            rightDoorBtn.Bounds.IsPhysicallyOnScreen(winX, winY);
        bool rightLightOnScreen = rightLightBtn is not null &&
            rightLightBtn.Bounds.IsPhysicallyOnScreen(winX, winY);

        // Derive Left/Center/Right directly from physical panel reachability so the narrator
        // NEVER announces "left" or "right" before both the door and light are actually clickable.
        OfficeViewSide viewSide = (leftDoorOnScreen && leftLightOnScreen)
            ? OfficeViewSide.Left
            : (rightDoorOnScreen && rightLightOnScreen)
                ? OfficeViewSide.Right
                : OfficeViewSide.Center;

        bool canLeftDoor = !monitorUp && !powerOut && leftDoorOnScreen;
        bool canLeftLight = !monitorUp && !powerOut && leftLightOnScreen;
        bool canRightDoor = !monitorUp && !powerOut && rightDoorOnScreen;
        bool canRightLight = !monitorUp && !powerOut && rightLightOnScreen;
        bool canMute = muteCall?.IsVisible == true;
        bool canHonk = !monitorUp && !powerOut && freddyNose is not null &&
            freddyNose.Bounds.IsPhysicallyOnScreen(winX, winY, minVisiblePx: 4);

        // Sighted-parity doorway checks: Bonnie/Chica are ONLY exposed when that door's light is ON
        // AND that side of the office is physically in view!
        ClickteamObjectSnapshot? bonnie = snap.FindByName("bonnie");
        ClickteamObjectSnapshot? chica = snap.FindByName("chica");
        ClickteamObjectSnapshot? freddy = snap.FindByName("freddy bear");
        ClickteamObjectSnapshot? readyLeft = snap.FindByName("ready to attack left");
        ClickteamObjectSnapshot? readyRight = snap.FindByName("ready to attack right");
        ClickteamObjectSnapshot? yellowBear = snap.FindByName("yellow bear");

        bool bonnieAtLeftDoor = leftLightOn && viewSide != OfficeViewSide.Right &&
            bonnie is not null && readyLeft is not null && bonnie.Bounds.Overlaps(readyLeft.Bounds);
        bool chicaAtRightWindow = rightLightOn && viewSide != OfficeViewSide.Left &&
            chica is not null && readyRight is not null && chica.Bounds.Overlaps(readyRight.Bounds);
        bool goldenFreddy = !monitorUp && yellowBear?.IsVisible == true;

        // Camera map buttons & currently viewed camera (only evaluated for the active camera!)
        var camRects = new Dictionary<FnafCamera, ClickteamRect>();
        ClickteamObjectSnapshot? youMarker = snap.FindByName("you");
        FnafCamera activeCam = FnafCamera.Cam1A;

        foreach (CameraDefinition def in AllCameras)
        {
            ClickteamObjectSnapshot? camBtn = snap.FindByName(def.ObjectName);
            if (camBtn is not null)
            {
                camRects[def.Camera] = camBtn.Bounds;
                if (youMarker is not null && youMarker.Bounds.Overlaps(camBtn.Bounds))
                {
                    activeCam = def.Camera;
                }
            }
        }

        // Note: "blip" is the brief (~150ms) white-flash animation on every camera switch;
        // do NOT treat it as video-loss static, otherwise switching cameras announces
        // "Video static" and then immediately interrupts the camera label with "Clear".
        bool camStatic = false;
        bool audioOnly = monitorUp && (activeCam == FnafCamera.Cam6 || snap.FindByName("audio only")?.IsVisible == true);

        FnafJumpscareCharacter jumpscare = DetectOfficeJumpscare(active3, powerOut);

        string camDescription = string.Empty;
        if (monitorUp)
        {
            camDescription = DescribeActiveCamera(snap, activeCam, camRects, bonnie, chica, freddy, active3, night, audioOnly);
        }

        return new FnafGameState
        {
            Screen = FnafScreen.Office,
            FrameIndex = snap.FrameIndex,
            FrameName = snap.FrameName,
            MainWindowHandle = hwnd,
            WindowX = winX,
            WindowY = winY,
            Jumpscare = jumpscare,
            Night = night,
            Hour = hour,
            PowerPercent = power,
            UsageLevel = usage,
            PowerOut = powerOut,
            ViewSide = viewSide,
            LeftDoorClosed = leftDoorClosed,
            LeftLightOn = leftLightOn,
            RightDoorClosed = rightDoorClosed,
            RightLightOn = rightLightOn,
            CanReachLeftDoor = canLeftDoor,
            CanReachLeftLight = canLeftLight,
            CanReachRightDoor = canRightDoor,
            CanReachRightLight = canRightLight,
            CanMuteCall = canMute,
            CanHonkNose = canHonk,
            LeftDoorButtonRect = leftDoorBtn?.Bounds,
            LeftLightButtonRect = leftLightBtn?.Bounds,
            RightDoorButtonRect = rightDoorBtn?.Bounds,
            RightLightButtonRect = rightLightBtn?.Bounds,
            MuteCallRect = muteCall?.Bounds,
            FreddyNoseRect = freddyNose?.Bounds,
            FlipPanelRect = flipPanel?.Bounds,
            FlipUpRect = flipUp?.Bounds,
            FlipDownRect = flipDown?.Bounds,
            BonnieVisibleAtLeftDoor = bonnieAtLeftDoor,
            ChicaVisibleAtRightWindow = chicaAtRightWindow,
            GoldenFreddyInOffice = goldenFreddy,
            MonitorUp = monitorUp,
            CurrentCamera = activeCam,
            CameraStatic = camStatic,
            CameraAudioOnly = audioOnly,
            ActiveCameraDescription = camDescription,
            CameraButtonRects = camRects
        };
    }

    private static short? _leftLightOnlyPanelSeq;
    private static short? _rightLightOnlyPanelSeq;

    private static void CalibratePanelLightSeq(
        ClickteamObjectSnapshot? door,
        ClickteamObjectSnapshot? panel,
        ClickteamObjectSnapshot? active3,
        bool powerOut,
        bool monitorUp,
        ref short? lightOnlySeq)
    {
        if (lightOnlySeq is not null || powerOut || monitorUp || door is null || panel is null)
        {
            return;
        }

        if (panel.AnimSequence is not (short pSeq and (12 or 13)))
        {
            return;
        }

        // Door firmly closed (seq 13, or >=2 frames into seq 12 closing) while panel is on one of 12/13 (not 14):
        // this sequence MUST be door-only, so the other of {12, 13} is light-only.
        if (door.AnimSequence == 13 || (door.AnimSequence == 12 && (door.AnimFrame ?? 0) >= 2))
        {
            lightOnlySeq = pSeq == 12 ? (short)13 : (short)12;
            return;
        }

        // Door fully open (seq 0) while office background is illuminated (not dark office img 40):
        // this sequence MUST be light-only.
        if (door.AnimSequence == 0 && active3 is not null && active3.ImageHandle != 40)
        {
            lightOnlySeq = pSeq;
        }
    }

    // Verified FNaF 1 v1.132 Active 3 jumpscare image handles (disjoint from all office/camera frames):
    // seq35 (11f) = Bonnie, seq44 (16f) = Chica, seq52 (25f) = Foxy, seq65 (31f) = Freddy.
    private static readonly HashSet<int> BonnieJumpscareImages =
        [301, 291, 303, 293, 294, 295, 296, 297, 298, 299, 300];

    private static readonly HashSet<int> ChicaJumpscareImages =
        [279, 66, 281, 70, 216, 228, 229, 230, 231, 232, 233, 234, 235, 236, 237, 239];

    private static readonly HashSet<int> FoxyJumpscareImages =
        [380, 242, 382, 243, 363, 364, 365, 366, 367, 368, 369, 370, 371, 372, 373, 374, 375, 376, 377, 378, 379, 381];

    private static readonly HashSet<int> FreddyJumpscareImages =
        [455, 421, 457, 425, 426, 427, 429, 431, 432, 433, 434, 435, 436, 437, 438, 439, 440, 441, 442, 443, 444, 445, 446, 447, 448, 449, 450, 451, 452, 453, 454];

    private static FnafJumpscareCharacter DetectOfficeJumpscare(ClickteamObjectSnapshot? active3, bool powerOut)
    {
        if (active3 is null)
        {
            return FnafJumpscareCharacter.None;
        }

        short? seq = active3.AnimSequence;
        int img = active3.ImageHandle;

        if (seq == 35 || BonnieJumpscareImages.Contains(img))
        {
            return FnafJumpscareCharacter.Bonnie;
        }

        if (seq == 44 || ChicaJumpscareImages.Contains(img))
        {
            return FnafJumpscareCharacter.Chica;
        }

        if (seq == 52 || FoxyJumpscareImages.Contains(img))
        {
            return FnafJumpscareCharacter.Foxy;
        }

        if (seq == 65 || FreddyJumpscareImages.Contains(img))
        {
            return powerOut ? FnafJumpscareCharacter.PowerOutFreddy : FnafJumpscareCharacter.Freddy;
        }

        return FnafJumpscareCharacter.None;
    }

    private static int? ReadSavedNightFromIni()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData))
            {
                return null;
            }

            string iniPath = Path.Combine(appData, "MMFApplications", "freddy");
            if (!File.Exists(iniPath))
            {
                return null;
            }

            bool inFreddySection = false;
            foreach (string rawLine in File.ReadLines(iniPath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == ';')
                {
                    continue;
                }

                if (line[0] == '[' && line[^1] == ']')
                {
                    inFreddySection = line.Equals("[freddy]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inFreddySection && line.StartsWith("level=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(line.AsSpan(6).Trim(), out int level))
                    {
                        return Math.Clamp(level, 1, 5);
                    }
                }
            }
        }
        catch
        {
            // Fall back cleanly to live Clickteam counter if INI cannot be read.
        }

        return null;
    }

    private static string DescribeActiveCamera(
        ClickteamFrameSnapshot snap,
        FnafCamera activeCam,
        Dictionary<FnafCamera, ClickteamRect> camRects,
        ClickteamObjectSnapshot? bonnie,
        ClickteamObjectSnapshot? chica,
        ClickteamObjectSnapshot? freddy,
        ClickteamObjectSnapshot? active3,
        int night,
        bool audioOnly)
    {
        if (audioOnly || activeCam == FnafCamera.Cam6)
        {
            return "Audio only";
        }

        int foxProgress = snap.GetCounter("fox progress") ?? 0;
        int randomPic = snap.GetCounter("random for pic") ?? 50;

        if (activeCam == FnafCamera.Cam1C)
        {
            return foxProgress switch
            {
                0 => "Curtains closed",
                1 => "Foxy peeking out",
                2 => "Foxy stepping out",
                _ => randomPic <= 10 ? "Empty, It's Me sign" : "Empty, Out of Order"
            };
        }

        bool hasFreddy = false;
        bool hasBonnie = false;
        bool hasChica = false;
        if (camRects.TryGetValue(activeCam, out ClickteamRect roomRect))
        {
            hasFreddy = freddy is not null && freddy.Bounds.Overlaps(roomRect);
            hasBonnie = bonnie is not null && bonnie.Bounds.Overlaps(roomRect);
            hasChica = chica is not null && chica.Bounds.Overlaps(roomRect);
        }

        var visible = new List<string>();

        switch (activeCam)
        {
            case FnafCamera.Cam1A:
                if (hasFreddy && !hasBonnie && !hasChica && randomPic <= 10)
                {
                    visible.Add("Freddy staring at camera");
                }
                else
                {
                    if (hasFreddy) visible.Add("Freddy");
                    if (hasBonnie) visible.Add("Bonnie");
                    if (hasChica) visible.Add("Chica");
                }
                break;

            case FnafCamera.Cam1B:
                if (hasFreddy) visible.Add("Freddy in shadows");
                if (hasBonnie) visible.Add("Bonnie");
                if (hasChica) visible.Add("Chica");
                break;

            case FnafCamera.Cam2A:
                if (active3?.AnimSequence == 51 || foxProgress >= 3)
                {
                    visible.Add("Foxy running down hall");
                }
                if (hasBonnie) visible.Add("Bonnie standing in hall");
                break;

            case FnafCamera.Cam2B:
                if (hasBonnie)
                {
                    visible.Add(night >= 4 ? "Bonnie twitching at corner" : "Bonnie at corner");
                }
                else if (active3?.AnimSequence == 75 || active3?.ImageHandle == 484)
                {
                    visible.Add("Golden Freddy poster");
                }
                else if (randomPic == 1)
                {
                    visible.Add("Freddy tearing head off poster");
                }
                break;

            case FnafCamera.Cam3:
                if (hasBonnie) visible.Add("Bonnie standing in closet");
                break;

            case FnafCamera.Cam4A:
                if (hasFreddy) visible.Add("Freddy in hall");
                if (hasChica) visible.Add("Chica in hall");
                break;

            case FnafCamera.Cam4B:
                if (hasFreddy)
                {
                    visible.Add("Freddy staring close up");
                }
                else if (hasChica)
                {
                    visible.Add(night >= 4 ? "Chica twitching at corner" : "Chica staring at corner");
                }
                else if (randomPic is >= 97 and <= 100)
                {
                    visible.Add(randomPic switch
                    {
                        97 => "Newspaper clipping: Kids vanish at local pizzeria",
                        98 => "Newspaper clipping: Five children now reported missing",
                        99 => "Newspaper clipping: Local pizzeria threatened with shutdown",
                        _ => "Newspaper clipping: Local pizzeria said to close by year's end"
                    });
                }
                break;

            case FnafCamera.Cam5:
                if (hasBonnie)
                {
                    visible.Add(randomPic <= 10 ? "Bonnie staring close up" : "Bonnie standing by table");
                }
                else if (randomPic <= 5)
                {
                    visible.Add("Endoskeleton and heads staring at camera");
                }
                break;

            case FnafCamera.Cam7:
                if (hasFreddy) visible.Add("Freddy hiding in dark");
                if (hasChica) visible.Add("Chica in restrooms");
                break;
        }

        return visible.Count > 0 ? string.Join(", ", visible) : "Clear";
    }
}
