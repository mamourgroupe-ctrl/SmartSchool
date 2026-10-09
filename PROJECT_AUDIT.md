# SmartSchool Project Audit

## Repository
Path: D:\Development\Repositories\AI-Hub\SmartSchool. Branch: master. Latest commit: 12ed98a. Existing unrelated modifications and untracked paths were preserved; no destructive Git commands were used.
## Current Architecture
SmartSchoolAPI is an ASP.NET Core net10.0 Web API using EF Core SQLite, JWT, controllers, and Development Swagger. SmartSchoolMobile is a .NET MAUI client. SmartSchool.API is an unintegrated in-memory Quran code area without a project file.

## Backend
Controllers: Auth, Students, Teachers, Courses. Only basic list/create operations were observed. No service layer, validation layer, centralized errors, pagination, or audit logging.
## Frontend
No web frontend was found. The UI is .NET MAUI XAML in SmartSchoolMobile.
## Mobile
MainPage fetches api/students on login instead of authenticating. Base URL is http://localhost:5200. Token storage and bearer handling were not observed.
## Database
SchoolDbContext exposes only Users, Students, Teachers, and Courses through SQLite. Quran, attendance, behavior, parent, halaqah, notification, and intervention entities are absent.

## Authentication
JWT validation is configured, but startup seeds admin with literal admin123 in PasswordHash; secure hashing is absent.
## Authorization
Roles are effectively Admin, Student, Teacher. Required six-role RBAC and resource-level isolation for parents, teachers, and students were not found.
## AI
No AI provider abstraction, LangChain, LangGraph, Gemini, Ollama, RAG, agents, or MCP integration was found.
## Quran Features
SmartSchool.API contains only an in-memory QuranProgress model/controller. It is not connected to the active database, authentication, authorization, or client. Hifz, Murajaah, recitation errors, stability, risk, and intervention history are missing.

## Testing
No test project was observed. Unit, integration, API, permission, AI-tool, prompt-injection, and data-isolation tests are missing.
## Security
Critical risks: plaintext/default credential behavior, user objects returned from broad endpoints, no visible validation, rate limiting, or audit logs, and an HTTP mobile URL. Secrets and .env contents were not opened or modified.
## Deployment
No Dockerfile, deployment manifest, or documented production configuration was found.
## Current Working Features
Basic ASP.NET Core API with SQLite/JWT configuration, student/teacher/course endpoints, and MAUI school/Quran navigation exist but require build/runtime verification.

## Broken Features
Mobile login bypasses authentication; Quran progress is isolated and process-local; default credential handling is unsafe.
## Missing Features
Student 360, attendance, behavior, parent, halaqah, integrated Quran analytics, AI/RAG/MCP, full RBAC, and automated tests.
## Technical Debt
Duplicated backend areas, direct database access in controllers, weak DTOs, inconsistent naming, missing API contract, and local database artifacts.
## Critical Risks
P0: insecure credentials, possible user-record exposure, missing resource authorization, unverified build. P1: mobile auth bypass, disconnected Quran, absent tests and security controls. P2: duplication and broad product gaps.

PROJECT HEALTH: 28/100
P0 Critical: secure password hashing; resource-level authorization; verified build/test baseline.
P1 High: repair mobile auth; integrate Quran; add validation, errors, audit logs, rate limiting, and security tests.
P2 Medium: incremental refactoring, domain modules, and documentation.
Next Recommended Action: fix the security/authentication baseline, then implement authenticated Student 360 with parent/teacher isolation and durable attendance/Quran records.

## P0 Update
A PBKDF2 password service, environment-only JWT key requirement, optional Development-only admin seed, password redaction, and hashed student/teacher creation were implemented. P0 remains incomplete because mobile login, resource-level RBAC, Quran persistence, and automated tests are still missing.

## P0 Follow-up Fixes (2026-10-09)
The critical findings from the 2026-10-09 audit were fixed:

1. **Python backend hardened** (`backend/`): the hard-coded `admin`/`password` login and fake `jwt-sample-token-12345` were removed. Login now reads credentials from the environment (`API_USERNAME`, `API_PASSWORD_HASH`), verifies passwords with PBKDF2-SHA256 (same format as the .NET PasswordService), and issues real signed HS256 JWTs. Login fails closed (503) when not configured. The AI chat endpoint now requires a valid bearer token. CORS no longer uses a wildcard with credentials; allowed origins come from `CORS_ORIGINS`. The auth route moved to `/api/v1/auth/login` to avoid colliding with the ASP.NET Core API. New tests: `backend/tests/` (10 passing).
2. **Course creation locked down**: `POST /api/courses` now requires `SUPER_ADMIN` or `SCHOOL_ADMIN` (previously any authenticated user could create courses). `GET /api/courses` is role-scoped.
3. **List endpoints isolated by role**: `GET /api/students` and `GET /api/teachers` no longer return the full directory to any authenticated user. SuperAdmin sees all; SchoolAdmin is scoped to their organization; Teacher sees only students in assigned sections and only their own teacher record; Parent sees only linked, authorized children and their teachers; Student sees only their own record and their section teachers. Unknown roles fail closed with an empty list.
4. **MAUI login repaired**: `MainPage` now performs a real login through `ApiService` (the previous code fetched `api/students` without authenticating on a wrong port `localhost:5200`). The default `admin`/`admin123` values were removed from the login form. `ApiService` now reads the `accessToken` field (the API never returned a `token` field, so the stored token was always empty), and `AddStudentAsync` sends the `Username`/`Password` fields the API requires. `AddStudentPage` collects username/password, and the teachers list binding was fixed (`SubjectSpecialty`).
5. **`backend/requirements.txt` re-encoded** from UTF-16 to UTF-8 so `pip install -r requirements.txt` works; added `backend/requirements-dev.txt` (pytest) and `backend/pytest.ini`.

