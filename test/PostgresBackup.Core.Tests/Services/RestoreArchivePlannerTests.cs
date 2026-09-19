using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

public class RestoreArchivePlannerTests
{
    private const string ArchiveList = """
        ; Archive created for regression testing
        6; 2615 17958 SCHEMA - hangfire pex
        417; 1259 18313 TABLE hangfire aggregatedcounter pex
        416; 1259 18312 SEQUENCE hangfire aggregatedcounter_id_seq pex
        3950; 0 18313 TABLE DATA hangfire aggregatedcounter pex
        3729; 2606 18325 CONSTRAINT hangfire aggregatedcounter aggregatedcounter_key_key pex
        3692; 1259 18326 INDEX hangfire ix_hangfire_counter_expireat pex
        500; 1259 20000 TABLE public MissingTable pex
        501; 0 20000 TABLE DATA public MissingTable pex
        502; 2606 20001 CONSTRAINT public MissingTable PK_MissingTable pex
        """;

    [Fact]
    public void Build_CommentsExistingEntriesAndKeepsMissingObjectGraph()
    {
        var catalog = new RestoreTargetCatalog();
        catalog.Schemas.Add("hangfire");
        catalog.Schemas.Add("public");
        catalog.Relations.Add(RestoreTargetCatalog.Key("hangfire", "aggregatedcounter"));
        catalog.Relations.Add(RestoreTargetCatalog.Key("hangfire", "aggregatedcounter_id_seq"));
        catalog.Relations.Add(RestoreTargetCatalog.Key("hangfire", "ix_hangfire_counter_expireat"));
        catalog.Constraints.Add(RestoreTargetCatalog.Key("hangfire", "aggregatedcounter", "aggregatedcounter_key_key"));

        var plan = RestoreArchivePlanner.Build(ArchiveList, catalog);

        Assert.Contains("; skipped-existing: 6; 2615 17958 SCHEMA - hangfire pex", plan.Content);
        Assert.Contains("; skipped-existing: 417; 1259 18313 TABLE hangfire aggregatedcounter pex", plan.Content);
        Assert.Contains("; skipped-existing: 3950; 0 18313 TABLE DATA hangfire aggregatedcounter pex", plan.Content);
        Assert.Contains("; skipped-existing: 3729; 2606 18325 CONSTRAINT hangfire aggregatedcounter aggregatedcounter_key_key pex", plan.Content);
        Assert.Contains("500; 1259 20000 TABLE public MissingTable pex", plan.Content);
        Assert.Contains("501; 0 20000 TABLE DATA public MissingTable pex", plan.Content);
        Assert.Contains("502; 2606 20001 CONSTRAINT public MissingTable PK_MissingTable pex", plan.Content);
        Assert.Equal(6, plan.SkippedExistingCount);
        Assert.Equal(3, plan.IncludedCount);
        Assert.Equal(3, plan.ActionableCount);
    }

    [Fact]
    public void Build_SkipsMetadataThatWouldMutateExistingObjects()
    {
        const string list = """
            10; 0 0 COMMENT - SCHEMA public pex
            11; 0 0 ACL public TABLE ExistingTable pex
            """;

        var plan = RestoreArchivePlanner.Build(list, new RestoreTargetCatalog());

        Assert.Contains("; skipped-metadata:", plan.Content);
        Assert.Equal(2, plan.SkippedMetadataCount);
        Assert.Equal(0, plan.IncludedCount);
    }

    [Fact]
    public void Build_MatchesQuotedIdentifiersWithoutLosingCaseOrSpaces()
    {
        const string list = "10; 1259 100 TABLE \"Odd Schema\" \"Odd Table\" owner";
        var catalog = new RestoreTargetCatalog();
        catalog.Relations.Add(RestoreTargetCatalog.Key("Odd Schema", "Odd Table"));

        var plan = RestoreArchivePlanner.Build(list, catalog);

        Assert.Equal(1, plan.SkippedExistingCount);
        Assert.Contains("; skipped-existing:", plan.Content);
    }

    [Fact]
    public void BuildDataOnly_CollectsExistingTablesAndSequenceSets()
    {
        const string list = """
            10; 0 100 TABLE DATA public Quote owner
            11; 0 101 TABLE DATA hangfire job owner
            12; 0 102 SEQUENCE SET public Quote_Id_seq owner
            """;
        var catalog = new RestoreTargetCatalog();
        catalog.Relations.Add(RestoreTargetCatalog.Key("public", "Quote"));
        catalog.Relations.Add(RestoreTargetCatalog.Key("hangfire", "job"));
        catalog.Relations.Add(RestoreTargetCatalog.Key("public", "Quote_Id_seq"));

        var plan = RestoreArchivePlanner.BuildDataOnly(list, catalog);

        Assert.Equal(3, plan.DataEntryCount);
        Assert.Equal(
            [new RestoreTableIdentity("hangfire", "job"), new RestoreTableIdentity("public", "Quote")],
            plan.Tables);
        Assert.Empty(plan.MissingObjects);
        Assert.Empty(plan.UnsupportedDescriptions);
        Assert.Empty(plan.CyclicTables);
    }

    [Fact]
    public void BuildDataOnly_ReportsMissingTablesBeforeRestore()
    {
        const string list = "10; 0 100 TABLE DATA public MissingTable owner";

        var plan = RestoreArchivePlanner.BuildDataOnly(list, new RestoreTargetCatalog());

        Assert.Equal(["public.MissingTable"], plan.MissingObjects);
    }

    [Fact]
    public void BuildDataOnly_OrdersParentTableBeforeChildTableFromTargetForeignKeys()
    {
        const string list = """
            10; 0 100 TABLE DATA public FinancialMetric owner
            11; 0 101 TABLE DATA public Instrument owner
            """;
        var catalog = new RestoreTargetCatalog();
        catalog.Relations.Add(RestoreTargetCatalog.Key("public", "FinancialMetric"));
        catalog.Relations.Add(RestoreTargetCatalog.Key("public", "Instrument"));
        catalog.ForeignKeyDependencies.Add(new RestoreForeignKeyDependency(
            new RestoreTableIdentity("public", "FinancialMetric"),
            new RestoreTableIdentity("public", "Instrument")));

        var plan = RestoreArchivePlanner.BuildDataOnly(list, catalog);

        Assert.Equal(
            [new RestoreTableIdentity("public", "Instrument"), new RestoreTableIdentity("public", "FinancialMetric")],
            plan.Tables);
        Assert.True(
            plan.Content.IndexOf("TABLE DATA public Instrument", StringComparison.Ordinal)
            < plan.Content.IndexOf("TABLE DATA public FinancialMetric", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildDataOnly_ReportsForeignKeyCycles()
    {
        const string list = """
            10; 0 100 TABLE DATA public Alpha owner
            11; 0 101 TABLE DATA public Beta owner
            """;
        var alpha = new RestoreTableIdentity("public", "Alpha");
        var beta = new RestoreTableIdentity("public", "Beta");
        var catalog = new RestoreTargetCatalog();
        catalog.Relations.Add(RestoreTargetCatalog.Key(alpha.Schema, alpha.Name));
        catalog.Relations.Add(RestoreTargetCatalog.Key(beta.Schema, beta.Name));
        catalog.ForeignKeyDependencies.Add(new RestoreForeignKeyDependency(alpha, beta));
        catalog.ForeignKeyDependencies.Add(new RestoreForeignKeyDependency(beta, alpha));

        var plan = RestoreArchivePlanner.BuildDataOnly(list, catalog);

        Assert.Equal(["public.Alpha", "public.Beta"], plan.CyclicTables);
    }
}
