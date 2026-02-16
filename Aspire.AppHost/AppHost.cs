var builder = DistributedApplication.CreateBuilder(args);

// Add RabbitMQ with management plugin enabled
var messagingPassword = builder.AddParameter("messaging-password");
var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
    .WithManagementPlugin()
    .WithDataVolume();

var webApi = builder.AddProject<Projects.WebApi>("webapi")
    .WithReference(messaging)
    .WaitFor(messaging);

builder.Build().Run();