# PRD: TicketGuard

**Document Status:** Draft  
**Version:** 1.0.0  
**Author:** Methas Pakpoompong  
**Created:** March 2026  
**Last Updated:** March 2026  

---

## Table of Contents

1. [Overview](#1-overview)
2. [Problem Statement](#2-problem-statement)
3. [Goals & Non-Goals](#3-goals--non-goals)
4. [User Personas](#4-user-personas)
5. [User Stories](#5-user-stories)
6. [Functional Requirements](#6-functional-requirements)
7. [Non-Functional Requirements](#7-non-functional-requirements)
8. [System Architecture](#8-system-architecture)
9. [Data Model](#9-data-model)
10. [API Specification](#10-api-specification)
11. [UI / UX Requirements](#11-ui--ux-requirements)
12. [Security Requirements](#12-security-requirements)
13. [Error Handling & Logging](#13-error-handling--logging)
14. [Testing Requirements](#14-testing-requirements)
15. [Deployment](#15-deployment)
16. [Success Metrics](#16-success-metrics)
17. [Out of Scope / Future Work](#17-out-of-scope--future-work)
18. [Glossary](#18-glossary)

---

## 0. Project Identity

| Field | Value |
|-------|-------|
| **Repo name** | `booking-guardian` |
| **Description** | A support tool that watches for broken bookings and helps engineers fix them safely. |
| **Root folder** | `booking-guardian/` |

### Folder Structure
```
booking-guardian/
├── src/
│   └── BookingGuardian/              ← dotnet new mvc -o BookingGuardian
│       ├── Controllers/
│       ├── Models/
│       ├── Views/
│       ├── Services/
│       └── BackgroundServices/
├── tests/
│   └── BookingGuardian.Tests/
├── database/
│   └── seed.sql
├── .gitignore
├── docker-compose.yml
├── README.md
└── PRD.md
```

### Tech Stack
| Layer | Technology |
|-------|-----------|
| Runtime | .NET 8 |
| Language | C# 12 |
| Framework | ASP.NET Core MVC (single project — Controller = backend, Razor View = frontend) |
| JSON endpoints | Controller returns `Json()` — no separate Web API project, no CORS needed |
| ORM | Entity Framework Core 8 |
| Database | MySQL 8.0 |
| Auth | ASP.NET Core Identity + JWT Bearer |
| Logging | Serilog → Console + MySQL sink |
| Scheduler | .NET BackgroundService (built-in) |
| Testing | xUnit + Moq |
| Container | Docker + docker-compose |

---

## 1. Overview

### 1.1 Summary

**TicketGuard** is an internal web application for support engineers at online ticket booking platforms (e.g. bus, train, ferry). It automatically detects bookings where payment was charged successfully but the booking confirmation was never issued — and provides a safe, audited way to recover those records without direct database access.

### 1.2 Background

Online ticket booking systems process payment and booking creation as two separate steps. If the system crashes, times out, or encounters an error between those steps, the customer is debited but receives no ticket. This "stuck booking" scenario is one of the most common and damaging support issues in e-commerce — it generates angry customers, refund requests, and manual database work that is both slow and error-prone.

Today, support teams at most companies handle this by:
- Waiting for customers to call or email
- Manually querying the database to find the stuck record
- Running UPDATE statements directly in production (high risk)
- Logging the fix in a spreadsheet (no reliable audit trail)

TicketGuard replaces that entire workflow with a structured, safe, and transparent tool.

### 1.3 Elevator Pitch

> *"TicketGuard catches stuck bookings before customers do, and lets support engineers fix them in two clicks — with a full audit trail, no raw SQL, and no direct database access."*

---

## 2. Problem Statement

### 2.1 Core Problem

When `payment_status = SUCCESS` but `booking_status` remains `PENDING` beyond a reasonable timeout (10 minutes), a booking is considered **anomalous**. The customer has been charged, but has no confirmed ticket.

### 2.2 Current State Pain Points

| Pain Point | Impact |
|-----------|--------|
| No automated detection — support only finds out when a customer complains | Customer experience damage, delayed resolution |
| Recovery requires manual SQL UPDATE in production database | High risk of human error, cascading failures |
| No audit log of who fixed what, and when | Zero accountability, impossible to investigate disputes |
| No visibility into whether 3rd-party services (payment gateway, SMS) are degraded | Support team is reactive, not proactive |
| Anomaly data lives in spreadsheets | Not searchable, not shareable, not reliable |

### 2.3 Root Cause

The booking system does not implement idempotent booking creation or a compensating transaction pattern. Until that architectural fix is made (a larger engineering effort), support engineers need a safe tool to handle the fallout.

---

## 3. Goals & Non-Goals

### 3.1 Goals

- **G1** — Automatically detect anomalous bookings (payment success, booking pending > 10 minutes) without human intervention.
- **G2** — Allow support engineers to recover or ignore anomalies through a UI, without writing SQL.
- **G3** — Record a complete, immutable audit log of every action taken on every anomaly.
- **G4** — Provide real-time and historical health status for critical external endpoints (payment gateway, SMS, etc.).
- **G5** — Be deployable with a single command (`docker-compose up`).

### 3.2 Non-Goals

- **NG1** — This tool does **not** fix the root cause (non-idempotent booking creation). That is a separate engineering project.
- **NG2** — This tool does **not** automatically recover bookings without human approval. All recovery actions require explicit human confirmation.
- **NG3** — This tool does **not** process refunds. Refunds are handled by a separate finance workflow.
- **NG4** — This tool is **not** a customer-facing interface.
- **NG5** — This tool does **not** replace the existing booking system or its database.

---

## 4. User Personas

### Persona A — Support Engineer (Primary User)

| Attribute | Detail |
|-----------|--------|
| Role | Application Support Developer / L1–L3 support |
| Technical level | Can read logs and understand HTTP status codes; not comfortable writing production SQL |
| Primary need | See anomalies the moment they appear; recover them quickly with confidence |
| Fear | Accidentally making a bad UPDATE that breaks more records |
| Uses TicketGuard | Every day, multiple times per day |

### Persona B — Support Team Lead (Secondary User)

| Attribute | Detail |
|-----------|--------|
| Role | Team lead or senior engineer |
| Technical level | High — can review audit logs and spot patterns |
| Primary need | Oversight: who fixed what, how fast, are there recurring anomalies? |
| Uses TicketGuard | Weekly review of audit logs and resolution metrics |

---

## 5. User Stories

### Epic 1 — Anomaly Detection

| ID | Story | Priority |
|----|-------|----------|
| US-01 | As a support engineer, I want the system to automatically flag stuck bookings every 5 minutes, so I find out before the customer does. | Must Have |
| US-02 | As a support engineer, I want to see all open anomalies on one screen sorted by age (oldest first), so I can prioritise the most urgent ones. | Must Have |
| US-03 | As a support engineer, I want to filter anomalies by status (Open / Resolved / Ignored), so I can focus only on what needs action. | Should Have |

### Epic 2 — Recovery

| ID | Story | Priority |
|----|-------|----------|
| US-04 | As a support engineer, I want to recover a stuck booking with a single button click, so I don't need to write SQL or ask a developer. | Must Have |
| US-05 | As a support engineer, I want to be required to add a note before recovering any anomaly, so there is always a reason recorded. | Must Have |
| US-06 | As a support engineer, I want the system to reject recovery if the payment status is not SUCCESS, so I cannot accidentally confirm an unpaid booking. | Must Have |
| US-07 | As a support engineer, I want to mark an anomaly as Ignored (with a note), so I can dismiss false positives cleanly. | Should Have |

### Epic 3 — Audit Log

| ID | Story | Priority |
|----|-------|----------|
| US-08 | As a team lead, I want every recovery and ignore action to be recorded with the engineer's name, timestamp, and note, so I can audit any decision. | Must Have |
| US-09 | As a team lead, I want to export the audit log as CSV, so I can include it in weekly reports. | Should Have |

### Epic 4 — Endpoint Health

| ID | Story | Priority |
|----|-------|----------|
| US-10 | As a support engineer, I want to see the current status of all critical external endpoints (payment gateway, SMS) in real time, so I know if a third-party outage is causing new anomalies. | Must Have |
| US-11 | As a support engineer, I want to see a 24-hour uptime history per endpoint, so I can correlate outages with anomaly spikes. | Should Have |

### Epic 5 — Authentication

| ID | Story | Priority |
|----|-------|----------|
| US-12 | As a system administrator, I want all endpoints to require JWT authentication, so only authorised engineers can access the tool. | Must Have |
| US-13 | As a system administrator, I want role-based access (Admin vs Supporter), so Admin users can manage configuration while Supporters can only view and recover. | Out of Scope |

---

## 6. Functional Requirements

### 6.1 Anomaly Detection Job

**FR-01** The system SHALL run an anomaly detection job every 5 minutes via a .NET `BackgroundService`.

**FR-02** The detection query SHALL identify bookings where:
```
payment_status = 'SUCCESS'
AND booking_status = 'PENDING'
AND payment_at < NOW() - INTERVAL 10 MINUTE
```

**FR-03** For each detected anomaly, the system SHALL create one record in the `anomalies` table with `status = 'OPEN'`. If the anomaly record already exists, the system SHALL NOT create a duplicate.

**FR-04** Detection results SHALL be written to the application log via Serilog, including the count of new anomalies found per run.

---

### 6.2 Anomaly Recovery

**FR-05** `POST /api/anomalies/{id}/recover` SHALL perform the following as a single database transaction:
1. Validate that `booking.payment_status = 'SUCCESS'`
2. Validate that `anomaly.status = 'OPEN'`
3. Update `booking.booking_status → 'CONFIRMED'`
4. Update `anomaly.status → 'RESOLVED'`, set `resolved_at` and `resolved_by`
5. Insert a record into `audit_logs`

**FR-06** If any validation in FR-05 fails, the system SHALL return an appropriate HTTP 400 or 409 error with a descriptive message. No data SHALL be modified.

**FR-07** The `note` field in the recovery request body is **required** (minimum 10 characters). Requests without a valid note SHALL be rejected with HTTP 422.

---

### 6.3 Endpoint Health Check Job

**FR-08** The system SHALL run an endpoint health check job every 5 minutes.

**FR-09** The list of endpoints to check SHALL be configurable in `appsettings.json` without requiring code changes.

**FR-10** For each endpoint, the system SHALL:
1. Send an HTTP GET request with a 10-second timeout
2. Record `response_ms`, HTTP status, and derived `status` (UP / DEGRADED / DOWN)
3. Insert a record into `endpoint_health`

**FR-11** Status derivation rules:
- `response_ms < 2000` AND `HTTP 2xx` → `UP`
- `response_ms >= 2000` AND `HTTP 2xx` → `DEGRADED`
- Any non-2xx response or timeout → `DOWN`

---

### 6.4 Dashboard

**FR-12** The MVC dashboard at `/` SHALL display:
- Count of OPEN anomalies
- Count of anomalies RESOLVED today
- Current health status of all configured endpoints (UP / DEGRADED / DOWN)
- The 10 most recent OPEN anomalies with: reference number, amount, route, time since detected

**FR-13** The dashboard SHALL auto-refresh every 60 seconds without a full page reload.

---

### 6.5 Authentication

**FR-14** All API endpoints except `GET /health` (the .NET health check endpoint) SHALL require a valid JWT Bearer token.

**FR-15** `POST /api/auth/login` SHALL accept `email` + `password`, validate against the users table, and return a signed JWT with a 8-hour expiry.

**FR-16** The JWT payload SHALL include `sub` (user ID), `email`, `role`, and `exp`.

---

## 7. Non-Functional Requirements

### 7.1 Performance

| Requirement | Target |
|-------------|--------|
| Dashboard initial load | < 1 second on localhost |
| Anomaly detection job execution time | < 5 seconds for up to 10,000 bookings |
| API response time (p95) | < 300ms |

### 7.2 Reliability

**NFR-01** The recovery operation (FR-05) MUST be wrapped in a database transaction. Partial updates are not acceptable.

**NFR-02** Background jobs SHALL continue running even if one execution throws an exception. Exceptions SHALL be caught, logged, and the job SHALL retry on the next scheduled interval.

### 7.3 Maintainability

**NFR-03** Business logic SHALL reside in the `Application` layer, not in controllers or the database layer. Controllers are thin — they validate input, call a use case, and return a response.

**NFR-04** Database connection strings and secrets SHALL NOT be committed to the repository. They SHALL be provided via environment variables or `appsettings.Development.json` (git-ignored).

**NFR-05** All public methods in the `Application` layer SHALL have XML doc comments.

### 7.4 Portability

**NFR-06** The entire application (app + MySQL) SHALL be startable with `docker-compose up -d` with no additional setup steps.

---

## 8. System Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        Client Browser                        │
└───────────────────────────┬─────────────────────────────────┘
                            │ HTTP
┌───────────────────────────▼─────────────────────────────────┐
│                    ASP.NET Core Host                         │
│                                                              │
│  ┌──────────────────────────────────┐   ┌──────────────┐ │
│  │       ASP.NET Core MVC           │   │  Background  │ │
│  │  Controller (backend logic)      │   │   Services   │ │
│  │  Razor View (HTML frontend)      │   │              │ │
│  │  Controller.Json() (JSON resp.)  │   │              │ │
│  └──────────────────┬───────────────┘   └──────┬───────┘ │
│                     │                           │         │
│  ┌──────────────────▼───────────────────────────▼───────┐ │
│  │                    Services Layer                      │ │
│  │              (Business Logic / EF Core)                │ │
│  └──────────────────────────┬──────────────────────────── ┘ │
│                             │                                │
│  ┌──────────────────────────▼──────────────────────────────┐ │
│  │                  Infrastructure Layer                    │ │
│  │         (EF Core Repositories, HTTP clients)            │ │
│  └──────────────────────────┬──────────────────────────────┘ │
└─────────────────────────────┼────────────────────────────────┘
                              │
              ┌───────────────▼──────────────┐
              │          MySQL 8              │
              │  bookings | anomalies         │
              │  audit_logs | endpoint_health │
              │  users                        │
              └──────────────────────────────┘
```

### Layer Responsibilities

| Layer | Responsibility |
|-------|---------------|
| Controllers | Receive HTTP requests, call Services, return View or Json |
| Razor Views | Render HTML — uses Bootstrap + jQuery for interactivity |
| Services | Business logic (detect anomalies, recover bookings, health checks) |
| Models | EF Core entities + ViewModels |
| BackgroundServices | Scheduled jobs (anomaly detection, endpoint health ping) |

---

## 9. Data Model

### 9.1 Entity Relationship

```
users ──────────────────────────────────────────────┐
                                                     │ performed_by
bookings ──┬── anomalies ──┬── audit_logs ───────────┘
           │               │
           └───────────────┘ booking_id
                             └── endpoint_health (independent)
```

### 9.2 Table Definitions

```sql
CREATE TABLE users (
    id            INT AUTO_INCREMENT PRIMARY KEY,
    email         VARCHAR(100) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    full_name     VARCHAR(100) NOT NULL,
    role          ENUM('Admin','Supporter') NOT NULL DEFAULT 'Supporter', -- reserved for future use
    created_at    DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE bookings (
    id               INT AUTO_INCREMENT PRIMARY KEY,
    reference_no     VARCHAR(20)  NOT NULL UNIQUE,
    customer_name    VARCHAR(100) NOT NULL,
    route            VARCHAR(150) NOT NULL,
    operator_name    VARCHAR(100) NOT NULL,          -- bus company name
    passenger_count  TINYINT      NOT NULL DEFAULT 1,
    travel_date      DATE         NOT NULL,
    amount           DECIMAL(10,2) NOT NULL,
    payment_status   ENUM('PENDING','SUCCESS','FAILED') NOT NULL DEFAULT 'PENDING',
    booking_status   ENUM('PENDING','CONFIRMED','CANCELLED','RECOVERED') NOT NULL DEFAULT 'PENDING',
    payment_at       DATETIME NULL,
    created_at       DATETIME DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_anomaly_detection (payment_status, booking_status, payment_at)
);

CREATE TABLE anomalies (
    id                INT AUTO_INCREMENT PRIMARY KEY,
    booking_id        INT  NOT NULL,
    detected_at       DATETIME DEFAULT CURRENT_TIMESTAMP,
    detection_run_at  DATETIME NOT NULL,              -- which job run caught this
    status            ENUM('OPEN','RESOLVED','IGNORED') NOT NULL DEFAULT 'OPEN',
    resolved_at       DATETIME NULL,
    resolved_by       VARCHAR(100) NULL,
    note              TEXT NULL,
    FOREIGN KEY (booking_id) REFERENCES bookings(id),
    UNIQUE KEY uq_booking_anomaly (booking_id)
);

CREATE TABLE audit_logs (
    id            INT AUTO_INCREMENT PRIMARY KEY,
    action        VARCHAR(100) NOT NULL,
    entity_type   VARCHAR(50)  NOT NULL,
    entity_id     INT          NOT NULL,
    performed_by  VARCHAR(100) NOT NULL,
    ip_address    VARCHAR(45)  NULL,                 -- IPv4 or IPv6
    user_agent    VARCHAR(255) NULL,
    detail        JSON NULL,
    created_at    DATETIME DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_entity (entity_type, entity_id),
    INDEX idx_performed_at (created_at)
);

CREATE TABLE endpoint_health (
    id            INT AUTO_INCREMENT PRIMARY KEY,
    name          VARCHAR(100) NOT NULL,
    url           VARCHAR(255) NOT NULL,
    status        ENUM('UP','DEGRADED','DOWN') NOT NULL,
    response_ms   INT NULL,
    http_code     INT NULL,
    checked_at    DATETIME DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_name_time (name, checked_at)
);
```

### 9.3 Seed Data

```sql
-- 20 mock bookings, 5 are anomalous (payment=SUCCESS, booking=PENDING, payment_at > 10 min ago)
INSERT INTO users (email, password_hash, full_name, role) VALUES
  ('admin@monitor.dev', '<bcrypt_hash>', 'Admin User', 'Admin');
```

Password for seed user (plaintext for dev only): `Monitor1234!`

---

## 10. API Specification

### Base URL
`https://localhost:5001/api`

### Authentication Header
`Authorization: Bearer <jwt_token>`

---

### POST /auth/login
**Request**
```json
{
  "email": "admin@monitor.dev",
  "password": "Monitor1234!"
}
```
**Response 200**
```json
{
  "token": "eyJhbGci...",
  "expiresAt": "2026-03-10T08:00:00Z",
  "user": {
    "email": "admin@monitor.dev",
    "fullName": "Admin User",
    "role": "Admin"
  }
}
```
**Response 401** — Invalid credentials

---

### GET /anomalies
**Query params:** `status` (OPEN | RESOLVED | IGNORED), `page`, `pageSize` (default 20)

**Response 200**
```json
{
  "data": [
    {
      "id": 1,
      "detectedAt": "2026-03-10T02:15:00Z",
      "minutesSinceDetected": 47,
      "status": "OPEN",
      "booking": {
        "referenceNo": "BK-20260310-042",
        "customerName": "Somchai Jaidee",
        "route": "Bangkok → Chiang Mai",
        "travelDate": "2026-03-15",
        "amount": 850.00,
        "paymentAt": "2026-03-10T01:28:00Z"
      }
    }
  ],
  "total": 3,
  "page": 1,
  "pageSize": 20
}
```

---

### POST /anomalies/{id}/recover
**Request**
```json
{
  "note": "Customer called, payment confirmed in Stripe. Recovering manually."
}
```
**Response 200**
```json
{
  "message": "Booking BK-20260310-042 successfully recovered.",
  "recoveredAt": "2026-03-10T03:02:15Z",
  "recoveredBy": "support@monitor.dev"
}
```
**Response 400** — `payment_status` is not SUCCESS  
**Response 409** — Anomaly is already RESOLVED or IGNORED  
**Response 422** — Note is missing or too short (< 10 chars)

---

### POST /anomalies/{id}/ignore
**Request**
```json
{
  "note": "Test transaction created by QA team. Not a real booking."
}
```
**Response 200**
```json
{
  "message": "Anomaly #1 marked as ignored.",
  "ignoredAt": "2026-03-10T03:05:00Z"
}
```

---

### GET /health
**No auth required**

**Response 200**
```json
{
  "endpoints": [
    {
      "name": "Payment Gateway",
      "status": "UP",
      "responseMs": 145,
      "checkedAt": "2026-03-10T03:00:00Z"
    },
    {
      "name": "SMS Service",
      "status": "DOWN",
      "responseMs": null,
      "checkedAt": "2026-03-10T03:00:00Z"
    }
  ]
}
```

---

### GET /health/history
**Query params:** `name`, `hours` (default 24, max 72)

**Response 200**
```json
{
  "name": "Payment Gateway",
  "uptimePercent": 97.2,
  "history": [
    { "checkedAt": "2026-03-10T02:55:00Z", "status": "UP",   "responseMs": 132 },
    { "checkedAt": "2026-03-10T03:00:00Z", "status": "UP",   "responseMs": 145 }
  ]
}
```

---

## 11. UI / UX Requirements

### 11.1 Dashboard `/`

- Summary cards: Open Anomalies, Resolved Today, Endpoints Online (e.g. "3/3")
- Anomaly table: reference no, route, amount, time since detected, action button
- Endpoint health: one row per endpoint with coloured status badge (green/amber/red)
- Auto-refresh every 60 seconds (via `setInterval` + `fetch`)
- Responsive — usable on a laptop browser (mobile not required)

### 11.2 Anomaly Detail / Recovery Modal

- Triggered by clicking "Recover" or "Ignore" on any anomaly row
- Shows full booking details before confirming
- `note` textarea (required, min 10 characters, shows character count)
- Confirmation button is disabled until note is valid
- On success: show toast notification, update anomaly status in table without full reload

### 11.3 Colour Conventions

| Status | Colour | Bootstrap class |
|--------|--------|-----------------|
| OPEN | Amber | `text-warning` |
| RESOLVED | Green | `text-success` |
| IGNORED | Grey | `text-secondary` |
| UP | Green | `badge bg-success` |
| DEGRADED | Amber | `badge bg-warning` |
| DOWN | Red | `badge bg-danger` |

---

## 12. Security Requirements

**SEC-01** Passwords SHALL be stored as bcrypt hashes (work factor ≥ 12). Plaintext passwords SHALL never be stored or logged.

**SEC-02** JWT tokens SHALL be signed with HMAC-SHA256. The signing key SHALL be at least 32 characters, stored in environment variables, and never committed to source control.

**SEC-03** All endpoints except `GET /health` SHALL enforce JWT authentication via ASP.NET Core middleware.

**SEC-04** The recovery endpoint (`POST /anomalies/{id}/recover`) SHALL verify that the requesting user's `email` matches what is recorded in the audit log. The server derives the user identity from the JWT — it is not accepted from the request body.

**SEC-05** MySQL connection string SHALL be provided via environment variable (`DB_CONNECTION_STRING`), not hardcoded in any source file.

**SEC-06** `.gitignore` SHALL exclude: `appsettings.Development.json`, `*.env`, `*.pfx`, `bin/`, `obj/`

---

## 13. Error Handling & Logging

### 13.1 Global Exception Handler

All unhandled exceptions SHALL be caught by a global middleware that:
1. Logs the full exception with Serilog (including stack trace)
2. Returns a generic HTTP 500 response — **never** exposing stack traces to the client

```json
{
  "status": 500,
  "message": "An unexpected error occurred. Reference: <correlation-id>"
}
```

### 13.2 Serilog Configuration

Logs SHALL be written to two sinks simultaneously:
1. Console (for Docker / local development)
2. MySQL table `app_logs` (for searching through the dashboard in future)

Minimum log levels:
| Namespace | Level |
|-----------|-------|
| Default | Information |
| Microsoft.EntityFrameworkCore | Warning |
| System.Net.Http | Warning |
| BookingAnomalyMonitor | Debug |

### 13.3 Structured Log Examples

```
[INF] AnomalyDetectionJob run complete. NewAnomalies=3 TotalOpen=7 Duration=412ms
[INF] Booking recovered. BookingId=42 Reference=BK-20260310-042 RecoveredBy=support@monitor.dev
[WRN] Endpoint health check failed. Name="SMS Service" Url=https://sms.example.com Error="Timeout"
[ERR] Unhandled exception in RecoverBookingUseCase. CorrelationId=abc-123 {exception}
```

---

## 14. Testing Requirements

### 14.1 Unit Tests (Required)

Location: `tests/Application.Tests/`

| Test | What is verified |
|------|-----------------|
| `DetectAnomalies_ShouldFlag_WhenPaymentSuccessAndBookingPending` | Core detection logic |
| `DetectAnomalies_ShouldNotFlag_WhenBookingAlreadyConfirmed` | No false positives |
| `DetectAnomalies_ShouldNotDuplicate_WhenAnomalyAlreadyExists` | Idempotency |
| `RecoverBooking_ShouldSucceed_WhenPaymentSuccessAndStatusOpen` | Happy path |
| `RecoverBooking_ShouldFail_WhenPaymentNotSuccess` | Business rule enforcement |
| `RecoverBooking_ShouldFail_WhenAlreadyResolved` | Duplicate prevention |
| `RecoverBooking_ShouldFail_WhenNoteIsTooShort` | Input validation |
| `RecoverBooking_ShouldWriteAuditLog` | Audit trail |

### 14.2 Integration Tests (Nice to Have)

Location: `tests/Api.IntegrationTests/`

Use `WebApplicationFactory<Program>` with an in-memory or test MySQL database (Testcontainers).

### 14.3 Manual Test Checklist

- [ ] `docker-compose up` starts successfully with no errors
- [ ] Seed data produces 5 visible OPEN anomalies on dashboard
- [ ] Recovering an anomaly updates its status immediately (no page reload needed)
- [ ] Recovery without a note is rejected
- [ ] Recovering an already-resolved anomaly returns 409
- [ ] All endpoints return 401 without a valid JWT
- [ ] Dashboard auto-refreshes after 60 seconds
- [ ] Health check page shows at least one DOWN endpoint (use seed config)

---

## 15. Deployment

### 15.1 docker-compose.yml

```yaml
version: '3.9'
services:
  db:
    image: mysql:8.0
    environment:
      MYSQL_ROOT_PASSWORD: rootpass
      MYSQL_DATABASE: bam_db
    ports:
      - "3306:3306"
    volumes:
      - db_data:/var/lib/mysql
      - ./database/seed.sql:/docker-entrypoint-initdb.d/seed.sql

  app:
    build: .
    ports:
      - "5000:8080"
    environment:
      DB_CONNECTION_STRING: "server=db;database=bam_db;user=root;password=rootpass"
      JWT_SECRET: "change-me-in-production-minimum-32-chars"
      ASPNETCORE_ENVIRONMENT: Development
    depends_on:
      - db

volumes:
  db_data:
```

### 15.2 Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `DB_CONNECTION_STRING` | Yes | MySQL connection string |
| `JWT_SECRET` | Yes | HMAC signing key (min 32 chars) |
| `ASPNETCORE_ENVIRONMENT` | No | `Development` or `Production` |
| `DETECTION_INTERVAL_MINUTES` | No | Default: 5 |
| `ANOMALY_THRESHOLD_MINUTES` | No | Default: 10 |

---

## 16. Success Metrics

This is a portfolio project. Success is defined as:

| Metric | Target |
|--------|--------|
| All Must Have user stories (US-01 to US-08, US-10, US-12) implemented | 100% |
| Unit tests pass with 0 failures | 100% |
| Manual test checklist passes | 100% |
| `docker-compose up` works on a fresh machine with no extra setup | Yes |
| README is clear enough for a stranger to run the project in under 5 minutes | Yes |
| No secrets or credentials committed to Git history | Yes |

---

## 17. Out of Scope / Future Work

The following are intentionally excluded from v1.0 but noted for future consideration:

| Feature | Reason Deferred |
|---------|----------------|
| Email / LINE notification when new anomaly detected | Requires external messaging integration |
| Automatic recovery (without human approval) | Too risky for v1; requires additional safeguards |
| Metrics dashboard (charts, trends) | Nice to have; not core to the support workflow |
| Multi-tenant support (multiple booking platforms) | Overcomplicated for a portfolio project |
| Full refund workflow integration | Requires finance system access |
| Mobile-responsive UI | Support engineers use desktop |
| US-13: Role-based access control (Admin vs Supporter) | Deferred to future iteration; `role` column is reserved for future use |

---

## 18. Glossary

| Term | Definition |
|------|-----------|
| **Anomaly** | A booking where `payment_status = SUCCESS` and `booking_status = PENDING` for more than 10 minutes |
| **Recovery** | The action of confirming a stuck booking — setting `booking_status = CONFIRMED` |
| **Audit Log** | An immutable record of every action taken by a support engineer |
| **Background Service** | A .NET `IHostedService` that runs on a timer independently of HTTP requests |
| **DEGRADED** | An endpoint that responds with a 2xx code but takes longer than 2 seconds |
| **Stuck Booking** | Synonymous with Anomaly |
| **TicketGuard** | A support tool that watches for broken bookings and helps engineers fix them safely. |
