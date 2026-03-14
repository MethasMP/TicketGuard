# TicketGuard

TicketGuard is an ASP.NET Core 8 application for detecting stuck bookings, linking them to endpoint outages, recovering eligible bookings, and generating monthly operational reports.

## Key features

- Detects anomalies where payment succeeded but booking is still pending
- Runs background auto-recovery when enabled and when tracked endpoints are healthy
- Tracks endpoint health with current status and history views
- Writes audit logs for recover, ignore, and SMS notification outcomes
- Serves dashboard, reports, audit, health, and login pages
- Exposes CSV export in the current reports UI
- Generates PDF reports in backend using QuestPDF

## Quick start

### Prerequisites

- Docker Desktop
- .NET 8 SDK

### Start infrastructure

```bash
docker-compose up -d
```

This starts:

- MySQL on `localhost:3306`
- The app container on `http://localhost:5080`

### Run the app locally

```bash
cd BookingGuardian
dotnet run
```

If environment variables are not set, the app falls back to values in [`BookingGuardian/appsettings.json`](/Users/maemp/Desktop/booking-guardian/BookingGuardian/appsettings.json).

### Seeded login

From [`database/seed.sql`](/Users/maemp/Desktop/booking-guardian/database/seed.sql):

- Email: `admin@monitor.dev`
- Password: `Monitor1234!`

## What the current UI exposes

- `/` dashboard
- `/reports`
- `/health/history`
- `/audit`
- `/Account/Login`

Important:

- The current reports page exposes a CSV download button only, in [`BookingGuardian/Views/Reports/Index.cshtml`](/Users/maemp/Desktop/booking-guardian/BookingGuardian/Views/Reports/Index.cshtml).
- PDF generation exists in backend services and the reports controller, but it is not currently linked from the reports page.

## Report exports

- UI button: `/reports/download?month=YYYY-MM&format=csv`
- Backend endpoint supports:
  - CSV
  - PDF

Current monthly PDF generation is implemented through `MonthlyPdfReportService` and includes:

- total anomalies detected
- resolved
- ignored
- unresolved
- revenue recovered
- mean time to resolve
- endpoint uptime
- top causes
- recommendations

## How the system works

### Background jobs

- `AnomalyDetectionJob`
  - Detects stuck bookings
  - Links anomalies to nearby `DOWN` endpoint health records when possible
  - Auto-recovers open anomalies with a 1 second stagger between attempts
  - Halts recovery for the run if any latest tracked endpoint status is `DOWN`

- `EndpointHealthCheckJob`
  - Polls configured endpoints
  - Stores `UP`, `DEGRADED`, or `DOWN` snapshots
  - Suppresses redundant unchanged snapshots to reduce data growth

- `MonthlyReportEmailJob`
  - Generates last month's PDF report
  - Sends it on day 1 after 08:00 UTC when auto-send, SMTP host, and recipients are configured

### Core services

- `BookingService`
  - recover single anomaly
  - ignore anomaly
  - bulk recover anomalies atomically
  - write audit logs
  - attempt SMS notification after successful recovery

- `ReportService`
  - builds report page view models
  - builds monthly report data
  - exports monthly CSV

- `MonthlyPdfReportService`
  - generates monthly PDF reports with QuestPDF

- `SmsNotificationService`
  - sends recovery notifications through an external HTTP endpoint when enabled

- `PaymentGatewayService`
  - currently simulated
  - does not call a real provider API yet

## Tech stack

- .NET 8
- ASP.NET Core MVC + Web API
- Entity Framework Core
- MySQL 8
- Serilog
- QuestPDF
- xUnit + Moq
- Docker Compose

## Project structure

```text
booking-guardian/
├── BookingGuardian/          # ASP.NET Core app
├── BookingGuardian.Tests/    # Unit tests
├── database/seed.sql         # MySQL schema + seed data
├── docker-compose.yml        # Local MySQL + app stack
└── Dockerfile                # App container build
```

## Configuration

Relevant settings in [`BookingGuardian/appsettings.json`](/Users/maemp/Desktop/booking-guardian/BookingGuardian/appsettings.json):

- `ConnectionStrings:DefaultConnection`
- `JwtSettings:Secret`
- `AnomalyDetection:IntervalMinutes`
- `AnomalyDetection:ThresholdMinutes`
- `AnomalyDetection:AutoRecoveryEnabled`
- `HealthCheck:IntervalMinutes`
- `HealthCheck:Endpoints`
- `SmsService:Url`
- `SmsService:ApiKey`
- `SmsService:Enabled`
- `MonthlyReport:AutoSend`
- `MonthlyReport:Recipients`
- `MonthlyReport:SmtpHost`
- `PaymentGateway:ApiKey`
- `PaymentGateway:Enabled`

Primary runtime environment variables:

- `DB_CONNECTION_STRING`
- `JWT_SECRET`

## Security and auth

- Login issues a JWT and stores it in the `JWT_TOKEN` cookie
- Authorization policies:
  - `AdminOnly`
  - `SupportOrAdmin`
- Anti-forgery validation is applied to mutating endpoints used by the UI
- Response headers include:
  - CSP
  - `X-Content-Type-Options`
  - `X-Frame-Options`
  - `Referrer-Policy`

## Tests

Run:

```bash
dotnet test BookingGuardian.sln
```

Current test coverage includes:

- anomaly detection behavior
- duplicate anomaly prevention
- endpoint outage linking
- single recovery flow
- bulk recovery transaction behavior
- audit log creation

## Limitations and implementation notes

- `PaymentGatewayService` is simulated and not integrated with a real provider API
- `SmsNotificationService` only sends when `SmsService:Enabled` is true and a target URL is configured
- `MonthlyReport:AutoSend` is `false` by default
- There is no `.env.example`; configuration currently lives in environment variables or `appsettings.json`
