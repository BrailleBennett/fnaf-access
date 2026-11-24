using fnaf_access.Config;
using fnaf_access.Game;
using fnaf_access.Speech;

namespace fnaf_access.Narration;

/// <summary>
/// Sighted-parity state-diff and on-demand narration engine driving Prism speech + braille.
/// Announces only what a sighted player can legitimately observe right now.
/// </summary>
public sealed class GameNarrator
{
    private readonly Lock _sync = new();
    private readonly SpeechService _speech;
    private readonly KeybindConfig _config;
    private FnafGameState? _prev;
    private string _lastAnnouncement = string.Empty;
    private int _lastPowerMilestone = 100;
    private int _expectedNight;
    private int _lastOfficeNight;
    private long _lastCameraAnnounceTick;
    private FnafJumpscareCharacter _lastJumpscareAnnounced;

    public bool HasRecentJumpscare => _lastJumpscareAnnounced != FnafJumpscareCharacter.None;

    public GameNarrator(SpeechService speech, KeybindConfig config)
    {
        _speech = speech;
        _config = config;
    }

    public void Say(string text, bool interrupt = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lock (_sync)
        {
            _lastAnnouncement = text;
        }

        _speech.Output(text, interrupt);
    }

    public void Update(FnafGameState cur)
    {
        FnafGameState? prev;
        lock (_sync)
        {
            prev = _prev;
            _prev = cur;
            UpdateExpectedNightLocked(prev, cur);
        }

        if (prev is null || prev.Screen != cur.Screen || prev.FrameIndex != cur.FrameIndex)
        {
            OnScreenEntered(prev, cur);
            return;
        }

        switch (cur.Screen)
        {
            case FnafScreen.Title:
                UpdateTitle(prev, cur);
                break;
            case FnafScreen.Office:
                UpdateOffice(prev, cur);
                break;
            case FnafScreen.CustomNight:
                UpdateCustomNight(prev, cur);
                break;
        }
    }

    private void UpdateExpectedNightLocked(FnafGameState? prev, FnafGameState cur)
    {
        switch (cur.Screen)
        {
            case FnafScreen.Title:
                _expectedNight = cur.TitleOptionSelected switch
                {
                    0 => 1,
                    1 => cur.SavedNight > 0 ? cur.SavedNight : 1,
                    2 => 6,
                    3 => 7,
                    _ => _expectedNight
                };
                break;

            case FnafScreen.CustomNight:
                _expectedNight = 7;
                break;

            case FnafScreen.Office:
                if (cur.Night > 0)
                {
                    _expectedNight = cur.Night;
                    _lastOfficeNight = cur.Night;
                }

                break;

            case FnafScreen.SixAm:
                if (cur.Night > 0)
                {
                    // Clickteam's "next day" frame already increments "night number" to the upcoming night.
                    _expectedNight = cur.Night;
                }
                else if (prev?.Screen != FnafScreen.SixAm && _expectedNight > 0)
                {
                    _expectedNight = Math.Min(_expectedNight + 1, 7);
                }

                break;

            case FnafScreen.NightIntro:
                if (cur.Night > 0)
                {
                    _expectedNight = cur.Night;
                }

                break;
        }
    }

    private int ResolveNight(FnafGameState cur)
    {
        if (cur.Night > 0)
        {
            return cur.Night;
        }

        lock (_sync)
        {
            return _expectedNight;
        }
    }

    private const string WarningText =
        "Warning! This game contains flashing lights, loud noises, and lots of jumpscares!";

    private const string HelpWantedText =
        "Help Wanted. Freddy Fazbear's Pizza. Family pizzeria looking for security guard to work the nightshift. " +
        "12 AM to 6 AM. Monitor cameras, ensure safety of equipment and animatronic characters. " +
        "Not responsible for injury or dismemberment. 120 dollars a week. To apply call: 1-888-FAZ-FAZBEAR.";

    private const string Night5PaycheckText =
        "Good job, sport! See you next week! " +
        "Freddy Fazbear's Pizza. Date: 11-12-XX. " +
        "Pay to the order of: Mike Schmidt. Amount: 120 dollars.";

