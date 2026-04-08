---
name: aspnet-minimal-api-openapi
description: 'Create ASP.NET Minimal API endpoints with proper OpenAPI documentation'
---

# ASP.NET Minimal API with OpenAPI

Your goal is to help me create well-structured ASP.NET Minimal API endpoints with correct types and comprehensive OpenAPI/Swagger documentation.

## API Organization
- Group related endpoints using `MapGroup()`.
- Use endpoint filters for cross-cutting concerns.
- Structure larger APIs with separate endpoint classes.
- Consider a feature-based folder structure for complex APIs.

## Request and Response Types
- Define explicit request and response DTOs/models.
- Create clear model classes with proper validation attributes.
- Use record types for immutable request/response objects.
- Use meaningful property names aligned with API design standards.
- Apply `[Required]` and related attributes to enforce constraints.
- Use ProblemDetailsService and StatusCodePages to provide standard error responses.

## Type Handling
- Use strongly typed route parameters with explicit type binding.
- Use `Results` to represent multiple response types.
- Prefer `TypedResults` for strongly typed responses.
- Leverage nullable annotations and init-only properties.

## OpenAPI Documentation
- Use built-in OpenAPI document support available in .NET 9.
- Define operation summary and description.
- Add operation IDs via `WithName`.
- Add property/parameter descriptions with `[Description()]`.
- Set proper content types for requests and responses.
- Use document transformers for servers, tags, and security schemes.
- Use schema transformers to customize generated schemas.
