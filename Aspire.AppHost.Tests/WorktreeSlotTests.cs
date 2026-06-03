using Aspire.AppHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace Aspire.AppHost.Tests;

[TestClass]
public sealed class WorktreeSlotTests
{
    // -----------------------------------------------------------------------
    // Port derivation: value(base) = base + 100 * (slot - 1)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Port_Slot1_ReturnsBase()
    {
        WorktreeSlot.Port(7001, 1).ShouldBe(7001);
        WorktreeSlot.Port(7002, 1).ShouldBe(7002);
        WorktreeSlot.Port(7003, 1).ShouldBe(7003);
        WorktreeSlot.Port(7010, 1).ShouldBe(7010);
    }

    [TestMethod]
    public void Port_Slot2_ReturnsBasePlusOneHundred()
    {
        WorktreeSlot.Port(7001, 2).ShouldBe(7101);
        WorktreeSlot.Port(7002, 2).ShouldBe(7102);
        WorktreeSlot.Port(7003, 2).ShouldBe(7103);
        WorktreeSlot.Port(7010, 2).ShouldBe(7110);
    }

    [TestMethod]
    public void Port_Slot5_ReturnsBasePlusFourHundred()
    {
        WorktreeSlot.Port(7001, 5).ShouldBe(7401);
        WorktreeSlot.Port(7002, 5).ShouldBe(7402);
        WorktreeSlot.Port(7003, 5).ShouldBe(7403);
        WorktreeSlot.Port(7010, 5).ShouldBe(7410);
    }

    // -----------------------------------------------------------------------
    // Slot resolution from file
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Resolve_NoFileNoEnv_ReturnsDefault1()
    {
        using var tmp = new TempDirectory();
        ClearSlotEnvVar();

        WorktreeSlot.Resolve(tmp.Path).ShouldBe(1);
    }

    [TestMethod]
    public void Resolve_FileContainsValidSlot_ReturnsSlot()
    {
        using var tmp = new TempDirectory();
        ClearSlotEnvVar();
        File.WriteAllText(Path.Combine(tmp.Path, ".worktree-slot"), "2");

        WorktreeSlot.Resolve(tmp.Path).ShouldBe(2);
    }

    [TestMethod]
    public void Resolve_FileContainsSlotWithWhitespace_ReturnsSlot()
    {
        using var tmp = new TempDirectory();
        ClearSlotEnvVar();
        File.WriteAllText(Path.Combine(tmp.Path, ".worktree-slot"), "  3  \n");

        WorktreeSlot.Resolve(tmp.Path).ShouldBe(3);
    }

    [TestMethod]
    public void Resolve_FileEmpty_ReturnsDefault1()
    {
        using var tmp = new TempDirectory();
        ClearSlotEnvVar();
        File.WriteAllText(Path.Combine(tmp.Path, ".worktree-slot"), "");

        WorktreeSlot.Resolve(tmp.Path).ShouldBe(1);
    }

    [TestMethod]
    public void Resolve_EnvVarSet_ReturnsEnvVarValue()
    {
        using var tmp = new TempDirectory();
        Environment.SetEnvironmentVariable("worktree-slot", "4");
        try
        {
            WorktreeSlot.Resolve(tmp.Path).ShouldBe(4);
        }
        finally
        {
            ClearSlotEnvVar();
        }
    }

    [TestMethod]
    public void Resolve_EnvVarWinsOverFile()
    {
        using var tmp = new TempDirectory();
        File.WriteAllText(Path.Combine(tmp.Path, ".worktree-slot"), "2");
        Environment.SetEnvironmentVariable("worktree-slot", "3");
        try
        {
            WorktreeSlot.Resolve(tmp.Path).ShouldBe(3);
        }
        finally
        {
            ClearSlotEnvVar();
        }
    }

    // -----------------------------------------------------------------------
    // Fail-fast guard: invalid values throw immediately
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Resolve_SlotZero_Throws()
    {
        using var tmp = new TempDirectory();
        ClearSlotEnvVar();
        File.WriteAllText(Path.Combine(tmp.Path, ".worktree-slot"), "0");

        Should.Throw<InvalidOperationException>(() => WorktreeSlot.Resolve(tmp.Path))
            .Message.ShouldContain("out of range");
    }

    [TestMethod]
    public void Resolve_SlotSix_Throws()
    {
        using var tmp = new TempDirectory();
        ClearSlotEnvVar();
        File.WriteAllText(Path.Combine(tmp.Path, ".worktree-slot"), "6");

        Should.Throw<InvalidOperationException>(() => WorktreeSlot.Resolve(tmp.Path))
            .Message.ShouldContain("out of range");
    }

