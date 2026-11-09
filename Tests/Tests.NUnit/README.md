# Invoice Service NUnit Tests

## Overview

This test project provides comprehensive unit tests for the `IInvoiceService` interface using NUnit framework with dependency injection support for Mock, EF Core in-memory and SQLite implementations. The integration tests use SQLite in-memory as a lightweight relational database, so they run without Docker. The application itself uses SQL Server, and SQL Server-specific behaviour such as indexes and execution plans is not covered by these tests.

## Test Structure

### Test Classes
- **InvoiceServiceGetAllAsyncTests** - tests covering `GetAllAsync()` method
- **InvoiceServiceAddAsyncTests** - tests covering `AddAsync()` method

### Implementation Coverage
- **Mock Implementation Tests** - tests for GetAllAsync and AddAsync operations
- **In-Memory Implementation Tests** - tests for GetAllAsync and AddAsync operations using the EF Core in-memory provider


## Approach: Dependency Injection

- **ServiceTestFixture** - Configures DI container for different implementations
- **Mock Message Publisher** - Prevents actual RabbitMQ calls during tests
- **In-Memory Database** - `ServiceTestFixture` uses the EF Core in-memory provider. `SqliteInvoiceFixture` uses SQLite in-memory.


## Running Tests:

### Command Line
```powershell
dotnet test Tests/Tests.NUnit/
```

### Visual Studio
- Test Explorer
- Run all tests or individual test classes

## Dependencies

- **NUnit 3.13.3** - Testing framework
- **Microsoft.EntityFrameworkCore.InMemory** - In-memory database for testing
- **Microsoft.EntityFrameworkCore.Sqlite** - SQLite in-memory database for integration tests
- **Microsoft.Extensions.DependencyInjection** - DI container
- **Moq 4.20.69** - Mocking framework for IMessagePublisherService
- **DataLayer Project** - Access to Invoice services and models

