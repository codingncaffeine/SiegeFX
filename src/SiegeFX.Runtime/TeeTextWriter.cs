using System.IO;
using System.Text;

namespace SiegeFX.Runtime;

/// <summary>TextWriter that fans every write out to two underlying writers.
/// Used by Program.cs to mirror Console.Out into a log file (gated on the
/// SIEGEFX_DEBUG_LOG_FILE env var) so diag output is available off the
/// console without asking the user to copy/paste.</summary>
internal sealed class TeeTextWriter : TextWriter
{
    readonly TextWriter _a;
    readonly TextWriter _b;
    public TeeTextWriter(TextWriter a, TextWriter b) { _a = a; _b = b; }
    public override Encoding Encoding => _a.Encoding;
    public override void Write(char value) { _a.Write(value); _b.Write(value); }
    public override void Write(string? value) { _a.Write(value); _b.Write(value); }
    public override void WriteLine() { _a.WriteLine(); _b.WriteLine(); }
    public override void WriteLine(string? value) { _a.WriteLine(value); _b.WriteLine(value); }
    public override void Flush() { _a.Flush(); _b.Flush(); }
}
