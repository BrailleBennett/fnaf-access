namespace fnaf_access.Game;

public enum ClickteamObjectType : short
{
    Active = 2,
    String = 3,
    Counter = 7,
    Ini = 33
}

public readonly record struct ClickteamRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int CenterX => Left + Width / 2;
    public int CenterY => Top + Height / 2;

    public bool Overlaps(ClickteamRect other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    public bool ContainsPoint(int x, int y) =>
        x >= Left && x <= Right && y >= Top && y <= Bottom;

    /// <summary>
    /// Returns true if a usable portion of this object's bounding box is physically inside
    /// the visible 1280x720 viewport given the current frame scroll offset (windowX, windowY).
    /// </summary>
    public bool IsPhysicallyOnScreen(int windowX, int windowY, int viewWidth = 1280, int viewHeight = 720, int minVisiblePx = 16)
    {
        int visLeft = Math.Max(Left, windowX);
        int visRight = Math.Min(Right, windowX + viewWidth);
        int visTop = Math.Max(Top, windowY);
        int visBottom = Math.Min(Bottom, windowY + viewHeight);
        return (visRight - visLeft) >= minVisiblePx && (visBottom - visTop) >= minVisiblePx;
    }

    /// <summary>
    /// Computes a clamped clickable point in logical 1280x720 viewport coordinates,
    /// guaranteed to lie inside both this object's bounds and the visible viewport.
    /// </summary>
    public bool TryGetVisibleClickPoint(int windowX, int windowY, out double logicalX, out double logicalY, int viewWidth = 1280, int viewHeight = 720)
    {
        int visLeft = Math.Max(Left, windowX);
        int visRight = Math.Min(Right, windowX + viewWidth);
        int visTop = Math.Max(Top, windowY);
        int visBottom = Math.Min(Bottom, windowY + viewHeight);

        if (visRight - visLeft < 4 || visBottom - visTop < 4)
        {
            logicalX = 0;
            logicalY = 0;
            return false;
        }

        logicalX = ((visLeft + visRight) / 2.0) - windowX;
        logicalY = ((visTop + visBottom) / 2.0) - windowY;
        return true;
    }
}

public sealed class ClickteamObjectSnapshot
{
    public required short SlotIndex { get; init; }
    public required short ObjectInfoId { get; init; }
    public required short Type { get; init; }
    public required string Name { get; init; }
    public required uint LphoAddress { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int ImageWidth { get; init; }
    public required int ImageHeight { get; init; }
    public required int ImageHandle { get; init; }
    public required ClickteamRect Bounds { get; init; }
    public required bool IsVisible { get; init; }

    // Counter fields (Type == 7)
    public int? CounterIntValue { get; init; }
    public double? CounterDoubleValue { get; init; }

    // String fields (Type == 3)
    public string? StringText { get; init; }

    // Active animation fields (Type == 2)
    public short? AnimSequence { get; init; }
    public short? AnimDirection { get; init; }
    public short? AnimFrame { get; init; }
}

public sealed class ClickteamFrameSnapshot
{
    public required int FrameIndex { get; init; }
    public required string FrameName { get; init; }
    public required int LevelWidth { get; init; }
    public required int LevelHeight { get; init; }
    public required int WindowX { get; init; }
    public required int WindowY { get; init; }
    public required uint FrameTimerMs { get; init; }
    public required uint MainWindowHandle { get; init; }
    public required uint EditWindowHandle { get; init; }
    public required IReadOnlyList<ClickteamObjectSnapshot> Objects { get; init; }

    public ClickteamObjectSnapshot? FindByName(string name) =>
        Objects.FirstOrDefault(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public int? GetCounter(string name) => FindByName(name)?.CounterIntValue;
}

public readonly record struct FrameReadResult(
    ClickteamFrameSnapshot? Snapshot,
    bool ProcessExited,
    bool IsTransitioning);

/// <summary>
/// Direct O(1) Clickteam Fusion 2.5 runtime reader for Five Nights at Freddy's 1 (Steam v1.132).
/// Reads CRunApp, CRunFrame, RunHeader, rhOiList, and rhObjectList cleanly without runtime scanning.
/// </summary>
public sealed class ClickteamRuntime : IDisposable
{
    // Verified static RVAs in FiveNightsatFreddys.exe (CF 2.5 Unicode runtime, FNaF 1 v1.132)
    private const uint RvaRunAppPtr = 0x0AC9AC;
    private const uint RvaRunFramePtr = 0x0AC9B0;
    private const uint RvaRunHeaderPtr = 0x0AC9B4;
    private const uint MagicPamu = 0x554D4150; // "PAMU"

