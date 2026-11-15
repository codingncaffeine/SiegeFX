using System.IO;
using System.Text;

namespace SiegeFX.Runtime;

/// <summary>TextWriter that fans every write out to two underlying writers.
/// Used by Program.cs to mirror Console.Out into a log file (gated on the
/// SIEGEFX_DEBUG_LOG_FILE env var) so diag output is available off the
/// console without asking the user to copy/paste. The log file is written
/// first: it is the record a bug report attaches, and the console is the
/// writer that can fail (see <see cref="ConsoleTextWriter"/>).
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
    public override void Write(char value) { WriteB(value.ToString()); _a.Write(value); }
    public override void Write(string? value) { WriteB(value); _a.Write(value); }
    public override void WriteLine() { WriteB(Environment.NewLine); _a.WriteLine(); }
    public override void WriteLine(string? value) { WriteB(value + Environment.NewLine); _a.WriteLine(value); }
    public override void Flush() { _b.Flush(); _a.Flush(); }

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

/// <summary>A console writer that goes quiet, for good, the first time the
/// console fails. The console can disappear under a running game: on macOS a
/// SiegeFX started by double-click runs in a Terminal window that closes its
/// terminal when that first process exits, and the copy it relaunched into
/// the world then gets "Input/output error" on every console write, which
/// took the game down at the difficulty screen (#56). A dead console is not
/// an error the game can do anything about, and the session log is the
/// record that matters, so from then on the writes are dropped.</summary>
internal sealed class ConsoleTextWriter : TextWriter
{
    readonly TextWriter _inner;
    bool _dead;
    public ConsoleTextWriter(TextWriter inner) { _inner = inner; }
    public override Encoding Encoding => _inner.Encoding;
    public override void Write(char value) { if (_dead) return; try { _inner.Write(value); } catch (IOException) { _dead = true; } }
    public override void Write(string? value) { if (_dead) return; try { _inner.Write(value); } catch (IOException) { _dead = true; } }
    public override void Write(char[] buffer, int index, int count) { if (_dead) return; try { _inner.Write(buffer, index, count); } catch (IOException) { _dead = true; } }
    public override void WriteLine() { if (_dead) return; try { _inner.WriteLine(); } catch (IOException) { _dead = true; } }
    public override void WriteLine(string? value) { if (_dead) return; try { _inner.WriteLine(value); } catch (IOException) { _dead = true; } }
    public override void Flush() { if (_dead) return; try { _inner.Flush(); } catch (IOException) { _dead = true; } }
}
