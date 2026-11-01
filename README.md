# Doing Life

Doing Life is an AI-powered personal guidance platform that helps people work out what they want to do with their lives and build a realistic path toward it. It covers education, careers, skills, and personal goals, for people at any stage of life, including newcomers navigating an unfamiliar education system.

> **Status: early development.** The backend is being built first. This repository currently contains the account and authentication foundation. The AI features are not built yet.

## Vision

Most guidance tools give generic advice. Doing Life aims to build a long-term understanding of each person (interests, strengths, education, goals) and adapt its recommendations as they grow.

Planned examples:

- A 16-year-old exploring careers based on their strongest subjects and interests
- An international student comparing their high school credentials with Canadian admission requirements and finding gaps or bridging programs
- Optional one-on-one sessions with human advisors and mentors

## What's built so far

- **User accounts** on ASP.NET Core Identity, using the existing `users` table with an integer key
- **Registration** that creates the user, profile, and preference rows in one database transaction
- **Email verification** with encoded confirmation tokens, a browser confirmation page, and a resend endpoint that doesn't reveal whether an account exists
- **Age gate:** date of birth is collected at registration, and the 18+ rule is enforced on the server
- **Input validation** for email format and person names, kept in one shared constants file
- **A result pattern** (`ServiceResult` and `ServiceResult<T>`) so services report expected failures without throwing exceptions
- **EF Core migrations** as the single source of truth for the PostgreSQL schema, with snake_case naming
- **Secrets kept out of source control** through user secrets in development

## In progress

- Login that blocks unverified emails
- JWT access tokens with hashed, rotating refresh tokens stored per session
- Logout by revoking sessions

## Roadmap

- Google and Apple sign-in
- Parental-consent flow for users under 18
- Password reset
- AI guidance with user-controlled memory
- Document upload (for example transcripts) with private, access-controlled storage
- Education and career pathway recommendations
- Advisor and mentor booking
- Deployment to Azure

## Tech stack

| Area | Technology |
|---|---|
| Language and framework | C#, ASP.NET Core Web API |
| Authentication | ASP.NET Core Identity, JWT (in progress) |
| Database | PostgreSQL |
| ORM | Entity Framework Core with Npgsql, code-first migrations |
| API docs | Swagger / OpenAPI |

## Project structure

```
backend/DoingLife.WebApi
├── Controllers/    HTTP endpoints
├── BLL/            Business logic (services and interfaces)
├── DAL/            AppDbContext
├── Entities/       Database entities (User inherits IdentityUser<int>)
├── ViewModel/      Request and response models
├── Shared/         Helpers and constants
└── Migrations/     EF Core migrations
```

## API endpoints

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/auth/register` | Create an account and send a verification email |
| GET | `/api/auth/confirm-email` | Confirm an email address from the emailed link |
| POST | `/api/auth/resend-confirmation` | Send a new verification email |

## Design decisions

- **One `AppDbContext` inheriting `IdentityDbContext`.** Identity and application tables share one context, one migration history, and working foreign keys.
- **Thin controllers, services hold the logic.** Controllers translate results into HTTP responses, and services stay reusable.
- **Never return entities.** Responses use dedicated models, so fields like password hashes never leave the API.
- **The server enforces the rules.** The age check and validation run in the API, not only in a client.
- **Store token hashes, not tokens.** Refresh tokens will be saved only as hashes.
- **Don't leak account existence.** Resend responses are the same whether or not the account exists.

## Running locally

**Prerequisites:** the .NET SDK, PostgreSQL, and Visual Studio or the `dotnet` CLI.

1. Clone the repository.
2. Create an empty PostgreSQL database.
3. Set secrets for the project (user secrets, not `appsettings.json`):

```bash
cd backend/DoingLife.WebApi
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=doinglife;Username=YOUR_USER;Password=YOUR_PASSWORD"
dotnet user-secrets set "Jwt:Key" "a-long-random-string-of-at-least-32-characters"
```

4. Check that `App:BaseUrl` in `appsettings.json` matches your local HTTPS address.
5. Apply the migrations and run:

```bash
dotnet ef database update
dotnet run
```

6. Open `/swagger` on your local HTTPS address.

In development, verification emails are written to the application log instead of being sent. Copy the confirmation link from the console output.

## Author

**Beckline Matuke** — backend developer focused on C#, ASP.NET Core, and PostgreSQL.
[GitHub](https://github.com/Beckline90)
