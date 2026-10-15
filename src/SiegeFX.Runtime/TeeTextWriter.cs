using System.IO;
using System.Text;

namespace SiegeFX.Runtime;

/// <summary>TextWriter that fans every write out to two underlying writers.
/// Used by Program.cs to mirror Console.Out into a log file (gated on the
/// SIEGEFX_DEBUG_LOG_FILE env var) so diag output is available off the
/// console without asking the user to copy/paste.
/// With <c>stampLines</c>, every line written to the SECOND writer (the log
/// file) starts with the wall-clock time (HH:mm:ss.fff), so a log line can
/// be matched to the frame of a screen recording, whose file name carries
/// its start time.</summary>
internal sealed class TeeTextWriter : TextWriter
{
    readonly TextWriter _a;
    readonly TextWriter _b;
    readonly bool _stamp;
    bool _atLineStart = true;
    public TeeTextWriter(TextWriter a, TextWriter b, bool stampLines = false) { _a = a; _b = b; _stamp = stampLines; }
    public override Encoding Encoding => _a.Encoding;
    public override void Write(char value) { _a.Write(value); WriteB(value.ToString()); }
    public override void Write(string? value) { _a.Write(value); WriteB(value); }
    public override void WriteLine() { _a.WriteLine(); WriteB(Environment.NewLine); }
    public override void WriteLine(string? value) { _a.WriteLine(value); WriteB(value + Environment.NewLine); }
    public override void Flush() { _a.Flush(); _b.Flush(); }

    void WriteB(string? s)
    {
        if (string.IsNullOrEmpty(s)) return;
        if (!_stamp) { _b.Write(s); return; }
        int start = 0;
        while (start < s.Length)
        {
            if (_atLineStart)
            {
                _b.Write(DateTime.Now.ToString("HH:mm:ss.fff "));
                _atLineStart = false;
            }
            int nl = s.IndexOf('\n', start);
            if (nl < 0) { _b.Write(s.AsSpan(start)); return; }
            _b.Write(s.AsSpan(start, nl - start + 1));
            _atLineStart = true;
            start = nl + 1;
        }
    }
}
