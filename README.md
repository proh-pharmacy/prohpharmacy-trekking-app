# Proh Pharmacy Trekking API

Backend API for a field operations platform where trekking vans visit pharmacies across Ghana. Manages trek scheduling, delivery recording, customer ledgers, GPS tracking, and staff coordination.

## Tech Stack

| Concern | Library |
|---|---|
| Framework | ASP.NET Core 8 (Minimal APIs) |
| Routing | Carter |
| CQRS | MediatR |
| Validation | FluentValidation |
| ORM | EF Core + Npgsql (PostgreSQL) |
| Auth | JWT (custom JWTProvider) |
| Password hashing | BCrypt.Net-Next |
| Email | FluentEmail.Razor + Resend HTTP API |
| File uploads | ImageKit SDK v6 |
| PDF generation | QuestPDF |
| GPS tracking | Traccar |
| Real-time | SignalR |
| API docs | Scalar (OpenAPI via Swashbuckle) |

## Architecture

Vertical Slice Architecture (VSA) — every feature lives in `Features/<Module>/`. Each operation is its own file with its command/query, validator, handler, and Carter endpoint all in one place. No repositories, no service layers, no controllers.

```
Features/
  Customers/
    CreateCustomer.cs
    GetCustomer.cs
    UpdateCustomer.cs
    ...
    Entities/
    Enums/
  Trekking/
  Ledger/
  Staff/
  Fleet/
  ...
```

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- PostgreSQL 14+
- Docker (optional)

### Local Setup

1. Clone the repo:
   ```bash
   git clone https://github.com/proh-pharmacy/prohpharmacy-trekking-app.git
   cd prohpharmacy-trekking-app
   ```

2. Copy the example config and fill in your values:
   ```bash
   cp appsettings.example.json appsettings.json
   ```

3. Restore dependencies and run:
   ```bash
   dotnet restore
   dotnet run
   ```

Migrations apply automatically on startup. The API will be available at `http://localhost:5000` and the API docs at `http://localhost:5000/scalar`.

### Docker

```bash
docker build -t prohpharmacy-trekking-api .
docker run -p 8080:8080 --env-file .env prohpharmacy-trekking-api
```

## Configuration

Copy `appsettings.example.json` to `appsettings.json` and set the following:

| Key | Description |
|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string |
| `SiteSettings:AppKey` | Secure base64 key (min 32 chars) for JWT signing |
| `SiteSettings:FrontendUrl` | Frontend URL used in email links |
| `JwtSettings:validIssuer` | JWT issuer claim |
| `JwtSettings:validAudience` | JWT audience claim |
| `EmailSettings:ResendApiKey` | [Resend](https://resend.com) API key for transactional email |
| `TraccarSettings:BaseUrl` | Traccar server URL for GPS tracking |
| `TraccarSettings:WebhookSecret` | Secret for verifying Traccar webhook payloads |
| `ImageKitSettings:PrivateKey` | [ImageKit](https://imagekit.io) private key for file uploads |

## API Docs

Run the app and open `/scalar` in your browser. All endpoints are grouped by module (Auth, Staff, Fleet, Trekking, Customers, Ledger, etc.) with request/response schemas.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on branching, commit style, and the feature slice pattern used throughout this project.

## License

Private — Proh Pharmacy. All rights reserved.
