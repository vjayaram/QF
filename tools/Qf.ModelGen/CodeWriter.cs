using System.Text;

namespace Qf.ModelGen;

/// <summary>Minimal indented writer. Always emits LF line endings so output is byte-stable across OSes.</summary>
internal sealed class CodeWriter
{
    private readonly StringBuilder _buffer = new();
    private int _indent;

    public CodeWriter Line(string text = "")
    {
        if (text.Length > 0)
        {
            _buffer.Append(' ', _indent * 4).Append(text);
        }

        _buffer.Append('\n');
        return this;
    }

    public CodeWriter Lines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        foreach (var line in lines)
        {
            Line(line);
        }

        return this;
    }

    public IDisposable Block(string header, string close = "}")
    {
        Line(header);
        Line("{");
        _indent++;
        return new Scope(this, close);
    }

    public void Summary(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            Line($"/// <summary>{System.Security.SecurityElement.Escape(text)}</summary>");
        }
    }

    public override string ToString() => _buffer.ToString();

    private sealed class Scope(CodeWriter writer, string close) : IDisposable
    {
        public void Dispose()
        {
            writer._indent--;
            writer.Line(close);
        }
    }
}
