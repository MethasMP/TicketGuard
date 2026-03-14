# TicketGuard

> Payment succeeded. Booking did not.
> TicketGuard finds the gap, links it to system health, and recovers what can be recovered.

TicketGuard is an ASP.NET Core 8 application for one operational problem: bookings that are paid but still left pending. It watches for those failures, records what happened, tracks upstream endpoint health, and gives operators a clean control surface for recovery and reporting.

## What makes it useful

- Detects stuck bookings where payment is `SUCCESS` but booking remains `PENDING`
- Runs automatic recovery when recovery is enabled and tracked endpoints are healthy
- Correlates booking issues with endpoint outages when evidence exists
- Records a full audit trail for recover, ignore, and SMS notification outcomes
- Ships a dashboard, audit view, health history, and reports area
- Exposes CSV export in the current reports UI
- Generates PDF reports in backend with QuestPDF

## The shape of the product

### What the current UI exposes

- `/` dashboard
- `/reports`
- `/health/history`
- `/audit`
- `/Account/Login`

### What the reports UI actually lets you download

- CSV only, from [`BookingGuardian/Views/Reports/Index.cshtml`](/Users/maemp/Desktop/booking-guardian/BookingGuardian/Views/Reports/Index.cshtml)

### What the backend supports beyond that

- `/reports/download?month=YYYY-MM&format=csv`
- `/reports/download?month=YYYY-MM&format=pdf`

PDF generation is implemented in backend services and the reports controller, but it is not currently linked from the reports page.

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
- the app container on `http://localhost:5080`

### Run the app locally

```bash
cd BookingGuardian
dotnet run
```

If runtime environment variables are not set, the app falls back to values in [`BookingGuardian/appsettings.json`](/Users/maemp/Desktop/booking-guardian/BookingGuardian/appsettings.json).

### Seeded login

From [`database/seed.sql`](/Users/maemp/Desktop/booking-guardian/database/seed.sql):

- Email: `admin@monitor.dev`
- Password: `Monitor1234!`

## How it works

### 1. Detection

`AnomalyDetectionJob` scans for bookings where:

- payment is successful
- booking is still pending
- the booking is older than the configured threshold

It creates an anomaly record and tries to attach nearby endpoint outage context when a matching `DOWN` event exists.

### 2. Safety gate

Before auto-recovery runs, the job checks the latest tracked endpoint states.

If any latest endpoint status is `DOWN`, recovery is inhibited for that run.

### 3. Recovery

When recovery is allowed, `BookingService`:

- confirms the booking
- marks the anomaly resolved
- writes audit logs
- attempts post-recovery SMS notification

Bulk recovery is also supported and handled atomically.

### 4. Reporting

`ReportService` builds report data for the UI and CSV exports.

`MonthlyPdfReportService` builds PDF reports with QuestPDF, including:

- total anomalies detected
- resolved
- ignored
- unresolved
- revenue recovered
- mean time to resolve
- endpoint uptime
- top causes
- recommendations

## Main runtime pieces

### Background jobs

- `AnomalyDetectionJob`
  - detects stuck bookings
  - links anomalies to relevant outage evidence
  - auto-recovers open anomalies with a 1 second stagger
  - halts recovery for the run when tracked endpoints are down

- `EndpointHealthCheckJob`
  - polls configured endpoints
  - stores `UP`, `DEGRADED`, or `DOWN`
  - suppresses redundant unchanged snapshots

- `MonthlyReportEmailJob`
  - generates last month's PDF report
  - sends it on day 1 after 08:00 UTC when configured

### Core services

- `BookingService`
- `ReportService`
- `MonthlyPdfReportService`
- `SmsNotificationService`
- `PaymentGatewayService`

## Important implementation notes

- `PaymentGatewayService` is currently simulated and does not call a real provider API yet
- `SmsNotificationService` only sends when `SmsService:Enabled` is true and a target URL is configured
- `MonthlyReport:AutoSend` is `false` by default
- There is no `.env.example`; configuration currently lives in environment variables or `appsettings.json`

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

- Login issues a JWT stored in the `JWT_TOKEN` cookie
- Authorization policies:
  - `AdminOnly`
  - `SupportOrAdmin`
- Anti-forgery validation is applied to mutating endpoints used by the UI
- Response headers include CSP, `X-Content-Type-Options`, `X-Frame-Options`, and `Referrer-Policy`

## Tests

Run:

```bash
dotnet test BookingGuardian.sln
```

Current tests cover:

- anomaly detection behavior
- duplicate anomaly prevention
- endpoint outage linking
- single recovery flow
- bulk recovery transaction behavior
- audit log creation
