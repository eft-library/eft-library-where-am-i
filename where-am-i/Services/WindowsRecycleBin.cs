using System.IO;
using System.Runtime.InteropServices;

namespace where_am_i.Services;

internal static class WindowsRecycleBin
{
    private const uint RecycleOnDelete = 0x00080000;
    private const uint EarlyFailure = 0x00100000;
    private const uint AddUndoRecord = 0x20000000;
    private const uint NoUi = 0x0614;
    public static void MoveFile(string path)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2)) throw new IOException("Recycle-only operations require Windows 8 or later.");
        IFileOperation? operation = null;
        IShellItem? item = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), throwOnError: true)!;
            operation = (IFileOperation)Activator.CreateInstance(type)!;
            // RECYCLEONDELETE + EARLYFAILURE + no UI. No permanent-delete fallback or elevation prompt.
            operation.SetOperationFlags(RecycleOnDelete | EarlyFailure | AddUndoRecord | NoUi);
            var iid = typeof(IShellItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item));
            operation.DeleteItem(item, IntPtr.Zero);
            operation.PerformOperations();
            operation.GetAnyOperationsAborted(out var aborted);
            if (aborted || File.Exists(path)) throw new IOException("Recycle operation was not completed.");
        }
        finally
        {
            if (item is not null) Marshal.ReleaseComObject(item);
            if (operation is not null) Marshal.ReleaseComObject(operation);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr binding, ref Guid iid, out IShellItem item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { }

    // Native IFileOperation vtable order; unused methods are declared to preserve slot positions.
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IntPtr sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(IntPtr dialog);
        void SetProperties(IntPtr changes);
        void SetOwnerWindow(IntPtr owner);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems(IntPtr items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        void RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IntPtr sink);
        void MoveItems(IntPtr items, IShellItem destination);
        void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IntPtr sink);
        void CopyItems(IntPtr items, IShellItem destination);
        void DeleteItem(IShellItem item, IntPtr sink);
        void DeleteItems(IntPtr items);
        void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, IntPtr sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}
