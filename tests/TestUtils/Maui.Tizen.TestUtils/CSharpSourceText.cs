using System.Text;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Reduces C# source to the text that is actually code.
/// </summary>
/// <remarks>
/// <para>
/// Banned-symbol scanning has to ignore comments and string literals, otherwise the guard fires on
/// the very documentation that explains why a symbol is banned. That is not hypothetical: this
/// repository's own comments discuss <c>Tizen.Maps</c> and <c>Window.Instance</c> at length.
/// </para>
/// <para>
/// This is a lexer for the shapes that appear in real source - line and block comments, regular,
/// verbatim, interpolated and raw string literals, and character literals - not a full C# parser.
/// Characters that are removed are replaced with spaces so that line and column numbers of the
/// surviving code are preserved exactly, which is what lets a violation report a usable location.
/// </para>
/// </remarks>
public static class CSharpSourceText
{
    /// <summary>
    /// Returns <paramref name="source"/> with comments and literal contents blanked out, preserving
    /// length, line breaks and therefore all positions.
    /// </summary>
    public static string StripCommentsAndLiterals(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = new StringBuilder(source.Length);
        var i = 0;

        while (i < source.Length)
        {
            var c = source[i];

            // Line comment.
            if (c == '/' && Peek(source, i + 1) == '/')
            {
                while (i < source.Length && source[i] is not ('\n' or '\r'))
                    result.Append(Blank(source[i++]));

                continue;
            }

            // Block comment.
            if (c == '/' && Peek(source, i + 1) == '*')
            {
                result.Append("  ");
                i += 2;

                while (i < source.Length && !(source[i] == '*' && Peek(source, i + 1) == '/'))
                    result.Append(Blank(source[i++]));

                if (i < source.Length)
                {
                    result.Append("  ");
                    i += 2;
                }

                continue;
            }

            // Raw string literal: three or more quotes.
            if (c == '"' && Peek(source, i + 1) == '"' && Peek(source, i + 2) == '"')
            {
                var fenceLength = 0;
                while (Peek(source, i + fenceLength) == '"')
                    fenceLength++;

                var fence = new string('"', fenceLength);
                result.Append(' ', fenceLength);
                i += fenceLength;

                var close = source.IndexOf(fence, i, StringComparison.Ordinal);
                var contentEnd = close < 0 ? source.Length : close;

                while (i < contentEnd)
                    result.Append(Blank(source[i++]));

                if (close >= 0)
                {
                    result.Append(' ', fenceLength);
                    i += fenceLength;
                }

                continue;
            }

            // Verbatim string, optionally interpolated: @"..." or $@"..." or @$"..."
            if ((c == '@' && Peek(source, i + 1) == '"') ||
                (c is '$' && Peek(source, i + 1) == '@' && Peek(source, i + 2) == '"'))
            {
                var prefix = c == '@' ? 1 : 2;
                result.Append(' ', prefix + 1);
                i += prefix + 1;

                while (i < source.Length)
                {
                    // "" is an escaped quote inside a verbatim string.
                    if (source[i] == '"' && Peek(source, i + 1) == '"')
                    {
                        result.Append("  ");
                        i += 2;
                        continue;
                    }

                    if (source[i] == '"')
                    {
                        result.Append(' ');
                        i++;
                        break;
                    }

                    result.Append(Blank(source[i++]));
                }

                continue;
            }

            // Regular or interpolated string.
            if (c == '"' || (c == '$' && Peek(source, i + 1) == '"'))
            {
                var prefix = c == '"' ? 0 : 1;
                result.Append(' ', prefix + 1);
                i += prefix + 1;

                while (i < source.Length)
                {
                    if (source[i] == '\\' && i + 1 < source.Length)
                    {
                        result.Append("  ");
                        i += 2;
                        continue;
                    }

                    if (source[i] is '"' or '\n')
                    {
                        result.Append(Blank(source[i]));
                        i++;
                        break;
                    }

                    result.Append(Blank(source[i++]));
                }

                continue;
            }

            // Character literal.
            if (c == '\'')
            {
                result.Append(' ');
                i++;

                while (i < source.Length)
                {
                    if (source[i] == '\\' && i + 1 < source.Length)
                    {
                        result.Append("  ");
                        i += 2;
                        continue;
                    }

                    if (source[i] == '\'')
                    {
                        result.Append(' ');
                        i++;
                        break;
                    }

                    result.Append(Blank(source[i++]));
                }

                continue;
            }

            result.Append(c);
            i++;
        }

        return result.ToString();
    }

    static char Peek(string source, int index) =>
        index < source.Length ? source[index] : '\0';

    /// <summary>Newlines survive so line numbers stay correct; everything else becomes a space.</summary>
    static char Blank(char c) => c is '\n' or '\r' ? c : ' ';
}
