# Web tests

Run `dotnet test Tests/Tests.NUnit/Tests.NUnit.csproj` from the repository root.
Prerequisites: .NET 10, LibMan (the web build runs `libman restore`), and Node.js 22 or newer on PATH.

The polling tests use Node's built-in test runner, fake HTTP responses and controlled timers. No npm install is needed.
Run them separately with `node --test Tests/Tests.NUnit/Web/messaging-status.test.cjs`.

HTTP tests use an isolated mock service or in-memory SQLite. RabbitMQ startup is removed from test hosts; status tests use strict messaging mocks and real cookie/JWT authentication.