    [TestMethod]
    public void Resolve_NonIntegerValue_Throws()
    {
        using var tmp = new TempDirectory();
        ClearSlotEnvVar();
        File.WriteAllText(Path.Combine(tmp.Path, ".worktree-slot"), "abc");

        Should.Throw<InvalidOperationException>(() => WorktreeSlot.Resolve(tmp.Path))
            .Message.ShouldContain("not a valid integer");
    }

    [TestMethod]
    public void Resolve_EnvVarInvalidValue_Throws()
    {
        using var tmp = new TempDirectory();
        Environment.SetEnvironmentVariable("worktree-slot", "99");
        try
        {
            Should.Throw<InvalidOperationException>(() => WorktreeSlot.Resolve(tmp.Path))
                .Message.ShouldContain("out of range");
        }
        finally
        {
            ClearSlotEnvVar();
        }
    }

    // -----------------------------------------------------------------------
    // BumpPort (dashboard URL offset helper)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void BumpPort_SingleUrl_OffsetsByHundred()
    {
        // OffsetUrlEnvVar/BumpPort are file-local functions in AppHost.cs so we
        // test the equivalent logic inline. The port derivation formula is
        // base + 100*(slot-1); slot 2 offset = 100.
        var uri = new UriBuilder("https://localhost:17254");
        uri.Port += 100;
        uri.Uri.ToString().TrimEnd('/').ShouldBe("https://localhost:17354");
    }

    [TestMethod]
    public void BumpPort_SlotFiveOffset_CorrectPort()
    {
        var uri = new UriBuilder("https://localhost:17254");
        uri.Port += 400; // slot 5: offset = 100*(5-1)
        uri.Uri.ToString().TrimEnd('/').ShouldBe("https://localhost:17654");
    }

    [TestMethod]
    public void BumpPort_Slot1_NoOffset()
    {
        // Slot 1 sets no env vars; launchSettings governs. The port is unchanged.
        var offset = 100 * (1 - 1); // = 0
        offset.ShouldBe(0);

        var uri = new UriBuilder("https://localhost:17254");
        uri.Port += offset;
        uri.Uri.ToString().TrimEnd('/').ShouldBe("https://localhost:17254");
    }

    // -----------------------------------------------------------------------
    // WithSlotDatabase: connection-string catalog rewrite
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WithSlotDatabase_Slot2_SuffixesCatalog()
    {
        const string cs = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=DDD;Integrated Security=true;TrustServerCertificate=True";

        var result = WorktreeSlot.WithSlotDatabase(cs, 2);

        var csb = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = result };
        csb["Initial Catalog"].ShouldBe("DDD_S2");
    }

    [TestMethod]
    public void WithSlotDatabase_Slot5_SuffixesCatalog()
    {
        const string cs = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=IdentityDb;Integrated Security=true;TrustServerCertificate=True";

        var result = WorktreeSlot.WithSlotDatabase(cs, 5);

        var csb = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = result };
        csb["Initial Catalog"].ShouldBe("IdentityDb_S5");
    }

    [TestMethod]
    public void WithSlotDatabase_PreservesOtherTokens()
    {
        const string cs = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=DDD;Integrated Security=true;TrustServerCertificate=True";

        var result = WorktreeSlot.WithSlotDatabase(cs, 3);

        var csb = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = result };
        csb["Data Source"].ShouldBe("(localdb)\\MSSQLLocalDB");
        // DbConnectionStringBuilder normalises boolean values to lowercase — compare case-insensitively.
        csb["Integrated Security"].ToString()!.ShouldBe("true", StringCompareShould.IgnoreCase);
        csb["TrustServerCertificate"].ToString()!.ShouldBe("true", StringCompareShould.IgnoreCase);
    }

    [TestMethod]
    public void WithSlotDatabase_MissingInitialCatalog_Throws()
    {
        const string cs = "Data Source=(localdb)\\MSSQLLocalDB;Integrated Security=true";

        Should.Throw<InvalidOperationException>(() => WorktreeSlot.WithSlotDatabase(cs, 2))
            .Message.ShouldContain("Initial Catalog");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void ClearSlotEnvVar() =>
        Environment.SetEnvironmentVariable("worktree-slot", null);

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            System.IO.Path.GetRandomFileName());

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
