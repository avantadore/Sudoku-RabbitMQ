using System.Text.Json.Serialization;
using RabbitMQ.Client;
using Scalar.AspNetCore;
using SudokuRabbitMQ.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.AddRabbitMQClient("messaging");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(services => new GameStore(
    services.GetRequiredService<IConnectionFactory>(), services.GetRequiredService<TimeProvider>(), GameStore.DefaultIdleAfter));
builder.Services.AddHostedService<IdleGameSuspender>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGameEndpoints();
app.MapDefaultEndpoints();

app.Run();