    private const string Night6OvertimeText =
        "Good job, sport! You've earned some overtime! " +
        "Freddy Fazbear's Pizza. Date: 11-13-XX. " +
        "Pay to the order of: Mike Schmidt. Amount: 120 dollars and 50 cents.";

    private const string Night7PinkSlipText =
        "Notice of Termination. You're fired. " +
        "Reason: Tampering with the animatronics. General unprofessionalism. Odor. " +
        "Thanks, management.";

    private string FormatEndingText(FnafGameState cur)
    {
        FnafEndingType ending = cur.EndingType;
        if (ending == FnafEndingType.Unknown)
        {
            int officeNight;
            lock (_sync)
            {
                officeNight = _lastOfficeNight;
            }

            ending = officeNight switch
            {
                5 => FnafEndingType.Night5Paycheck,
                6 => FnafEndingType.Night6Overtime,
                >= 7 => FnafEndingType.Night7PinkSlip,
                _ => FnafEndingType.Night5Paycheck
            };
        }

        return ending switch
        {
            FnafEndingType.Night5Paycheck => Night5PaycheckText,
            FnafEndingType.Night6Overtime => Night6OvertimeText,
            FnafEndingType.Night7PinkSlip => Night7PinkSlipText,
            _ => "Ending screen."
        };
    }

    private void OnScreenEntered(FnafGameState? prev, FnafGameState cur)
    {
        // Safety net: if a jumpscare frame has a generic Clickteam name ("Frame 15", etc.)
        // and was classified as Unknown, infer Golden Freddy / Power-Out Freddy from the
        // exact preceding state (Custom Night 1-9-8-7, Golden Freddy in office, or Power Out).
        if (cur.Screen == FnafScreen.Unknown && prev is not null)
        {
            if ((prev.Screen == FnafScreen.CustomNight &&
                 prev.FreddyAi == 1 && prev.BonnieAi == 9 && prev.ChicaAi == 8 && prev.FoxyAi == 7) ||
                (prev.Screen == FnafScreen.Office && prev.GoldenFreddyInOffice))
            {
                AnnounceJumpscareIfNew(FnafJumpscareCharacter.GoldenFreddy);
                return;
            }

            if (prev.Screen == FnafScreen.Office && prev.PowerOut)
            {
                AnnounceJumpscareIfNew(FnafJumpscareCharacter.PowerOutFreddy);
                return;
            }
        }

        switch (cur.Screen)
        {
            case FnafScreen.Warning:
                Say(WarningText);
                break;

            case FnafScreen.Title:
                _lastJumpscareAnnounced = FnafJumpscareCharacter.None;
                string stars = cur.StarsUnlocked > 0 ? $", {cur.StarsUnlocked} {(cur.StarsUnlocked == 1 ? "star" : "stars")}" : "";
                Say($"Five Nights at Freddy's title screen{stars}. {FormatTitleOption(cur)}.");
                break;

            case FnafScreen.Newspaper:
                Say(HelpWantedText);
                break;

            case FnafScreen.NightIntro:
                _lastJumpscareAnnounced = FnafJumpscareCharacter.None;
                int introNight = ResolveNight(cur);
                if (cur.FrameName.Equals("wait", StringComparison.OrdinalIgnoreCase))
                {
                    Say(introNight > 0 ? $"Loading Night {introNight}." : "Loading.");
                }
                else
                {
                    Say(introNight > 0 ? $"Night {introNight}, 12 AM." : "12 AM.");
                }

                break;

            case FnafScreen.Office:
                _lastJumpscareAnnounced = FnafJumpscareCharacter.None;
                _lastPowerMilestone = cur.PowerPercent;
                Say($"Office. Night {cur.Night}, {FormatHour(cur.Hour)}, power {cur.PowerPercent} percent. Facing {FormatSide(cur.ViewSide)}.");
                break;

            case FnafScreen.SixAm:
                Say("6 AM! Night complete!");
                break;

            case FnafScreen.GameOver:
                // Don't cut off the jumpscare character announcement if Game Over loads right after.
                Say("Game over.", interrupt: _lastJumpscareAnnounced == FnafJumpscareCharacter.None);
                break;

            case FnafScreen.CustomNight:
                _lastJumpscareAnnounced = FnafJumpscareCharacter.None;
                Say($"Custom Night. Freddy {cur.FreddyAi}, Bonnie {cur.BonnieAi}, Chica {cur.ChicaAi}, Foxy {cur.FoxyAi}. Focus: Freddy, {cur.FreddyAi}.");
                break;

            case FnafScreen.Ending:
                Say(FormatEndingText(cur));
                break;

            case FnafScreen.Jumpscare:
                AnnounceJumpscareIfNew(cur.Jumpscare);
                break;
        }
    }

