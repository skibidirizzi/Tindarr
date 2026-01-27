param(
	[string] $StartupProject = "src/Tindarr.Api/Tindarr.Api.csproj",
	[string] $Project = "src/Tindarr.Infrastructure/Tindarr.Infrastructure.csproj",
	[string] $Environment = "Development",
	[switch] $Update,
	[string] $AddMigration
)

$ErrorActionPreference = "Stop"

if (-not $Update -and [string]::IsNullOrWhiteSpace($AddMigration)) {
	$Update = $true
}

Write-Host "Restoring local dotnet tools..."
dotnet tool restore | Out-Host

if (-not [string]::IsNullOrWhiteSpace($AddMigration)) {
	Write-Host "Adding migration '$AddMigration'..."
	dotnet tool run dotnet-ef migrations add $AddMigration `
		--project $Project `
		--startup-project $StartupProject `
		--output-dir "Persistence/Migrations" `
		-- `
		--environment $Environment | Out-Host
}

if ($Update) {
	Write-Host "Applying migrations..."
	dotnet tool run dotnet-ef database update `
		--project $Project `
		--startup-project $StartupProject `
		-- `
		--environment $Environment | Out-Host
}

