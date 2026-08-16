using System.Runtime.InteropServices;
using System.Text;

namespace Fpe2001Remake.ByteSources.File;

/// <summary>
/// 文件版本标记（规格 §3）：路径规范化 + 长度 + LastWriteUtc + FileId。
/// 任何提交前必须复验。
/// </summary>
public static class FileVersionToken
{
    public static string Compute(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        var fileId = GetFileId(fullPath);
        return $"{fullPath}|len:{info.Length:X16}|mtime:{info.LastWriteTimeUtc:o}|id:{fileId}";
    }

    private static string GetFileId(string path)
    {
        try
        {
            var handle = CreateFile(
                path,
                FileReadAttributes,
                ShareReadWriteDelete,
                IntPtr.Zero,
                OpenExisting,
                0x02000000 /* FILE_FLAG_BACKUP_SEMANTICS */,
                IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return "0";
            }
            try
            {
                if (!GetFileInformationByHandleEx(handle, FileIdInfoClass, out var info, (uint)Marshal.SizeOf<FILE_ID_INFO>()))
                {
                    return "0";
                }
                var id = info.VolumeSerialNumber.ToString("X8")
                         + info.FileId.Identifier[0].ToString("X16")
                         + info.FileId.Identifier[1].ToString("X16");
                return id;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
        catch
        {
            return "0";
        }
    }

    // ---------- Win32（长路径/FileId） ----------

    private const uint FileReadAttributes = 0x00000080; // FILE_READ_ATTRIBUTES
    private const uint ShareReadWriteDelete = 0x00000007; // READ|WRITE|DELETE
    private const uint OpenExisting = 3;                  // OPEN_EXISTING
    private const int FileIdInfoClass = 18;               // FileIdInfo

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ID_INFO
    {
        public ulong VolumeSerialNumber;
        public FILE_ID_128 FileId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ID_128
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] Identifier;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(
        IntPtr hFile, int FileInformationClass, out FILE_ID_INFO lpFileInformation, uint dwBufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
