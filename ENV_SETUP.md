# Environment Variables Setup Guide

Secure configuration management for API keys and sensitive data.

## 📋 Overview

The trading bot uses environment variables to manage sensitive credentials like API keys instead of storing them in source code. This follows security best practices:

- **Never commit actual API keys** to version control
- **Use `.env` files** for local development
- **Use CI/CD secrets** for production deployments
- **Different credentials** for testnet vs mainnet

---

## 🚀 Quick Start

### 1. Create `.env` file from template

```bash
cp .env.example .env
```

### 2. Fill in your credentials

Edit `.env` with your actual API keys:

```bash
# Using PowerShell
notepad .env

# Using bash/WSL
nano .env
```

### 3. Load environment variables

#### Option A: PowerShell (Windows)

```powershell
# Create a helper script
cat > load-env.ps1 << 'EOF'
$envFile = ".env"
if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        if ($_ -match '^\s*([^#=]+)=(.*)$') {
            $name = $Matches[1].Trim()
            $value = $Matches[2].Trim()
            if ($value.StartsWith('"') -and $value.EndsWith('"')) {
                $value = $value.Substring(1, $value.Length - 2)
            }
            [Environment]::SetEnvironmentVariable($name, $value, [EnvironmentVariableTarget]::Process)
        }
    }
    Write-Host "✅ Loaded .env variables"
} else {
    Write-Host "❌ .env file not found"
}
EOF

# Load it before running the app
. .\load-env.ps1
dotnet run
```

#### Option B: Bash/WSL

```bash
# Create helper script
cat > load-env.sh << 'EOF'
#!/bin/bash
set -a  # Auto-export
[ -f .env ] && source .env
set +a
EOF

# Load it before running the app
source load-env.sh
dotnet run
```

#### Option C: .NET Configuration (Recommended)

Add to `Program.cs` to load from `.env` file:

```csharp
var builder = WebApplicationBuilder.CreateBuilder(args);

// Load .env file if it exists
var envFile = Path.Combine(AppContext.BaseDirectory, ".env");
if (File.Exists(envFile))
{
    var lines = File.ReadAllLines(envFile);
    foreach (var line in lines)
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            continue;

        var parts = line.Split('=', 2);
        if (parts.Length == 2)
        {
            var key = parts[0].Trim();
            var value = parts[1].Trim().Trim('"');
            Environment.SetEnvironmentVariable(key, value);
        }
    }
    Console.WriteLine("✅ Loaded .env file");
}

// Then load configuration as usual
builder.Configuration
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .AddEnvironmentVariables("TRADING_");
```

---

## 🔐 Environment Variables Reference

### Binance API Keys

```env
# For Testnet (development/testing)
BINANCE_TESTNET_KEY=your-testnet-api-key
BINANCE_TESTNET_SECRET=your-testnet-api-secret

# For Mainnet (live trading)
BINANCE_MAINNET_KEY=your-mainnet-api-key
BINANCE_MAINNET_SECRET=your-mainnet-api-secret
```

