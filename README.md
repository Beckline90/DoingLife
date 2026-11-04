# Doing Life

Doing Life is an AI-powered personal guidance platform designed to help people discover their strengths, explore career and education pathways, set goals, and plan their future.

The platform aims to support students, adults, and newcomers navigating different education and career systems.

## Project Status

**Backend deployed to Microsoft Azure — Frontend integration underway.**

The authentication system is implemented, deployed, and tested. Development is continuing on user management and the platform's core features.

AI-powered guidance features have not yet been implemented.

## Features Implemented

- User registration with ASP.NET Core Identity
- Email verification and confirmation
- Resend email confirmation
- Secure login with JWT authentication
- Access and refresh token functionality
- Protected API endpoints
- User profile and preference database structure
- PostgreSQL database with EF Core migrations
- Azure Communication Services for email delivery
- CORS configuration for frontend development
- Swagger API documentation

## Currently Working On

- Frontend integration
- Expanding user profile management
- Improving API security and error handling

## Planned Features

- Google and Apple authentication
- Password reset
- AI-powered career and education guidance
- Personalized goals and progress tracking
- Educational qualification and transcript assessment
- Career pathways across different countries
- Optional one-on-one sessions with career advisors
- User-controlled AI memory
- Age-appropriate access and parental consent

## Tech Stack

| Technology | Purpose |
|---|---|
| C# / ASP.NET Core 10 | Backend API |
| PostgreSQL | Database |
| Entity Framework Core | Database management and migrations |
| ASP.NET Core Identity | User management |
| JWT | Authentication |
| Azure App Service | API hosting |
| Azure Database for PostgreSQL | Cloud database |
| Azure Communication Services | Email delivery |
| Swagger / OpenAPI | API documentation |

## API Endpoints

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/auth/register` | Register a new user |
| GET | `/api/auth/confirm-email` | Verify email address |
| POST | `/api/auth/resend-confirmation` | Resend verification email |
| POST | `/api/auth/login` | Authenticate user |
| GET | `/api/users/me` | Retrieve authenticated user |

## Live API

**API:** https://doinglife-api-f0gecrd6a7e9cqar.canadacentral-01.azurewebsites.net

**Swagger Documentation:** https://doinglife-api-f0gecrd6a7e9cqar.canadacentral-01.azurewebsites.net/swagger/index.html

## Project Architecture

The backend follows a layered architecture with separation of responsibilities:

- **Controllers:** Handle HTTP requests and responses.
- **BLL:** Contains business logic and service interfaces.
- **DAL:** Manages database access through Entity Framework Core.
- **Entities:** Represents database models.
- **ViewModel:** Defines API request and response models.
- **Shared:** Contains reusable helpers and constants.
- **Migrations:** Tracks database schema changes.

## Development

The backend is developed using C# and ASP.NET Core 10 with PostgreSQL.

Local configuration uses .NET User Secrets, while production configuration is managed through Azure App Service environment variables.

Database changes are managed through Entity Framework Core migrations.

## Author

**Beckline Matuke**

Backend Developer | C# | ASP.NET Core | PostgreSQL

[GitHub](https://github.com/Beckline90)