    private void AnnounceJumpscareIfNew(FnafJumpscareCharacter ch)
    {
        if (ch == FnafJumpscareCharacter.None)
        {
            Say("Jumpscare!");
            return;
        }

        if (_lastJumpscareAnnounced == ch)
        {
            return;
        }

        _lastJumpscareAnnounced = ch;
        Say(FormatJumpscare(ch));
    }

    private void UpdateTitle(FnafGameState prev, FnafGameState cur)
    {
        if (prev.TitleOptionSelected != cur.TitleOptionSelected ||
            (cur.TitleOptionSelected == 1 && prev.SavedNight != cur.SavedNight && cur.SavedNight > 0))
        {
            Say(FormatTitleOption(cur));
        }
    }

    private void UpdateCustomNight(FnafGameState prev, FnafGameState cur)
    {
        if (prev.FreddyAi != cur.FreddyAi)
        {
            Say($"Freddy {cur.FreddyAi}");
        }
        else if (prev.BonnieAi != cur.BonnieAi)
        {
            Say($"Bonnie {cur.BonnieAi}");
        }
        else if (prev.ChicaAi != cur.ChicaAi)
        {
            Say($"Chica {cur.ChicaAi}");
        }
        else if (prev.FoxyAi != cur.FoxyAi)
        {
            Say($"Foxy {cur.FoxyAi}");
        }
    }

