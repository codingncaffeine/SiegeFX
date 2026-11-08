using System.Numerics;
using SiegeFX.Core.Save;
using Silk.NET.Input;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>The staging area's co-op character screens, drawn where the
/// player list sits, from the original's interface files:
/// staging_area_select_character.gas (the list: portrait, name, class, the
/// four skill levels; Create New Character..., Import Character...,
/// Delete Character, Accept, Cancel), staging_area_import_character.gas (a
/// single-player saved game from a drop-down, then its hero),
/// staging_area_create_character.gas (hero name, a turning preview, the
/// Character / Head / Skin / Hair / Shirt / Pants spinners) and mp_dialog.gas
/// (the Yes / No box that asks before a delete). The panel only shows and
/// collects; the game does the reading and writing (off the frame) and feeds
/// the results back.</summary>
internal sealed class MpCharacterPanel
{
    public enum View { Closed, Select, Import, Create }

    public enum Action
    {
        None,
        Accept, Cancel, OpenCreate, OpenImport, DeleteConfirmed,
        CreateAccept, CreateCancel,
        ImportPickSave, ImportAccept, ImportCancel,
        PickerChanged,
    }

    public View Mode { get; private set; } = View.Closed;
    public bool IsOpen => Mode != View.Closed;
    public bool AskingDelete { get; private set; }

    // ---- Select --------------------------------------------------------
    public IReadOnlyList<CoopCharacterEntry> Characters { get; private set; } = Array.Empty<CoopCharacterEntry>();
    public bool Loading { get; set; }
    public int Selected { get; private set; } = -1;
    public CoopCharacterEntry? SelectedEntry => Selected >= 0 && Selected < Characters.Count ? Characters[Selected] : null;
    int _scroll;
    const int Rows = 4;

    // ---- Import --------------------------------------------------------
    public IReadOnlyList<SaveStore.SaveSlot> Saves { get; private set; } = Array.Empty<SaveStore.SaveSlot>();
    public int SaveIndex { get; private set; } = -1;
    public SaveStore.SaveSlot? PickedSave => SaveIndex >= 0 && SaveIndex < Saves.Count ? Saves[SaveIndex] : null;
    /// <summary>The picked save's hero (null while it is read or when it has none).</summary>
    public CoopCharacterEntry? ImportHero { get; set; }
    public string ImportStatus { get; set; } = "";
    bool _comboOpen;
    int _comboScroll;
    const int ComboRows = 11;

    // ---- Create --------------------------------------------------------
    public string NewName { get; private set; } = "";
    public HeroVariantPicker Picker { get; private set; } = new() { Gender = HeroGender.Boy, BodyTypeIdx = -1 };

    /// <summary>The host restricts players to new characters (Game Settings):
    /// only a character not yet played can be chosen, and nothing is imported.</summary>
    public bool NewOnly;
    bool Allowed(CoopCharacterEntry e) => !NewOnly || e.New;
    bool CanAccept => SelectedEntry is { } e && Allowed(e);

    /// <summary>Gender → the hero template's portrait texture (null: no portrait).</summary>
    public Func<string, GlTexture?>? Portrait;

    readonly AuthoredFrame _f = new();
    IconRenderer? _icons;
    string _hover = "", _pressed = "";
    Action _pending = Action.None;
    long _lastClickMs; int _lastClickRow = -1;

    public Action ConsumeAction() { var a = _pending; _pending = Action.None; return a; }

    public void OpenSelect(IReadOnlyList<CoopCharacterEntry>? list = null, string? selectPath = null)
    {
        Mode = View.Select; AskingDelete = false; _comboOpen = false;
        if (list is not null) SetCharacters(list, selectPath);
    }

    public void SetCharacters(IReadOnlyList<CoopCharacterEntry> list, string? selectPath)
    {
        Characters = list;
        Selected = -1;
        for (int i = 0; i < list.Count; i++)
            if (selectPath is not null && string.Equals(list[i].Path, selectPath, StringComparison.Ordinal)) Selected = i;
        if (Selected < 0 && list.Count > 0 && selectPath is null) Selected = 0;
        _scroll = Selected >= Rows ? Selected - Rows + 1 : 0;
        Loading = false;
    }

