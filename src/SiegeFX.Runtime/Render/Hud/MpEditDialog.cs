using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>The original's one-line entry dialogs, by their authored files:
/// enter_password.gas (a joiner's password for a protected game) and
/// staging_area_change_password.gas (the host's Change Game Password...).
/// Shared by the game lists and the staging area; the owner opens it, feeds it
/// typed characters and reads <see cref="Text"/> on OK.</summary>
internal sealed class MpEditDialog
{
    public enum Kind { EnterPassword, ChangePassword }
    public enum Result { None, Ok, Cancel }

    public Kind Mode { get; private set; }
    public bool IsOpen { get; private set; }
    public string Text { get; private set; } = "";
    public const int MaxLength = 24;

    readonly AuthoredFrame _f = new();
    string _hover = "", _pressed = "";
    Result _pending = Result.None;
    public Result Consume() { var r = _pending; _pending = Result.None; return r; }

    public void Open(Kind kind, string text = "")
    {
        Mode = kind; IsOpen = true; Text = text; _pending = Result.None; _pressed = "";
    }

    public void Close() { IsOpen = false; _pressed = ""; }

    (int X0, int Y0, int X1, int Y1) OkRect => Mode == Kind.EnterPassword ? (290, 324, 390, 340) : (310, 254, 390, 270);
    (int X0, int Y0, int X1, int Y1) CancelRect => Mode == Kind.EnterPassword ? (410, 324, 510, 340) : (410, 254, 490, 270);

    string HitName(int px, int py)
    {
        var ok = OkRect; var cancel = CancelRect;
        if (_f.Hit(ok.X0, ok.Y0, ok.X1, ok.Y1, px, py)) return "ok";
        if (_f.Hit(cancel.X0, cancel.Y0, cancel.X1, cancel.Y1, px, py)) return "cancel";
        return "";
    }

    AuthoredFrame.Press State(string n) => _pressed == n && _hover == n ? AuthoredFrame.Press.Down
        : _hover == n ? AuthoredFrame.Press.Hover : AuthoredFrame.Press.Up;

    public void OnMouseMove(int px, int py, int vw, int vh) { if (!IsOpen) return; _f.Layout(vw, vh); _hover = HitName(px, py); }
    public void OnMouseDown(int px, int py, int vw, int vh) { if (!IsOpen) return; _f.Layout(vw, vh); _pressed = HitName(px, py); }
    public void OnMouseUp(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return;
        _f.Layout(vw, vh);
        string hit = HitName(px, py);
        if (hit.Length > 0 && hit == _pressed) _pending = hit == "ok" ? Result.Ok : Result.Cancel;
        _pressed = "";
    }

    /// <summary>A typed character; '\b' erases, '\r' is OK.</summary>
    public void OnChar(char c)
    {
        if (!IsOpen) return;
        if (c == '\b') { if (Text.Length > 0) Text = Text[..^1]; return; }
        if (c == '\r') { _pending = Result.Ok; return; }
        if (c >= ' ' && c != '|' && Text.Length < MaxLength) Text += c;
    }

    public void Draw(int vw, int vh, IconRenderer icons, BarRenderer bars, TextRenderer text,
                     Func<string, GlTexture?> tex, Func<string, GlTexture?> common)
    {
        if (!IsOpen) return;
        _f.Begin(vw, vh, icons, bars, text, tex, common);
        bool caret = (Environment.TickCount64 / 500) % 2 == 0;
        if (Mode == Kind.EnterPassword)
        {
            // enter_password.gas: the entry is masked.
            _f.Box(250, 240, 550, 360, "cpbox_wide");
            _f.Label(270, 260, 530, 276, "Enter Password");
            _f.Box(270, 289, 530, 311);
            _f.Label(273, 292, 527, 308, new string('*', Text.Length) + (caret ? "_" : ""));
        }
        else
        {
            // staging_area_change_password.gas: the host's own password, shown.
            _f.Box(280, 170, 520, 290, "cpbox_thin_dark");
            _f.Label(300, 190, 500, 210, "Change Game Password");
            _f.Box(300, 222, 500, 242);
            _f.Label(304, 224, 496, 240, Text + (caret ? "_" : ""));
        }
        var ok = OkRect; var cancel = CancelRect;
        _f.Button(ok.X0, ok.Y0, ok.X1, ok.Y1, "OK", State("ok"));
        _f.Button(cancel.X0, cancel.Y0, cancel.X1, cancel.Y1, "Cancel", State("cancel"));
    }
}