    // CRunApp / CRunFrame offsets
    private const uint AppOffsetCurrentFrame = 0x1EC;
    private const uint FrameOffsetNamePtr = 0x010;

    // RunHeader (rhPtr) offsets
    private const uint RhOffsetMainWin = 0x00C;
    private const uint RhOffsetEditWin = 0x010;
    private const uint RhOffsetNObjects = 0x03C;
    private const uint RhOffsetMaxObjects = 0x040;
    private const uint RhOffsetMaxOi = 0x054;
    private const uint RhOffsetOiList = 0x058;
    private const uint RhOffsetLevelSx = 0x0A8;
    private const uint RhOffsetLevelSy = 0x0AC;
    private const uint RhOffsetWindowX = 0x0B0;
    private const uint RhOffsetWindowY = 0x0B4;
    private const uint RhOffsetTimer = 0x0C0;
    private const uint RhOffsetObjectList = 0x1278;

    // objInfoList offsets
    private const uint OiStructStride = 0x92;
    private const uint OiOffsetName = 0x5A;

    // LPHO (CObject + 8) offsets
    private const uint HoOffsetRunHeader = 0x000;
    private const uint HoOffsetOi = 0x00A;
    private const uint HoOffsetType = 0x010;
    private const uint HoOffsetOiList = 0x014;
    private const uint HoOffsetX = 0x044;
    private const uint HoOffsetY = 0x04C;
    private const uint HoOffsetImgWidth = 0x058;
    private const uint HoOffsetImgHeight = 0x05C;
    private const uint HoOffsetImgHandle = 0x0D4;
    private const uint HoOffsetRectLeft = 0x110;
    private const uint HoOffsetRectTop = 0x114;
    private const uint HoOffsetRectRight = 0x118;
    private const uint HoOffsetRectBottom = 0x11C;

    // Active (type == 2) animation + sprite offsets
    private const uint HoOffsetCommon = 0x03C;
    private const uint ActiveOffsetAnimPtr = 0x1DA;
    private const uint ActiveOffsetAnimDir = 0x1E4;
    private const uint ActiveOffsetAnimFrame = 0x20E;
    private const uint ActiveOffsetRsFlags = 0x226;
    private const int CommonOffsetAnimHeader = 0x74;

    // Counter (type == 7) offsets
    private const uint CounterOffsetRsFlags = 0x1E8;
    private const uint CounterOffsetValueType = 0x200;
    private const uint CounterOffsetValueData = 0x202;

    // String (type == 3) offsets
    private const uint StringOffsetRsFlags = 0x1E8;
    private const uint StringOffsetTextPtr = 0x226;

    private const ushort RsFlagHidden = 0x0080;

    private readonly ProcessMemoryReader _reader;
    private readonly uint _mainExeBase;
    private uint _cachedRhPtr;
    private readonly Dictionary<uint, Dictionary<uint, short>> _animTableCache = [];

    public uint ProcessId => _reader.ProcessId;
    public bool IsAttached => _reader.IsOpen;

    private ClickteamRuntime(ProcessMemoryReader reader, uint mainExeBase)
    {
        _reader = reader;
        _mainExeBase = mainExeBase;
    }

    public static ClickteamRuntime? TryAttach(uint processId)
    {
        ProcessMemoryReader? reader = null;
        try
        {
            reader = ProcessMemoryReader.Open(processId);
            IReadOnlyList<RemoteModuleInfo> modules = reader.EnumerateModules();
            RemoteModuleInfo? mainExe = modules.FirstOrDefault(m =>
                m.Name.Equals("FiveNightsatFreddys.exe", StringComparison.OrdinalIgnoreCase));

            if (mainExe is null || mainExe.Value.BaseAddress == 0)
            {
                reader.Dispose();
                return null;
            }

            uint baseAddr = mainExe.Value.BaseAddress;
            if (!reader.TryReadUInt32(baseAddr + RvaRunAppPtr, out uint appPtr) ||
                !reader.TryReadUInt32(appPtr, out uint magic) ||
                magic != MagicPamu)
            {
                reader.Dispose();
                return null;
            }

            return new ClickteamRuntime(reader, baseAddr);
        }
        catch
        {
            reader?.Dispose();
            return null;
        }
    }

