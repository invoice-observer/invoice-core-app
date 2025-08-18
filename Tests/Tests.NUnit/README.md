# Invoice Service NUnit Tests

## Overview

This test project provides comprehensive unit tests for the `IInvoiceService` interface using NUnit framework with dependency injection support for both Mock and SQLite implementations.

## Test Structure

### Test Classes
- **InvoiceServiceGetAllAsyncTests** - tests covering `GetAllAsync()` method
- **InvoiceServiceAddAsyncTests** - tests covering `AddAsync()` method

### Implementation Coverage
- **Mock Implementation Tests** - tests for GetAllAsync and AddAsync operations
- **SQLite Implementation Tests** - tests for GetAllAsync and AddAsync operations using in-memory database


## Approach: Dependency Injection

- **ServiceTestFixture** - Configures DI container for different implementations
- **Mock Message Publisher** - Prevents actual RabbitMQ calls during tests
- **In-Memory Database** - Uses EF Core in-memory provider for SQLite tests


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
- **Microsoft.Extensions.DependencyInjection** - DI container
- **Moq 4.20.69** - Mocking framework for IMessagePublisherService
- **DataLayer Project** - Access to Invoice services and models

