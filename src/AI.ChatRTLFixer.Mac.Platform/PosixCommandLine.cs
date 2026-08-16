using System.Text;

namespace AI.ChatRTLFixer.Mac;

/// <summary>Parses the shell-style quoting emitted in macOS process command lines.</summary>
internal static class PosixCommandLine
{
    public static IReadOnlyList<string> Split(string value)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        var escaping = false;
        var tokenStarted = false;

        foreach (var character in value)
        {
            if (escaping)
            {
                current.Append(character);
                escaping = false;
                tokenStarted = true;
                continue;
            }

            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
                else if (character == '\\' && quote == '"')
                {
                    escaping = true;
                }
                else
                {
                    current.Append(character);
                }
                tokenStarted = true;
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                tokenStarted = true;
            }
            else if (character == '\\')
            {
                escaping = true;
                tokenStarted = true;
            }
            else if (char.IsWhiteSpace(character))
            {
                Flush();
            }
            else
            {
                current.Append(character);
                tokenStarted = true;
            }
        }

        if (escaping) current.Append('\\');
        Flush();
        return result;

        void Flush()
        {
            if (!tokenStarted) return;
            result.Add(current.ToString());
            current.Clear();
            tokenStarted = false;
        }
    }

    public static string Quote(string value) =>
        value.Length > 0 && value.All(character => !char.IsWhiteSpace(character) && character is not '\'' and not '"' and not '\\')
            ? value
            : "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
