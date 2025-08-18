# Invoice Core System

## Overview

Invoice processing system built with ASP.NET Core, implementing secure API
endpoints for invoice data management with RabbitMQ integration.

## Key Features

1. Endpoints for invoice management.
2. JWT-based login for secure API access.
3. Razor Pages UI with cookie authentication.
4. Unit Tests for Data Layer and Postman Sequences for API testing.

## System Architecture

Consists of two applications, developed with a concept of independent microservices in mind:

1. **API Service (ASP.NET Core Application)**
   - Authenticated endpoint for invoice submission
   - SQLite persistence via Entity Framework
   - RabbitMQ queue producer

2. **Monitoring Application (.NET Invoice Monitoring CLI)**
   - RabbitMQ queue consumer
   - Message processing and display functionality
   - Resides in a separate repository, [here](https://github.com/e-danz/invoice-monitoring-cli.git)

## Data Model

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
## REST API Endpoints

- GET /api/invoices — Returns all invoices.
- GET /api/invoices/{id} — Returns a specific invoice by ID.
- POST /api/invoices — Creates a new invoice.
- (not implemented) PUT /api/invoices/{id} — Updates an existing invoice.
- (not implemented) DELETE /api/invoices/{id} — Deletes an invoice by ID.

All invoice endpoints require valid JWT authentication.

- POST /api/auth/login — Authenticates user credentials and returns a JWT token.
 

## Development

### Requirements

- .NET 5 SDK or later
- Entity Framework Core tools
- Postman (for API testing)

### Getting Started

1. Clone the repository
2. Configure connection strings in appsettings.json
3. Run database migrations
4. Start the API service
5. Start the console processing service

### Testing

A Postman collection is provided for comprehensive workflow testing including:
- Authentication token acquisition
- Invoice submission
- End-to-end verification

## Related Projects

Works together with [Invoice Monitoring CLI](https://github.com/e-danz/invoice-monitoring-cli.git).

## Project Status

Mostly finished, not completely yet reviewed. See the [project board](https://github.com/users/e-danz/projects/2) for current priorities and progress tracking.