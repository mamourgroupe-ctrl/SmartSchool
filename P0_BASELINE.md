# SmartSchool P0 Baseline

## Repository
Branch: master. Existing unrelated working-tree changes were preserved. No reset, clean, or destructive deletion was performed.
## Backend Build
SmartSchoolAPI net10.0: PASS with dotnet build --no-restore.
## Mobile Build
Windows target: PASS. iOS and MacCatalyst targets: PASS with nullable event-handler warnings. Android target: FAIL in the current environment; the full diagnostic should be captured in the next focused Android build.
## Existing Warnings and Errors
Existing nullable EventHandler warnings exist in MainPage. Android build reported one error in the combined build output and requires a focused build for the exact diagnostic.
## Database
Existing SQLite database and EF Core migrations are present. No database was replaced or deleted. Current schema includes Users, Students, Teachers, and Courses.
## Authentication
Before this baseline, login compared a request password-like field directly to PasswordHash and the mobile flow was not fully authenticated. JWT and SQLite are configured in the active API.
## Baseline Scope
This document records the state before the next P0 changes; it is not a production-readiness claim.
