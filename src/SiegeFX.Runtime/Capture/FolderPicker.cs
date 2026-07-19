using System.Runtime.InteropServices;

namespace SiegeFX.Runtime.Capture;

/// <summary>SC-RECORD — native folder chooser (the modern IFileOpenDialog
/// in FOS_PICKFOLDERS mode) for the Advanced tab's capture-folder rows.
/// Runs on a dedicated STA thread: the GLFW render thread has no COM
/// apartment and doesn't pump while blocked, so the dialog is shown
/// OWNERLESS — an owned modal would wait on the owner's (frozen) message
/// loop and deadlock. The render loop simply holds its last frame until
/// the user picks or cancels.</summary>
internal static class FolderPicker
{
    /// <summary>Show the picker. Returns the chosen folder path, or null
    /// on cancel/failure.</summary>
    public static string? Pick(string title, string? initialDir)
    {
        string? result = null;
        var t = new Thread(() =>
        {
            try { result = PickCore(title, initialDir); }
            catch { result = null; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join();
        return result;
    }

    static string? PickCore(string title, string? initialDir)
    {
        const uint FOS_PICKFOLDERS = 0x20;
        const uint FOS_FORCEFILESYSTEM = 0x40;
        const uint SIGDN_FILESYSPATH = 0x80058000;

        var dialog = (IFileDialog)new FileOpenDialogRcw();
        try
        {
            dialog.GetOptions(out uint opts);
            dialog.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);
            dialog.SetTitle(title);
            if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
            {
                var iid = typeof(IShellItem).GUID;
                if (SHCreateItemFromParsingName(initialDir, 0, ref iid, out var start) == 0)
                    dialog.SetFolder(start);
            }
            if (dialog.Show(0) != 0) return null; // cancelled
            dialog.GetResult(out var item);
            item.GetDisplayName(SIGDN_FILESYSPATH, out nint pszPath);
            try { return Marshal.PtrToStringUni(pszPath); }
            finally { Marshal.FreeCoTaskMem(pszPath); }
        }
        finally { Marshal.ReleaseComObject(dialog); }
    }

    [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    class FileOpenDialogRcw { }

    // IModalWindow + IFileDialog vtable, in declaration order — only the
    // methods before the ones we call need correct SLOTS, not signatures,
    // so unused entries stay as opaque placeholders.
    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileDialog
    {
        [PreserveSig] int Show(nint hwndOwner);
        void SetFileTypes(uint cFileTypes, nint rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(nint pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName(out nint pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(nint pFilter);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(nint pbc, ref Guid bhid, ref Guid riid, out nint ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out nint ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateItemFromParsingName(string pszPath, nint pbc,
        ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);
}
