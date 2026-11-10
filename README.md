# Doing Life

Doing Life is an AI-powered personal guidance platform that helps people discover their strengths, explore career and education pathways, set goals, and plan their future.

It is built for students, adults changing direction, and newcomers navigating an unfamiliar education system, such as international students comparing their qualifications with Canadian requirements.

## Project Status

**Backend deployed to Microsoft Azure. Front-end integration is underway.**

The authentication system is built, deployed, and tested end to end with a live web front end. Development is moving on to user profile management and the platform's first guidance feature. The AI features are not built yet.

## Features Implemented

**Accounts and verification**
- Registration on ASP.NET Core Identity, with date of birth and terms acceptance collected at sign-up
- Server-enforced 18+ age rule
- Email verification through a link to the front end, confirmed with a `POST` request
- Resend verification email that never reveals whether an account exists
- Verification emails sent through Azure Communication Services

**Login and sessions**
- Login with JWT access tokens (15 minutes) and rotating refresh tokens (7 days)
- Refresh tokens stored only as SHA-256 hashes
- Reuse detection: presenting an already-used refresh token revokes all of that user's sessions
- Logout of one session, and logout of all devices
- Account lockout after repeated failed logins
- Login history recording both successes and failures, with IP address and user agent
- Unverified, inactive, and deleted accounts blocked at login and refresh

**Password recovery**
- Password reset by emailed link, completed through the front end with a `POST` request
- Single-use reset tokens, so a link stops working once it has been used
- A reset signs the user out of every device and lifts any lockout
- The "forgot password" response is identical for known and unknown emails

**Error handling**
- Machine-readable error codes on failed responses (for example `invalid_credentials`, `email_not_verified`, `account_locked`, `invalid_token`), so front ends don't need to parse message text
- Status codes that match the failure: 401 for bad credentials, 403 for an unverified or unavailable account, 423 for a locked account

**Platform**
- Protected endpoint (`GET /api/users/me`) and a bearer-token button in Swagger
- Rate limiting on authentication endpoints
- CORS limited to approved front-end origins
- Forwarded-header handling for the Azure proxy
- ASP.NET Core Data Protection keys persisted to Azure Blob Storage, so email links survive restarts and deployments
- PostgreSQL schema managed with EF Core code-first migrations
- Startup validation of required configuration (the app refuses to start with a missing or weak JWT key)

## In Progress

- User profile and preference endpoints
- Front-end integration

## Roadmap

- Education background and goals, starting with guidance for studying in Canada
- AI-powered career and education guidance with user-controlled memory
- Transcript and qualification assessment
- Google and Apple sign-in
- Parental-consent flow for users under 18
- Optional one-on-one sessions with advisors
- Automated tests and a deployment pipeline
- Custom domain, with the refresh token moved to an `HttpOnly` cookie for the web app

## Tech Stack

| Technology | Purpose |
|---|---|
| C# / ASP.NET Core 10 | Web API |
| ASP.NET Core Identity | User management |
| JWT | Access tokens |
| PostgreSQL | Database |
| Entity Framework Core + Npgsql | Data access and migrations |
| Azure App Service | API hosting |
| Azure Database for PostgreSQL | Managed database |
| Azure Communication Services | Transactional email |
| Azure Blob Storage | Data Protection key storage |
| Swagger / OpenAPI | API documentation |

## API Endpoints

| Method | Endpoint | Description | Auth |
|---|---|---|---|
| POST | `/api/auth/register` | Create an account and send a verification email | No |
| POST | `/api/auth/confirm-email` | Confirm an email address with `userId` and `token` | No |
| POST | `/api/auth/resend-confirmation` | Send a new verification email | No |
| POST | `/api/auth/login` | Log in and receive access and refresh tokens | No |
| POST | `/api/auth/refresh` | Exchange a refresh token for a new token pair | No |
| POST | `/api/auth/logout` | End one session | No |
| POST | `/api/auth/logout-all` | End every session for the signed-in user | Yes |
| POST | `/api/auth/forgot-password` | Send a password reset email | No |
| POST | `/api/auth/reset-password` | Set a new password with `userId` and `token` | No |
| GET | `/api/users/me` | Get the signed-in user | Yes |

## Live API

**API:** https://doinglife-api-f0gecrd6a7e9cqar.canadacentral-01.azurewebsites.net

**Swagger:** https://doinglife-api-f0gecrd6a7e9cqar.canadacentral-01.azurewebsites.net/swagger/index.html

## Security Design

- **Hashed refresh tokens.** Only a hash is stored, so a database leak can't be used to get new tokens.
- **Rotation with reuse detection.** Each refresh issues a new token and retires the old one atomically. A reused token ends all sessions for that user.
- **No account enumeration.** Login, resend, forgot-password, and verification answer the same way for known and unknown emails, and the login path does comparable work for unknown emails.
- **The server enforces the rules.** Age limits, validation, and account-status checks run in the API, not only in the client.
- **Dedicated request and response models.** Entities, with fields like password hashes, are never returned.
- **Secrets stay out of source control.** User Secrets locally, and Azure app settings in production.
- **Fails fast on bad configuration.** Missing or invalid settings stop the app at startup with a clear message.

## Project Architecture

```
backend/DoingLife.WebApi
├── Controllers/   HTTP endpoints
├── BLL/           Business logic (services and interfaces)
├── DAL/           AppDbContext
├── Entities/      Database models (User inherits IdentityUser<int>)
├── ViewModel/     Request and response models
├── Shared/        Helpers and constants
└── Migrations/    EF Core migrations
```

Controllers stay thin and translate results into HTTP responses. Services hold the business rules and return a result object (`SResult`) for expected failures instead of throwing exceptions.

## Running Locally

**Prerequisites:** the .NET 10 SDK and PostgreSQL.

1. Create an empty PostgreSQL database.
2. From `backend/DoingLife.WebApi`, set secrets (not `appsettings.json`):

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=doinglife;Username=YOUR_USER;Password=YOUR_PASSWORD"
dotnet user-secrets set "Jwt:Key" "a-random-string-of-at-least-32-characters"
```

3. Apply the migrations and run:

```bash
dotnet ef database update
dotnet run
```

4. Open `/swagger` on the local HTTPS address.

In development, verification and reset emails are written to the console instead of being sent. Copy the link from the log, and send its `userId` and `token` to the matching `POST` endpoint.

## Team

- **Backend:** Beckline Matuke
- **Front end:** built separately by a front-end developer: https://doinglifeapp-production.up.railway.app

## Author

**Beckline Matuke**

Backend Developer | C# | ASP.NET Core | PostgreSQL | Azure

[GitHub](https://github.com/Beckline90)
