var builder = DistributedApplication.CreateBuilder(args);

// The broker the grids talk on. No data volume: games live in the Api's memory, and their streams with them.
var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin();

var api = builder.AddProject<Projects.SudokuRabbitMQ_Api>("api")
    .WithHttpHealthCheck("/health")
    .WithReference(messaging)
    .WaitFor(messaging);

builder.AddProject<Projects.SudokuRabbitMQ_Web>("web")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
