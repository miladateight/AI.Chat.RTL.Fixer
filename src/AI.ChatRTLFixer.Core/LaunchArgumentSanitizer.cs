namespace AI.ChatRTLFixer.Core;

/// <summary>Removes stale CDP flags before a target app is relaunched.</summary>
public static class LaunchArgumentSanitizer
{
    private static readonly string[] DebugOptions =
    [
        "--remote-debugging-port",
        "--remote-debugging-address",
        "--remote-debugging-pipe",
    ];

    public static IReadOnlyList<string> RemoveRemoteDebuggingArguments(IEnumerable<string> arguments)
    {
        var source = arguments.ToList();
        var result = new List<string>(source.Count);
        for (var index = 0; index < source.Count; index++)
        {
            var argument = source[index];
            var option = DebugOptions.FirstOrDefault(candidate =>
                string.Equals(argument, candidate, StringComparison.OrdinalIgnoreCase) ||
                argument.StartsWith(candidate + "=", StringComparison.OrdinalIgnoreCase));

            if (option is null)
            {
                result.Add(argument);
                continue;
            }

            // Chromium accepts both --option=value and --option value. Remove
            // the following value only for the latter form.
            if (string.Equals(argument, option, StringComparison.OrdinalIgnoreCase) &&
                index + 1 < source.Count && !source[index + 1].StartsWith('-'))
                index++;
        }
        return result;
    }
}
