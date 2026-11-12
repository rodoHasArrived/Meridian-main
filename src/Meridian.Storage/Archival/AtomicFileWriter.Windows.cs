using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Meridian.Storage.Archival;

public static partial class AtomicFileWriter
{
    // ReplaceFile can rename the old destination to its backup before publishing the new
    // name. A single FileRenameInfoEx operation instead keeps the destination continuously
    // visible, including while readers retain a deletion-sharing handle to its old generation.
    [SupportedOSPlatform("windows")]
    private sealed class WindowsRename : IDisposable
    {
        private readonly SafeFileHandle _source;
        private readonly SafeFileHandle? _destination;
        private readonly IntPtr _information;
        private readonly uint _informationLength;

        private WindowsRename(
            SafeFileHandle source, SafeFileHandle? destination, IntPtr information, uint informationLength)
        {
            _source = source;
            _destination = destination;
            _information = information;
            _informationLength = informationLength;
        }

        public static WindowsRename Prepare(string sourcePath, string destinationPath)
        {
            SafeFileHandle? destination = null;
            SafeFileHandle? source = null;
            var information = IntPtr.Zero;
            try
            {
                var fullDestination = Path.GetFullPath(destinationPath);
                // Retain DELETE access until commit. This also preserves the sharing contract
                // of readers that deliberately deny deletion, independently of POSIX flags.
                destination = WindowsInterop.CreateFile(ExtendedPath(fullDestination), DeleteAccess,
                    ShareAll, IntPtr.Zero, OpenExisting, NormalAttributes, IntPtr.Zero);
                if (destination.IsInvalid)
                {
                    var error = Marshal.GetLastPInvokeError();
                    destination.Dispose();
                    destination = null;
                    if (error != FileNotFound)
                        ThrowWindowsPublicationError(error);
                }
                else if ((File.GetAttributes(fullDestination) & FileAttributes.ReadOnly) != 0)
                {
                    ThrowWindowsPublicationError(AccessDenied);
                }

                source = WindowsInterop.CreateFile(ExtendedPath(Path.GetFullPath(sourcePath)), DeleteAccess,
                    ShareAll, IntPtr.Zero, OpenExisting, NormalAttributes, IntPtr.Zero);
                if (source.IsInvalid)
                    ThrowWindowsPublicationError(Marshal.GetLastPInvokeError());

                var name = ExtendedPath(fullDestination).ToCharArray();
                // FILE_RENAME_INFO contains DWORD Flags, an aligned HANDLE RootDirectory,
                // DWORD FileNameLength, then UTF-16 characters (20/12-byte header on 64/32-bit).
                var rootOffset = IntPtr.Size;
                var lengthOffset = rootOffset + IntPtr.Size;
                var nameOffset = lengthOffset + sizeof(uint);
                var nameLength = checked(name.Length * sizeof(char));
                var informationLength = checked(nameOffset + nameLength + sizeof(char));
                information = Marshal.AllocHGlobal(informationLength);
                Marshal.WriteInt32(information, 0, ReplaceIfExists | PosixSemantics);
                if (IntPtr.Size == 8)
                    Marshal.WriteInt32(information, sizeof(uint), 0);
                Marshal.WriteIntPtr(information, rootOffset, IntPtr.Zero);
                Marshal.WriteInt32(information, lengthOffset, nameLength);
                Marshal.Copy(name, 0, IntPtr.Add(information, nameOffset), name.Length);
                Marshal.WriteInt16(information, nameOffset + nameLength, 0);
                return new WindowsRename(source, destination, information, (uint)informationLength);
            }
            catch
            {
                source?.Dispose();
                destination?.Dispose();
                if (information != IntPtr.Zero)
                    Marshal.FreeHGlobal(information);
                throw;
            }
        }

        public void Publish()
        {
            if (WindowsInterop.SetFileInformationByHandle(
                    _source, FileRenameInfoEx, _information, _informationLength) == 0)
                ThrowWindowsPublicationError(Marshal.GetLastPInvokeError());
        }

        public void Dispose()
        {
            _source.Dispose();
            _destination?.Dispose();
            Marshal.FreeHGlobal(_information);
        }

        private static string ExtendedPath(string path)
            => path.StartsWith(@"\\?\", StringComparison.Ordinal)
                ? path
                : path.StartsWith(@"\\", StringComparison.Ordinal)
                    ? @"\\?\UNC\" + path[2..]
                    : @"\\?\" + path;

        private const uint DeleteAccess = 0x00010000;
        private const uint ShareAll = 0x00000007;
        private const uint OpenExisting = 3;
        private const uint NormalAttributes = 0x00000080;
        private const int FileNotFound = 2;
        private const int AccessDenied = 5;
        private const int ReplaceIfExists = 0x00000001;
        private const int PosixSemantics = 0x00000002;
        private const int FileRenameInfoEx = 22;

        private static void ThrowWindowsPublicationError(int error)
        {
            if (error == AccessDenied)
                throw new UnauthorizedAccessException("Atomic file publication was denied.");
            // Retain the Win32 code for bounded sharing retries without exposing path names.
            throw new IOException("Atomic file publication failed.", unchecked((int)(0x80070000u | (uint)error)));
        }
    }

    private static partial class WindowsInterop
    {
        [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        internal static partial SafeFileHandle CreateFile(string path, uint access, uint share,
            IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr template);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial int SetFileInformationByHandle(
            SafeFileHandle file, int informationClass, IntPtr information, uint informationLength);
    }
}
