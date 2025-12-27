# Invoice Observer (and Invoice Core)

## Overall System Architecture

Invoice Observer consists of two applications, developed with a concept of independent microservices in mind:

1. **Invoice Core (ASP.NET Core Application)**
   - Authenticated endpoint for invoice submission
   - SQLite persistence via Entity Framework
   - RabbitMQ queue publisher for invoice events

2. **Invoice Monitoring (.NET Invoice Monitoring CLI)**
   - RabbitMQ queue consumer
   - Message processing and display functionality
   - Resides in a separate repository, [Invoice Monitoring](https://github.com/invoice-observer/invoice-monitoring-cli.git)

## Invoice Core

ASP.NET Core MVP / API implementing a primitive invoice management with JWT authentication, simple Razor Pages UI,
publishing events to RabbitMQ.

### Key Features

1. Endpoints for invoice management.
2. JWT-based login for secure API access.
3. Razor Pages UI with cookie authentication.
4. Unit Tests for Data Layer and Postman Sequences for API testing.
5. Events are published to RabbitMQ in order to enable monitoring
6. [Postman Tests for API workflow verification](Tests/Tests.Postman/README.md)
7. [Unit Tests for core invoice services using NUnit](Tests/Tests.NUnit/README.md)

### Data Model

```csharp
public class Invoice
{
    public string Description { get; set; }
    public DateTime DueDate { get; set; }
    public string Supplier { get; set; }
    public List<InvoiceLine> Lines { get; set; }
}

public class InvoiceLine
{
    public string Description { get; set; }
    public double Price { get; set; }
    public int Quantity { get; set; }
}
```

### Technologies Used

- .NET 5 SDK or later
- Entity Framework Core tools
- Postman (for API testing)
- NUnit (for unit testing)

### REST API Endpoints

- GET /api/invoices — Returns all invoices.
- GET /api/invoices/{id} — Returns a specific invoice by ID.
- POST /api/invoices — Creates a new invoice.
- (not implemented) PUT /api/invoices/{id} — Updates an existing invoice.
- (not implemented) DELETE /api/invoices/{id} — Deletes an invoice by ID.

All invoice endpoints require valid JWT authentication.

- POST /api/auth/login — Authenticates user credentials and returns a JWT token.

### Start in Development Environment

1. Clone the repository
2. [Re]configure connection strings in appsettings.json
3. Start the API service
4. Start the console processing service

### Testing via Postman

A Postman collection is provided for comprehensive workflow testing including:
- Authentication token acquisition
- Invoice submission
- End-to-end verification

See [Tests/Tests.Postman/README.md](Tests/Tests.Postman/README.md) for details about structure,
dependencies, and how to run the tests.

### Unit Testing

Unit test coverage for the core invoice services is provided in the NUnit test project.
See [Tests/Tests.NUnit/README.md](Tests/Tests.NUnit/README.md) for details about structure,
dependencies, and how to run the tests.
