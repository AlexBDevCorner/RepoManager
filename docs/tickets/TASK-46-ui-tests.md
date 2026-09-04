# Task 46 — Add UI tests only where valuable

- Milestone: 8 — Convenience and hardening
- Type: testing

Do not spend enormous effort testing every XAML binding. Focus on Core unit tests + Git integration tests. ViewModels get a few tests for commands / loading / error state / mapping. Dangerous behaviour is Git logic, not whether one TextBlock renders exactly.

## Acceptance criteria

- [ ] VM tests for load/commands/errors/mapping; no brittle XAML tests.