New tests: `SmartSchoolAPI.Tests/ResourceIsolationTests.cs` (9 tests) covering course-creation roles and per-role list isolation. Existing security tests were reviewed and remain compatible with the new filters.

Remaining known gaps (P2): production deployment configuration (HTTPS termination at a reverse proxy, database migration from SQLite to PostgreSQL). The legacy create endpoints for students/teachers intentionally create organization-less records that become tenant-scoped at enrollment/membership time.

## Final P1 Audit + Refresh Tokens + Tenant Isolation (2026-10-09)

### Final P1 audit result
All P1 items verified resolved in code: MAUI performs a real authenticated login (no bypass), the Quran slice is persistent and wired end-to-end, 39 .NET integration tests plus 10 Python tests exist, the Expo app persists tokens and guards routes, and CORS is configurable with a fail-closed production default. Two gaps remained and were fixed below.

### Refresh tokens (session management)
- New `RefreshToken` entity (user, SHA-256 token hash only — never the raw token, expiry, revocation timestamp) with migration `20261009120000_AddRefreshTokens`, unique index on the hash.
- `POST /api/auth/login` now also returns a `refreshToken` (7-day lifetime, configurable via `Jwt:RefreshTokenLifetimeDays`).
- `POST /api/auth/refresh` rotates the pair: the presented token is revoked and a fresh access+refresh pair is issued. Unknown, revoked, or expired tokens return 401. Rate limited (20/min) and audited (`TOKEN_REFRESHED` / `TOKEN_REFRESH_FAILURE`).
- `POST /api/auth/logout` (authenticated) revokes all of the user's active refresh tokens and is audited (`LOGOUT`).
- Clients: the Expo app stores both tokens in `expo-secure-store`, silently refreshes once on a 401, and calls logout server-side; the MAUI `ApiService` stores both tokens in `SecureStorage` and exposes `RefreshAsync`/`LogoutAsync`.
- New tests: `SmartSchoolAPI.Tests/RefreshTokenTests.cs` (7 tests: rotation, reuse rejection, expiry, logout revocation, hash-only storage, rate limit, auth required).

### Tenant (organization) isolation
- Fixed the confirmed cross-tenant write: `POST /api/courses` now verifies via `Stage1AccessService.CanAdminTeacherAsync` that the teacher belongs to an organization the caller manages (SuperAdmin bypass). Cross-organization attempts return 404 without revealing existence.
- New tests: `SmartSchoolAPI.Tests/OrganizationIsolationTests.cs` (3 tests: cross-org denied, own-org allowed, SuperAdmin allowed).
- Full endpoint sweep: every controller requires authentication; every write endpoint enforces role + resource/organization checks; every list endpoint is role-scoped and fails closed for unknown roles.

### Multi-tenant isolation verification matrix
| Boundary | Enforcement | Tests |
|---|---|---|
| Student ↔ own data only | `CanViewAsync`/`CanManageAsync` | ResourceIsolationTests, Stage1IntegrationTests |
| Parent ↔ linked children only | `StudentParent.IsAuthorized` | ResourceIsolationTests, Stage1IntegrationTests, QuranIntegrationTests |
| Teacher ↔ assigned sections | active enrollment + section teacher + org membership | ResourceIsolationTests, QuranIntegrationTests |
| SchoolAdmin ↔ own organization | `OrganizationMembership` scoping on every Stage 1 endpoint | Stage1IntegrationTests, OrganizationIsolationTests |
| Course creation ↔ teacher's organization | `CanAdminTeacherAsync` | OrganizationIsolationTests |
| Unknown roles | fail closed (empty list / 403) | ResourceIsolationTests |

### Verification
- Python backend: 10/10 tests pass. Expo: `tsc --noEmit` and `expo lint` clean. All changed C# files structurally validated.
- .NET SDK is not available in the agent environment, so `dotnet build`/`dotnet test` must be run locally:
  ```text
  dotnet test SmartSchoolAPI.Tests/SmartSchoolAPI.Tests.csproj
  dotnet ef database update --project SmartSchoolAPI/SmartSchoolAPI.csproj
  ```

