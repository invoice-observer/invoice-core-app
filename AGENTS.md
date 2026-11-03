# AGENTS Guidelines for the current repository

## General
Before starting any task, read `../.github/.agents/AGENTS.md` if it exists and follow its instructions.
If and only if `../.github/.agents/AGENTS.md` does not exist, read `../.agents/AGENTS.md` if it exists and follow its instructions.

## Project
- .NET 10; ASP.NET Core API and Razor Pages.
- Persistence and messaging: Projects/DataLayer.
- Follow existing C# style, dependency injection, and nullable annotations.
- Keep controllers thin; use IInvoiceService and propagate CancellationToken.
- Preserve authentication and HTTP response behavior unless the task changes them.
- Keep credentials out of committed files and logs.

## Verification
- Build: dotnet build InvoiceCoreApp.sln
- Build prerequisite: libman must be available; the web project invokes it.
- Unit tests: dotnet test Tests/Tests.NUnit/Tests.NUnit.csproj
- API checks: follow Tests/Tests.Postman/README.md when relevant.
- Report checks actually run and any failures.