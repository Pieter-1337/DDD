using Aspire.AppHost;
using BuildingBlocks.Application.Messaging;

// ---------------------------------------------------------------------------
// Worktree slot — resolved BEFORE CreateBuilder so we can offset the Aspire
// dashboard/OTLP/resource-service ports via env vars that beat launchSettings.
//
// Source priority (highest first):
//   1. 'worktree-slot' environment variable
//   2. First line of Aspire.AppHost/.worktree-slot  (gitignored)
//   3. Default: 1
//
// Slot 1 = main checkout; reproduces today's behaviour byte-for-byte (no env
// vars set, no volume changes, no container-name changes).
// Slots 2–5 = agent / developer worktrees; all ports shifted by +100*(slot-1).
// ---------------------------------------------------------------------------
var appHostDir = AppContext.BaseDirectory; // bin/Debug/net9.0 — resolve relative at runtime
// Walk up from bin/… to the project directory so the .worktree-slot file is found
// whether we're launched from Visual Studio, `dotnet run`, or the Aspire runner.
var projectDir = FindProjectDirectory(appHostDir) ?? appHostDir;
var slot = WorktreeSlot.Resolve(projectDir);

// For slot >= 2: offset the dashboard UI, OTLP, and resource-service ports so two
// live Aspire instances don't collide on those well-known localhost ports.
// For slot 1: set nothing — let launchSettings.json govern exactly as before.
//
// Acceptance check #1: The Aspire dashboard UI is served on the AppHost process's
// own ASPNETCORE_URLS (the "Login to the dashboard at https://localhost:NNNNN" URL).
// Setting ASPNETCORE_URLS here, before CreateBuilder, overrides launchSettings at
// process-start time, so the dashboard UI port moves without editing launchSettings.
// This satisfies check #1 purely from Program.cs — no thin launch wrapper needed.
if (slot > 1)
{
    OffsetDashboardPorts(slot);
}

var builder = DistributedApplication.CreateBuilder(args);

// Which broker resource to provision under the single 'messaging' name.
// Read from configuration at build time (a parameter *resource* value is not
// available while the graph is being built, so we cannot branch on AddParameter).
// Default is RabbitMQ — clone-and-run behaviour is unchanged from before this
// change. To provision the Azure Service Bus emulator instead, set the value to
// "AzureServiceBus" via any of:
//   user secrets : dotnet user-secrets set "Parameters:messaging-broker" AzureServiceBus
//   env var      : Parameters__messaging-broker=AzureServiceBus  (or ASPIRE_MESSAGING_BROKER below)
//   appsettings  : { "Parameters": { "messaging-broker": "AzureServiceBus" } }
// We check the Aspire "Parameters:" section first (the natural home for an
// AppHost parameter), then a flat ASPIRE_MESSAGING_BROKER env var as a
// convenience. Broker name constants come from the shared module so the literal
// strings cannot drift between orchestrator, services, and extensions (ADR-0001).
var brokerChoice =
    builder.Configuration["Parameters:messaging-broker"]
    ?? builder.Configuration["ASPIRE_MESSAGING_BROKER"]
    ?? BrokerNames.RabbitMq;

// The two transports return different resource-builder types; both expose a
// connection string, so we keep them behind the common
// IResourceWithConnectionString interface and reference 'messaging' uniformly.
IResourceBuilder<IResourceWithConnectionString> messaging;

// On the Azure Service Bus *emulator* path only, we also inject a SECOND
// connection string ('messaging-admin') that points at the emulator's separate
// management/HTTP plane (container port 5300) instead of the AMQP data plane
// (5672). MassTransit's startup topology creation uses the Service Bus
// Administration Client, which speaks HTTP to the management plane; a single
// connection string carries a single host:port, so one string cannot serve both
// planes on the emulator (see ADR-0001). A real Azure namespace needs no second
// string (one endpoint serves both planes), so this stays null off the emulator
// path — the MassTransit extension then keeps its single-connection-string code
// path and real namespaces remain zero-code-change.
ReferenceExpression? messagingAdminConnectionString = null;