    private void UpdateOffice(FnafGameState prev, FnafGameState cur)
    {
        // Jumpscare announcement takes absolute top priority (including during power-out).
        if (cur.Jumpscare != FnafJumpscareCharacter.None)
        {
            if (prev.Jumpscare != cur.Jumpscare)
            {
                AnnounceJumpscareIfNew(cur.Jumpscare);
            }

            return;
        }

        // Power outage takes top priority
        if (!prev.PowerOut && cur.PowerOut)
        {
            Say("Power out!");
            return;
        }

        if (cur.PowerOut)
        {
            return;
        }

        // Hour progression (1 AM .. 5 AM)
        if (prev.Hour != cur.Hour)
        {
            Say(FormatHour(cur.Hour), interrupt: false);
        }

        // Low-power callouts at 20%, 10%, 5%
        foreach (int milestone in new[] { 20, 10, 5 })
        {
            if (_lastPowerMilestone > milestone && cur.PowerPercent <= milestone)
            {
                _lastPowerMilestone = milestone;
                Say($"Power {cur.PowerPercent} percent.", interrupt: false);
                break;
            }
        }

        // Golden Freddy in office
        if (!prev.GoldenFreddyInOffice && cur.GoldenFreddyInOffice)
        {
            Say("Golden Freddy in office!");
        }

        // Mute Call button appearing
        if (!prev.CanMuteCall && cur.CanMuteCall)
        {
            string muteKey = GetKeyLabel(GameAction.MuteCall);
            Say($"Mute call available, press {muteKey}.", interrupt: false);
        }

        // Monitor raised / lowered
        if (!prev.MonitorUp && cur.MonitorUp)
        {
            Volatile.Write(ref _lastCameraAnnounceTick, Environment.TickCount64);
            Say($"Monitor up. {FormatActiveCamera(cur)}");
            return;
        }

        if (prev.MonitorUp && !cur.MonitorUp)
        {
            Say($"Monitor down. Facing {FormatSide(cur.ViewSide)}.");
            return;
        }

        // Inside Camera Monitor
        if (cur.MonitorUp)
        {
            if (prev.CurrentCamera != cur.CurrentCamera)
            {
                Volatile.Write(ref _lastCameraAnnounceTick, Environment.TickCount64);
                Say(FormatActiveCamera(cur));
            }
            else if (prev.ActiveCameraDescription != cur.ActiveCameraDescription)
            {
                // If status updated right after switching cameras, repeat the full camera label
                // so speech never cuts off the camera name with a bare "Clear" or animatronic name.
                long elapsedMs = Environment.TickCount64 - Volatile.Read(ref _lastCameraAnnounceTick);
                if (elapsedMs < 1200)
                {
                    Volatile.Write(ref _lastCameraAnnounceTick, Environment.TickCount64);
                    Say(FormatActiveCamera(cur));
                }
                else
                {
                    Say(cur.ActiveCameraDescription);
                }
            }

            return;
        }

        // Office panning side changes
        if (prev.ViewSide != cur.ViewSide)
        {
            Say(FormatSide(cur.ViewSide));
        }

        // Brief door narration ("Closed" / "Opened")
        if (prev.LeftDoorClosed != cur.LeftDoorClosed)
        {
            Say(cur.LeftDoorClosed ? "Closed" : "Opened");
        }

        if (prev.RightDoorClosed != cur.RightDoorClosed)
        {
            Say(cur.RightDoorClosed ? "Closed" : "Opened");
        }

        // Brief light narration ("On" / "Off") + sighted-parity doorway check
        bool leftLightChanged = prev.LeftLightOn != cur.LeftLightOn;
        bool rightLightChanged = prev.RightLightOn != cur.RightLightOn;

        if (leftLightChanged && cur.LeftLightOn)
        {
            Say(cur.BonnieVisibleAtLeftDoor ? "Bonnie! On" : "On");
        }
        else if (rightLightChanged && cur.RightLightOn)
        {
            Say(cur.ChicaVisibleAtRightWindow ? "Chica! On" : "On");
        }
        else if ((leftLightChanged && !cur.LeftLightOn && cur.CanReachLeftLight) ||
                 (rightLightChanged && !cur.RightLightOn && cur.CanReachRightLight))
        {
            Say("Off");
        }
        else if (cur.LeftLightOn && !prev.BonnieVisibleAtLeftDoor && cur.BonnieVisibleAtLeftDoor)
        {
            Say("Bonnie!");
        }
        else if (cur.RightLightOn && !prev.ChicaVisibleAtRightWindow && cur.ChicaVisibleAtRightWindow)
        {
            Say("Chica!");
        }
    }

    public void SpeakPowerAndUsage(FnafGameState cur)
    {
        if (cur.Screen == FnafScreen.NightIntro)
        {
            int loadingNight = ResolveNight(cur);
            Say(loadingNight > 0 ? $"Loading Night {loadingNight}." : "Loading.");
            return;
        }

        if (cur.Screen != FnafScreen.Office)
        {
            Say("Not in office.");
            return;
        }

        if (cur.PowerOut)
        {
            Say("Power out.");
            return;
        }

        Say($"Power {cur.PowerPercent} percent, usage {cur.UsageLevel}.");
    }

    public void SpeakTimeAndNight(FnafGameState cur)
    {
        if (cur.Screen == FnafScreen.Office)
        {
            Say($"{FormatHour(cur.Hour)}, Night {cur.Night}.");
        }
        else if (cur.Screen == FnafScreen.Title)
        {
            Say($"Saved Night {cur.SavedNight}.");
        }
        else if (cur.Screen == FnafScreen.NightIntro)
        {
            int loadingNight = ResolveNight(cur);
            Say(loadingNight > 0 ? $"12 AM, Night {loadingNight}." : "Loading.");
        }
        else if (cur.Screen == FnafScreen.SixAm)
        {
            Say("6 AM.");
        }
        else
        {
            Say("Not in office.");
        }
    }

