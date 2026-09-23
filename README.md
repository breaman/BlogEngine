## Bootstrap
Bootstrap and Bootstrap Icons come from npm and are self-hosted (no CDN), so install the npm packages once after cloning. The build copies `bootstrap.bundle.min.js` and the Bootstrap Icons font files from `node_modules` into `wwwroot`, and it fails if they are missing:

```
cd src/BlogEngine.Server
npm install
```

Since this template utilizes bootstrap scss, the initial css file needs to be generated. There are two scripts included for doing this, one is sass-dev that will run the process in watch mode and the other is sass-prod that will compress the css file for production use. In order to do this perform the following steps in your terminal:

```
cd src/BlogEngine.Server
npm run sass-dev (or sass-prod depending on which one you want)
```

## EF Migrations
This project adds EF as a dotnet tool, so before running any EF commands, one needs to run the following command from the project folder (there is also a command in the Aspire dashboard to run this restore command if the app is started before the restore command is run manually):

```
dotnet tool restore
```

After creating a new project with this template, migrations need to be run since there is some authentication that has been added. To do this, run the following command in your terminal:

```
cd src/BlogEngine.Server
dotnet ef migrations add InitialDatabase -p ../BlogEngine.Data
```

Migrations will run when the aspire project is started, so no need to run the migration manually after it is created.

## Aspire
This project is configured with aspire and should use the aspire cli, so the recommended way to kick off project execution is the following:

```
aspire run
```

### Admin account
On first run there are no accounts. Browse to `/setup` to create the admin (display name, email and password); the page returns 404 once any account exists. Public registration is closed by default (`AllowRegistration` in site settings).

To skip `/setup`, seed the admin from Aspire parameters in the AppHost's user secrets. They are only used while no account exists:

```
cd aspire/BlogEngine.AppHost
dotnet user-secrets set "Parameters:admin-email" "you@example.com"
dotnet user-secrets set "Parameters:admin-password" "<password>"
```

## Tests
The tests use [TUnit](https://tunit.dev) on Microsoft.Testing.Platform (enabled for `dotnet test` in `global.json`):

- `tests/BlogEngine.UnitTests`: fast tests for shared utilities. Snapshot tests use [Verify](https://github.com/VerifyTests/Verify); commit `*.verified.*` files, never `*.received.*` files. Verify requires a sponsorship declaration: this repo claims the OpenSource exemption in `Directory.Build.props`, which expires 2027-09 and must be renewed.
- `tests/BlogEngine.IntegrationTests`: hosts the server in memory against a SQL Server container (Testcontainers) shared by the whole test run, with migrations applied on startup. **Docker must be running.**

Run everything from the repository root:

```
dotnet test
```
