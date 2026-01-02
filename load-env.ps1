# Load environment variables from .env file
# Usage: . .\load-env.ps1

param(
    [string]$EnvFile = ".env",
    [switch]$Show = $false,
    [switch]$Validate = $false
)

function Load-EnvFile {
    param(
        [string]$Path,
        [bool]$ShowVars = $false,
        [bool]$ValidateVars = $false
    )

    if (-not (Test-Path $Path)) {
        Write-Host "Error: .env file not found at '$Path'" -ForegroundColor Red
        Write-Host "Create .env from template: cp .env.example .env" -ForegroundColor Yellow
        return $false
    }

    $requiredVars = @(
        "BINANCE_TESTNET_KEY",
        "BINANCE_TESTNET_SECRET"
    )

    $loadedVars = 0
    $loadedCount = @{}

    try {
        Get-Content $Path | ForEach-Object {
            if ([string]::IsNullOrWhiteSpace($_) -or $_.Trim().StartsWith("#")) {
                return
            }

            if ($_ -match '^\s*([^#=]+)=(.*)$') {
                $name = $Matches[1].Trim()
                $value = $Matches[2].Trim()

                if ($value.StartsWith('"') -and $value.EndsWith('"')) {
                    $value = $value.Substring(1, $value.Length - 2)
                }

                [Environment]::SetEnvironmentVariable($name, $value, [EnvironmentVariableTarget]::Process)
                $loadedCount[$name] = $value
                $loadedVars++

                if ($ShowVars -and -not $name.Contains("SECRET") -and -not $name.Contains("TOKEN")) {
                    Write-Host "  OK $name = $value" -ForegroundColor Green
                } elseif ($ShowVars) {
                    Write-Host "  OK $name = ***" -ForegroundColor Green
                }
            }
        }

        Write-Host "Loaded $loadedVars environment variables from $Path" -ForegroundColor Green

        if ($ValidateVars) {
            Write-Host ""
            Write-Host "Validating required variables..." -ForegroundColor Cyan
            $missing = @()

            foreach ($var in $requiredVars) {
                $value = [Environment]::GetEnvironmentVariable($var, [EnvironmentVariableTarget]::Process)
                if ([string]::IsNullOrEmpty($value) -or $value.StartsWith("your-")) {
                    $missing += $var
                    Write-Host "  MISSING $var" -ForegroundColor Red
                } else {
                    Write-Host "  OK $var" -ForegroundColor Green
                }
            }

            if ($missing.Count -gt 0) {
                Write-Host ""
                Write-Host "Missing variables: $($missing -join ', ')" -ForegroundColor Yellow
                Write-Host "Edit .env and fill in actual values" -ForegroundColor Yellow
                return $false
            }
        }

        return $true
    } catch {
        Write-Host "Error loading .env file: $_" -ForegroundColor Red
        return $false
    }
}

$success = Load-EnvFile -Path $EnvFile -ShowVars $Show -ValidateVars $Validate

if ($success) {
    Write-Host ""
    Write-Host "Ready to run: dotnet run" -ForegroundColor Cyan
} else {
    Write-Host ""
    Write-Host "Please fix the .env file before running" -ForegroundColor Yellow
}
