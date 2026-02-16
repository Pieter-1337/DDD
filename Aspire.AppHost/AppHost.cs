var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("sql")
    .WithDataVolume()
    .AddDatabase("scheduling-db");

// Add RabbitMQ with management plugin enabled
var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin()
    .WithDataVolume();

var webApi = builder.AddProject<Projects.WebApi>("webapi")
    .WithReference(sqlServer)
    .WithReference(messaging)
    .WaitFor(sqlServer)
    .WaitFor(messaging);

builder.Build().Run();