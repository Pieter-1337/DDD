namespace Aspire.AppHost;

/// <summary>
/// Reads the worktree slot from a gitignored <c>.worktree-slot</c> file next to the
/// AppHost project (one integer, 1–5). Slot 1 is the default (main checkout) and
/// reproduces today's behaviour byte-for-byte. Slots 2–5 are agent/developer worktrees.
///
/// Port derivation: value(base) = base + 100 * (slot - 1)
///   Slot 1 → offsets 0  → unchanged 7010/7001/7002/7003
///   Slot 2 → offsets 100 → 7110/7101/7102/7103
///   ...
/// </summary>
internal static class WorktreeSlot
{
    internal const int Min = 1;
    internal const int Max = 5;

    /// <summary>
    /// Resolves the slot integer.
    /// Resolution order (highest wins):
    ///   1. <c>worktree-slot</c> environment variable
    ///   2. First line of <c>.worktree-slot</c> file in <paramref name="appHostDirectory"/>
    ///   3. Default: 1
    ///
    /// A non-empty value that is not a valid integer in 1–5 throws immediately so a
    /// misconfigured slot fails fast rather than silently running on the wrong ports.
    /// </summary>
    internal static int Resolve(string appHostDirectory)
    {
        // Env var wins — matches .NET config-precedence expectation and allows
        // CI / launch scripts to override without touching the file.
        var envValue = Environment.GetEnvironmentVariable("worktree-slot");
        if (!string.IsNullOrEmpty(envValue))
            return Parse(envValue, "environment variable 'worktree-slot'");

        var filePath = Path.Combine(appHostDirectory, ".worktree-slot");
        if (File.Exists(filePath))
        {
            var line = File.ReadLines(filePath).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(line))
                return Parse(line.Trim(), $"file '{filePath}'");
        }

        return 1;
    }

    /// <summary>
    /// Derives a port number from a base port and the active slot.
    /// <c>Port(base, slot) = base + 100 * (slot - 1)</c>
    /// </summary>
    internal static int Port(int basePort, int slot) => basePort + 100 * (slot - 1);

    private static int Parse(string value, string source)
    {
        if (!int.TryParse(value, out var slot))
            throw new InvalidOperationException(
                $"Worktree slot value '{value}' from {source} is not a valid integer. " +
                $"Allowed values: {Min}–{Max}.");

        if (slot < Min || slot > Max)
            throw new InvalidOperationException(
                $"Worktree slot value {slot} from {source} is out of range. " +
                $"Allowed values: {Min}–{Max}. " +
                $"Slot 1 is the main checkout; slots 2–{Max} are worktrees.");

        return slot;
    }
}
