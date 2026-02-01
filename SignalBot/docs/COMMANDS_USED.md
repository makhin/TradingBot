# Commands used during analysis

```
rg -n "DefaultSymbolSuffix|BinanceApi" SignalBot/docs/SIGNALBOT_DESIGN.md
sed -n '330,520p' SignalBot/docs/SIGNALBOT_DESIGN.md
nl -ba SignalBot/docs/SIGNALBOT_DESIGN.md | sed -n '470,720p'
```
