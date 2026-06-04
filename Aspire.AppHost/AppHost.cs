using Aspire.AppHost;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.WorktreeSlots;

// Worktree slot (ADR-0002): slot 1 = main checkout, unchanged; slots 2–5 shift every
// port/DB/cookie by 100*(slot-1). Resolved before CreateBuilder so the dashboard port
// offset can beat launchSettings.json.
var slot = AppHostHelper.ResolveSlot();
AppHostHelper.OffsetDashboardPorts(slot);

var builder = DistributedApplication.CreateBuilder(args);

// Messaging framework — MassTransit by default, Wolverine opt-in (PRD #24).
// Read from configuration (so `--parameter`/`Parameters:messaging-framework`/env can
// override it) with a MassTransit fallback. Fanned out to both services below so the
// AppHost is one local-dev knob; production runs each service from its own config files.
var messagingFramework =
    builder.Configuration["Parameters:messaging-framework"]
    ?? builder.Configuration["ASPIRE_MESSAGING_FRAMEWORK"]
    ?? MessagingFrameworkNames.MassTransit;

// Messaging broker — RabbitMQ by default, Azure Service Bus emulator opt-in (ADR-0001).
// `messagingBroker` is the resolved name; like the framework it is fanned out to both
// services (below) so flipping the one AppHost knob moves the provisioned container AND
// each service's MessageBroker together — they can't drift locally. Outside the AppHost
// no env is injected, so other deployments keep their per-service config files.
var messaging = builder.AddConfiguredMessaging(slot, out var messagingAdmin, out var messagingBroker);

// APIs — endpoint ports derived from the slot.
var identityApi = builder.AddProject<Projects.Identity_WebApi>("identity-webapi")
    .WithHttpsEndpoint(port: WorktreeSlot.Port(WorktreeSlot.IdentityBasePort, slot), name: "identity-https");

var schedulingApi = builder.AddProject<Projects.Scheduling_WebApi>("scheduling-webapi")
    .WithHttpsEndpoint(port: WorktreeSlot.Port(WorktreeSlot.SchedulingBasePort, slot), name: "scheduling-https")
    .WithReference(messaging)
    .WithReference(identityApi)
    .WithEnvironment("MessagingFramework", messagingFramework)
    .WithEnvironment("MessageBroker", messagingBroker)
    .WaitFor(messaging);

var billingApi = builder.AddProject<Projects.Billing_WebApi>("billing-webapi")
    .WithHttpsEndpoint(port: WorktreeSlot.Port(WorktreeSlot.BillingBasePort, slot), name: "billing-https")
    .WithReference(messaging)
    .WithReference(identityApi)
    .WithEnvironment("MessagingFramework", messagingFramework)
    .WithEnvironment("MessageBroker", messagingBroker)
    .WaitFor(messaging);

// ASB emulator only: hand both services the management-plane connection string so
// MassTransit or Wolverine can create topology (null on RabbitMQ / real Azure namespaces).
if (messagingAdmin is not null)
{
    schedulingApi.WithEnvironment("ConnectionStrings__messaging-admin", messagingAdmin);
    billingApi.WithEnvironment("ConnectionStrings__messaging-admin", messagingAdmin);
}

// Per-slot overrides (authority, cookie name, CORS, DB catalog). No-op on slot 1.
builder.ApplySlotOverrides(slot, identityApi, schedulingApi, billingApi);

// Angular SPA.
builder.AddJavaScriptApp("angularapp", "../Frontend/Angular/Scheduling.AngularApp", "start-aspire")
    .WithReference(schedulingApi)
    .WithReference(billingApi)
    .WithReference(identityApi)
    .WithHttpsEndpoint(port: WorktreeSlot.Port(WorktreeSlot.SpaBasePort, slot), env: "PORT")
    .WithExternalHttpEndpoints();

// Vue SPA.
builder.AddJavaScriptApp("vueapp", "../Frontend/Vue/Scheduling.VueApp", "start-aspire")
    .WithReference(schedulingApi)
    .WithReference(billingApi)
    .WithReference(identityApi)
    .WithHttpsEndpoint(port: WorktreeSlot.Port(WorktreeSlot.VueBasePort, slot), env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();
