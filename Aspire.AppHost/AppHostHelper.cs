using BuildingBlocks.Application.Messaging;
using BuildingBlocks.WorktreeSlots;

namespace Aspire.AppHost;

/// <summary>
/// The bulkier resource-configuration steps, kept out of AppHost.cs so the top-level
/// orchestration reads at a glance. The "why" behind these lives here and in
/// ADR-0001 (messaging) / ADR-0002 (worktree slots).
/// </summary>
internal static class AppHostHelper
{
    // ----------------------------------------------------------------------
    // Worktree slot (resolved before CreateBuilder)
    // ----------------------------------------------------------------------

    /// <summary>
    /// Resolves the worktree slot. Walks up from the runtime base dir to find
    /// Aspire.AppHost.csproj so the gitignored .worktree-slot file is located
    /// regardless of launch mode (Visual Studio, <c>dotnet run</c>, Aspire runner).
    /// </summary>
    public static int ResolveSlot()
    {
        var baseDir = AppContext.BaseDirectory; // bin/Debug/net9.0 at runtime
        var projectDir = FindProjectDirectory(baseDir) ?? baseDir;
        return WorktreeSlot.Resolve(projectDir);
    }

    /// <summary>
    /// Offsets the Aspire dashboard/OTLP/resource-service ports by 100*(slot-1) so
    /// concurrent slots don't collide. Sets the env vars before CreateBuilder so they
    /// beat launchSettings.json (git-tracked, can't be per-worktree). No-op for slot 1.
    /// </summary>
    public static void OffsetDashboardPorts(int slot)
    {
        if (slot <= 1) return;

        var offset = 100 * (slot - 1);
        OffsetUrlEnvVar("ASPNETCORE_URLS", offset);
        OffsetUrlEnvVar("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", offset);
        OffsetUrlEnvVar("ASPIRE_DASHBOARD_MCP_ENDPOINT_URL", offset);
        OffsetUrlEnvVar("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL", offset);
    }

    // Bumps every port in a (semicolon-separated) URL env var by offset. No-op if unset.
    private static void OffsetUrlEnvVar(string varName, int offset)
    {
        var current = Environment.GetEnvironmentVariable(varName);
        if (string.IsNullOrEmpty(current))
            return;

        var bumped = current
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(url => BumpPort(url, offset));
        Environment.SetEnvironmentVariable(varName, string.Join(";", bumped));
    }

    // Adds offset to the URL's port; returns the input unchanged if it can't be parsed.
    private static string BumpPort(string url, int offset)
    {
        try
        {
            var ub = new UriBuilder(url);
            if (ub.Port > 0)
            {
                ub.Port += offset;
                return ub.Uri.ToString().TrimEnd('/');
            }
        }
        catch (UriFormatException) { /* fall through */ }
        return url;
    }