## Stage 1 Follow-up: Quran Slice, Clients, and Transport Hardening (2026-10-09)

### Quran persistence vertical slice (was P1)
- New `QuranRecord` entity (student, surah, ayah range, NEW/REVIEW type, 0-10 rating, tajweed errors, teacher notes, date, recorded-by) with EF Core migration `20261009090000_AddQuranRecords` and index `(StudentId, Date)`.
- New `QuranController`: `POST /api/students/{id}/quran` (SuperAdmin/SchoolAdmin/Teacher, manage permission enforced, validated input, audit logged), `GET /api/students/{id}/quran` (view permission enforced), `GET /api/students/{id}/quran/summary` (deterministic aggregation from stored rows).
- Quran records are included in the Student 360 overview.
- The orphan in-memory prototype `SmartSchool.API/` was removed; the MAUI `QuranProgressPage` now selects a student and loads records through the authenticated API (no more `localhost:5200` calls).
- New tests: `SmartSchoolAPI.Tests/QuranIntegrationTests.cs` (8 tests).

### Expo client (was P1 template/token gaps)
- Login now persists the JWT with `expo-secure-store` before navigating.
- The tabs layout guards authentication and redirects to `/login` when no token exists; a 401 from the API clears the token and returns to login.
- The home tab is a real students screen (authenticated fetch, pull-to-refresh, logout); the explore tab is a simple about screen; the template modal and unused template components were removed.
- TypeScript (`tsc --noEmit`) and `expo lint` pass.

### Transport hardening
- CORS is now configurable via `Cors:AllowedOrigins` / `SMARTSCHOOL_CORS_ORIGINS`; Development keeps the permissive fallback, production fails closed when no origins are configured. Dev origins are declared in `appsettings.Development.json`.
- HSTS is enabled outside Development (TLS termination remains a proxy responsibility).
- List endpoints accept optional `page`/`pageSize` (max 200) and remain backward compatible.

### Verification
- Python backend: 10/10 tests pass.
- Expo app: `tsc --noEmit` clean, `expo lint` clean.
- .NET: no SDK is available in this environment, so `dotnet build`/`dotnet test` could not be executed; all changed C# files were reviewed and structurally validated, and the new tests follow the existing verified patterns. Run `dotnet test SmartSchoolAPI.Tests` in a .NET 10 SDK environment to confirm.

## CI and Final Verification Gate (2026-10-09)

### Static model consistency verification (static equivalent of `has-pending-model-changes`)
A full static comparison of DbContext model ↔ ModelSnapshot ↔ both hand-written migrations passed: 18 entities, all scalar property names/types/nullability, all maxLength values, table names, keys, indexes (columns + uniqueness), all 18 relationship blocks (HasOne + FK + DeleteBehavior + IsRequired), and every CreateTable/CreateIndex/ForeignKey in the two new migrations match the snapshot exactly. `dotnet ef migrations has-pending-model-changes` is expected to report no pending changes; if it does not, regenerate:
```text
git checkout -- SmartSchoolAPI/Migrations/SchoolDbContextModelSnapshot.cs
rm SmartSchoolAPI/Migrations/20261009090000_AddQuranRecords.* SmartSchoolAPI/Migrations/20261009120000_AddRefreshTokens.*
dotnet ef migrations add AddQuranRecords --project SmartSchoolAPI/SmartSchoolAPI.csproj
dotnet ef migrations add AddRefreshTokens --project SmartSchoolAPI/SmartSchoolAPI.csproj
```

### CI pipeline (`.github/workflows/ci.yml`)
One job per toolchain, run on every push and pull request:
- **dotnet**: build → `dotnet ef migrations has-pending-model-changes` → `dotnet ef database update` → `dotnet test` (the design-time `SchoolDbContextFactory` means EF commands need no secrets).
- **python**: `pip install -r requirements.txt -r requirements-dev.txt` → `pytest` (also validates the UTF-8 `requirements.txt` fix).
- **expo**: `npm ci` → `npx tsc --noEmit`.

This makes build, test, migration-drift, dependency-install, and type errors visible automatically on every change — including any lost or conflicting edits.

### Status
No new features were added in this pass. P2 (PostgreSQL, TLS termination, MAUI auto-refresh) is intentionally deferred until the local .NET verification (`dotnet build -warnaserror`, `dotnet test`) and the first green CI run are complete.

**Update (2026-10-09, PR #1):** The first full CI run is green on real runners: `dotnet` (build + `ef has-pending-model-changes` + `database update` + all 49 tests), `python` (full install on Python 3.12 + 10 tests), `expo` (`npm ci` + `tsc --noEmit`), and the Semgrep scan all pass. Two CI-breaking issues were found and fixed by CI itself: `IntegrationTestFactory.SeedUser` returned `void` while a new overload expected `int` (compile error), and the python job needed Python 3.12 (`numpy==2.5.2` requires >= 3.12). The hand-written EF migrations were confirmed consistent by `has-pending-model-changes` (no pending model changes). P2 is now unblocked.
