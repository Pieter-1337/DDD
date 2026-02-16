var builder = DistributedApplication.CreateBuilder(args);

// Add RabbitMQ with management plugin enabled
var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin()
    .WithDataVolume();

var webApi = builder.AddProject<Projects.WebApi>("webapi")
    .WithReference(messaging)
    .WaitFor(messaging);

builder.Build().Run();