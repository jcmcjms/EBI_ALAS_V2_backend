# Production .NET 10 / C# 14 Backend Engineering Rules

Standalone, repository-independent rules for AI-assisted development of production ASP.NET Core backends.

## Source of truth

Read `AGENTS.md`, then `docs/engineering/rules/00-index.md`. `docs/engineering/rules/11-ai-slop.md` is mandatory for every code-changing task.

## Goal

Produce the smallest complete implementation that is correct, secure, readable, maintainable, observable, and efficient under measured load. Do not confuse quality with abstraction, volume, comments, layers, package count, or generated boilerplate.

The rules explicitly reject AI slop: placeholders, fake success paths, invented contracts, speculative abstractions, copy-pasted logic, meaningless tests, unnecessary files, unexplained dependencies, security theater, performance theater, and unverified completion claims.

## Included standards

- .NET 10 and C# 14 practices
- feature-first architecture and folder discipline
- model/API contract design
- ASP.NET Core and EF Core performance
- security, data isolation, and reliability
- testing and production verification
- observability and operational safety
- mandatory AI-slop rejection and pre-completion review

These standards do not depend on a previous application, business domain, specific vendor, or fashionable architecture. Framework-native solutions and evidence-based design are preferred.
