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

1. Copy `demo/.env.example` to `demo/.env` if it does not exist yet, then replace the placeholders in `demo/.env`
   (`RABBITMQ_USER` must be `demo`, the `RabbitMQ:UserName` in `appsettings.Development.json`):
   ```
   if (-not (Test-Path demo/.env)) { Copy-Item demo/.env.example demo/.env }
   ```
2. Start Docker:
   ```
   docker compose -f demo/compose.yaml --env-file demo/.env up -d
   ```
3. Set the User Secrets (see [Configuration and Secrets](#configuration-and-secrets)).
4. Run the app in a separate terminal:
   ```
   dotnet run --project Projects/InvoiceCoreApp --launch-profile https
   ```
5. Run the unit tests:
   ```
   dotnet test Tests/Tests.NUnit/Tests.NUnit.csproj
   ```
6. Run the Postman collection:
   ```
   newman run Tests/Tests.Postman/Collections/InvoiceAPI.postman_collection.json -e Tests/Tests.Postman/Environments/InvoiceCore.Local.postman_environment.json --env-var auth_username=<user> --env-var auth_password=<password> --insecure
   ```

The SQL Server data lives in a named Docker volume and is deleted only by `docker compose down -v`.
The passwords in `demo/.env` take effect only when a volume is first created, so they must match the User Secrets.

## Configuration and Secrets

No credentials are committed. The application **fails at startup** if `Auth:UserName`, `Auth:Password` or `Auth:JwtKey`
is missing or empty, or if `Auth:JwtKey` is shorter than 32 bytes (the HMAC-SHA256 minimum).
With `InvoiceService` set to `SqlServer`, it also fails if `ConnectionStrings:InvoiceDb` or `Database:Password`
is missing or empty.

| Key                     | Purpose                                         |
|-------------------------|-------------------------------------------------|
| `Auth:UserName`         | User name accepted by `POST /api/auth/login`    |
| `Auth:Password`         | Password accepted by `POST /api/auth/login`     |
| `Auth:JwtKey`           | Symmetric key that signs and validates JWTs     |
| `Database:Password`     | SQL Server password                             |
| `RabbitMQ:UserPassword` | RabbitMQ broker password (see below)            |

- Development (User Secrets, stored outside the repository):
  ```
  dotnet user-secrets set "Auth:UserName" "<user name>" --project Projects/InvoiceCoreApp
  dotnet user-secrets set "Auth:Password" "<password>" --project Projects/InvoiceCoreApp
  dotnet user-secrets set "Auth:JwtKey" "<random key>" --project Projects/InvoiceCoreApp
  dotnet user-secrets set "Database:Password" "<password>" --project Projects/InvoiceCoreApp
  ```
  Generate a random key of at least 32 bytes, e.g. in PowerShell:
  ```
  [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
  ```
- Production: environment variables `Auth__UserName`, `Auth__Password`, `Auth__JwtKey`, `Database__Password`
  (or a Key Vault / Kubernetes secret mapped to them).
- `ConnectionStrings:InvoiceDb` is set only in `appsettings.Development.json`. Outside Development, supply it as the
  environment variable `ConnectionStrings__InvoiceDb`, without a password; `Database:Password` is added at startup.

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
