using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SiegeSmith.Controls;

/// <summary>#35 — the syntax-colored code editor used by the GAS viewer, the Skrit
/// editor and the Effects Lab. A plain TextBox keeps ALL editing behaviour (caret,
/// selection, undo, IME); its glyphs are transparent, and a colour layer with the
/// SAME monospace metrics renders beneath, kept in sync with text + scroll. That
/// keeps the implementation tiny and dependency-free while looking like a real
/// code editor. Documents past 300 KB fall back to plain text (the layer is a
/// single TextBlock — recolouring a megabyte of runs would stall typing).</summary>
public sealed class CodeEditor : Grid
{
    // palette — tuned for Themes/Dark.xaml (#1E1E1E family)
    private static readonly Brush BrDefault = Freeze(0xFFD6D6D0);
    private static readonly Brush BrComment = Freeze(0xFF6E9955);
    private static readonly Brush BrHeader  = Freeze(0xFFD8A657); // [t:…] headers / keywords — forge bronze
    private static readonly Brush BrAttr    = Freeze(0xFF9CDCFE);
    private static readonly Brush BrString  = Freeze(0xFFCE9178);
    private static readonly Brush BrNumber  = Freeze(0xFFB5CEA8);
    private static readonly Brush BrHandle  = Freeze(0xFF4EC9B0); // #SOURCE / $vars
    private static readonly Brush BrPunct   = Freeze(0xFF8A8F98);

    private const int PlainFallbackLength = 300_000;