**Where to get testnet keys:**
1. Go to [Binance Testnet](https://testnet.binance.vision/)
2. Sign in with your regular Binance account
3. Navigate to "API Management"
4. Create API key with "Spot Trading" permissions
5. Copy Key and Secret to `.env`

**⚠️ Mainnet keys (KEEP SECURE!):**
- Use testnet for development
- Enable IP whitelisting for mainnet keys
- Rotate keys periodically
- Never share keys with anyone

### Trading Configuration

```env
# Use testnet for testing (true/false)
TRADING_BinanceApi__UseTestnet=true

# Risk management settings
TRADING_RiskManagement__RiskPerTradePercent=1.5
TRADING_RiskManagement__MaxDrawdownPercent=20
TRADING_RiskManagement__MaxDailyDrawdownPercent=3
TRADING_RiskManagement__InitialCapital=1000

# Strategy parameters
TRADING_Strategy__AdxPeriod=14
TRADING_Strategy__AdxThreshold=25
TRADING_Strategy__FastEmaPeriod=20
TRADING_Strategy__SlowEmaPeriod=50
TRADING_Strategy__AtrMultiplier=2.5
TRADING_Strategy__VolumeThreshold=1.5
```

### Optional: Telegram Notifications

```env
# Bot token from @BotFather
TELEGRAM_BOT_TOKEN=123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11

# Your chat ID (get it from @userinfobot)
TELEGRAM_CHAT_ID=987654321
```

### Logging

```env
# Logging level: Trace, Debug, Information, Warning, Error, Critical
LOGGING_LEVEL=Information
```

---

## 📂 File Structure

```
TradingBot/
├── .env                    ❌ NEVER COMMIT (local credentials)
├── .env.example            ✅ Commit this (template)
├── .env.local              ❌ Never commit (local overrides)
├── .env.production         ❌ Never commit (could contain keys)
├── .gitignore              ✅ Commit (includes .env patterns)
├── appsettings.json        ✅ Commit (public config)
└── appsettings.user.json   ❌ Never commit (user overrides)
```

---

## 🧪 Testing with Different Credentials

### Run with Testnet

```bash
# .env contains testnet keys
dotnet run

# OR explicitly set
$env:BINANCE_TESTNET_KEY = "test-key"
$env:TRADING_BinanceApi__UseTestnet = "true"
dotnet run
```

### Run Unit Tests

```bash
# Unit tests use mocked data, no credentials needed
dotnet test ComplexBot.Tests

# Integration configuration tests also need no credentials
dotnet test ComplexBot.Integration --filter "ConfigurationIntegrationTests"
```

### Activate Binance Integration Tests

```bash
# Copy .env with testnet credentials
cp .env.example .env
# Edit and add real testnet keys
nano .env

# Load credentials and run
. .\load-env.ps1
dotnet test ComplexBot.Integration --filter "BinanceTestnetIntegrationTests"
```

---

## 🔄 Configuration Priority (Highest to Lowest)

1. **Environment Variables** (from `.env` or system)
2. **appsettings.user.json** (user overrides, not committed)
3. **appsettings.{Environment}.json** (Development/Production)
4. **appsettings.json** (default config)

Example priority chain:

```csharp
var key = Environment.GetEnvironmentVariable("BINANCE_TESTNET_KEY")
    ?? config["BinanceApi:TestnetKey"]
    ?? "default-value";
```

---

## ✅ Security Checklist

- [ ] `.env` added to `.gitignore`
- [ ] `.env.example` shows all required variables (no real values)
- [ ] `.env` file created with actual credentials
- [ ] `.env` file is in `.gitignore`
- [ ] Run `git status` to verify `.env` isn't staged
- [ ] Never share `.env` file via email or chat
- [ ] Use testnet for development only
- [ ] Rotate API keys periodically
- [ ] Use IP whitelisting on mainnet keys

### Verify nothing is committed

```bash
# Check what would be committed
git status

# Verify .env is ignored
git check-ignore -v .env
# Output: .env      .gitignore

# Double-check history
git log --all -- '.env' | head -5
# Should show nothing if never committed
```

---

## 🚨 If API Keys Are Exposed

### Immediate Actions

1. **Stop the bot immediately** (kill all processes)
2. **Revoke the API key** on Binance
   - Go to API Management
   - Delete the exposed key
3. **Create new API key** with same permissions
4. **Update `.env`** with new credentials
5. **Restart the bot** with new key

### Check if compromised

```bash
# View all API key usage on Binance
# - Login to Binance > Account > API key/Secret
# - Check "Recent API activities" for suspicious access
# - Enable "IP Whitelist" for mainnet keys
```

---

## 📖 Examples

### Example `.env` for Local Development

```env
# Development with Testnet
BINANCE_TESTNET_KEY=vmPvGuzBUY92d8c8nBfXk8X9Yh3kZ7aB1c2dE3fG4hI5jK6lM
BINANCE_TESTNET_SECRET=tOaBcdEfGhIjKlMnOpQrStUvWxYzAbCdEfGhIjKlMnOpQrStUvWx

# Low risk for testing
TRADING_BinanceApi__UseTestnet=true
TRADING_RiskManagement__RiskPerTradePercent=0.5
TRADING_RiskManagement__MaxDailyDrawdownPercent=1

# Telegram notifications
TELEGRAM_BOT_TOKEN=123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11
TELEGRAM_CHAT_ID=987654321
```

### Example for CI/CD (GitHub Actions)

```yaml
# .github/workflows/test.yml
name: Tests
on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0'

      # Configuration tests (no credentials needed)
      - name: Configuration Tests
        run: dotnet test ComplexBot.Integration --filter "Configuration"

      # Unit tests (no credentials needed)
      - name: Unit Tests
        run: dotnet test ComplexBot.Tests

      # Integration tests (with secrets)
      - name: Integration Tests (Binance)
        if: secrets.BINANCE_TESTNET_KEY != ''
        run: dotnet test ComplexBot.Integration --filter "BinanceTestnetIntegrationTests"
        env:
          BINANCE_TESTNET_KEY: ${{ secrets.BINANCE_TESTNET_KEY }}
          BINANCE_TESTNET_SECRET: ${{ secrets.BINANCE_TESTNET_SECRET }}
```

---

## 🆘 Troubleshooting

### "API key not found"

```bash
# Check if .env is loaded
$env:BINANCE_TESTNET_KEY
# If empty, run: . .\load-env.ps1

# Check if variable is set correctly
Get-ChildItem env: | grep BINANCE
```

### "Credentials not recognized by Binance"

```bash
# Verify in .env file
cat .env | grep BINANCE_TESTNET

# Check for trailing spaces (common issue)
# Make sure: BINANCE_TESTNET_KEY=abc123 (no spaces)
# Not: BINANCE_TESTNET_KEY = abc123 (spaces break it)
```

### Tests still asking for credentials

```bash
# Verify configuration is being read
dotnet run -- --check-config

# Run with verbose to see configuration source
dotnet test -v detailed | grep -i "configuration"
```

---

## 📚 Related Files

- [.env.example](.env.example) - Template with all available variables
- [.gitignore](.gitignore) - Ignore patterns for .env files
- [appsettings.json](ComplexBot/appsettings.json) - Default public configuration
- [INTEGRATION_TESTS_SETUP.md](INTEGRATION_TESTS_SETUP.md) - How to activate testnet tests

---

**Security Note**: Environment variables provide another layer of security, but always follow these principles:

✅ **DO:**
- Use `.env.example` as template
- Never commit actual credentials
- Use testnet for development
- Rotate keys regularly
- Use strong API key passwords

❌ **DON'T:**
- Commit `.env` files
- Hardcode API keys in code
- Share `.env` files via email
- Use same key for multiple systems
- Disable IP whitelisting on mainnet