if (string.Equals(brokerChoice, BrokerNames.AzureServiceBus, StringComparison.OrdinalIgnoreCase))
{
    // Azure Service Bus. Local dev defaults to the emulator (RunAsEmulator),
    // so clone-and-run needs no Azure subscription, secrets, or cost. A real
    // Azure namespace is a user-secrets 'messaging' connection-string override
    // with zero code change (it replaces the emulator-provided value).
    var serviceBus = builder.AddAzureServiceBus("messaging")
        .RunAsEmulator(emulator =>
        {
            // Aspire 13.1.1 pins the emulator image to 1.1.2, whose port-5300
            // endpoint only exposes a /health API — NOT the Service Bus
            // management protocol. Administration Client support (the management
            // plane the topology creation needs) arrived in emulator image
            // 2.0.0 (released 2026-01-16). Pin it explicitly so MassTransit's
            // CreateTopic/CreateSubscription calls have a management endpoint to
            // talk to. (See ADR-0001 and the PR #8 manual-QA checklist.)
            emulator.WithImageTag("2.0.0");
        });

    messaging = serviceBus;

    // Build the admin connection string from the emulator's 'emulatorhealth'
    // endpoint (Aspire's name for the container's port-5300 mapping). We
    // interpolate its host-mapped HostAndPort (the dynamically allocated proxy
    // port the host process can reach — the same resolution the AMQP data-plane
    // string uses) and reuse the emulator's fixed well-known SAS key. The
    // 'UseDevelopmentEmulator=true' flag is what tells the Azure SDK to target
    // the emulator (and, on Azure.Messaging.ServiceBus >= 7.20.1, to honour the
    // explicit :5300-style port rather than resetting it).
    var adminEndpoint = serviceBus.GetEndpoint("emulatorhealth");
    messagingAdminConnectionString = ReferenceExpression.Create(
        $"Endpoint=sb://{adminEndpoint.Property(EndpointProperty.HostAndPort)};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;");
}
else
{
    // RabbitMQ (default).
    // Slot 1: keep .WithDataVolume() for durable broker state — today's behaviour.
    // Slots 2–5: ephemeral (no volume); scratch worktrees need no broker durability.
    //
    // Container naming:
    // Aspire/DCP generates container names from the resource name + a per-run
    // random suffix (e.g. "messaging-abc12345"). That suffix makes simultaneous
    // multi-slot boots safe on the *name* — two slots don't collide.
    // However, Docker volumes ARE deterministic by name (.WithDataVolume uses
    // a fixed name derived from the solution/project). Removing .WithDataVolume()
    // for slots 2–5 eliminates the only deterministic shared-state collision path.
    //
    // Acceptance check #2: because DCP appends a random run suffix to container
    // names, two simultaneous RabbitMQ containers (slot 1 + slot 2) will have
    // distinct Docker names without any additional .WithContainerName call.
    // The only collision risk was the data volume; that is resolved by the
    // conditional below. No .WithContainerName override needed.
    var messagingPassword = builder.AddParameter("messaging-password");
    var rabbitMq = builder.AddRabbitMQ("messaging", password: messagingPassword)
        .WithManagementPlugin();

    if (slot == 1)
    {
        rabbitMq.WithDataVolume();
    }

    messaging = rabbitMq;
}

// Add Apis — ports derived from slot: value(base) = base + 100 * (slot - 1)
var identityApi = builder.AddProject<Projects.Identity_WebApi>("identity-webapi")
    .WithHttpsEndpoint(port: WorktreeSlot.Port(7010, slot), name: "identity-https");

var schedulingApi = builder.AddProject<Projects.Scheduling_WebApi>("scheduling-webapi")
    .WithHttpsEndpoint(port: WorktreeSlot.Port(7001, slot), name: "scheduling-https")
    .WithReference(messaging)
    .WithReference(identityApi)
    .WaitFor(messaging);

var billingApi = builder.AddProject<Projects.Billing_WebApi>("billing-webapi")
    .WithHttpsEndpoint(port: WorktreeSlot.Port(7002, slot), name: "billing-https")
    .WithReference(messaging)
    .WithReference(identityApi)
    .WaitFor(messaging);

// On the emulator path, hand both services the management-plane connection
// string as 'messaging-admin'. The MassTransit extension picks it up and builds
// a separate Service Bus Administration Client for topology creation; when it's
// absent (real Azure namespace or RabbitMQ), the extension stays on its single
// 'messaging' connection-string path. Env-var form mirrors Aspire's own
// ConnectionStrings__<name> convention so it lands in ConnectionStrings:messaging-admin.
if (messagingAdminConnectionString is not null)
{
    schedulingApi.WithEnvironment("ConnectionStrings__messaging-admin", messagingAdminConnectionString);
    billingApi.WithEnvironment("ConnectionStrings__messaging-admin", messagingAdminConnectionString);
}