    private readonly TextBox _box;
    private readonly TextBlock _layer;
    private readonly TranslateTransform _scroll = new();
    private readonly System.Windows.Threading.DispatcherTimer _recolor;
    private bool _syncingText;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(CodeEditor),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, e) => ((CodeEditor)d).OnTextDpChanged((string)e.NewValue)));

    public static readonly DependencyProperty SyntaxProperty = DependencyProperty.Register(
        nameof(Syntax), typeof(string), typeof(CodeEditor),
        new PropertyMetadata("gas", (d, _) => ((CodeEditor)d).Recolor()));

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(CodeEditor),
        new PropertyMetadata(false, (d, e) => ((CodeEditor)d)._box.IsReadOnly = (bool)e.NewValue));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Syntax { get => (string)GetValue(SyntaxProperty); set => SetValue(SyntaxProperty, value); }
    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }

    public CodeEditor()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x19, 0x1A, 0x1D));
        ClipToBounds = true;

        _layer = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12.5,
            Foreground = BrDefault,
            Margin = new Thickness(7, 4, 0, 0), // TextBox border(1) + padding(6,3)
            IsHitTestVisible = false,
            RenderTransform = _scroll,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        TextOptions.SetTextFormattingMode(_layer, TextFormattingMode.Display);

        _box = new TextBox
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12.5,
            Foreground = Brushes.Transparent,
            CaretBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE2)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 3, 6, 3),
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        TextOptions.SetTextFormattingMode(_box, TextFormattingMode.Display);

        Children.Add(_layer);
        Children.Add(_box);

        _recolor = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(140),
        };
        _recolor.Tick += (_, _) => { _recolor.Stop(); Recolor(); };

        _box.TextChanged += (_, _) =>
        {
            if (!_syncingText)
            {
                _syncingText = true;
                SetCurrentValue(TextProperty, _box.Text);
                _syncingText = false;
            }
            _recolor.Stop();
            _recolor.Start();
        };
        _box.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrolled));
    }

    private void OnScrolled(object sender, ScrollChangedEventArgs e)
    {
        _scroll.X = -e.HorizontalOffset;
        _scroll.Y = -e.VerticalOffset;
    }

    private void OnTextDpChanged(string value)
    {
        if (_syncingText) return;
        _syncingText = true;
        _box.Text = value ?? "";
        _syncingText = false;
        Recolor();
    }

    private void Recolor()
    {
        var text = _box.Text;
        _layer.Inlines.Clear();
        if (string.IsNullOrEmpty(text)) return;
        if (text.Length > PlainFallbackLength)
        {
            _layer.Inlines.Add(new Run(text) { Foreground = BrDefault });
            return;
        }
        foreach (var (span, brush) in Tokenize(text, Syntax))
            _layer.Inlines.Add(new Run(span) { Foreground = brush });
    }

    private static Brush Freeze(uint argb)
    {
        var b = new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return b;
    }

    // ── tokenizer ────────────────────────────────────────────────

    private static readonly HashSet<string> SkritKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "else", "while", "for", "return", "owner", "this", "goto", "state", "event",
        "property", "int", "float", "string", "bool", "void", "true", "false", "and", "or",
        "not", "startup", "trigger", "transition", "at", "when", "every", "doc", "author",
        "hidden", "shared", "static",
    };

    private static readonly HashSet<string> SfxKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "sfx", "create", "start", "destroy", "finish", "target", "attach", "attach_point",
        "position_at", "offset", "rat", "direction", "friendly", "sound", "play", "stop",
        "pause", "call", "set", "waitfor", "get", "if", "else", "collision", "sig", "end",
    };

    /// <summary>Single-pass tokenizer shared by the three DS1 text dialects. Adjacent
    /// same-brush characters merge into one run, so a colored document stays at a few
    /// hundred runs rather than one per character.</summary>
    private static IEnumerable<(string Span, Brush Brush)> Tokenize(string s, string syntax)
    {
        bool gas = string.Equals(syntax, "gas", StringComparison.OrdinalIgnoreCase);
        bool skrit = string.Equals(syntax, "skrit", StringComparison.OrdinalIgnoreCase);
        var keywords = skrit ? SkritKeywords : SfxKeywords;

        var runs = new List<(int Start, int End, Brush Brush)>();
        int i = 0, n = s.Length;
        while (i < n)
        {
            char c = s[i];

            // comments — all three dialects share // and /* */
            if (c == '/' && i + 1 < n && s[i + 1] == '/')
            {
                int e = s.IndexOf('\n', i);
                if (e < 0) e = n;
                runs.Add((i, e, BrComment)); i = e; continue;
            }
            if (c == '/' && i + 1 < n && s[i + 1] == '*')
            {
                int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                e = e < 0 ? n : e + 2;
                runs.Add((i, e, BrComment)); i = e; continue;
            }

            // strings
            if (c == '"')
            {
                int e = i + 1;
                while (e < n && s[e] != '"' && s[e] != '\n') e++;
                if (e < n && s[e] == '"') e++;
                runs.Add((i, e, BrString)); i = e; continue;
            }

            // gas [headers] — the whole bracket span
            if (gas && c == '[')
            {
                int e = s.IndexOf(']', i);
                if (e > 0 && e - i < 160)
                {
                    runs.Add((i, e + 1, BrHeader)); i = e + 1; continue;
                }
            }

            // #HANDLES and $vars (sfx / skrit)
            if (!gas && (c == '#' || c == '$') && i + 1 < n && (char.IsLetter(s[i + 1]) || s[i + 1] == '_'))
            {
                int e = i + 1;
                while (e < n && (char.IsLetterOrDigit(s[e]) || s[e] == '_')) e++;
                runs.Add((i, e, BrHandle)); i = e; continue;
            }

            // numbers (incl. 0x hex)
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(s[i + 1])))
            {
                int e = i;
                if (c == '0' && i + 1 < n && (s[i + 1] == 'x' || s[i + 1] == 'X'))
                {
                    e = i + 2;
                    while (e < n && Uri.IsHexDigit(s[e])) e++;
                }
                else
                {
                    while (e < n && (char.IsDigit(s[e]) || s[e] == '.')) e++;
                }
                runs.Add((i, e, BrNumber)); i = e; continue;
            }

            // identifiers
            if (char.IsLetter(c) || c == '_')
            {
                int e = i;
                while (e < n && (char.IsLetterOrDigit(s[e]) || s[e] == '_')) e++;
                var word = s[i..e];
                Brush b = BrDefault;
                if (gas)
                {
                    // attribute name when the next non-space char is '='
                    int k = e;
                    while (k < n && (s[k] == ' ' || s[k] == '\t')) k++;
                    if (k < n && s[k] == '=') b = BrAttr;
                }
                else if (keywords.Contains(word)) b = BrHeader;
                runs.Add((i, e, b)); i = e; continue;
            }

            if (c is '{' or '}' or ';' or '=' or '(' or ')' or ',' or '[' or ']')
            {
                runs.Add((i, i + 1, BrPunct)); i++; continue;
            }

            i++; // whitespace and anything else rides the gap-fill below
        }

        // stitch runs + the plain gaps between them into contiguous spans,
        // merging same-brush neighbours so run count stays small
        var output = new List<(string Span, Brush Brush)>();
        Brush? cur = null;
        int curStart = 0, curEnd = 0, pos = 0;

        void Flush()
        {
            if (cur is not null && curEnd > curStart) output.Add((s[curStart..curEnd], cur));
        }

        void Push(Brush b, int start, int end)
        {
            if (end <= start) return;
            if (cur is not null && ReferenceEquals(cur, b) && start == curEnd) { curEnd = end; return; }
            Flush();
            cur = b; curStart = start; curEnd = end;
        }

        foreach (var (start, end, brush) in runs)
        {
            if (start > pos) Push(BrDefault, pos, start);
            Push(brush, start, end);
            if (end > pos) pos = end;
        }
        if (pos < n) Push(BrDefault, pos, n);
        Flush();
        return output;
    }
}
