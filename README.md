# Invoice Core

ASP.NET Core MVP / API implementing a primitive invoice management with JWT authentication, simple Razor Pages UI,
publishing events to RabbitMQ.

Part of Invoice Observer System. For the overall system architecture, see the
[Invoice Observer README](https://github.com/invoice-observer/.github/blob/main/profile/README.md).

## Key Features

1. Endpoints for invoice management.
2. JWT-based login for secure API access.
3. Razor Pages UI with cookie authentication.
4. Unit Tests for Data Layer and Postman Sequences for API testing.
5. Events are published to RabbitMQ in order to enable monitoring
6. [Postman Tests for API workflow verification](Tests/Tests.Postman/README.md)
7. [Unit Tests for core invoice services using NUnit](Tests/Tests.NUnit/README.md)

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

## Technologies Used

- .NET 10.0
- Entity Framework Core tools
- Postman (for API testing)
- NUnit (for unit testing)

## REST API Endpoints

- GET /api/invoices — Returns all invoices.
- GET /api/invoices/{id} — Returns a specific invoice by ID.
- POST /api/invoices — Creates a new invoice.
- PUT /api/invoices/{id} — Updates an existing invoice.
- DELETE /api/invoices/{id} — Deletes an invoice by ID.

All invoice endpoints require valid JWT authentication.

- POST /api/auth/login — Authenticates user credentials and returns a JWT token.

## Start in Development Environment

1. Clone the repository
2. [Re]configure connection strings in appsettings.json
3. Provide the login credentials, the JWT signing key and the RabbitMQ password as secrets (see below)
4. Start the API service
5. Start the console processing service

## Configuration and Secrets

No credentials are committed. The application **fails at startup** if `Auth:UserName`, `Auth:Password` or `Auth:JwtKey`
is missing or empty, or if `Auth:JwtKey` is shorter than 32 bytes (the HMAC-SHA256 minimum).

| Key                     | Purpose                                         |
|-------------------------|-------------------------------------------------|
| `Auth:UserName`         | User name accepted by `POST /api/auth/login`    |
| `Auth:Password`         | Password accepted by `POST /api/auth/login`     |
| `Auth:JwtKey`           | Symmetric key that signs and validates JWTs     |
| `RabbitMQ:UserPassword` | RabbitMQ broker password (see below)            |

- Development (User Secrets, stored outside the repository):
  ```
  dotnet user-secrets set "Auth:UserName" "<user name>" --project Projects/InvoiceCoreApp
  dotnet user-secrets set "Auth:Password" "<password>" --project Projects/InvoiceCoreApp
  dotnet user-secrets set "Auth:JwtKey" "<random key>" --project Projects/InvoiceCoreApp
  ```
  Generate a random key of at least 32 bytes, e.g. in PowerShell:
  ```
  [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
  ```
- Production: environment variables `Auth__UserName`, `Auth__Password`, `Auth__JwtKey`
  (or a Key Vault / Kubernetes secret mapped to them).

Changing `Auth:JwtKey` invalidates all previously issued tokens. Earlier revisions of this repository contained
hardcoded credentials; they are compromised and must never be reused.

### RabbitMQ

The `RabbitMQ` section of `appsettings.json` holds only non-secret values: the broker URI **without credentials**
(`amqps://host/vhost`), `UserName`, exchange, queue and routing key. The `UserPassword` is never committed;
it is supplied through the standard configuration providers and set on the `ConnectionFactory` directly,
so no URI escaping of special characters is needed and the password never appears in a string that could be logged.

- Development (User Secrets):
  ```
  dotnet user-secrets set "RabbitMQ:UserPassword" "<password>" --project Projects/InvoiceCoreApp
  ```
- Production: environment variable `RabbitMQ__UserPassword` (or a Key Vault / Kubernetes secret mapped to it).

Use `amqps://` (TLS) outside of local development and a dedicated least-privilege broker user per service.

## Testing via Postman

A Postman collection is provided for comprehensive workflow testing including:
- Authentication token acquisition
- Invoice submission
- End-to-end verification

See [Tests/Tests.Postman/README.md](Tests/Tests.Postman/README.md) for details about structure,
dependencies, and how to run the tests.

## Unit Testing

Unit test coverage for the core invoice services is provided in the NUnit test project.
See [Tests/Tests.NUnit/README.md](Tests/Tests.NUnit/README.md) for details about structure,
dependencies, and how to run the tests.
