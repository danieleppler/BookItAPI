# BookIt – Appointment Booking API

C# / .NET 8 Web API with PostgreSQL and EF Core (Npgsql).

## Setup and run

1. Start Postgres:

   ```bash
   docker run --name bookit-db -e POSTGRES_PASSWORD=bookit -e POSTGRES_DB=bookit \
     -p 5432:5432 -d postgres:16
   ```

2. Run the API (migrations are applied and sample data is seeded automatically on startup):

   ```bash
   dotnet run
   ```

The connection string is in `appsettings.json` (`ConnectionStrings:DefaultConnection`).

## Database

- Schema is managed with EF Core migrations (`Migrations/`).
- To add a migration: `dotnet dotnet-ef migrations add <Name>` (the `dotnet-ef` tool is a local tool; run `dotnet tool restore` first).
- On startup the app runs `MigrateAsync()` and then seeds data **only if the Users table is empty**:
  - 10 users (4 business owners, 6 clients)
  - 20 businesses
  - 50 services
  - 200 appointments (±30 days around today, mixed statuses)

## Seeded login credentials

All seeded users share the password `Password123!`.

| Role          | Email                                      |
|---------------|--------------------------------------------|
| BusinessOwner | owner1@bookit.com … owner4@bookit.com      |
| Client        | client1@bookit.com … client6@bookit.com    |
