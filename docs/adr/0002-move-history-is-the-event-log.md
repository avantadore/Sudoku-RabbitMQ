# The move history, not the step stream, is the event log

Status: amended by ADR 0003 and ADR 0004. Propagation is no longer deterministic, so a replay that reaches a contradiction may rebuild a different contradicted grid. A grid is now a cache that is rebuilt by replay after it has been idle (ADR 0004).

ADR 0001 expected the stream of steps to become the event log. We record only moves instead, and rebuild a game by replaying them, because propagation is deterministic (single-threaded on the current-thread scheduler, no randomness, fixed subscription order): the same moves in the same order always produce the same steps and the same grid. Storing the full cascade would duplicate what the rules already compute and would go stale whenever the rules change.

A replay rebuilds the grid by applying the move history up to a position on a fresh grid, done by the backend in one operation, not by the client re-sending its PUT requests. Moves after the position are kept for going forward again until a new move is made, which discards them. This is also how a game leaves a contradiction.

## Consequences

- Steps remain a live, derived stream. Anything that needs a past cascade (for example a count of deductions per move) gets it from a replay or from a cache derived while the move was applied, never as the source of truth.
- A replayed grid must be built before anything subscribes to its steps, both because `Move` throws when called from inside a step subscriber and so subscribers do not see the replayed steps as new.
- Only accepted moves enter the history, and only an accepted move discards the moves after the position. Rejected and unchanged moves leave no trace.
