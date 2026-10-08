# Greenfield .NET 10 / C# 14 Backend Engineering Rules

This repository contains standalone AI and engineering rules for building production-grade ASP.NET Core backends from scratch.

The rules are intentionally independent of any previous application or repository. They define a default engineering standard for:

- .NET 10 and C# 14
- simple, readable, maintainable architecture
- feature-first folder structure without file dumping or artificial folder proliferation
- strong model and API contract design
- high-throughput ASP.NET Core APIs
- efficient EF Core and relational database access
- horizontal scalability for very large user populations
- security and data isolation
- automated testing and performance verification
- disciplined AI-assisted development

## Source of truth

Start with `AGENTS.md`, then `docs/engineering/rules/00-index.md`.

The rules favor framework-native .NET capabilities and measurable engineering decisions. They do not require a particular business domain, previous repository, vendor, messaging platform, cache provider, or architectural fashion.

## Design goal

The target is not "the most abstract" backend. The target is the simplest backend that remains correct, secure, fast, scalable, and easy for another engineer to understand.

For scale-sensitive code, optimize the complete request path:

```text
request
→ validation/auth
→ business logic
→ database/external I/O
→ mapping
→ serialization
→ response
```

Avoid unnecessary work at every stage.

## Included

- mandatory AI engineering contract
- architecture and folder-structure rules
- C# 14 / .NET 10 rules
- performance and scalability rules
- model, EF Core, and SQL rules
- ASP.NET Core API rules
- security rules
- testing and load-testing rules
- change-management rules
- AI development rules
- architecture decision guidance
- production operations, observability, and resilience
