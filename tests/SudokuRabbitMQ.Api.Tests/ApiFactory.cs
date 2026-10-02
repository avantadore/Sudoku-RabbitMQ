using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SudokuRabbitMQ.Core.Tests;

namespace SudokuRabbitMQ.Api.Tests;

/// <summary>The Api, talking to the test assembly's RabbitMQ container where Aspire would give it its own.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:messaging", Broker.ConnectionStringAsync().GetAwaiter().GetResult());
}
