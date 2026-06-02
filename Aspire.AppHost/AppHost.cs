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

if (string.Equals(brokerChoice, BrokerNames.AzureServiceBus, StringComparison.OrdinalIgnoreCase))
{
    // Azure Service Bus. Local dev defaults to the emulator (RunAsEmulator),
    // so clone-and-run needs no Azure subscription, secrets, or cost. A real
    // Azure namespace is a user-secrets 'messaging' connection-string override
    // with zero code change (it replaces the emulator-provided value).
    messaging = builder.AddAzureServiceBus("messaging")
        .RunAsEmulator();
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

//Add Frontends
// Add Angular app and define script to run on startup serve/start/other...
builder.AddJavaScriptApp("scheduling-angularapp", "../Frontend/Angular/Scheduling.AngularApp", "start-aspire")
    .WithReference(schedulingApi)
    .WithReference(billingApi)
    .WithReference(identityApi)
    .WithHttpsEndpoint(port: 7003, env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();
