# Stage 0: test and CI safety net

This phase changes tests, test tooling, CI, and this document only. It does not
change the application's authentication, authorization, data model, or mobile
code. The existing `SmartSchoolAPI.Tests` xUnit project is reused rather than
creating a second project.

## API integration tests

`IntegrationTestFactory` uses `WebApplicationFactory<Program>` and one open
SQLite `:memory:` connection per factory. Each factory creates its own schema;
it never opens the application's `school_system.db`. `SeedTwoSchools` creates
two schools with an enrolled student, assigned teacher, school administrator,
and authorized parent for each. The new safety-net tests authenticate through
the API, check several roles against both schools, and confirm that separate
factory instances do not share student data.

These tests cover the specific reads they exercise; they are not proof that all
cross-school writes are safe. In particular, the previously identified
cross-school enrollment issue is not fixed by this testing-only phase.

CI builds the API and test project, checks and applies EF migrations, then runs
`dotnet test` and writes a TRX report. `scripts/assert_dotnet_test_results.py`
requires at least one executed and one passing test and fails on a missing,
malformed, or empty report. The gate itself has Python tests, including the
zero-test case. `dotnet test` must also succeed: the gate does not replace test
failure handling.

Example commands from the repository root, on a machine with .NET 10 and NuGet
access:

```bash
dotnet build SmartSchoolAPI/SmartSchoolAPI.csproj --configuration Release
dotnet build SmartSchoolAPI.Tests/SmartSchoolAPI.Tests.csproj --configuration Release
results_dir="$(mktemp -d)"
dotnet test SmartSchoolAPI.Tests/SmartSchoolAPI.Tests.csproj --configuration Release --no-build \
  --logger "trx;LogFileName=api-tests.trx" --results-directory "$results_dir"
python3 scripts/assert_dotnet_test_results.py "$results_dir/api-tests.trx"
```

## Python and Expo

The Python JWT tests pass merged, non-duplicated verifier arguments and require
an HTTP 401 for a wrong secret, issuer, audience, malformed token, tampered
signature, and an expired token. A separate test substitutes a deliberately
permissive decoder and confirms that each wrong-parameter rejection test fails.

CI uses Python 3.12 and the project's pinned development requirements. It also
runs Expo TypeScript, lint, and a static web export. Export is a build check,
not a browser or native-device functional test.

```bash
(cd backend && python -m pip install -r requirements-dev.txt && python -m pytest)
npm ci
npx tsc --noEmit
npm run lint
EXPO_OFFLINE=1 npx expo export --platform web --output-dir /tmp/smartschool-expo-web
```

## MAUI exclusion

MAUI is not a required CI job in this phase. Static inspection found that
`SmartSchoolMobile/Services/ApiService.cs` references `QuranRecordDto`, but no
definition exists in the repository; fixing mobile source code is outside the
requested testing-only scope. MAUI also needs platform workloads and Android
SDK tooling on a suitable runner. A MAUI build was **not** executed here; the
missing type is an inference from the source, not a reported build result.

After fixing the mobile project separately, add an Android build on a runner
with the .NET 10 MAUI Android workload and Android SDK, for example:

```bash
dotnet workload install maui-android
dotnet build SmartSchoolMobile/SmartSchoolMobile.csproj -f net10.0-android -c Release
```

## Local .NET limitation

No `dotnet` executable was available in the inspection environment. The official
installer was obtained from the dotnet/install-scripts GitHub repository, but
its attempt to download SDK 10.0.100 from `builds.dotnet.microsoft.com` failed;
`api.nuget.org` was also unreachable. Thus the .NET build and tests require the
GitHub Actions runner or another machine with SDK and NuGet access. A historical
CI success on the base commit does not establish the result of the changed
workflow.
