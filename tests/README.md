# tests/

Cross-cutting test suites that don't belong inside a single service's own test project.

- `contract/` — contract tests verifying service APIs/events against `packages/event-contracts`.
- `integration/` — integration tests spanning multiple services against the local Compose stack.
- `load/` — load and performance test scripts.
- `e2e/` — end-to-end tests against the full running stack, including `apps/dashboard-web`.

Not yet created.
