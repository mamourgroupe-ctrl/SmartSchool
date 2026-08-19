# SmartSchool Architecture

## Existing Architecture
The active backend is SmartSchoolAPI, an ASP.NET Core Web API targeting net10.0. It uses controller endpoints, Entity Framework Core, SQLite, JWT bearer middleware, and Development-only Swagger. SmartSchoolMobile is a .NET MAUI client using XAML pages and code-behind. SmartSchool.API is an unintegrated Quran prototype and is not part of the active build.
## Current Data Boundary
SchoolDbContext currently owns Users, Students, Teachers, and Courses. Controllers access the context directly. The database is local SQLite. No domain services or repository boundary was observed.
## Security Boundary
Authentication is JWT-based, but password storage and seeded credentials require remediation. Authorization currently relies on broad Authorize attributes and limited role checks. The required boundary is: administrators manage school data; teachers access assigned students; parents access linked children only; students access their own records; AI tools receive the same scoped principal.
## Incremental Target
Preserve the ASP.NET Core and MAUI foundation. First establish secure authentication, DTO projections, validation, and tests. Next add durable Student 360, attendance, and Quran records to the same DbContext. Then add behavior, parent relationships, reports, notifications, and scoped analytics. Only after deterministic tools exist should provider abstraction, agents, RAG, and natural-language explanations be introduced.
## Domain Expansion
Student 360 is the aggregate view over personal, academic, attendance, behavior, homework, grades, parent, Quran, Hifz, Murajaah, recitation, errors, stability, risk, notes, notifications, and AI insights. Attendance and Quran metrics must be calculated from stored events and results; AI may explain or recommend but must not invent or automatically make official decisions.
## AI and RAG Boundary
Future AI requests must flow through intent, permission, explicit safe tool, database, analytics, and explanation. No raw unrestricted SQL is exposed to an LLM. Gemini and Ollama should implement a common provider interface, while RAG responses should retain source metadata. This layer is not present yet and must not be represented as implemented.
## Quality Gates
Each vertical slice requires build verification, unit tests, API tests, permission tests, and data-isolation tests. Documentation must describe only code that exists. Existing user changes and secrets are preserved.

## Current P0 Update
The active API now has environment-only JWT key validation, PBKDF2 password verification, public login/user response DTOs, explicit public projections for core resources, development environment templates, and login rate limiting. Mobile API configuration distinguishes Windows and Android emulator defaults. Quran remains intentionally unimplemented until its persistent EF Core vertical slice is designed.