    public void SpeakVisibleDoorStatus(FnafGameState cur)
    {
        if (cur.Screen != FnafScreen.Office)
        {
            Say("Not in office.");
            return;
        }

        if (cur.PowerOut)
        {
            Say("Power out.");
            return;
        }

        if (cur.MonitorUp)
        {
            Say("Monitor is up.");
            return;
        }

        if (cur.CanReachLeftDoor || cur.CanReachLeftLight)
        {
            string prefix = cur.BonnieVisibleAtLeftDoor ? "Bonnie! " : "";
            Say($"{prefix}Left door {(cur.LeftDoorClosed ? "closed" : "open")}, light {(cur.LeftLightOn ? "on" : "off")}.");
            return;
        }

        if (cur.CanReachRightDoor || cur.CanReachRightLight)
        {
            string prefix = cur.ChicaVisibleAtRightWindow ? "Chica! " : "";
            Say($"{prefix}Right door {(cur.RightDoorClosed ? "closed" : "open")}, light {(cur.RightLightOn ? "on" : "off")}.");
            return;
        }

        Say("No door in view.");
    }

    public void SpeakCurrentView(FnafGameState cur)
    {
        if (cur.Screen == FnafScreen.Title)
        {
            string stars = cur.StarsUnlocked == 1 ? "1 star" : $"{cur.StarsUnlocked} stars";
            Say($"Title screen, {stars}. {FormatTitleOption(cur)}.");
            return;
        }

        if (cur.Screen == FnafScreen.CustomNight)
        {
            Say($"Custom Night. Freddy {cur.FreddyAi}, Bonnie {cur.BonnieAi}, Chica {cur.ChicaAi}, Foxy {cur.FoxyAi}.");
            return;
        }

        if (cur.Screen == FnafScreen.Warning)
        {
            Say(WarningText);
            return;
        }

        if (cur.Screen == FnafScreen.Newspaper)
        {
            Say(HelpWantedText);
            return;
        }

        if (cur.Screen == FnafScreen.Ending)
        {
            Say(FormatEndingText(cur));
            return;
        }

        if (cur.Screen == FnafScreen.Jumpscare)
        {
            Say(FormatJumpscare(cur.Jumpscare));
            return;
        }

        if (cur.Screen != FnafScreen.Office)
        {
            Say(cur.Screen.ToString());
            return;
        }

        if (cur.PowerOut)
        {
            Say("Office, power out.");
            return;
        }

        if (cur.MonitorUp)
        {
            Say($"Monitor up. {FormatActiveCamera(cur)}");
            return;
        }

        var parts = new List<string>();
        if (cur.GoldenFreddyInOffice)
        {
            parts.Add("Golden Freddy in office");
        }

        if (cur.CanReachLeftDoor && cur.BonnieVisibleAtLeftDoor)
        {
            parts.Add("Bonnie at door");
        }

        if (cur.CanReachRightDoor && cur.ChicaVisibleAtRightWindow)
        {
            parts.Add("Chica at window");
        }

        parts.Add($"Facing {FormatSide(cur.ViewSide)}");
        if (cur.CanReachLeftDoor)
        {
            parts.Add($"left door {(cur.LeftDoorClosed ? "closed" : "open")}, light {(cur.LeftLightOn ? "on" : "off")}");
        }

        if (cur.CanReachRightDoor)
        {
            parts.Add($"right door {(cur.RightDoorClosed ? "closed" : "open")}, light {(cur.RightLightOn ? "on" : "off")}");
        }

        if (cur.CanMuteCall)
        {
            parts.Add("mute call visible");
        }

        Say(string.Join(". ", parts) + ".");
    }

    public void RepeatOrDescribe(FnafGameState cur)
    {
        if (cur.Screen == FnafScreen.Office && cur.MonitorUp)
        {
            Say(FormatActiveCamera(cur));
            return;
        }

        string last;
        lock (_sync)
        {
            last = _lastAnnouncement;
        }

        if (!string.IsNullOrEmpty(last))
        {
            Say(last);
        }
        else
        {
            SpeakCurrentView(cur);
        }
    }

