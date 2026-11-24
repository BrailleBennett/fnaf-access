using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using fnaf_access.Win32;

namespace fnaf_access.Game;

public readonly record struct RemoteModuleInfo(
    string Name,
    string Path,
    uint BaseAddress,
    uint Size);

public readonly record struct MemoryRegion(
    uint BaseAddress,
    uint Size,
    uint Protect,
    uint Type);

/// <summary>
/// Safe, read-only memory reader targeting a 32-bit Windows game process (e.g., Clickteam Fusion 2.5).
/// Works cleanly when this companion runs as either x64 or x86.
/// </summary>
public sealed class ProcessMemoryReader : IDisposable
{
    private IntPtr _processHandle;
    private bool _disposed;

    public uint ProcessId { get; }
    public bool IsOpen => !_disposed && _processHandle != IntPtr.Zero;

    private ProcessMemoryReader(uint processId, IntPtr processHandle)
    {
        ProcessId = processId;
        _processHandle = processHandle;
    }

    public static ProcessMemoryReader Open(uint processId)
    {
        IntPtr handle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_QUERY_INFORMATION,
            false,
            processId);

        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to open process {processId} for reading.");
        }

        return new ProcessMemoryReader(processId, handle);
    }

    public IReadOnlyList<RemoteModuleInfo> EnumerateModules()
    {
        var results = new List<RemoteModuleInfo>();
        IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(
            NativeMethods.TH32CS_SNAPMODULE | NativeMethods.TH32CS_SNAPMODULE32,
            ProcessId);

        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return results;
        }

        try
        {
            var entry = new NativeMethods.MODULEENTRY32W
            {
                dwSize = (uint)Marshal.SizeOf<NativeMethods.MODULEENTRY32W>()
            };

            if (NativeMethods.Module32FirstW(snapshot, ref entry))
            {
                do
                {
                    ulong addr = (ulong)entry.modBaseAddr.ToInt64();
                    if (addr <= uint.MaxValue)
                    {
                        results.Add(new RemoteModuleInfo(
                            entry.szModule,
                            entry.szExePath,
                            (uint)addr,
                            entry.modBaseSize));
                    }

                    entry.dwSize = (uint)Marshal.SizeOf<NativeMethods.MODULEENTRY32W>();
                }
                while (NativeMethods.Module32NextW(snapshot, ref entry));
            }
        }
        finally
        {
            NativeMethods.CloseHandle(snapshot);
        }

        return results;
    }

    public IReadOnlyList<MemoryRegion> EnumerateReadableRegions(bool writableOnly = false)
    {
        var regions = new List<MemoryRegion>();
        ulong address = 0x10000;
        const ulong max32BitUserAddress = 0x7FFF0000;
        nuint mbiSize = (nuint)Marshal.SizeOf<NativeMethods.MEMORY_BASIC_INFORMATION>();

        while (address < max32BitUserAddress)
        {
            nuint queried = NativeMethods.VirtualQueryEx(
                _processHandle,
                new IntPtr((long)address),
                out NativeMethods.MEMORY_BASIC_INFORMATION mbi,
                mbiSize);

            if (queried == 0)
            {
                break;
            }

            ulong regionBase = (ulong)mbi.BaseAddress.ToInt64();
            ulong regionSize = mbi.RegionSize.ToUInt64();
            if (regionSize == 0)
            {
                break;
            }

            bool isCommitted = mbi.State == NativeMethods.MEM_COMMIT;
            bool isGuardOrNoAccess = (mbi.Protect & (NativeMethods.PAGE_GUARD | NativeMethods.PAGE_NOACCESS)) != 0;
            bool isReadable = (mbi.Protect & (
                NativeMethods.PAGE_READONLY |
                NativeMethods.PAGE_READWRITE |
                NativeMethods.PAGE_WRITECOPY |
                NativeMethods.PAGE_EXECUTE_READ |
                NativeMethods.PAGE_EXECUTE_READWRITE |
                NativeMethods.PAGE_EXECUTE_WRITECOPY)) != 0;

            bool isWritable = (mbi.Protect & (
                NativeMethods.PAGE_READWRITE |
                NativeMethods.PAGE_WRITECOPY |
                NativeMethods.PAGE_EXECUTE_READWRITE |
                NativeMethods.PAGE_EXECUTE_WRITECOPY)) != 0;

            if (isCommitted && !isGuardOrNoAccess && isReadable && (!writableOnly || isWritable) &&
                regionBase <= uint.MaxValue && regionSize <= int.MaxValue)
            {
                regions.Add(new MemoryRegion((uint)regionBase, (uint)regionSize, mbi.Protect, mbi.Type));
            }

            ulong next = regionBase + regionSize;
            if (next <= address)
            {
                break;
            }

            address = next;
        }

        return regions;
    }

    public bool TryReadBytes(uint address, byte[] buffer, out int bytesRead)
    {
        bytesRead = 0;
        if (!IsOpen || address == 0 || buffer.Length == 0)
        {
            return false;
        }

        bool ok = NativeMethods.ReadProcessMemory(
            _processHandle,
            new IntPtr(address),
            buffer,
            (nuint)buffer.Length,
            out nuint read);

        bytesRead = (int)read;
        return ok && bytesRead > 0;
    }

    public bool TryReadBytesExact(uint address, byte[] buffer) =>
        TryReadBytes(address, buffer, out int read) && read == buffer.Length;

    public bool TryReadUInt32(uint address, out uint value)
    {
        byte[] buf = new byte[4];
        if (!TryReadBytesExact(address, buf))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToUInt32(buf, 0);
        return true;
    }

    public bool TryReadInt32(uint address, out int value)
    {
        byte[] buf = new byte[4];
        if (!TryReadBytesExact(address, buf))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToInt32(buf, 0);
        return true;
    }

    public bool TryReadInt16(uint address, out short value)
    {
        byte[] buf = new byte[2];
        if (!TryReadBytesExact(address, buf))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToInt16(buf, 0);
        return true;
    }

    public bool TryReadSingle(uint address, out float value)
    {
        byte[] buf = new byte[4];
        if (!TryReadBytesExact(address, buf))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToSingle(buf, 0);
        return true;
    }

    public bool TryReadDouble(uint address, out double value)
    {
        byte[] buf = new byte[8];
        if (!TryReadBytesExact(address, buf))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToDouble(buf, 0);
        return true;
    }

    public string? ReadAnsiString(uint address, int maxLength = 128)
    {
        byte[] buf = new byte[maxLength];
        if (!TryReadBytes(address, buf, out int read) || read == 0)
        {
            return null;
        }

        int zero = Array.IndexOf(buf, (byte)0, 0, read);
        int len = zero >= 0 ? zero : read;
        return Encoding.ASCII.GetString(buf, 0, len);
    }

    public string? ReadUnicodeString(uint address, int maxChars = 128)
    {
        byte[] buf = new byte[maxChars * 2];
        if (!TryReadBytes(address, buf, out int read) || read < 2)
        {
            return null;
        }

        int byteLen = read - (read % 2);
        for (int i = 0; i < byteLen; i += 2)
        {
            if (buf[i] == 0 && buf[i + 1] == 0)
            {
                byteLen = i;
                break;
            }
        }

        return Encoding.Unicode.GetString(buf, 0, byteLen);
    }

    /// <summary>
    /// Scans matching memory regions for an exact byte sequence (aligned if step &gt; 1).
    /// </summary>
    public List<uint> ScanBytes(
        ReadOnlySpan<byte> pattern,
        bool writableOnly = true,
        int step = 1,
        int maxResults = 256)
    {
        var matches = new List<uint>();
        if (pattern.IsEmpty || !IsOpen)
        {
            return matches;
        }

        byte[] needle = pattern.ToArray();
        IReadOnlyList<MemoryRegion> regions = EnumerateReadableRegions(writableOnly);
        const int chunkLimit = 4 * 1024 * 1024;

        foreach (MemoryRegion region in regions)
        {
            if (region.Size < needle.Length)
            {
                continue;
            }

            uint offset = 0;
            while (offset < region.Size)
            {
                uint toRead = Math.Min((uint)chunkLimit, region.Size - offset);
                byte[] buffer = new byte[toRead];
                if (TryReadBytes(region.BaseAddress + offset, buffer, out int read) && read >= needle.Length)
                {
                    int limit = read - needle.Length;
                    for (int i = 0; i <= limit; i += step)
                    {
                        bool eq = true;
                        for (int j = 0; j < needle.Length; j++)
                        {
                            if (buffer[i + j] != needle[j])
                            {
                                eq = false;
                                break;
                            }
                        }

                        if (eq)
                        {
                            matches.Add(region.BaseAddress + offset + (uint)i);
                            if (matches.Count >= maxResults)
                            {
                                return matches;
                            }
                        }
                    }
                }

                if (toRead < (uint)chunkLimit)
                {
                    break;
                }

                offset += (uint)(chunkLimit - needle.Length + 1);
            }
        }

        return matches;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_processHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_processHandle);
            _processHandle = IntPtr.Zero;
        }
    }
}
