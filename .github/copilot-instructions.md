# Repository AI Instructions

`AGENTS.md` is mandatory. `docs/engineering/rules/11-ai-slop.md` is also mandatory for every code-changing task.

## Before changing code

1. Read `AGENTS.md`, the rules index, and all applicable rules.
2. Inspect the target feature and its neighboring implementations.
3. Follow actual repository contracts; never invent API, schema, configuration, package, or domain behavior.
4. Look for existing framework features and abstractions before introducing anything new.
5. Implement the smallest complete solution.

## Reject AI slop

Do not submit code just because it looks polished or compiles. Reject placeholders, fake implementations, speculative architecture, pass-through wrappers, duplicate logic, excessive comments, meaningless tests, broad exception swallowing, unbounded work, unnecessary dependencies, unrelated cleanup, and unsupported claims.

Every new type, file, abstraction, dependency, cache, retry, layer, and test must have a specific reason connected to the requirement or a demonstrated risk. If the reason cannot be stated plainly, do not add it.

Prefer direct, cohesive code over architecture theater. Do not create files, interfaces, factories, managers, generic repositories, mediator pipelines, or folders just to make a solution look enterprise-grade.

## Verification is mandatory

Build the affected project, run relevant tests and analyzers, inspect warnings, verify database/migration effects, and check security and concurrency semantics. Tests must prove observable behavior rather than merely mock implementation details.

Never claim a test, build, benchmark, security review, or performance improvement that was not actually verified. Report incomplete verification and remaining risks explicitly.
