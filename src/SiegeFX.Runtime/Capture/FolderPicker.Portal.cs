using System.Text;
using Tmds.DBus.Protocol;

namespace SiegeFX.Runtime.Capture;

/// <summary>SC-RECORD — folder chooser for the builds without the Windows
/// shell dialog: the desktop's file-chooser portal
/// (org.freedesktop.portal.FileChooser), which shows the desktop's own dialog
/// (KDE's on Plasma, GTK's on GNOME) and also works inside a sandbox. The
/// portal answers asynchronously with a Response signal on a request
/// object, so the game keeps drawing while the dialog is up.</summary>
internal static class FolderPicker
{
    const string PortalService = "org.freedesktop.portal.Desktop";
    const string PortalPath = "/org/freedesktop/portal/desktop";

    /// <summary>Shows the dialog and returns the chosen folder, or null on
    /// cancel. Throws when there is no session bus or no portal.</summary>
    public static async Task<string?> PickAsync(string title, string? initialDir)
    {
        string address = DBusAddress.Session
            ?? throw new InvalidOperationException("no D-Bus session bus");
        using var connection = new DBusConnection(address);
        await connection.ConnectAsync();

        // The request object's path is predictable from our bus name and a
        // token we choose, so the Response signal is subscribed BEFORE the
        // call and a fast answer cannot slip past.
        string token = "siegefx" + Guid.NewGuid().ToString("N");
        string sender = (connection.UniqueName ?? throw new InvalidOperationException("no bus name"))
            .TrimStart(':').Replace('.', '_');
        var rule = new MatchRule
        {
            Type = MessageType.Signal,
            Interface = "org.freedesktop.portal.Request",
            Member = "Response",
            Path = $"{PortalPath}/request/{sender}/{token}",
        };
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watch = await connection.AddMatchAsync(rule,
            (Message m, object? _) => ReadResponse(m),
            (Notification<string?> n) =>
            {
                if (n.Exception is not null) answer.TrySetException(n.Exception);
                else if (n.HasValue) answer.TrySetResult(n.Value);
                else if (n.IsCompletion) answer.TrySetResult(null);
            },
            false, ObserverFlags.EmitOnConnectionClosed | ObserverFlags.EmitOnConnectionFailed, null);

        MessageBuffer call;
        using (var w = connection.GetMessageWriter())
        {
            w.WriteMethodCallHeader(PortalService, PortalPath,
                "org.freedesktop.portal.FileChooser", "OpenFile", "ssa{sv}");
            w.WriteString("");      // no parent window identifier
            w.WriteString(title);
            var options = w.WriteDictionaryStart();
            w.WriteDictionaryEntryStart();
            w.WriteString("handle_token");
            w.WriteVariantString(token);
            w.WriteDictionaryEntryStart();
            w.WriteString("directory");
            w.WriteVariantBool(true);
            w.WriteDictionaryEntryStart();
            w.WriteString("modal");
            w.WriteVariantBool(true);
            if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
            {
                // current_folder is a NUL-terminated byte string (variant "ay").
                w.WriteDictionaryEntryStart();
                w.WriteString("current_folder");
                w.WriteSignature("ay");
                w.WriteArray(Encoding.UTF8.GetBytes(initialDir + '\0'));
            }
            w.WriteDictionaryEnd(options);
            call = w.CreateMessage();
        }
        await connection.CallMethodAsync(call);
        return await answer.Task;
    }

    /// <summary>Response(u response, a{sv} results): 0 = picked, 1 = the
    /// user cancelled, 2 = the dialog ended another way.</summary>
    static string? ReadResponse(Message m)
    {
        var reader = m.GetBodyReader();
        uint response = reader.ReadUInt32();
        var results = reader.ReadDictionaryOfStringToVariantValue();
        if (response != 0 || !results.TryGetValue("uris", out var uris) || uris.Count == 0)
            return null;
        return PathFromUri(uris.GetItem(0).GetString());
    }

    /// <summary>The local path of a file:// URI (percent-decoded), or null
    /// for anything else.</summary>
    internal static string? PathFromUri(string uri)
        => Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.IsFile ? u.LocalPath : null;
}