    public void OpenImport(IReadOnlyList<SaveStore.SaveSlot> saves)
    {
        Mode = View.Import; AskingDelete = false;
        Saves = saves; SaveIndex = -1; ImportHero = null; _comboScroll = 0;
        _comboOpen = saves.Count > 0;
        ImportStatus = saves.Count == 0 ? "No single-player saved games on this computer." : "";
    }

    public void OpenCreate(HeroVariantPicker look)
    {
        Mode = View.Create; AskingDelete = false; _comboOpen = false;
        NewName = ""; Picker = look;
    }

    public void Close() { Mode = View.Closed; AskingDelete = false; _comboOpen = false; _pressed = ""; }

    // ---- input ---------------------------------------------------------

    public void OnMouseMove(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return;
        _f.Layout(vw, vh);
        _hover = HitName(px, py);
    }

    public void OnMouseDown(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return;
        _f.Layout(vw, vh);
        _pressed = HitName(px, py);
        if (AskingDelete) return;
        if (Mode == View.Select && _pressed.StartsWith("row", StringComparison.Ordinal))
        {
            int row = _scroll + int.Parse(_pressed[3..]);
            if (row < Characters.Count)
            {
                long now = Environment.TickCount64;
                bool dbl = row == _lastClickRow && now - _lastClickMs < 450;
                Selected = row; _lastClickRow = row; _lastClickMs = now;
                if (dbl && CanAccept) _pending = Action.Accept;
            }
            _pressed = "";
        }
        else if (Mode == View.Import && _pressed.StartsWith("save", StringComparison.Ordinal))
        {
            int i = _comboScroll + int.Parse(_pressed[4..]);
            if (i < Saves.Count) { SaveIndex = i; ImportHero = null; ImportStatus = "Reading the saved game..."; _pending = Action.ImportPickSave; }
            _comboOpen = false; _pressed = "";
        }
        else if (Mode == View.Import && _comboOpen && _pressed.Length == 0) _comboOpen = false;
    }

    public void OnMouseUp(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return;
        _f.Layout(vw, vh);
        string hit = HitName(px, py);
        string pressed = _pressed; _pressed = "";
        if (hit.Length == 0 || hit != pressed) return;
        if (AskingDelete)
        {
            if (hit == "yes") { AskingDelete = false; _pending = Action.DeleteConfirmed; }
            else if (hit == "no") AskingDelete = false;
            return;
        }
        switch (Mode, hit)
        {
            case (View.Select, "create"): _pending = Action.OpenCreate; break;
            case (View.Select, "import"): if (!NewOnly) _pending = Action.OpenImport; break;
            case (View.Select, "delete"): if (SelectedEntry is not null) AskingDelete = true; break;
            case (View.Select, "accept"): if (CanAccept) _pending = Action.Accept; break;
            case (View.Select, "cancel"): _pending = Action.Cancel; break;
            case (View.Select, "up"): Scroll(-1); break;
            case (View.Select, "down"): Scroll(+1); break;
            case (View.Import, "combo"): _comboOpen = !_comboOpen && Saves.Count > 0; break;
            case (View.Import, "accept"): if (ImportHero is not null) _pending = Action.ImportAccept; break;
            case (View.Import, "cancel"): _pending = Action.ImportCancel; break;
            case (View.Create, "fromsp"): if (!NewOnly) _pending = Action.OpenImport; break;
            case (View.Create, "accept"): if (NewName.Trim().Length > 0) _pending = Action.CreateAccept; break;
            case (View.Create, "cancel"): _pending = Action.CreateCancel; break;
            case (View.Create, _) when hit.StartsWith("spin", StringComparison.Ordinal):
                if (hit[4] != '0' && !HeroBodies.IsFarm(Picker.Gender)) break;   // the dwarf and skeleton keep their look
                Spin(hit[4] - '0', hit[5] == 'r' ? +1 : -1); _pending = Action.PickerChanged; break;
        }
    }

    public void OnWheel(int delta)
    {
        if (Mode == View.Select) Scroll(delta < 0 ? +1 : -1);
        else if (Mode == View.Import && _comboOpen)
            _comboScroll = Math.Clamp(_comboScroll + (delta < 0 ? 1 : -1), 0, Math.Max(0, Saves.Count - ComboRows));
    }

    void Scroll(int d) => _scroll = Math.Clamp(_scroll + d, 0, Math.Max(0, Characters.Count - Rows));

