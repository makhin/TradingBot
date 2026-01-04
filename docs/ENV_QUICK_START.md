# 🔐 Environment Variables - Quick Start

Secure your API keys the right way.

## 30-Second Setup

### 1️⃣ Copy template
```bash
cp .env.example .env
```

### 2️⃣ Add your credentials
```bash
# Windows PowerShell
notepad .env

# Linux/WSL/Mac
nano .env
```

**Add your testnet keys:**
```env
BINANCE_TESTNET_KEY=your-actual-testnet-key
BINANCE_TESTNET_SECRET=your-actual-testnet-secret
TRADING_BinanceApi__UseTestnet=true
```

### 3️⃣ Load and run

#### PowerShell (Windows)
```powershell
. .\load-env.ps1 -Validate
dotnet run
```

#### Bash/WSL
```bash
source load-env.sh true
dotnet run
```

---

## 📁 Key Files

| File | Purpose | Git Status |
|------|---------|-----------|
| `.env.example` | Template with all variables | ✅ Commit |
| `.env` | Your actual credentials | ❌ Never commit |
| `load-env.ps1` | PowerShell helper | ✅ Commit |
| `load-env.sh` | Bash helper | ✅ Commit |
| `ENV_SETUP.md` | Full documentation | ✅ Commit |

---

## ⚠️ Never Commit `.env`

```bash
# Verify .env is ignored
git status
# Should NOT list .env

# Double check it's in .gitignore
git check-ignore -v .env
```

---

## 🧪 Test It Works

```bash
# Load and validate
. .\load-env.ps1 -Show -Validate

# Run unit tests (no credentials needed)
dotnet test ComplexBot.Tests

# Run integration tests (configuration only)
dotnet test ComplexBot.Integration --filter "Configuration"
```

---

## 🔗 Where to Get Keys

### Binance Testnet (FREE - Recommended for development)

1. Go to https://testnet.binance.vision/
2. Sign in with your regular Binance account
3. Click "API Management"
4. Create new API key
5. Copy Key and Secret → paste in `.env`

### Binance Mainnet (Real money - CAREFUL!)

1. Go to https://www.binance.com/account/api-management
2. Create new API key
3. Set permissions to **"Spot Trading"** only
4. Enable IP whitelist with your IP only
5. **Copy to `.env` ONLY on secure machine**

---

## ✅ Security Checklist

- [ ] Created `.env` from `.env.example`
- [ ] Added real testnet keys to `.env`
- [ ] Ran `git status` - `.env` NOT listed
- [ ] Ran load script - no validation errors
- [ ] Can run `dotnet test ComplexBot.Tests` successfully

---

## 🚨 If Keys Leak

1. **Stop immediately** - kill all running processes
2. **Revoke key** on Binance website
3. **Generate new key** with same permissions
4. **Update `.env`** with new credentials
5. **Restart** application

---

## 📚 Need More Info?

→ See [ENV_SETUP.md](ENV_SETUP.md) for comprehensive guide

---

**Remember:** `.env` is for your eyes only! 🔐
