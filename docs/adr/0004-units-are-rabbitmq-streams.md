# Units are RabbitMQ streams

Status: accepted. Supersedes the channel workers of ADR 0003, and amends ADR 0002 (a grid is a cache rebuilt by replay).

The purpose of the project changed from modelling Sudoku with `System.Threading.Channels` to modelling it with RabbitMQ, which is publish/subscribe rather than point-to-point. So `System.Threading.Channels` is removed entirely, and a unit is no longer a worker that relays to its cells: it is a topic that its cells talk on. Each grid has 27 unit streams, named `sudoku.grid.<grid>.row.1`–`9`, `sudoku.grid.<grid>.column.1`–`9` and `sudoku.grid.<grid>.box.1`–`9` (boxes row by row from the top left), and each game has one steps stream, `sudoku.game.<game>.steps`, which belongs to the game rather than its grid so that watchers keep it through a replay. They are RabbitMQ streams (`x-queue-type: stream`), because a stream is read in full by every consumer, so one queue per unit really is the topic, where a classic queue hands each message to only one of its consumers.

A cell reads its row, column and box streams, and publishes `Filled` and `CandidateLost` to all three. It eliminates by itself on hearing a peer's `Filled`, so `EliminateCandidate` is no longer a command. Each unit also has a watcher on its stream, which keeps the unit's view of which cells can still hold each digit and publishes a `PlaceDeduction` addressed to a cell when it finds a hidden single. A move is a `PlaceMove` on the cell's row stream, answered through direct reply-to. Messages go through the default exchange by stream name, with their kind in the `type` property, the cell in the `row` and `column` headers, and a JSON body.

## Considered Options

- **Classic queues, one per listener** (81 cells and 27 watchers bound to a topic exchange per grid): gives each cell one ordered inbox, but 108 queues for 27 topics, so the queues no longer read as the units.
- **27 classic queues, each read by a unit worker that relays to its cells**: rejected, because that uses the topic as point-to-point, which is ADR 0003 in different clothes.
- **MassTransit or similar**: rejected, because it hides exchanges, queues and names behind its own conventions, and modelling with the broker's primitives is the point.
- **Steps stream as the game's history**: rejected for ADR 0002's reasons. Streams are transport, deleted with their grid or game.

## Consequences

- Workers still run in the Api process, on one connection per game. Each worker has one AMQP channel, so a cell's three consumers are dispatched one message at a time, and workers run in parallel with each other. Concurrency, and the non-deterministic step order of ADR 0003, are unchanged.
- Every consumer of a unit stream reads every message on it, including ones it skips. A move knows its cascade is over by a count of deliveries in flight: 10 per publish to a unit stream (9 cells and the watcher), one fewer per message handled or skipped.
- A peer that shares two units with a cell hears its `Filled` twice; elimination is idempotent.
- A grid costs 27 streams and about 109 AMQP channels, so it is a cache: disposed after a few minutes idle, which deletes its streams, and rebuilt by replay on the next request. A game in contradiction may come back as a different contradicted grid (ADR 0003).
- A grid being rebuilt by replay publishes no steps. A watcher reads the steps stream from `next`, so it sees only steps from when it started watching, and each step counts one delivery per watcher towards the cascade being over, so a finished move has been read by every watcher.
- Tests need a real broker, started with Testcontainers, which needs Docker Engine API 1.44 or later (Docker Desktop 4.27+); `SUDOKU_TEST_RABBITMQ` points them at an existing broker instead.