    /// <summary>A typed character for the hero name (the original's edit-box
    /// rule: no &lt;&gt;:/\|?*.%; and at most 14). True when it was taken.</summary>
    public bool OnChar(char c)
    {
        if (Mode != View.Create || AskingDelete) return false;
        if (c == '\b') { if (NewName.Length > 0) NewName = NewName[..^1]; return true; }
        if (c < ' ' || c > '~' || "<>:/\\|?*.%;\"".IndexOf(c) >= 0 || NewName.Length >= 14) return true;
        NewName += c;
        return true;
    }

    /// <summary>A key while the panel is up. True when the panel used it.</summary>
    public bool OnKey(Key key)
    {
        if (!IsOpen) return false;
        if (AskingDelete)
        {
            if (key is Key.Enter or Key.KeypadEnter) { AskingDelete = false; _pending = Action.DeleteConfirmed; }
            else if (key == Key.Escape) AskingDelete = false;
            return true;
        }
        switch (Mode)
        {
            case View.Select:
                if (key == Key.Up && Selected > 0) { Selected--; if (Selected < _scroll) _scroll = Selected; }
                else if (key == Key.Down && Selected < Characters.Count - 1) { Selected++; if (Selected >= _scroll + Rows) _scroll = Selected - Rows + 1; }
                else if (key is Key.Enter or Key.KeypadEnter && CanAccept) _pending = Action.Accept;
                else if (key == Key.Escape) _pending = Action.Cancel;
                else if (key == Key.Delete && SelectedEntry is not null) AskingDelete = true;
                else return false;
                return true;
            case View.Import:
                if (key is Key.Enter or Key.KeypadEnter && ImportHero is not null) _pending = Action.ImportAccept;
                else if (key == Key.Escape) { if (_comboOpen) _comboOpen = false; else _pending = Action.ImportCancel; }
                else return false;
                return true;
            case View.Create:
                if (key == Key.Backspace) OnChar('\b');
                else if (key is Key.Enter or Key.KeypadEnter && NewName.Trim().Length > 0) _pending = Action.CreateAccept;
                else if (key == Key.Escape) _pending = Action.CreateCancel;
                else return false;
                return true;
        }
        return false;
    }

    // Character / Head / Skin / Hair / Shirt / Pants, as the single-player
    // creator maps them (Head = hairstyle, Skin = face, Hair = hair tint).
    void Spin(int row, int d)
    {
        var p = Picker;
        Picker = new HeroVariantPicker
        {
            // Character: the original's multiplayer characters in turn
            // (multiplayer.gas [characters]: farmboy, farmgirl, dwarf, skeleton_good).
            Gender = row == 0 ? (HeroGender)(((int)p.Gender + d + 4) % 4) : p.Gender,
            BodyTypeIdx = p.BodyTypeIdx, SkinSuffix = p.SkinSuffix, HairSuffix = p.HairSuffix, PantsSuffix = p.PantsSuffix,
            StyleIdx = p.StyleIdx + (row == 1 ? d : 0),
            FaceIdx = p.FaceIdx + (row == 2 ? d : 0),
            ColorIdx = p.ColorIdx + (row == 3 ? d : 0),
            ShirtIdx = p.ShirtIdx + (row == 4 ? d : 0),
            PantsIdx = p.PantsIdx + (row == 5 ? d : 0),
        };
    }

    static readonly string[] SpinLabels = { "Character", "Head", "Skin", "Hair", "Shirt", "Pants" };
    static readonly Vector4 WarningRed = new(1f, 0f, 0f, 1f);

    /// <summary>The Create screen's preview box (character_background) in
    /// reference coordinates.</summary>
    public static readonly (int X0, int Y0, int X1, int Y1) PreviewBox = (270, 110, 530, 290);

