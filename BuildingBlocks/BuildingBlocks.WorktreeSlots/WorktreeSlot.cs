using System.Data.Common;

namespace BuildingBlocks.WorktreeSlots;

/// <summary>
/// Single source of the worktree-slot mechanics shared by the Aspire AppHost
/// (orchestration) and the Identity host (dev-gated cookie isolation + client seeding).
/// A "slot" is one integer (1–5) per worktree; every per-instance value derives from it.
///
/// Port derivation: <c>Port(base, slot) = base + 100 * (slot - 1)</c>
///   Slot 1 → unchanged 7010/7001/7002/7003 (main checkout, today's behaviour byte-for-byte)
///   Slot 2 → 7110/7101/7102/7103 … and so on through slot 5.
///
/// This type is intentionally dependency-free (only in-framework
/// <see cref="DbConnectionStringBuilder"/>) so it can be referenced from both an
/// Aspire host and a web host without pulling extra packages into either.
/// </summary>
public static class WorktreeSlot
{
    public const int Min = 1;
    public const int Max = 5;

    // Base ports for each browser-facing service — the single source for the
    // derivation, consumed by both AppHost.cs and IdentityServerConfig.
    public const int IdentityBasePort = 7010;
    public const int SchedulingBasePort = 7001;
    public const int BillingBasePort = 7002;
    public const int SpaBasePort = 7003;
    public const int VueBasePort = 7004;

    /// <summary>
    /// Resolves the slot from the AppHost's environment: <c>worktree-slot</c> env var
    /// (wins), then the first line of <c>.worktree-slot</c> in <paramref name="appHostDirectory"/>,
    /// then default 1. A non-empty but out-of-range value fails fast.
    /// </summary>
    public static int Resolve(string appHostDirectory)
    {
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
    /// Resolves the slot from a single configuration value (e.g. <c>config["worktree-slot"]</c>),
    /// for web hosts that receive the slot as an injected env var rather than reading the file.
    /// Null/blank → default 1; out-of-range fails fast (hardens the old silent inline parse).
    /// </summary>
    public static int FromValue(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return 1;

        return Parse(rawValue.Trim(), "configuration value 'worktree-slot'");
    }

    /// <summary>Derives a port from a base port and slot: <c>base + 100 * (slot - 1)</c>.</summary>
    public static int Port(int basePort, int slot) => basePort + 100 * (slot - 1);

    /// <summary>
    /// Rewrites the <c>Initial Catalog</c> token of <paramref name="connectionString"/>
    /// to <c>{catalog}_S{slot}</c>. Throws if the token is absent — a missing catalog would
    /// silently connect to the wrong database, defeating the isolation that is the point.
    /// </summary>
    public static string WithSlotDatabase(string connectionString, int slot)
    {
        var csb = new DbConnectionStringBuilder { ConnectionString = connectionString };
        const string key = "Initial Catalog";
        if (!csb.ContainsKey(key))
            throw new InvalidOperationException(
                $"No '{key}' token found in connection string. " +
                $"Cannot suffix database for slot {slot}. " +
                $"Connection string: {connectionString}");

        csb[key] = $"{csb[key]}_S{slot}";
        return csb.ConnectionString;
    }

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
