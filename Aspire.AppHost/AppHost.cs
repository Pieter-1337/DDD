using BuildingBlocks.Application.Messaging;

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
    // RabbitMQ (default) — exactly as before: management plugin + data volume.
    var messagingPassword = builder.AddParameter("messaging-password");
    messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
        .WithManagementPlugin()
        .WithDataVolume();
}

// Add Apis
var identityApi = builder.AddProject<Projects.Identity_WebApi>("identity-webapi")
    .WithHttpsEndpoint(port: 7010, name: "identity-https");

var schedulingApi = builder.AddProject<Projects.Scheduling_WebApi>("scheduling-webapi")
    .WithHttpsEndpoint(port: 7001, name: "scheduling-https")
    .WithReference(messaging)
    .WithReference(identityApi)
    .WaitFor(messaging);

var billingApi = builder.AddProject<Projects.Billing_WebApi>("billing-webapi")
    .WithHttpsEndpoint(port: 7002, name: "billing-https")
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

//Add Frontends
// Add Angular app and define script to run on startup serve/start/other...
builder.AddJavaScriptApp("scheduling-angularapp", "../Frontend/Angular/Scheduling.AngularApp", "start-aspire")
    .WithReference(schedulingApi)
    .WithReference(billingApi)
    .WithReference(identityApi)
    .WithHttpsEndpoint(port: 7003, env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();