    string HitName(int px, int py)
    {
        bool H(int x0, int y0, int x1, int y1) => _f.Hit(x0, y0, x1, y1, px, py);
        if (AskingDelete)
            return H(310, 374, 390, 390) ? "yes" : H(410, 374, 490, 390) ? "no" : "";
        switch (Mode)
        {
            case View.Select:
                if (H(346, 76, 546, 92)) return "create";
                if (H(556, 76, 756, 92)) return "import";
                if (H(580, 108, 748, 124)) return "delete";
                if (H(252, 308, 392, 324)) return "accept";
                if (H(408, 308, 548, 324)) return "cancel";
                if (Characters.Count > Rows && H(736, 130, 752, 146)) return "up";
                if (Characters.Count > Rows && H(736, 280, 752, 296)) return "down";
                for (int i = 0; i < Rows; i++) if (H(50, 131 + 40 * i, 730, 170 + 40 * i)) return "row" + i;
                return "";
            case View.Import:
                if (_comboOpen)
                    for (int i = 0; i < Math.Min(ComboRows, Saves.Count - _comboScroll); i++)
                        if (H(270, 122 + 16 * i, 500, 138 + 16 * i)) return "save" + i;
                if (H(270, 102, 500, 122)) return "combo";
                if (H(252, 308, 392, 324)) return "accept";
                if (H(408, 308, 548, 324)) return "cancel";
                return "";
            case View.Create:
                if (H(500, 76, 740, 92)) return "fromsp";
                if (H(252, 308, 392, 324)) return "accept";
                if (H(408, 308, 548, 324)) return "cancel";
                for (int i = 0; i < 6; i++)
                {
                    int y = 128 + 26 * i;
                    if (H(586, y, 602, y + 16)) return $"spin{i}l";
                    if (H(704, y, 720, y + 16)) return $"spin{i}r";
                }
                return "";
        }
        return "";
    }

    AuthoredFrame.Press State(string name) =>
        _pressed == name && _hover == name ? AuthoredFrame.Press.Down
        : _hover == name ? AuthoredFrame.Press.Hover : AuthoredFrame.Press.Up;

    // ---- drawing -------------------------------------------------------

    public void Draw(int vw, int vh, IconRenderer icons, BarRenderer bars, TextRenderer text,
                     Func<string, GlTexture?> tex, Func<string, GlTexture?> common)
    {
        if (!IsOpen) return;
        _icons = icons;
        _f.Begin(vw, vh, icons, bars, text, tex, common);
        switch (Mode)
        {
            case View.Select: DrawSelect(bars); break;
            case View.Import: DrawImport(); break;
            case View.Create: DrawCreate(); break;
        }
    }

    /// <summary>The delete question, over everything (the screen draws it last).</summary>
    public void DrawModal(int vw, int vh, IconRenderer icons, BarRenderer bars, TextRenderer text,
                          Func<string, GlTexture?> tex, Func<string, GlTexture?> common)
    {
        if (!AskingDelete) return;
        _icons = icons;
        _f.Begin(vw, vh, icons, bars, text, tex, common);
        DrawConfirm(bars);
    }