    private static string? FindProjectDirectory(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("Aspire.AppHost.csproj").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    // ----------------------------------------------------------------------
    // Messaging broker (ADR-0001)
    // ----------------------------------------------------------------------

    /// <summary>
    /// Provisions the 'messaging' broker chosen at build time: RabbitMQ by default, or
    /// the Azure Service Bus emulator when <c>Parameters:messaging-broker</c> /
    /// <c>ASPIRE_MESSAGING_BROKER</c> = "AzureServiceBus". On the ASB emulator path only,
    /// <paramref name="adminConnectionString"/> is set to the management-plane endpoint
    /// (MassTransit topology creation needs it; the AMQP data-plane string can't carry
    /// it too). Both transports expose a connection string, so callers reference
    /// 'messaging' uniformly via <see cref="IResourceWithConnectionString"/>.
    /// <paramref name="brokerChoice"/> returns the resolved broker name so the host can
    /// fan the same value out to each service's <c>MessageBroker</c> (keeping the
    /// provisioned container and the services' broker selection aligned under the AppHost).
    /// </summary>
    public static IResourceBuilder<IResourceWithConnectionString> AddConfiguredMessaging(
        this IDistributedApplicationBuilder builder, int slot, out ReferenceExpression? adminConnectionString,
        out string brokerChoice)
    {
        adminConnectionString = null;

        brokerChoice =
            builder.Configuration["Parameters:messaging-broker"]
            ?? builder.Configuration["ASPIRE_MESSAGING_BROKER"]
            ?? BrokerNames.RabbitMq;

        if (string.Equals(brokerChoice, BrokerNames.AzureServiceBus, StringComparison.OrdinalIgnoreCase))
        {
            // Emulator (no Azure subscription/cost for clone-and-run). Pinned to 2.0.0:
            // earlier images lack the management plane MassTransit needs (ADR-0001).
            var serviceBus = builder.AddAzureServiceBus("messaging")
                .RunAsEmulator(emulator => emulator.WithImageTag("2.0.0"));

            // Management plane via the 'emulatorhealth' (port-5300) endpoint, using the
            // host-mapped proxy port and the emulator's fixed SAS key (ADR-0001).
            var adminEndpoint = serviceBus.GetEndpoint("emulatorhealth");
            adminConnectionString = ReferenceExpression.Create(
                $"Endpoint=sb://{adminEndpoint.Property(EndpointProperty.HostAndPort)};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;");

            return serviceBus;
        }

        // RabbitMQ (default). Durable volume on slot 1 only; slots 2–5 run ephemeral so
        // concurrent boots don't fight over the deterministic volume name (container
        // names already get a per-run suffix from DCP, so only the volume collides).
        var rabbitMq = builder
            .AddRabbitMQ("messaging", password: builder.AddParameter("messaging-password"))
            .WithManagementPlugin();

        if (slot == 1)
            rabbitMq.WithDataVolume();

        return rabbitMq;
    }

    // ----------------------------------------------------------------------
    // Per-slot overrides (ADR-0002)
    // ----------------------------------------------------------------------

    /// <summary>
    /// Slot ≥ 2 only: shadows each service's authority, auth cookie name, CORS origins,
    /// and DB catalog via env vars (which beat appsettings/user-secrets). Slot 1 injects
    /// nothing, so behaviour is byte-for-byte the pre-slot default.
    /// </summary>
    public static void ApplySlotOverrides(
        this IDistributedApplicationBuilder builder, int slot,
        IResourceBuilder<ProjectResource> identityApi,
        IResourceBuilder<ProjectResource> schedulingApi,
        IResourceBuilder<ProjectResource> billingApi)
    {
        if (slot <= 1) return;

        var authority = $"https://localhost:{WorktreeSlot.Port(WorktreeSlot.IdentityBasePort, slot)}";
        var cookieName = $"DDD.Auth.S{slot}";
        var spaOrigin = $"https://localhost:{WorktreeSlot.Port(WorktreeSlot.SpaBasePort, slot)}";

        // Tell Identity which slot it is so it seeds only this slot's client URLs.
        identityApi.WithEnvironment("worktree-slot", slot.ToString());

        foreach (var api in new[] { schedulingApi, billingApi })
            api.WithEnvironment("Auth__Authority", authority)
               .WithEnvironment("Auth__CookieName", cookieName)
               .WithEnvironment("Cors__AllowedOrigins__0", spaOrigin)
               .WithEnvironment("Cors__AllowedOrigins__1", authority);

        // Per-slot databases: rewrite only the Initial Catalog (DDD → DDD_S{N}, etc).
        var defaultSlotted = WorktreeSlot.WithSlotDatabase(RequireConnectionString(builder, "DefaultConnection"), slot);
        var identitySlotted = WorktreeSlot.WithSlotDatabase(RequireConnectionString(builder, "IdentityDb"), slot);

        schedulingApi.WithEnvironment("ConnectionStrings__DefaultConnection", defaultSlotted);
        billingApi.WithEnvironment("ConnectionStrings__DefaultConnection", defaultSlotted);
        identityApi.WithEnvironment("ConnectionStrings__IdentityDb", identitySlotted);
    }

    private static string RequireConnectionString(IDistributedApplicationBuilder builder, string name) =>
        builder.Configuration[$"ConnectionStrings:{name}"]
        ?? throw new InvalidOperationException(
            $"Connection string '{name}' not found in configuration. " +
            "Ensure it is set in user secrets (shared UserSecretsId across all projects).");
}
