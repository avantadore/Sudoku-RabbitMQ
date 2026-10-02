# The grid propagates its constraints reactively, and contradictions are game states

Status: partly superseded by ADR 0003 and ADR 0004 (Rx.NET replaced by channel workers, then RabbitMQ streams). The contradiction rules still hold.

Each cell is an observable source: a placement emits a step, and the cell's peers subscribe and eliminate that digit, which can set off naked and hidden singles that the game places itself as deductions. We use Rx.NET (`System.Reactive`) rather than a conventional solver loop, because the purpose of the project is to model Sudoku with `IObservable<T>`, and the same stream of steps was originally meant to become the event log once event sourcing is introduced (see ADR 0002 for what replaced that).

A move is accepted as long as its digit is a candidate for the cell, even if its cascade then leads to a contradiction. A cascade is not computed on a copy and rolled back. Instead, the game moves to the state `Contradicted` and becomes read-only. A contradiction is a step on the stream, never an `OnError`, because an error would terminate every subscription and make later undo through event sourcing impossible.

## Considered Options

- **Atomic moves**: run the cascade on a copy and reject any move that leads to a contradiction. Rejected because it hides the consequences of a move and requires cloning a grid of live subscriptions.
- **`OnError` for contradictions**: rejected because it tears down the stream.

## Consequences

- Until undo exists, a game in contradiction cannot be recovered, and the player has to start a new game. (ADR 0002 adds it.)
- Placements are final. Undo will come from event sourcing, not from reversing eliminations in place. (ADR 0002 records only moves and replays them, instead of recording the steps.)
- Moves and deductions are only placed when the digit is still a candidate, and peers eliminate a placed digit before any queued deduction runs, so a digit is never actually placed twice in a unit. That form of contradiction surfaces instead as a cell with no candidates, or as a digit with no possible cell left in a unit.
