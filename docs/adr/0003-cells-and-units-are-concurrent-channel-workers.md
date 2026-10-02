# Cells and units are concurrent channel workers

Status: superseded by ADR 0004 (RabbitMQ streams replaced channel workers). Its concurrency, cascade-over and contradiction rules carry over.

The purpose of the project changed from modelling Sudoku with `IObservable<T>` to modelling it with `System.Threading.Channels`, so Rx.NET is removed entirely. Each of the 81 cells and 27 units (`Row 1`–`9`, `Column 1`–`9`, `Box 1`–`9`, boxes numbered row by row from the top left) is a worker: an async loop on the thread pool reading its own unbounded inbox channel. Commands (`PlaceMove`, `PlaceDeduction`, `EliminateCandidate`) only ever go into a cell's inbox; events (`CellFilled`, `CandidateLost`) only ever go into a unit's inbox. A cell announces its events to its row, column and box; a unit fans a `CellFilled` out as `EliminateCandidate` to its other eight cells and sends `PlaceDeduction` when it finds a hidden single. `Channel<T>` is point-to-point, so a unit is a worker with its own state, not a broadcast bus.

We accept real concurrency over a single deterministic pump, because independent workers are the point of the exercise. Propagation without a contradiction still reaches the same grid regardless of order, since eliminations and singles only ever add placements. But step order is no longer deterministic, and when a cascade reaches a contradiction, which contradiction is reported and what was deduced before it can differ between runs.

## Considered Options

- **Channels drained by one deterministic pump**: keeps ADR 0002's exact replay and the exact-order step tests. Rejected because cells would not be independent workers.
- **Units as passive fan-out wrappers**: rejected because hidden singles are unit-level facts; every cell would have to track all its peers' candidates.
- **Bounded channels**: rejected because messages flow in a cycle (cell → unit → cell), so two full inboxes could deadlock.
- **Dedicated threads per worker**: rejected because games live in memory for the process lifetime, and idle async loops cost no threads.

## Consequences

- ADR 0002 still holds that only moves are recorded and replayed, but a replay rebuilds the same grid only if the moves up to the position never reach a contradiction; otherwise it rebuilds *a* contradicted grid.
- A move knows its cascade is over by a shared count of messages in flight reaching zero. The same counter tallies the move's deductions. `Game.Move` becomes `MoveAsync`; a game handles one move at a time, and a move made during a cascade waits instead of throwing.
- The cell decides whether a move is accepted, through a reply slot on `PlaceMove`. A cell re-checks every `PlaceDeduction` on arrival (still empty, digit still a candidate, no contradiction), because a unit's view of its cells can lag.
- Contradiction keeps its rule from ADR 0001: a shared flag is set once, the first contradiction to set it becomes the step, deductions stop and eliminations continue.
- Steps are exposed through `Game.WatchSteps()`, a fresh `ChannelReader<Step>` per caller. Tests promise only cause-before-effect order and compare the rest as sets.
- A grid is `IAsyncDisposable`: it completes its channels and awaits its workers. A replay disposes the old grid.
