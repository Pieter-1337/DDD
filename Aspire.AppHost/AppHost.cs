var builder = DistributedApplication.CreateBuilder(args);

// Add RabbitMQ with management plugin enabled
var messagingPassword = builder.AddParameter("messaging-password");
var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
    .WithManagementPlugin()
    .WithDataVolume();

// Add Apis
var schedulingApi = builder.AddProject<Projects.Scheduling_WebApi>("scheduling-webapi")
    .WithReference(messaging)
    .WaitFor(messaging);

var billingApi = builder.AddProject<Projects.Billing_WebApi>("billing-webapi")
    .WithReference(messaging)
    .WaitFor(messaging);

//Add Frontends 
// Add Angular app and define script to run on startup serve/start/other...
builder.AddJavaScriptApp("scheduling-angularapp", "../Frontend/Angular/Scheduling.AngularApp", "start-aspire")
    .WithReference(schedulingApi)
    .WithReference(billingApi)
    .WithHttpsEndpoint(port: 7003, env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();