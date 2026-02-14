var builder = DistributedApplication.CreateBuilder(args);

// Add the WebApis
var schedulingApi = builder.AddProject<Projects.WebApi>("scheduling-api");

builder.Build().Run();
