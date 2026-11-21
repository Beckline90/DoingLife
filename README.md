# Doing Life

Doing Life is an AI-powered personal guidance platform that helps people discover their strengths, explore career and education pathways, set goals, and plan their future.

It is built for students, adults changing direction, and newcomers navigating an unfamiliar education system, such as international students comparing their qualifications with Canadian requirements.

## Project Status

**Backend deployed to Microsoft Azure.**

Authentication, user profiles and user preferences are built and covered by automated tests that run against a real PostgreSQL database. Development is moving on to account security (change password, change email, delete account) and the platform's first guidance feature. The AI features are not built yet.

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

**Profile**
- Read and update the signed-in user's first name, last name and bio
- `PUT` replaces the whole profile, so a missing bio clears it
- Email and date of birth are returned but cannot be changed through the profile endpoint
- Names and bio are saved together in a single transaction, or not at all
- Every operation re-checks that the account is active, confirmed, not deleted and not locked out, so a valid token cannot outlive a closed account
- A missing profile row is logged and refused, never silently repaired

**Preferences and consent**
- Language, time zone, notifications and memory settings
- `PATCH` writes only the settings that are sent, so two devices changing different settings do not overwrite each other, and an empty request changes nothing
- Language is checked against a supported list, and the time zone against real time zones
- Memory is on by default, and the server records when the user made the memory choice. The client cannot set that record, so consent cannot be forged

**Error handling**
- Machine-readable error codes on failed responses (for example `invalid_credentials`, `email_not_verified`, `account_locked`, `invalid_token`), so front ends don't need to parse message text
- Status codes that match the failure: 400 for invalid input, 401 for bad credentials or an account that is no longer allowed, 403 for an unverified or unavailable account at login, 409 for a conflict, 423 for a locked account, and 500 for a fault on the server's side
- Server-side faults return a generic message, and the detail goes to the log

**Platform**
- Protected endpoints and a bearer-token button in Swagger
- Rate limiting on authentication endpoints
- CORS limited to approved front-end origins
- Forwarded-header handling for the Azure proxy
- ASP.NET Core Data Protection keys persisted to Azure Blob Storage, so email links survive restarts and deployments
- PostgreSQL schema managed with EF Core code-first migrations
- Startup validation of required configuration (the app refuses to start with a missing or weak JWT key)
- GitHub Actions workflow that builds the solution and runs the full test suite on every push and pull request

## In Progress

- Account security: change password, change email (confirmed from the old inbox), and account deletion with a grace period
- Education background and goals for the first guidance feature

## Roadmap

- AI-powered career and education guidance with user-controlled memory (the AI module will record nothing until the memory choice has been made)
- Transcript and qualification assessment
- Google and Apple sign-in
- Parental-consent flow for users under 18
- Optional one-on-one sessions with advisors
- Automated deployment to Azure (continuous delivery)
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
| xUnit | Test framework |
| Testcontainers | Throwaway PostgreSQL in Docker for the tests |
| WebApplicationFactory | Runs the real app in memory for HTTP-level tests |
| GitHub Actions | Continuous integration |
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
| GET | `/api/users/me/profile` | Get the profile (names, email, bio, date of birth) | Yes |
| PUT | `/api/users/me/profile` | Replace first name, last name and bio | Yes |
| GET | `/api/users/me/preferences` | Get language, time zone, notifications and memory settings | Yes |
| PATCH | `/api/users/me/preferences` | Change only the settings that are sent | Yes |

## Live API

**API:** https://doinglife-api-f0gecrd6a7e9cqar.canadacentral-01.azurewebsites.net

**Swagger:** https://doinglife-api-f0gecrd6a7e9cqar.canadacentral-01.azurewebsites.net/swagger/index.html

## Security Design

- **Hashed refresh tokens.** Only a hash is stored, so a database leak can't be used to get new tokens.
- **Rotation with reuse detection.** Each refresh issues a new token and retires the old one atomically. A reused token ends all sessions for that user.
- **The user id comes only from the signed token.** No route, query string or request body can name another user's data.
- **Requests carry only what a client may change.** A client cannot send an email, date of birth, account flag or consent timestamp, because the request models do not define them.
- **Account status is checked on every request**, not only at login, so closing an account takes effect without waiting for a token to expire.
- **Consent is recorded by the server.** The memory choice and its timestamp are never accepted from the client.
- **No account enumeration.** Login, resend, forgot-password, and verification answer the same way for known and unknown emails, and the login path does comparable work for unknown emails.
- **The server enforces the rules.** Age limits, validation, and account-status checks run in the API, not only in the client.
- **Dedicated request and response models.** Entities, with fields like password hashes, are never returned.
- **Secrets stay out of source control.** User Secrets locally, and Azure app settings in production.
- **Fails fast on bad configuration.** Missing or invalid settings stop the app at startup with a clear message.

## Project Architecture

```
backend/
├── DoingLife.WebApi/          the API
│   ├── Controllers/           HTTP endpoints
│   ├── BLL/                   Business logic (auth and user services)
│   ├── DAL/                   AppDbContext
│   ├── Entities/              Database models (User inherits IdentityUser<int>)
│   ├── ViewModel/             Request and response models
│   ├── Shared/                Helpers and constants
│   └── Migrations/            EF Core migrations
└── DoingLife.WebApi.Tests/    automated tests
    ├── Infrastructure/        PostgreSQL container, in-memory app factory, fake email service
    ├── Auth/                  registration, verification, login, refresh, logout, password reset
    ├── Users/                 profile and preferences
    └── Endpoints/             HTTP-level tests of the real pipeline
```

Controllers stay thin and translate results into HTTP responses. Services hold the business rules and return a result object (`SResult`) for expected failures instead of throwing exceptions.

## Testing

The project has about 280 automated tests, in two layers.

**Service-level integration tests** run the real services, ASP.NET Core Identity and EF Core against a temporary PostgreSQL container started with Testcontainers, using the project's real migrations. Only the email sender is replaced, with a fake that captures messages so tests can follow verification and reset links. They cover registration, email verification, login and lockout, refresh-token rotation and reuse detection (including simultaneous requests), logout, password reset, profile and preferences. Running against a real database means the tests exercise the same transactions and queries that run in production.

**HTTP-level tests** start the real application in memory and send real requests. They check what the service tests cannot: a missing, expired, tampered or wrongly signed token is refused with a 401, one user can never read or change another's data, extra fields in a request body are ignored, invalid input returns the right status code, and the JSON the client receives has exactly the documented fields.

Every important protection was checked by breaking the code on purpose and watching the matching test fail.

## Running Locally

**Prerequisites:** the .NET 10 SDK, PostgreSQL, and Docker (for the tests).

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

**Tests:** make sure Docker is running, then run `dotnet test` from `backend/DoingLife.WebApi.Tests`. The first run downloads the PostgreSQL image, so it takes longer.

## Team

- **Backend:** Beckline Matuke
- **Front end:** built separately by a front-end developer: https://doinglifeapp-production.up.railway.app

## Author

**Beckline Matuke**

Backend Developer | C# | ASP.NET Core | PostgreSQL | Azure

[GitHub](https://github.com/Beckline90)
