# Sudoku-RabbitMQ

Sudoku on a grid that propagates its own constraints through RabbitMQ: every row, column and box is a stream that its nine cells talk on, as a topic. A filled cell announces it on its row, column and box, and its peers hear it and eliminate the digit themselves. See [GLOSSARY.md](GLOSSARY.md) for the domain language and [docs/adr/](docs/adr/) for the decisions behind it, in particular [ADR 0004](docs/adr/0004-units-are-rabbitmq-streams.md).

## Streams

Each grid has 27 unit streams, and each game one steps stream:

| Stream | What travels on it |
| --- | --- |
| `sudoku.grid.<grid>.row.1` … `row.9` | Row 1–9: its cells' `Filled` and `CandidateLost` events, and commands to its cells |
| `sudoku.grid.<grid>.column.1` … `column.9` | Column 1–9: the same |
| `sudoku.grid.<grid>.box.1` … `box.9` | Box 1–9, row by row from the top left: the same |
| `sudoku.game.<game>.steps` | Each move and the steps of its cascade, for whoever watches the game |

Open the RabbitMQ management UI from the Aspire dashboard to watch them during a cascade.

## Structure

| Project | Purpose |
| --- | --- |
| `src/SudokuRabbitMQ.AppHost` | Aspire AppHost: orchestrates RabbitMQ (with the management plugin), Api and Web |
| `src/SudokuRabbitMQ.ServiceDefaults` | Aspire service defaults (telemetry, health, service discovery, resilience) |
| `src/SudokuRabbitMQ.Core` | The domain: games, grids, and constraint propagation by cells and unit watchers on RabbitMQ streams |
| `src/SudokuRabbitMQ.Api` | REST API (Minimal APIs, OpenAPI, Scalar) |
| `src/SudokuRabbitMQ.Web` | Blazor Web App (Interactive Server), talks to the Api |
| `tests/SudokuRabbitMQ.Core.Tests` | xUnit v3 tests for Core, against a real broker |
| `tests/SudokuRabbitMQ.Api.Tests` | xUnit v3 tests for the Api, over HTTP and on its game store, against a real broker |
| `tests/SudokuRabbitMQ.Web.Tests` | xUnit v3 tests for the Web's API client and display mapping, and that its enums match Core's |

## Running

Requires the .NET 10 SDK, the Aspire CLI (`dotnet tool install -g Aspire.Cli`) and Docker (Engine API 1.44 or later, which is Docker Desktop 4.27+).

```sh
aspire run
```

The Aspire dashboard starts the RabbitMQ container and links to its management UI, the Api (Scalar UI at `/scalar`) and Web.

## Testing

```sh
dotnet test
```

The Core and Api tests start a RabbitMQ container with Testcontainers. To use a broker that is already running instead, set `SUDOKU_TEST_RABBITMQ` to its connection string, for example `amqp://guest:guest@localhost:5672/`.
