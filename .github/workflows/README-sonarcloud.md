# SonarQube Cloud analysis (CI)

Code analysis for the GeekBackend .NET solution runs in GitHub Actions via
[`sonarcloud.yml`](./sonarcloud.yml), using **SonarScanner for .NET**
(`dotnet-sonarscanner`, pinned in [`.config/dotnet-tools.json`](../../.config/dotnet-tools.json)).

- **Project key:** `jmartinemployment_GeekBackend`
- **Organization:** `jmartinemployment`
- **Host:** `https://sonarcloud.io`
- **Solution analysed:** `GEEKBACKEND.slnx` (all 6 projects, Release build)

## One-time activation

The workflow is committed but inert until two things are done:

### 1. Add the `SONAR_TOKEN` secret
1. SonarQube Cloud → your avatar → **My Account → Security** (or the project's
   **Administration → Analysis Method → With GitHub Actions**) → generate a token.
2. GitHub repo **Settings → Secrets and variables → Actions → New repository secret**
   - Name: `SONAR_TOKEN`
   - Value: the token from step 1

### 2. Turn OFF Automatic Analysis
In SonarQube Cloud → project `jmartinemployment_GeekBackend` →
**Administration → Analysis Method** → disable **Automatic Analysis**.

> CI-based analysis and Automatic Analysis are mutually exclusive. If Automatic
> Analysis stays on, the `end` step fails with: *"You are running CI analysis while
> Automatic Analysis is enabled."* The current issues on the project were produced by
> Automatic Analysis; switching to this pipeline replaces it (and gives fuller C#
> analysis — the MSBuild integration sees the real compilation).

Then push to `main` (or open a PR) and the workflow runs: **begin → build → end**,
publishing results to SonarQube Cloud. PRs are decorated automatically via the
SonarCloud GitHub App.

## Running the scanner locally (optional)

```bash
dotnet tool restore
dotnet sonarscanner begin /k:"jmartinemployment_GeekBackend" /o:"jmartinemployment" \
  /d:sonar.host.url="https://sonarcloud.io" /d:sonar.token="$SONAR_TOKEN"
dotnet build GEEKBACKEND.slnx --configuration Release --no-incremental
dotnet sonarscanner end /d:sonar.token="$SONAR_TOKEN"
```
Requires a JRE 17+ on PATH and `SONAR_TOKEN` exported. This uploads to the live
project, so prefer letting CI do it.

## Adding test coverage (optional follow-up)

Analysis already reports bugs, vulnerabilities, code smells, security hotspots and
duplication. To add C# line coverage:

1. Add `coverlet.collector` to `GeekBackend.Tests/GeekBackend.Tests.csproj`.
2. Add a `coverlet.runsettings` emitting OpenCover format.
3. Pass the report path to `begin`: `/d:sonar.cs.opencover.reportsPaths="**/coverage.opencover.xml"`.
4. Uncomment the "Unit tests with coverage" step in `sonarcloud.yml`
   (run only `GeekBackend.Tests` — `GeekBackend.IntegrationTests` needs Postgres/Mongo).