    public void SpeakHelp(FnafGameState cur)
    {
        if (cur.Screen == FnafScreen.Title)
        {
            Say($"Title screen controls. {GetKeyLabel(GameAction.MenuUp)} and {GetKeyLabel(GameAction.MenuDown)} to select, {GetKeyLabel(GameAction.MenuConfirm)} to confirm, {GetKeyLabel(GameAction.TogglePassthrough)} for passthrough.");
            return;
        }

        if (cur.Screen == FnafScreen.CustomNight)
        {
            Say($"Custom Night controls. {GetKeyLabel(GameAction.PanLeft)} and {GetKeyLabel(GameAction.PanRight)} select Freddy, Bonnie, Chica, or Foxy. {GetKeyLabel(GameAction.MenuUp)} and {GetKeyLabel(GameAction.MenuDown)} increase or decrease AI level from 0 to 20. Press {GetKeyLabel(GameAction.MenuConfirm)} or {GetKeyLabel(GameAction.ToggleMonitor)} to start. Press {GetKeyLabel(GameAction.ReadCurrentView)} to hear all levels.");
            return;
        }

        if (cur.Screen == FnafScreen.Office && cur.MonitorUp)
        {
            Say($"Camera controls. 1 through 0 and minus select cameras, {GetKeyLabel(GameAction.CamPrevious)} and {GetKeyLabel(GameAction.CamNext)} cycle cameras, {GetKeyLabel(GameAction.ToggleMonitor)} lowers monitor, {GetKeyLabel(GameAction.ReadPowerAndUsage)} power, {GetKeyLabel(GameAction.ReadTimeAndNight)} time.");
            return;
        }

        if (cur.Screen == FnafScreen.Office)
        {
            Say($"Office controls. Hold {GetKeyLabel(GameAction.PanLeft)} or {GetKeyLabel(GameAction.PanRight)} to pan. {GetKeyLabel(GameAction.ToggleLeftDoor)} left door, {GetKeyLabel(GameAction.ToggleLeftLight)} left light, {GetKeyLabel(GameAction.ToggleRightDoor)} right door, {GetKeyLabel(GameAction.ToggleRightLight)} right light when in view. {GetKeyLabel(GameAction.ReadDoorStatus)} door status, {GetKeyLabel(GameAction.ToggleMonitor)} monitor, {GetKeyLabel(GameAction.MuteCall)} mute call, {GetKeyLabel(GameAction.ReadPowerAndUsage)} power, {GetKeyLabel(GameAction.ReadTimeAndNight)} time, {GetKeyLabel(GameAction.ReadCurrentView)} view.");
            return;
        }

        Say($"Press {GetKeyLabel(GameAction.MenuConfirm)} to continue, or {GetKeyLabel(GameAction.TogglePassthrough)} to toggle passthrough.");
    }

    private string GetKeyLabel(GameAction action) =>
        _config.Bindings.TryGetValue(action, out string? k) ? k : action.ToString();

    private static string FormatTitleOption(FnafGameState cur) => cur.TitleOptionSelected switch
    {
        0 => "New Game",
        1 => $"Continue, Night {cur.SavedNight}",
        2 => "6th Night",
        3 => "Custom Night",
        _ => $"Option {cur.TitleOptionSelected}"
    };

    private static string FormatHour(int hour) => $"{(hour == 0 ? 12 : hour)} AM";

    private static string FormatSide(OfficeViewSide side) => side switch
    {
        OfficeViewSide.Left => "left",
        OfficeViewSide.Right => "right",
        _ => "center"
    };

    private static string FormatActiveCamera(FnafGameState cur)
    {
        CameraDefinition? def = FnafGameState.GetCameraDefinition(cur.CurrentCamera);
        string camLabel = def is not null
            ? $"{def.Value.Code}: {def.Value.DisplayName}"
            : cur.CurrentCamera.ToString();
        return $"{cur.ActiveCameraDescription}, {camLabel}.";
    }

    private static string FormatJumpscare(FnafJumpscareCharacter ch) => ch switch
    {
        FnafJumpscareCharacter.Bonnie => "Bonnie jumpscare!",
        FnafJumpscareCharacter.Chica => "Chica jumpscare!",
        FnafJumpscareCharacter.Foxy => "Foxy jumpscare!",
        FnafJumpscareCharacter.Freddy => "Freddy jumpscare!",
        FnafJumpscareCharacter.PowerOutFreddy => "Power out Freddy jumpscare!",
        FnafJumpscareCharacter.GoldenFreddy => "Golden Freddy jumpscare!",
        _ => "Jumpscare!"
    };
}