// ---------------------------------------------------------------------------
// Slot-aware auth injections (#15)
// Slot 1: inject nothing — appsettings.json slot-1 literals govern; behaviour
//          is byte-for-byte identical to before this change.
// Slots 2–5: override Authority, CookieName, and CORS origins with slot-derived
//             values. All injections use env-var form (double-underscore separator)
//             which beats appsettings/user-secrets in .NET's config precedence.
// ---------------------------------------------------------------------------
if (slot > 1)
{
    var identityAuthority = $"https://localhost:{WorktreeSlot.Port(7010, slot)}";
    var cookieName = $"DDD.Auth.S{slot}";
    var spaOrigin = $"https://localhost:{WorktreeSlot.Port(7003, slot)}";
    var identityOrigin = identityAuthority;

    // Identity: tell the seed service which slot it is so it generates only
    // this slot's redirect/post-logout/CORS URLs into its own IdentityDb_S{N}.
    identityApi.WithEnvironment("worktree-slot", slot.ToString());

    // Scheduling API: override authority, cookie name, and CORS origins.
    schedulingApi
        .WithEnvironment("Auth__Authority", identityAuthority)
        .WithEnvironment("Auth__CookieName", cookieName)
        .WithEnvironment("Cors__AllowedOrigins__0", spaOrigin)
        .WithEnvironment("Cors__AllowedOrigins__1", identityOrigin);

    // Billing API: same overrides.
    billingApi
        .WithEnvironment("Auth__Authority", identityAuthority)
        .WithEnvironment("Auth__CookieName", cookieName)
        .WithEnvironment("Cors__AllowedOrigins__0", spaOrigin)
        .WithEnvironment("Cors__AllowedOrigins__1", identityOrigin);
}

//Add Frontends
// Add Angular app and define script to run on startup serve/start/other...
builder.AddJavaScriptApp("scheduling-angularapp", "../Frontend/Angular/Scheduling.AngularApp", "start-aspire")
    .WithReference(schedulingApi)
    .WithReference(billingApi)
    .WithReference(identityApi)
    .WithHttpsEndpoint(port: WorktreeSlot.Port(7003, slot), env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/// <summary>
/// Offsets the Aspire dashboard UI, OTLP, and resource-service ports by
/// <c>100 * (slot - 1)</c> so concurrent slots don't collide on those ports.
/// Sets env vars BEFORE CreateBuilder so they override launchSettings.json values.
/// Only called for slot &gt;= 2; slot 1 leaves launchSettings in control.
/// </summary>
static void OffsetDashboardPorts(int slot)
{
    var offset = 100 * (slot - 1);
    OffsetUrlEnvVar("ASPNETCORE_URLS", offset);
    OffsetUrlEnvVar("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", offset);
    OffsetUrlEnvVar("ASPIRE_DASHBOARD_MCP_ENDPOINT_URL", offset);
    OffsetUrlEnvVar("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL", offset);
}

/// <summary>
/// Reads the current value of <paramref name="varName"/>, bumps every port in
/// every URL (semicolon-separated) by <paramref name="offset"/>, and writes
/// it back via <see cref="Environment.SetEnvironmentVariable"/>.
/// If the variable is not set, the call is a no-op — the Aspire host will
/// allocate a dynamic port of its own.
/// </summary>
static void OffsetUrlEnvVar(string varName, int offset)
{
    var current = Environment.GetEnvironmentVariable(varName);
    if (string.IsNullOrEmpty(current))
        return;

    var urls = current.Split(';', StringSplitOptions.RemoveEmptyEntries);
    var bumped = urls.Select(url => BumpPort(url, offset));
    Environment.SetEnvironmentVariable(varName, string.Join(";", bumped));
}

/// <summary>
/// Parses <paramref name="url"/> with <see cref="UriBuilder"/> and adds
/// <paramref name="offset"/> to its port. Returns the original string unchanged
/// if the URL cannot be parsed or has no explicit port.
/// </summary>
static string BumpPort(string url, int offset)
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

/// <summary>
/// Walks up the directory tree from <paramref name="startDirectory"/> looking
/// for the Aspire.AppHost.csproj file so the .worktree-slot file is resolved
/// relative to the project root regardless of whether the AppHost is launched
/// from Visual Studio, <c>dotnet run</c>, or the Aspire runner (which sets
/// BaseDirectory to <c>bin/Debug/net9.0</c>).
/// </summary>
static string? FindProjectDirectory(string startDirectory)
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