    void DrawSelect(BarRenderer bars)
    {
        _f.Label(44, 76, 200, 92, "Select Character", "14p");
        _f.Button(346, 76, 546, 92, "Create New Character...", State("create"));
        _f.Button(556, 76, 756, 92, "Import Character...", State("import"), !NewOnly);
        _f.Box(44, 100, 756, 300);
        _f.Label(50, 108, 240, 124, "Character", "14p");
        _f.Label(245, 108, 370, 124, "Class", "14p");
        _f.Label(375, 108, 600, 124, "Skills", "14p");
        _f.Button(580, 108, 748, 124, "Delete Character", State("delete"), SelectedEntry is not null);

        for (int i = 0; i < Rows; i++)
        {
            int idx = _scroll + i;
            int y = 131 + 40 * i;
            _f.Window(50, y + 3, 730, y + 35, "b_gui_fe_m_mp_staging_player_bg", i % 2 == 0 ? 0.3f : 0.2f);
            if (idx >= Characters.Count) continue;
            var c = Characters[idx];
            // character<n>_portrait: the hero template's portrait, cropped by
            // the authored uvcoords 0,0.28125,0.609375,1.0 (bottom-up).
            if (Portrait?.Invoke(c.Body) is { } portrait && _icons is not null)
            {
                var r = _f.S(59, y + 3, 86, y + 35);
                _icons.DrawIcon(_f.Vw, _f.Vh, portrait, r.X, r.Y, r.W, r.H, Vector4.One, 0f, 0f, 0.609375f, 1f - 0.28125f);
            }
            var ink = Allowed(c) ? AuthoredFrame.White : AuthoredFrame.Disabled;
            _f.Label(90, y + 11, 240, y + 27, c.Name, "", ink);
            _f.Label(250, y + 11, 370, y + 27, c.ClassName, "", ink);
            _f.Label(380, y + 11, 530, y + 27, c.New ? "New" : $"Level {c.Level}", "", ink);
            Skill(530, y + 11, "b_gui_ig_mnu_combat", c.Melee);
            Skill(580, y + 11, "b_gui_ig_mnu_ranged", c.Ranged);
            Skill(630, y + 11, "b_gui_ig_mnu_nature-magic", c.NatureMagic);
            Skill(680, y + 11, "b_gui_ig_mnu_combat-magic", c.CombatMagic);
            if (idx == Selected)
            {
                // character<n>_sel: has_border, border_color 0xFF00FF00.
                var s = _f.S(50, y, 730, y + 39);
                bars.DrawBorder(_f.Vw, _f.Vh, s.X, s.Y, s.W, s.H, AuthoredFrame.Green);
            }
        }
        if (Loading) _f.Label(50, 131, 730, 290, "Reading your co-op characters...", "", AuthoredFrame.Faint, AuthoredFrame.Justify.Center);
        else if (Characters.Count == 0)
        {
            _f.Label(50, 171, 730, 190, "You have no co-op characters yet.", "", AuthoredFrame.Faint, AuthoredFrame.Justify.Center);
            _f.Label(50, 191, 730, 210, "Create a new one, or import a hero from a single-player game.", "", AuthoredFrame.Faint, AuthoredFrame.Justify.Center);
        }
        if (Characters.Count > Rows) DrawSlider(_scroll, Characters.Count - Rows);
        if (NewOnly)
            _f.Label(44, 300, 756, 308, "The host has chosen to only allow new characters.", "", AuthoredFrame.Faint, AuthoredFrame.Justify.Center);

        _f.Button(252, 308, 392, 324, "Accept", State("accept"), CanAccept);
        _f.Button(408, 308, 548, 324, "Cancel", State("cancel"));
    }

    void Skill(int x, int y, string icon, int level)
    {
        _f.Window(x, y, x + 16, y + 16, icon);
        _f.Label(x + 20, y, x + 50, y + 16, level.ToString());
    }

    // The common slider: the track (slider_bg), its thumb (slider_button_up
    // top/mid/bottom) and the up/down arrows at its ends.
    void DrawSlider(int pos, int max)
    {
        _f.Window(736, 146, 752, 280, "b_gui_cmn_slider_bg");
        int span = 280 - 146, thumb = Math.Max(16, span * Rows / Math.Max(Rows, Characters.Count));
        int ty = 146 + (max <= 0 ? 0 : (span - thumb) * pos / max);
        _f.Window(736, ty, 752, ty + 6, "b_gui_cmn_slider_button_up_top");
        _f.Window(736, ty + 6, 752, ty + thumb - 6, "b_gui_cmn_slider_button_up_mid");
        _f.Window(736, ty + thumb - 6, 752, ty + thumb, "b_gui_cmn_slider_button_up_bottom");
        _f.Arrow(736, 130, 752, 146, "up", State("up"));
        _f.Arrow(736, 280, 752, 296, "down", State("down"));
    }

    void DrawImport()
    {
        _f.Label(44, 76, 200, 92, "Import Character", "14p");
        _f.Label(270, 80, 500, 96, "Select Saved Game");
        _f.Label(270, 134, 530, 150, "Select Character");
        _f.Box(270, 154, 530, 290);
        if (ImportHero is { } h)
        {
            _f.Selection(273, 157, 527, 175, 0.5f);
            _f.Label(277, 157, 523, 175, $"{h.Name}, level {h.Level} {h.ClassName}");
        }
        else if (ImportStatus.Length > 0)
            _f.Label(277, 160, 523, 284, ImportStatus, "", AuthoredFrame.Faint, AuthoredFrame.Justify.Center);
        _f.Button(252, 308, 392, 324, "Accept", State("accept"), ImportHero is not null);
        _f.Button(408, 308, 548, 324, "Cancel", State("cancel"));

        // combo_import_save_games: the box, the picked save, the down button;
        // open, the list drops below it over everything else.
        _f.Box(270, 102, 500, 122);
        _f.Label(274, 104, 482, 120, PickedSave is { } ps ? SaveLabel(ps) : "Choose a saved game");
        _f.Arrow(482, 104, 498, 120, "down", State("combo"));
        if (_comboOpen)
        {
            int n = Math.Min(ComboRows, Saves.Count - _comboScroll);
            _f.Box(270, 122, 500, 124 + 16 * n, "cpbox_thin_dark");
            for (int i = 0; i < n; i++)
            {
                int idx = _comboScroll + i, y = 122 + 16 * i;
                if (_hover == "save" + i) _f.Selection(272, y + 1, 498, y + 17, 0.8f);
                _f.Label(276, y + 1, 494, y + 17, SaveLabel(Saves[idx]));
            }
        }
    }

