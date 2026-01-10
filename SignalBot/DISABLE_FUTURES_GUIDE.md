# Disabling Futures Trading in SignalBot

## Overview

If you're experiencing issues connecting to the Binance Futures API (authentication errors, permission issues, etc.), you can disable Futures trading and run SignalBot in **monitoring-only mode**. In this mode, the bot will:

✅ **Monitor Telegram channels** for trading signals  
✅ **Parse and log signals** to the console  
✅ **Send notifications** about received signals  
❌ **NOT execute trades** or manage positions

This is useful for:
- Testing signal parsing and validation without live trading
- Monitoring while debugging API credential issues
- Running in a read-only mode for safety
- Validating signal patterns before enabling trading

## Configuration Options

### Option 1: appsettings.json (Permanent)

Edit `SignalBot/appsettings.json`:

```json
{
  "SignalBot": {
    "EnableFuturesTrading": false,
    ...rest of config
  }
}
```

### Option 2: Environment Variable (Runtime)

Set before running SignalBot:

**Linux/Mac:**
```bash
export TRADING_SignalBot__EnableFuturesTrading=false
dotnet run
```

**Windows PowerShell:**
```powershell
$env:TRADING_SignalBot__EnableFuturesTrading = "false"
dotnet run
```

**Windows Command Prompt:**
```cmd
set TRADING_SignalBot__EnableFuturesTrading=false
dotnet run
```

**Docker:**
```bash
docker run -e TRADING_SignalBot__EnableFuturesTrading=false ...
```

### Option 3: .env File (Development)

Edit or create `.env` in the SignalBot folder:

```dotenv
# Enable/disable Futures trading
# true = trading enabled (default)
# false = monitoring-only mode
TRADING_SignalBot__EnableFuturesTrading=false

# Other environment variables...
TRADING_BinanceApi__UseTestnet=true
TELEGRAM_BOT_TOKEN=your_token_here
```

## Startup Output

### With Futures Trading ENABLED (Normal Mode)
```
[19:01:06 INF] SignalBot starting up...
[19:01:06 INF] Using Binance Testnet API
[19:01:06 INF] Futures Trading: ENABLED
[19:01:08 INF] Starting SignalBot...
[19:01:10 INF] Connected to Binance Futures API
[19:01:10 INF] SignalBot started successfully
```

### With Futures Trading DISABLED (Monitoring-Only Mode)
```
[19:01:06 INF] SignalBot starting up...
[19:01:06 INF] Using Binance Testnet API
[19:01:06 INF] Futures Trading: DISABLED
[19:01:08 INF] Starting SignalBot...
[19:01:10 WRN] ⚠️ Futures trading is DISABLED in configuration
[19:01:10 INF] SignalBot will run in monitoring-only mode
[19:01:10 INF] ✅ SignalBot started in MONITORING-ONLY mode (no trading)
```

### With Futures API Connection Error
```
[19:01:06 INF] SignalBot starting up...
[19:01:06 INF] Using Binance Testnet API
[19:01:06 INF] Futures Trading: ENABLED
[19:01:08 INF] Starting SignalBot...
[19:01:10 ERR] Failed to connect to Binance Futures API
[19:01:10 WRN] ⚠️ Futures API credentials issue detected
[19:01:10 INF] To disable Futures trading, set 'EnableFuturesTrading' to false
[19:01:10 INF] Or set environment variable: TRADING_SignalBot__EnableFuturesTrading=false
```

## When Futures is Disabled

### What Happens:
1. ✅ Telegram listener starts and monitors channels
2. ✅ Signals are parsed and logged
3. ✅ Signal validation runs (checks price deviation, duplicates, etc.)
4. ✅ Notifications are sent about received signals
5. ❌ No actual trades are executed
6. ❌ Order monitor doesn't run (no positions to track)
7. ❌ Stop-loss and take-profit handlers don't run

### Signal Processing in Monitoring Mode:
```
[Signal Received] BTCUSDT LONG at $45000
  Entry: $45000, Stop: $44000, Target: $46500
  Risk: 1.0% | Position Size: $100
  [MONITORING ONLY] - Would open position but trading is disabled
```

## Fixing API Credential Issues

If you see the error about invalid API key or permissions, here are common solutions:

### 1. Check Testnet API Keys
Testnet and Mainnet use different keys. Make sure you're using the correct ones:
- **Testnet**: https://testnet.binance.vision/
- **Mainnet**: https://www.binance.com/en/my/settings/api-management

### 2. Verify API Permissions

Your API key needs these permissions:
- ✅ **Enable Trading** (for order placement)
- ✅ **Enable Reading Account Trade Volume** 
- ✅ **Enable Reading Account** (for balance)
- ❌ Withdrawals should be disabled

### 3. Check IP Whitelist
- Add your IP to the API key's IP whitelist
- Or use a dynamic IP service to manage it

### 4. Verify API Key Format
- API Key should be ~64 characters
- Secret Key should be ~64 characters
- Make sure no extra spaces or quotes

### 5. For Testnet Specifically
- Testnet credentials are separate from mainnet
- Testnet has limited features
- Make sure TRADING_BinanceApi__UseTestnet=true

## Full Configuration Example

**appsettings.json** (Monitoring-Only Mode):
```json
{
  "SignalBot": {
    "EnableFuturesTrading": false,
    
    "Telegram": {
      "ApiId": 12345,
      "ApiHash": "abcdef...",
      "PhoneNumber": "+1234567890",
      "ChannelIds": [1234567890, 9876543210]
    },
    
    "Trading": {
      "MaxConcurrentPositions": 5,
      "DefaultSymbolSuffix": "USDT"
    },
    
    "Notifications": {
      "TelegramBotToken": "123456:ABC-DEF...",
      "TelegramChatId": "123456789",
      "NotifyOnSignalReceived": true
    }
  },

  "BinanceApi": {
    "UseTestnet": true
  }
}
```

## Transition to Trading Mode

Once your API credentials are working:

1. Set `EnableFuturesTrading` to `true` in config
2. Verify connection with `dotnet run`
3. Check that bot says "Connected to Binance Futures API"
4. Carefully test with small position sizes first

## Troubleshooting

**Q: Signals show as "MONITORING ONLY" but I have trading enabled**
- A: Check if API credentials have permission issues, see logs for details

**Q: I disabled Futures but SignalBot exits immediately**
- A: There may be other configuration issues, check all logs

**Q: Can I enable trading while SignalBot is running?**
- A: No, restart SignalBot after changing EnableFuturesTrading

**Q: Do my position settings matter in monitoring mode?**
- A: No, they're ignored, but good to keep them configured for when trading is enabled

## More Information

- [SignalBot README](./README.md)
- [Binance API Documentation](https://binance-docs.github.io/apidocs/)
- [Testnet Setup](https://testnet.binance.vision/)
