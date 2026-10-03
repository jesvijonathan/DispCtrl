using System.Runtime.InteropServices;

namespace DispCtrl.Core.Settings;

/// <summary>
/// What a file looks like from its metadata alone: enough to tell whether it is
/// still the file a save produced, without opening it.
/// </summary>
/// <remarks>
/// Write time, creation time and length were not enough. Two saves inside one
/// tick of the system clock share a write time, and file system tunnelling gives
/// a file renamed over another the old one's creation time, so a same-length
/// save by another process passed for this process's own (the Control checks
/// failed this way on a release runner). Every save renames a new temporary file
/// into place, and each new file has its own file ID, so the ID tells the saves
/// apart. Windows 11 24H2 reads it by name without opening the file; on older
/// systems the ID is 0 and the times and length decide, as before.
/// </remarks>
internal readonly partial record struct FileStamp(long Id, DateTime Written, DateTime Created, long Length)
{
    public static FileStamp Of(string path)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100) && ById(path) is { } stamp) return stamp;
        var info = new FileInfo(path);
        return info.Exists ? new(0, info.LastWriteTimeUtc, info.CreationTimeUtc, info.Length) : default;
    }

    private static unsafe FileStamp? ById(string path)
    {
        try
        {
            FileStatInformation stat;
            if (!GetFileInformationByName(path, 0, &stat, (uint)sizeof(FileStatInformation))) return null;
            return new(stat.FileId, DateTime.FromFileTimeUtc(stat.LastWriteTime), DateTime.FromFileTimeUtc(stat.CreationTime), stat.EndOfFile);
        }
        catch (EntryPointNotFoundException) { return null; }
        catch (DllNotFoundException) { return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileStatInformation
    {
        public long FileId, CreationTime, LastAccessTime, LastWriteTime, ChangeTime, AllocationSize, EndOfFile;
        public uint FileAttributes, ReparseTag, NumberOfLinks, EffectiveAccess;
    }

    // FILE_INFO_BY_NAME_CLASS 0 is FileStatByNameInfo.
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetFileInformationByName(string path, int infoClass, FileStatInformation* buffer, uint size);
}
