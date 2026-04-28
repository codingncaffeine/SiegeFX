namespace SiegeFX.Core.Assets;

/// <summary>
/// Parsed Dungeon Siege GAS (Gas Attribute Script) document. GAS is the engine's
/// lingua franca for templates, config, and level data: a nested tree of
/// <c>[header]</c> blocks carrying <c>key = value;</c> attribute lines.
///
/// The parser keeps block headers and attribute values as raw text (trimmed of
/// surrounding whitespace). GAS value syntax is permissive — comma tuples, pipe
/// flag lists, hex literals, bare identifiers, quoted strings — so splitting
/// values into structured types is the caller's job. Type tags (<c>b</c>, <c>i</c>,
/// <c>f</c>, <c>s</c>) appearing before an attribute name are extracted into
/// <see cref="GasAttribute.TypeTag"/>.
/// </summary>
public sealed class GasDocument
{
    public IReadOnlyList<GasNode> Roots { get; }

    private GasDocument(IReadOnlyList<GasNode> roots) { Roots = roots; }

    public static GasDocument Load(byte[] bytes)
    {
        var s = (ReadOnlySpan<byte>)bytes;
        if (s.Length >= 2)
        {
            if (s[0] == 0xFF && s[1] == 0xFE)
                throw new InvalidDataException("GAS file has UTF-16 LE BOM; DS1 ships ASCII/UTF-8 only");
            if (s[0] == 0xFE && s[1] == 0xFF)
                throw new InvalidDataException("GAS file has UTF-16 BE BOM; DS1 ships ASCII/UTF-8 only");
        }
        if (s.Length >= 3 && s[0] == 0xEF && s[1] == 0xBB && s[2] == 0xBF) s = s[3..];
        return Parse(System.Text.Encoding.UTF8.GetString(s));
    }

    public static GasDocument Parse(string text)
    {
        var parser = new Parser(text);
        var roots = parser.ParseContents(closing: '\0');
        return new GasDocument(roots.Children);
    }

    private sealed class Parser
    {
        // Block-nesting depth cap. Real DS1 data tops out around 8 levels; 512 leaves
        // plenty of headroom while making a hostile input that recurses into SO infeasible.
        private const int MaxBlockDepth = 512;

        private readonly string _src;
        private int _pos;
        private int _line = 1;
        private int _depth;

        public Parser(string src) { _src = src; }

        public BlockContents ParseContents(char closing)
        {
            var children = new List<GasNode>();
            var attrs = new List<GasAttribute>();

            while (true)
            {
                SkipTrivia();
                if (_pos >= _src.Length)
                {
                    if (closing != '\0')
                        throw Err($"unexpected EOF, expected '{closing}'");
                    break;
                }
                var c = _src[_pos];
                if (c == closing) break;

                if (c == '[')
                {
                    var headerLine = _line;
                    var header = ReadBracketedHeader();
                    SkipTrivia();
                    // DS1 ships with asset typos between ']' and the body's '{': stray
                    // alphanumerics (`[anim_files]d`) or entire misplaced attribute lines
                    // (`[physics]\n gib_gore_good = true; \n {...}`). The retail game loads
                    // these, so mimic that tolerance: skip any content up to the next '{',
                    // bailing if we'd swallow a sibling '[' or the parent's '}'.
                    while (_pos < _src.Length && _src[_pos] != '{' && _src[_pos] != '[' && _src[_pos] != '}')
                    {
                        if (_src[_pos] == '\n') _line++;
                        _pos++;
                    }
                    if (_pos >= _src.Length || _src[_pos] != '{')
                        throw Err($"expected '{{' to open block [{header}] (opened at line {headerLine}, saw '{PeekChar()}')");
                    _pos++;
                    if (++_depth > MaxBlockDepth)
                        throw Err($"GAS nesting exceeds {MaxBlockDepth} (header [{header}] at line {headerLine})");
                    var body = ParseContents('}');
                    _depth--;
                    Expect('}', "to close block");
                    children.Add(new GasNode(header, body.Children, body.Attributes));
                    continue;
                }

                // Attribute line: read the left side up to '=' (no quotes permitted here).
                var leftStart = _pos;
                while (_pos < _src.Length && _src[_pos] != '=' && _src[_pos] != ';'
                       && _src[_pos] != '{' && _src[_pos] != '}' && _src[_pos] != '[' && _src[_pos] != ']')
                {
                    if (_src[_pos] == '\n') _line++;
                    _pos++;
                }
                if (_pos >= _src.Length || _src[_pos] != '=')
                    throw Err($"expected '=' after attribute name (saw '{PeekChar()}')");

                var leftText = _src[leftStart.._pos].Trim();
                _pos++; // consume '='

                var value = ReadValueUntilSemicolon();
                Expect(';', "to terminate attribute");

                attrs.Add(BuildAttribute(leftText, value));
            }

            return new BlockContents(children, attrs);
        }

