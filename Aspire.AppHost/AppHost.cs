var builder = DistributedApplication.CreateBuilder(args);

// Add RabbitMQ with management plugin enabled
var messagingPassword = builder.AddParameter("messaging-password");
var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
    .WithManagementPlugin()
    .WithDataVolume();

var schedulingApi = builder.AddProject<Projects.Scheduling_WebApi>("scheduling-webapi")
    .WithReference(messaging)
    .WaitFor(messaging);

var billingApi = builder.AddProject<Projects.Billing_WebApi>("billing-webapi")
    .WithReference(messaging)
    .WaitFor(messaging);

// Add Angular app — uses "start-aspire" script which reads PORT env var from Aspire
builder.AddJavaScriptApp("scheduling-angularapp", "../Frontend/Angular/Scheduling.AngularApp", "start-aspire")
    .WithReference(schedulingApi)
    .WithReference(billingApi)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();