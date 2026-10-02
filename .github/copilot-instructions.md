# EBI ALAS Backend AI Instructions

Use `AGENTS.md` as the repository entry point.

The authoritative engineering rules are under `docs/engineering/rules/`. Treat those documents as mandatory repository conventions, not optional suggestions.

For every task:

1. Inspect the relevant feature, infrastructure, tests, and configuration before editing.
2. Preserve the current feature boundary unless the task explicitly requires an architectural refactor.
3. Prefer the smallest safe change that solves the problem.
4. Keep endpoints thin and move orchestration/business rules into feature services.
5. Optimize database access before adding caches or concurrency.
6. Propagate cancellation through every asynchronous I/O boundary.
7. Avoid speculative abstractions and framework-heavy patterns that do not solve a current problem.
8. Keep security, correctness, and observability intact while optimizing performance.
9. Review any file approaching or exceeding 300 lines for decomposition.
10. Before declaring completion, verify build/test expectations and document anything that could not be executed locally.

When rules appear to conflict, follow the more specific feature rule first, then the general engineering rule, and preserve existing security/correctness behavior over micro-optimizations.
