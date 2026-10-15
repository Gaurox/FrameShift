using System.Runtime.CompilerServices;
using System.Text;

namespace FrameShift.Core.Helpers;

internal sealed class BoundedProcessText(int capacity)
{
    internal const int DiagnosticLimit = 64 * 1024;
    internal const int LineLimit = 16 * 1024;
    internal const int CaptureLimit = 8 * 1024 * 1024;
    private const string Marker = "[earlier output truncated]\n";
    private readonly char[] _data = new char[capacity];
    private readonly object _sync = new();
    private int _start, _count;
    private bool _truncated;

    private void Clear()
    {
        lock (_sync) { _start = 0; _count = 0; _truncated = false; }
    }

    public void Append(ReadOnlySpan<char> value)
    {
        lock (_sync)
        {
            if (value.Length >= capacity)
            {
                _truncated |= _count > 0 || value.Length > capacity;
                value[^capacity..].CopyTo(_data);
                _start = 0; _count = capacity;
                return;
            }
            var discard = Math.Max(0, _count + value.Length - capacity);
            _truncated |= discard > 0;
            _start = (_start + discard) % capacity;
            _count -= discard;
            var end = (_start + _count) % capacity;
            var first = Math.Min(value.Length, capacity - end);
            value[..first].CopyTo(_data.AsSpan(end));
            value[first..].CopyTo(_data);
            _count += value.Length;
        }
    }

    public override string ToString()
    {
        lock (_sync)
        {
            var skip = _truncated ? Math.Min(Marker.Length, _count) : 0;
            var start = (_start + skip) % capacity;
            var count = _count - skip;
            var first = Math.Min(count, capacity - start);
            return (_truncated ? Marker : "") + new string(_data, start, first) + new string(_data, 0, count - first);
        }
    }

    // Preserve both the beginning (showinfo fields) and end (the final error) of long
    // lines. ReadLineAsync itself would allocate the entire line before any limit.
    public static async IAsyncEnumerable<string> ReadLinesAsync(StreamReader reader,
        [EnumeratorCancellation] CancellationToken token)
    {
        var buffer = new char[4096];
        var head = new StringBuilder();
        var tail = new BoundedProcessText(LineLimit / 2);
        var hasTail = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (read == 0) break;
            for (var i = 0; i < read;)
            {
                var end = Array.IndexOf(buffer, '\n', i, read - i);
                var count = (end < 0 ? read : end) - i;
                var first = Math.Min(count, LineLimit / 2 - head.Length);
                head.Append(buffer, i, first);
                if (first < count) { tail.Append(buffer.AsSpan(i + first, count - first)); hasTail = true; }
                if (end < 0) break;
                yield return (head.ToString() + (hasTail ? tail.ToString() : "")).TrimEnd('\r');
                head.Clear(); tail.Clear(); hasTail = false;
                i = end + 1;
            }
        }
        if (head.Length > 0 || hasTail) yield return (head.ToString() + (hasTail ? tail.ToString() : "")).TrimEnd('\r');
    }
}