    public bool IsProcessAlive()
    {
        try
        {
            using System.Diagnostics.Process proc = System.Diagnostics.Process.GetProcessById((int)_reader.ProcessId);
            return !proc.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public FrameReadResult ReadFrame()
    {
        if (!_reader.IsOpen)
        {
            return new FrameReadResult(null, ProcessExited: true, IsTransitioning: false);
        }

        if (!_reader.TryReadUInt32(_mainExeBase + RvaRunAppPtr, out uint appPtr))
        {
            bool alive = IsProcessAlive();
            return new FrameReadResult(null, ProcessExited: !alive, IsTransitioning: alive);
        }

        if (appPtr == 0)
        {
            bool alive = IsProcessAlive();
            return new FrameReadResult(null, ProcessExited: !alive, IsTransitioning: alive);
        }

        if (!_reader.TryReadUInt32(_mainExeBase + RvaRunFramePtr, out uint framePtr) ||
            !_reader.TryReadUInt32(_mainExeBase + RvaRunHeaderPtr, out uint rhPtr) ||
            framePtr == 0 || rhPtr == 0)
        {
            // Normal during Clickteam frame transitions (e.g., Night Loading -> Office).
            bool alive = IsProcessAlive();
            return new FrameReadResult(null, ProcessExited: !alive, IsTransitioning: alive);
        }

        ClickteamFrameSnapshot? snap = ReadFrameSnapshotCore(appPtr, framePtr, rhPtr);
        if (snap is null)
        {
            bool alive = IsProcessAlive();
            return new FrameReadResult(null, ProcessExited: !alive, IsTransitioning: alive);
        }

        return new FrameReadResult(snap, ProcessExited: false, IsTransitioning: false);
    }

    public ClickteamFrameSnapshot? ReadFrameSnapshot() => ReadFrame().Snapshot;

    private ClickteamFrameSnapshot? ReadFrameSnapshotCore(uint appPtr, uint framePtr, uint rhPtr)
    {
        if (!_reader.TryReadInt32(appPtr + AppOffsetCurrentFrame, out int frameIndex))
        {
            return null;
        }

        string frameName = string.Empty;
        if (_reader.TryReadUInt32(framePtr + FrameOffsetNamePtr, out uint namePtr) && namePtr != 0)
        {
            frameName = _reader.ReadUnicodeString(namePtr, 64) ?? string.Empty;
        }

        _reader.TryReadUInt32(rhPtr + RhOffsetMainWin, out uint mainWin);
        _reader.TryReadUInt32(rhPtr + RhOffsetEditWin, out uint editWin);
        _reader.TryReadInt32(rhPtr + RhOffsetNObjects, out int nObjects);
        _reader.TryReadInt32(rhPtr + RhOffsetMaxObjects, out int maxObjects);
        _reader.TryReadInt32(rhPtr + RhOffsetMaxOi, out int maxOi);
        _reader.TryReadUInt32(rhPtr + RhOffsetOiList, out uint oiListPtr);
        _reader.TryReadInt32(rhPtr + RhOffsetLevelSx, out int levelSx);
        _reader.TryReadInt32(rhPtr + RhOffsetLevelSy, out int levelSy);
        _reader.TryReadInt32(rhPtr + RhOffsetWindowX, out int windowX);
        _reader.TryReadInt32(rhPtr + RhOffsetWindowY, out int windowY);
        _reader.TryReadUInt32(rhPtr + RhOffsetTimer, out uint timerMs);
        _reader.TryReadUInt32(rhPtr + RhOffsetObjectList, out uint objListPtr);

        var objects = new List<ClickteamObjectSnapshot>(Math.Clamp(nObjects, 0, 512));

        if (rhPtr != _cachedRhPtr)
        {
            _cachedRhPtr = rhPtr;
            _animTableCache.Clear();
        }

        if (objListPtr != 0 && maxObjects > 0 && maxObjects <= 4096)
        {
            byte[] tableBuf = new byte[maxObjects * 8];
            if (_reader.TryReadBytes(objListPtr, tableBuf, out int tableRead))
            {
                int slotCount = tableRead / 8;
                var oiNameCache = new Dictionary<uint, string>();
                byte[] lphoBuf = new byte[0x230];

                for (int slot = 0; slot < slotCount; slot++)
                {
                    uint cobjPtr = BitConverter.ToUInt32(tableBuf, slot * 8);
                    if (cobjPtr < 0x00120000 || cobjPtr >= 0x70000000)
                    {
                        continue;
                    }

                    uint lpho = cobjPtr + 8;
                    if (!_reader.TryReadBytes(lpho, lphoBuf, out int read) || read < 0x120)
                    {
                        continue;
                    }

                    if (BitConverter.ToUInt32(lphoBuf, (int)HoOffsetRunHeader) != rhPtr)
                    {
                        continue;
                    }

                    short oi = BitConverter.ToInt16(lphoBuf, (int)HoOffsetOi);
                    short type = BitConverter.ToInt16(lphoBuf, (int)HoOffsetType);
                    uint hoOiList = BitConverter.ToUInt32(lphoBuf, (int)HoOffsetOiList);

                    string name = string.Empty;
                    if (hoOiList != 0 && oiListPtr != 0 &&
                        hoOiList >= oiListPtr && hoOiList < oiListPtr + (uint)(Math.Max(1, maxOi) * OiStructStride))
                    {
                        if (!oiNameCache.TryGetValue(hoOiList, out name!))
                        {
                            name = _reader.ReadUnicodeString(hoOiList + OiOffsetName, 24) ?? string.Empty;
                            oiNameCache[hoOiList] = name;
                        }
                    }

                    int x = BitConverter.ToInt32(lphoBuf, (int)HoOffsetX);
                    int y = BitConverter.ToInt32(lphoBuf, (int)HoOffsetY);
                    int imgW = BitConverter.ToInt32(lphoBuf, (int)HoOffsetImgWidth);
                    int imgH = BitConverter.ToInt32(lphoBuf, (int)HoOffsetImgHeight);
                    int imgHandle = BitConverter.ToInt32(lphoBuf, (int)HoOffsetImgHandle);
                    var bounds = new ClickteamRect(
                        BitConverter.ToInt32(lphoBuf, (int)HoOffsetRectLeft),
                        BitConverter.ToInt32(lphoBuf, (int)HoOffsetRectTop),
                        BitConverter.ToInt32(lphoBuf, (int)HoOffsetRectRight),
                        BitConverter.ToInt32(lphoBuf, (int)HoOffsetRectBottom));

                    bool isVisible = true;
                    int? counterInt = null;
                    double? counterDbl = null;
                    string? stringText = null;
                    short? animSeq = null;
                    short? animDir = null;
                    short? animFrame = null;

                    if (type == (short)ClickteamObjectType.Active && read >= 0x228)
                    {
                        uint hoCommon = BitConverter.ToUInt32(lphoBuf, (int)HoOffsetCommon);
                        uint raAnimPtr = BitConverter.ToUInt32(lphoBuf, (int)ActiveOffsetAnimPtr);
                        animSeq = ResolveAnimSequence(hoCommon, raAnimPtr);
                        animDir = BitConverter.ToInt16(lphoBuf, (int)ActiveOffsetAnimDir);
                        animFrame = BitConverter.ToInt16(lphoBuf, (int)ActiveOffsetAnimFrame);
                        ushort rsFlags = BitConverter.ToUInt16(lphoBuf, (int)ActiveOffsetRsFlags);
                        isVisible = (rsFlags & RsFlagHidden) == 0;
                    }
                    else if (type == (short)ClickteamObjectType.Counter && read >= 0x20A)
                    {
                        ushort rsFlags = BitConverter.ToUInt16(lphoBuf, (int)CounterOffsetRsFlags);
                        isVisible = (rsFlags & RsFlagHidden) == 0;
                        short valType = BitConverter.ToInt16(lphoBuf, (int)CounterOffsetValueType);
                        if (valType == 0)
                        {
                            counterInt = ~BitConverter.ToInt32(lphoBuf, (int)CounterOffsetValueData);
                        }
                        else if (valType == 2)
                        {
                            double rawDbl = BitConverter.ToDouble(lphoBuf, (int)CounterOffsetValueData);
                            counterDbl = rawDbl;
                            counterInt = (int)Math.Round(rawDbl);
                        }
                    }
                    else if (type == (short)ClickteamObjectType.String && read >= 0x22A)
                    {
                        ushort rsFlags = BitConverter.ToUInt16(lphoBuf, (int)StringOffsetRsFlags);
                        isVisible = (rsFlags & RsFlagHidden) == 0;
                        uint txtPtr = BitConverter.ToUInt32(lphoBuf, (int)StringOffsetTextPtr);
                        if (txtPtr >= 0x00120000 && txtPtr < 0x70000000)
                        {
                            stringText = _reader.ReadUnicodeString(txtPtr, 256);
                        }
                    }

                    objects.Add(new ClickteamObjectSnapshot
                    {
                        SlotIndex = (short)slot,
                        ObjectInfoId = oi,
                        Type = type,
                        Name = name,
                        LphoAddress = lpho,
                        X = x,
                        Y = y,
                        ImageWidth = imgW,
                        ImageHeight = imgH,
                        ImageHandle = imgHandle,
                        Bounds = bounds,
                        IsVisible = isVisible,
                        CounterIntValue = counterInt,
                        CounterDoubleValue = counterDbl,
                        StringText = stringText,
                        AnimSequence = animSeq,
                        AnimDirection = animDir,
                        AnimFrame = animFrame
                    });
                }
            }
        }

        // Guard against half-initialized frames right as Clickteam tears down or spawns objects.
        if (nObjects > 0 && objects.Count == 0)
        {
            return null;
        }

        return new ClickteamFrameSnapshot
        {
            FrameIndex = frameIndex,
            FrameName = frameName,
            LevelWidth = levelSx,
            LevelHeight = levelSy,
            WindowX = windowX,
            WindowY = windowY,
            FrameTimerMs = timerMs,
            MainWindowHandle = mainWin,
            EditWindowHandle = editWin,
            Objects = objects
        };
    }

    private short? ResolveAnimSequence(uint hoCommon, uint raAnimPtr)
    {
        if (hoCommon < 0x00120000 || hoCommon >= 0x70000000 ||
            raAnimPtr < 0x00120000 || raAnimPtr >= 0x70000000)
        {
            return null;
        }

        if (!_animTableCache.TryGetValue(hoCommon, out Dictionary<uint, short>? map))
        {
            map = BuildAnimTable(hoCommon, raAnimPtr);
            _animTableCache[hoCommon] = map;
        }

        return map.TryGetValue(raAnimPtr, out short seq) ? seq : null;
    }

    private Dictionary<uint, short> BuildAnimTable(uint hoCommon, uint sampleRaAnimPtr)
    {
        var map = new Dictionary<uint, short>();

        // Read only the compact CObjectCommon header first (0x80 bytes) so we never fail
        // with ERROR_PARTIAL_COPY when hoCommon sits near a virtual memory page boundary.
        byte[] hdr = new byte[0x80];
        if (!_reader.TryReadBytes(hoCommon, hdr, out int read) || read < CommonOffsetAnimHeader + 4)
        {
            return map;
        }

        if (TryReadAnimHeader(hoCommon, CommonOffsetAnimHeader, map) &&
            (sampleRaAnimPtr == 0 || map.ContainsKey(sampleRaAnimPtr)))
        {
            return map;
        }

        map.Clear();
        for (int off = 0; off < Math.Min(0x7E, read - 2); off += 2)
        {
            ushort rel = BitConverter.ToUInt16(hdr, off);
            if (rel > 0x20 && rel < 0x2000 && (sampleRaAnimPtr == 0 || hoCommon + rel < sampleRaAnimPtr))
            {
                var candidate = new Dictionary<uint, short>();
                if (TryReadAnimHeader(hoCommon, rel, candidate) &&
                    (sampleRaAnimPtr == 0 || candidate.ContainsKey(sampleRaAnimPtr)))
                {
                    return candidate;
                }
            }
        }

        return map;
    }

    private bool TryReadAnimHeader(uint hoCommon, int hdrRel, Dictionary<uint, short> map)
    {
        uint ahAddr = hoCommon + (uint)hdrRel;
        byte[] ahPrefix = new byte[4];
        if (!_reader.TryReadBytesExact(ahAddr, ahPrefix))
        {
            return false;
        }

        ushort ahSize = BitConverter.ToUInt16(ahPrefix, 0);
        ushort ahMax = BitConverter.ToUInt16(ahPrefix, 2);
        if (ahMax == 0 || ahMax > 128)
        {
            return false;
        }

        int tableBytes = ahMax * 2;
        byte[] tableBuf = new byte[tableBytes];
        if (!_reader.TryReadBytesExact(ahAddr + 4, tableBuf))
        {
            return false;
        }

        int minOffset = 4 + tableBytes;
        for (int a = 0; a < ahMax; a++)
        {
            ushort aOff = BitConverter.ToUInt16(tableBuf, a * 2);
            if (aOff != 0 && aOff >= minOffset && (ahSize == 0 || aOff < ahSize))
            {
                map[ahAddr + aOff] = (short)a;
            }
        }

        return map.Count > 0;
    }

#if DEBUG
    /// <summary>
    /// Diagnostic helper: dumps all animation sequences (with frame counts &amp; sample image handles)
    /// for a given Active object's LPHO address.
    /// </summary>
    public List<string> DescribeObjectAnimations(uint lphoAddress)
    {
        var lines = new List<string>();
        if (!_reader.TryReadUInt32(lphoAddress + HoOffsetCommon, out uint hoCommon) ||
            hoCommon < 0x00120000 || hoCommon >= 0x70000000)
        {
            return lines;
        }

        _reader.TryReadUInt32(lphoAddress + ActiveOffsetAnimPtr, out uint curAnimPtr);
        Dictionary<uint, short> map = BuildAnimTable(hoCommon, curAnimPtr);
        if (map.Count == 0)
        {
            return lines;
        }

        byte[] animBuf = new byte[64];
        byte[] dirBuf = new byte[128];
        foreach (KeyValuePair<uint, short> kv in map.OrderBy(k => k.Value))
        {
            uint animAddr = kv.Key;
            short seqId = kv.Value;
            if (!_reader.TryReadBytes(animAddr, animBuf, out int aRead) || aRead < 64)
            {
                lines.Add($"seq {seqId}: <unreadable>");
                continue;
            }

            var dirs = new List<string>();
            for (int d = 0; d < 32; d++)
            {
                ushort dOff = BitConverter.ToUInt16(animBuf, d * 2);
                if (dOff == 0 || dOff >= 0x4000)
                {
                    continue;
                }

                if (_reader.TryReadBytes(animAddr + dOff, dirBuf, out int dRead) && dRead >= 8)
                {
                    byte minSpd = dirBuf[0];
                    byte maxSpd = dirBuf[1];
                    short nFrames = BitConverter.ToInt16(dirBuf, 6);
                    if (minSpd <= 100 && maxSpd <= 100 && nFrames > 0 && nFrames <= 128)
                    {
                        var imgs = new List<ushort>();
                        int maxSample = Math.Min((int)nFrames, (dRead - 8) / 2);
                        for (int f = 0; f < Math.Min(maxSample, 16); f++)
                        {
                            imgs.Add(BitConverter.ToUInt16(dirBuf, 8 + f * 2));
                        }

                        string suffix = nFrames > 16 ? ",..." : "";
                        dirs.Add($"d{d}:{nFrames}f[{string.Join(",", imgs)}{suffix}]");
                    }
                }
            }

            if (dirs.Count > 0)
            {
                string curMark = animAddr == curAnimPtr ? "*" : "";
                lines.Add($"seq{seqId}{curMark}({string.Join(" ", dirs)})");
            }
        }

        return lines;
    }
#endif

    public void Dispose() => _reader.Dispose();
}