        private GasAttribute BuildAttribute(string leftText, string value)
        {
            // Type tag is a single lowercase letter followed by whitespace, then the key.
            // DS1 uses b/i/f/s (bool/int/float/string) + x (hex uint) + d (double); being
            // permissive costs nothing since real attribute names never start with a single
            // lowercase letter + whitespace.
            string? tag = null;
            var name = leftText;
            if (leftText.Length >= 3)
            {
                var t = leftText[0];
                if (t >= 'a' && t <= 'z' && char.IsWhiteSpace(leftText[1]))
                {
                    tag = t.ToString();
                    name = leftText[1..].TrimStart();
                }
            }
            return new GasAttribute(name, tag, value.Trim());
        }

        private string ReadBracketedHeader()
        {
            _pos++; // consume '['
            var start = _pos;
            while (_pos < _src.Length && _src[_pos] != ']')
            {
                if (_src[_pos] == '\n') _line++;
                _pos++;
            }
            if (_pos >= _src.Length) throw Err("unterminated block header (missing ']')");
            var header = _src[start.._pos].Trim();
            _pos++; // consume ']'
            return header;
        }

        /// <summary>
        /// Reads an attribute value up to (but not including) a ';' that is outside a quoted
        /// string or a <c>[[ ... ]]</c> script literal. Backslashes are NOT escape sequences in GAS
        /// — they're literal path separators — so quoted strings are strictly delimited by
        /// the next '"'. The <c>[[ ... ]]</c> form is used for effect scripts (see
        /// /world/global/effects/*.gas) and carries its own mini-DSL with its own semicolons
        /// and braces that must not terminate the outer attribute.
        /// </summary>
        private string ReadValueUntilSemicolon()
        {
            var start = _pos;
            var inQuote = false;
            while (_pos < _src.Length)
            {
                var c = _src[_pos];
                if (c == '"') { inQuote = !inQuote; _pos++; continue; }
                if (!inQuote && c == '[' && _pos + 1 < _src.Length && _src[_pos + 1] == '[')
                {
                    _pos += 2;
                    // Scan to the next `]]` literally — no quote tracking. DS1's
                    // character_select.gas authors `excluded_chars = [["<>:/\|?*.%;]];`
                    // where the leading `"` opens a quote that never closes inside the
                    // script literal; tracking quotes inside [[...]] would let that lone
                    // `"` swallow the rest of the file. Shipped effect-script bodies in
                    // /world/global/effects/ never put `]]` inside a quoted string, so
                    // a literal scan is both correct and the simplest model.
                    while (_pos + 1 < _src.Length && !(_src[_pos] == ']' && _src[_pos + 1] == ']'))
                    {
                        if (_src[_pos] == '\n') _line++;
                        _pos++;
                    }
                    if (_pos + 1 >= _src.Length) throw Err("unterminated [[...]] script literal");
                    _pos += 2;
                    continue;
                }
                if (!inQuote && c == ';') break;
                if (c == '\n') _line++;
                _pos++;
            }
            if (_pos >= _src.Length) throw Err("unterminated attribute value (missing ';')");
            return _src[start.._pos];
        }

        private void SkipTrivia()
        {
            while (_pos < _src.Length)
            {
                var c = _src[_pos];
                if (c == ' ' || c == '\t' || c == '\r') { _pos++; continue; }
                if (c == '\n') { _line++; _pos++; continue; }
                if (c == '/' && _pos + 1 < _src.Length)
                {
                    if (_src[_pos + 1] == '/')
                    {
                        _pos += 2;
                        while (_pos < _src.Length && _src[_pos] != '\n') _pos++;
                        continue;
                    }
                    if (_src[_pos + 1] == '*')
                    {
                        _pos += 2;
                        while (_pos + 1 < _src.Length && !(_src[_pos] == '*' && _src[_pos + 1] == '/'))
                        {
                            if (_src[_pos] == '\n') _line++;
                            _pos++;
                        }
                        if (_pos + 1 >= _src.Length) throw Err("unterminated block comment");
                        _pos += 2;
                        continue;
                    }
                }
                break;
            }
        }

        private void Expect(char c, string ctx)
        {
            if (_pos >= _src.Length || _src[_pos] != c)
                throw Err($"expected '{c}' {ctx} (saw '{PeekChar()}')");
            _pos++;
        }

        private string PeekChar() =>
            _pos < _src.Length ? _src[_pos].ToString() : "<EOF>";

        private InvalidDataException Err(string msg) =>
            new($"GAS parse error at line {_line} (pos {_pos}): {msg}");
    }

    private readonly record struct BlockContents(List<GasNode> Children, List<GasAttribute> Attributes);
}

/// <summary>A parsed block: header text, ordered child blocks, ordered attributes.</summary>
public sealed class GasNode
{
    public string Header { get; }
    public IReadOnlyList<GasNode> Children { get; }
    public IReadOnlyList<GasAttribute> Attributes { get; }

    internal GasNode(string header, IReadOnlyList<GasNode> children, IReadOnlyList<GasAttribute> attributes)
    {
        Header = header;
        Children = children;
        Attributes = attributes;
    }
}

/// <summary>An attribute line. <paramref name="Value"/> is the raw text between '=' and ';',
/// trimmed. Commas, pipes, quoted strings are preserved verbatim for the caller to split.</summary>
public readonly record struct GasAttribute(string Name, string? TypeTag, string Value);