    static string SaveLabel(SaveStore.SaveSlot s) =>
        string.IsNullOrWhiteSpace(s.HeroName) || s.DisplayName.Contains(s.HeroName, StringComparison.OrdinalIgnoreCase)
            ? s.DisplayName : $"{s.DisplayName} ({s.HeroName})";

    void DrawCreate()
    {
        _f.Label(44, 76, 200, 92, "Create New Character", "14p");
        _f.Button(500, 76, 740, 92, "Import From Single Player...", State("fromsp"), !NewOnly);
        // create_new_character_warning (font_color 0x00FF0000): shown when the
        // host requires new characters.
        if (NewOnly)
        {
            _f.Label(220, 72, 490, 86, "Host requires new characters. If you want to play with", "", WarningRed, AuthoredFrame.Justify.Center);
            _f.Label(220, 86, 490, 100, "an existing character, join a different game", "", WarningRed, AuthoredFrame.Justify.Center);
        }
        _f.Label(60, 168, 234, 184, "Enter Hero Name", "", null, AuthoredFrame.Justify.Center);
        _f.Box(60, 188, 234, 212);
        bool caret = (Environment.TickCount64 / 500) % 2 == 0;
        _f.Label(64, 192, 230, 208, NewName + (caret ? "_" : " "));
        _f.Box(PreviewBox.X0, PreviewBox.Y0, PreviewBox.X1, PreviewBox.Y1);   // the preview turns inside it
        _f.Box(566, 110, 740, 290);
        for (int i = 0; i < 6; i++)
        {
            int y = 128 + 26 * i;
            // disable_color 0x99555555: Head..Pants grey out for a body with
            // no such choices (the dwarf, the skeleton).
            bool on = i == 0 || HeroBodies.IsFarm(Picker.Gender);
            _f.Arrow(586, y, 602, y + 16, "left", State($"spin{i}l"), on);
            _f.Label(602, y, 704, y + 16, SpinLabels[i], "", on ? null : AuthoredFrame.Disabled, AuthoredFrame.Justify.Center);
            _f.Arrow(704, y, 720, y + 16, "right", State($"spin{i}r"), on);
        }
        _f.Button(252, 308, 392, 324, "Accept", State("accept"), NewName.Trim().Length > 0);
        _f.Button(408, 308, 548, 324, "Cancel", State("cancel"));
    }

    // mp_dialog.gas (authored in the 640x480 frame, centered here: +80,+60):
    // a cpbox_thin_dark box, the question, Yes / No.
    void DrawConfirm(BarRenderer bars)
    {
        var all = _f.S(0, 0, 800, 600);
        bars.DrawRect(_f.Vw, _f.Vh, all.X, all.Y, all.W, all.H, new Vector4(0f, 0f, 0f, 0.45f));
        _f.Box(225, 215, 575, 410, "cpbox_thin_dark");
        _f.Label(238, 240, 562, 258, "Are you sure you want to delete this character?", "", null, AuthoredFrame.Justify.Center);
        if (SelectedEntry is { } c)
            _f.Label(238, 266, 562, 284, c.New ? c.Name : $"{c.Name}, level {c.Level} {c.ClassName}", "", AuthoredFrame.Faint, AuthoredFrame.Justify.Center);
        _f.Label(238, 296, 562, 314, "This cannot be undone.", "", AuthoredFrame.Faint, AuthoredFrame.Justify.Center);
        _f.Button(310, 374, 390, 390, "Yes", State("yes"));
        _f.Button(410, 374, 490, 390, "No", State("no"));
    }

}
