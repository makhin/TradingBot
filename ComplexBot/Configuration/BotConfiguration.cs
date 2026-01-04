using Binance.Net.Enums;
using ComplexBot.Services.Analytics;
using ComplexBot.Services.Backtesting;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.Trading;

namespace ComplexBot.Configuration;

public class BotConfiguration
{
    public BinanceApiSettings BinanceApi { get; set; } = new();
    public TelegramSettings Telegram { get; set; } = new();
    public RiskManagementSettings RiskManagement { get; set; } = new();
    public PortfolioRiskConfigSettings PortfolioRisk { get; set; } = new();
    public StrategyConfigSettings Strategy { get; set; } = new();
    public EnsembleConfigSettings Ensemble { get; set; } = new();
    public EnsembleOptimizerConfigSettings EnsembleOptimizer { get; set; } = new();
    public MaStrategyConfigSettings MaStrategy { get; set; } = new();
    public RsiStrategyConfigSettings RsiStrategy { get; set; } = new();
    public MaOptimizerConfigSettings MaOptimizer { get; set; } = new();
    public RsiOptimizerConfigSettings RsiOptimizer { get; set; } = new();
    public BacktestingSettings Backtesting { get; set; } = new();
    public LiveTradingSettings LiveTrading { get; set; } = new();
    public MultiPairLiveTradingSettings MultiPairLiveTrading { get; set; } = new();
    public OptimizationSettings Optimization { get; set; } = new();
    public GeneticOptimizerConfigSettings GeneticOptimizer { get; set; } = new();
}

public class BinanceApiSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public bool UseTestnet { get; set; } = true;
}

public class TelegramSettings
{
    public bool Enabled { get; set; } = false;
    public string BotToken { get; set; } = string.Empty;
    public long ChatId { get; set; } = 0;
}

public class RiskManagementSettings
{
    public decimal RiskPerTradePercent { get; set; } = 1.5m;
    public decimal MaxPortfolioHeatPercent { get; set; } = 15m;
    public decimal MaxDrawdownPercent { get; set; } = 20m;
    public decimal MaxDailyDrawdownPercent { get; set; } = 3m;
    public decimal AtrStopMultiplier { get; set; } = 2.5m;
    public decimal TakeProfitMultiplier { get; set; } = 1.5m;
    public decimal MinimumEquityUsd { get; set; } = 100m;

    public RiskSettings ToRiskSettings() => new()
    {
        RiskPerTradePercent = RiskPerTradePercent,
        MaxPortfolioHeatPercent = MaxPortfolioHeatPercent,
        MaxDrawdownPercent = MaxDrawdownPercent,
        MaxDailyDrawdownPercent = MaxDailyDrawdownPercent,
        AtrStopMultiplier = AtrStopMultiplier,
        TakeProfitMultiplier = TakeProfitMultiplier,
        MinimumEquityUsd = MinimumEquityUsd
    };
}

public class PortfolioRiskConfigSettings
{
    public decimal MaxTotalDrawdownPercent { get; set; } = 25m;
    public decimal MaxCorrelatedRiskPercent { get; set; } = 10m;
    public int MaxConcurrentPositions { get; set; } = 5;
    public Dictionary<string, string[]> CorrelationGroups { get; set; } = new();

    public PortfolioRiskSettings ToPortfolioRiskSettings() => new()
    {
        MaxTotalDrawdownPercent = MaxTotalDrawdownPercent,
        MaxCorrelatedRiskPercent = MaxCorrelatedRiskPercent,
        MaxConcurrentPositions = MaxConcurrentPositions
    };
}

public class StrategyConfigSettings
{
    // ADX settings
    public int AdxPeriod { get; set; } = 14;
    public decimal AdxThreshold { get; set; } = 25m;
    public decimal AdxExitThreshold { get; set; } = 18m;
    public bool RequireFreshTrend { get; set; } = false;
    public bool RequireAdxRising { get; set; } = false;
    public int AdxSlopeLookback { get; set; } = 5;
    public int AdxFallingExitBars { get; set; } = 0;
    public int MaxBarsInTrade { get; set; } = 0;

    // EMA settings
    public int FastEmaPeriod { get; set; } = 20;
    public int SlowEmaPeriod { get; set; } = 50;

    // ATR settings
    public int AtrPeriod { get; set; } = 14;
    public decimal AtrStopMultiplier { get; set; } = 2.5m;
    public decimal TakeProfitMultiplier { get; set; } = 1.5m;
    public decimal MinAtrPercent { get; set; } = 0m;
    public decimal MaxAtrPercent { get; set; } = 100m;

    // Volume settings
    public int VolumePeriod { get; set; } = 20;
    public decimal VolumeThreshold { get; set; } = 1.5m;
    public bool RequireVolumeConfirmation { get; set; } = true;

    // OBV settings
    public int ObvPeriod { get; set; } = 20;
    public bool RequireObvConfirmation { get; set; } = true;

    // Partial exit settings
    public decimal PartialExitRMultiple { get; set; } = 1m;
    public decimal PartialExitFraction { get; set; } = 0.5m;

    public StrategySettings ToStrategySettings() => new()
    {
        AdxPeriod = AdxPeriod,
        AdxThreshold = AdxThreshold,
        AdxExitThreshold = AdxExitThreshold,
        RequireFreshTrend = RequireFreshTrend,
        RequireAdxRising = RequireAdxRising,
        AdxSlopeLookback = AdxSlopeLookback,
        AdxFallingExitBars = AdxFallingExitBars,
        MaxBarsInTrade = MaxBarsInTrade,
        FastEmaPeriod = FastEmaPeriod,
        SlowEmaPeriod = SlowEmaPeriod,
        AtrPeriod = AtrPeriod,
        AtrStopMultiplier = AtrStopMultiplier,
        TakeProfitMultiplier = TakeProfitMultiplier,
        MinAtrPercent = MinAtrPercent,
        MaxAtrPercent = MaxAtrPercent,
        VolumePeriod = VolumePeriod,
        VolumeThreshold = VolumeThreshold,
        RequireVolumeConfirmation = RequireVolumeConfirmation,
        ObvPeriod = ObvPeriod,
        RequireObvConfirmation = RequireObvConfirmation,
        PartialExitRMultiple = PartialExitRMultiple,
        PartialExitFraction = PartialExitFraction
    };
}

public class EnsembleConfigSettings
{
    public bool Enabled { get; set; } = false;
    public decimal MinimumAgreement { get; set; } = 0.6m;
    public bool UseConfidenceWeighting { get; set; } = true;
    public Dictionary<string, decimal> StrategyWeights { get; set; } = new()
    {
        ["ADX Trend Following + Volume"] = 0.5m,
        ["MA Crossover"] = 0.25m,
        ["RSI Mean Reversion"] = 0.25m
    };

    public EnsembleSettings ToEnsembleSettings() => new()
    {
        MinimumAgreement = MinimumAgreement,
        UseConfidenceWeighting = UseConfidenceWeighting,
        StrategyWeights = StrategyWeights ?? new Dictionary<string, decimal>()
    };
}

public class MaStrategyConfigSettings
{
    public int FastMaPeriod { get; set; } = 10;
    public int SlowMaPeriod { get; set; } = 30;
    public int AtrPeriod { get; set; } = 14;
    public decimal AtrStopMultiplier { get; set; } = 2.0m;
    public decimal TakeProfitMultiplier { get; set; } = 2.0m;
    public int VolumePeriod { get; set; } = 20;
    public decimal VolumeThreshold { get; set; } = 1.2m;
    public bool RequireVolumeConfirmation { get; set; } = true;

    public MaStrategySettings ToMaStrategySettings() => new()
    {
        FastMaPeriod = FastMaPeriod,
        SlowMaPeriod = SlowMaPeriod,
        AtrPeriod = AtrPeriod,
        AtrStopMultiplier = AtrStopMultiplier,
        TakeProfitMultiplier = TakeProfitMultiplier,
        VolumePeriod = VolumePeriod,
        VolumeThreshold = VolumeThreshold,
        RequireVolumeConfirmation = RequireVolumeConfirmation
    };

    public static MaStrategyConfigSettings FromSettings(MaStrategySettings settings) => new()
    {
        FastMaPeriod = settings.FastMaPeriod,
        SlowMaPeriod = settings.SlowMaPeriod,
        AtrPeriod = settings.AtrPeriod,
        AtrStopMultiplier = settings.AtrStopMultiplier,
        TakeProfitMultiplier = settings.TakeProfitMultiplier,
        VolumePeriod = settings.VolumePeriod,
        VolumeThreshold = settings.VolumeThreshold,
        RequireVolumeConfirmation = settings.RequireVolumeConfirmation
    };
}

public class RsiStrategyConfigSettings
{
    public int RsiPeriod { get; set; } = 14;
    public decimal OversoldLevel { get; set; } = 30m;
    public decimal OverboughtLevel { get; set; } = 70m;
    public decimal NeutralZoneLow { get; set; } = 45m;
    public decimal NeutralZoneHigh { get; set; } = 55m;
    public bool ExitOnNeutral { get; set; } = false;
    public int AtrPeriod { get; set; } = 14;
    public decimal AtrStopMultiplier { get; set; } = 1.5m;
    public decimal TakeProfitMultiplier { get; set; } = 2.0m;
    public int TrendFilterPeriod { get; set; } = 50;
    public bool UseTrendFilter { get; set; } = true;
    public int VolumePeriod { get; set; } = 20;
    public decimal VolumeThreshold { get; set; } = 1.0m;
    public bool RequireVolumeConfirmation { get; set; } = false;

    public RsiStrategySettings ToRsiStrategySettings() => new()
    {
        RsiPeriod = RsiPeriod,
        OversoldLevel = OversoldLevel,
        OverboughtLevel = OverboughtLevel,
        NeutralZoneLow = NeutralZoneLow,
        NeutralZoneHigh = NeutralZoneHigh,
        ExitOnNeutral = ExitOnNeutral,
        AtrPeriod = AtrPeriod,
        AtrStopMultiplier = AtrStopMultiplier,
        TakeProfitMultiplier = TakeProfitMultiplier,
        TrendFilterPeriod = TrendFilterPeriod,
        UseTrendFilter = UseTrendFilter,
        VolumePeriod = VolumePeriod,
        VolumeThreshold = VolumeThreshold,
        RequireVolumeConfirmation = RequireVolumeConfirmation
    };

    public static RsiStrategyConfigSettings FromSettings(RsiStrategySettings settings) => new()
    {
        RsiPeriod = settings.RsiPeriod,
        OversoldLevel = settings.OversoldLevel,
        OverboughtLevel = settings.OverboughtLevel,
        NeutralZoneLow = settings.NeutralZoneLow,
        NeutralZoneHigh = settings.NeutralZoneHigh,
        ExitOnNeutral = settings.ExitOnNeutral,
        AtrPeriod = settings.AtrPeriod,
        AtrStopMultiplier = settings.AtrStopMultiplier,
        TakeProfitMultiplier = settings.TakeProfitMultiplier,
        TrendFilterPeriod = settings.TrendFilterPeriod,
        UseTrendFilter = settings.UseTrendFilter,
        VolumePeriod = settings.VolumePeriod,
        VolumeThreshold = settings.VolumeThreshold,
        RequireVolumeConfirmation = settings.RequireVolumeConfirmation
    };
}

public class EnsembleOptimizerConfigSettings
{
    public decimal WeightMin { get; set; } = 0.05m;
    public decimal WeightMax { get; set; } = 1.0m;
    public decimal MinimumAgreementMin { get; set; } = 0.4m;
    public decimal MinimumAgreementMax { get; set; } = 0.8m;
    public bool AllowConfidenceWeightingToggle { get; set; } = true;
    public bool DefaultUseConfidenceWeighting { get; set; } = true;
    public int MinTrades { get; set; } = 20;

    public EnsembleOptimizerConfig ToEnsembleOptimizerConfig() => new()
    {
        WeightMin = WeightMin,
        WeightMax = WeightMax,
        MinimumAgreementMin = MinimumAgreementMin,
        MinimumAgreementMax = MinimumAgreementMax,
        AllowConfidenceWeightingToggle = AllowConfidenceWeightingToggle,
        DefaultUseConfidenceWeighting = DefaultUseConfidenceWeighting,
        MinTrades = MinTrades
    };
}

public class MaOptimizerConfigSettings
{
    public int FastMaMin { get; set; } = 5;
    public int FastMaMax { get; set; } = 25;
    public int SlowMaMin { get; set; } = 20;
    public int SlowMaMax { get; set; } = 120;
    public int AtrPeriod { get; set; } = 14;
    public decimal AtrMultiplierMin { get; set; } = 1.5m;
    public decimal AtrMultiplierMax { get; set; } = 4.0m;
    public decimal TakeProfitMultiplierMin { get; set; } = 1.0m;
    public decimal TakeProfitMultiplierMax { get; set; } = 3.0m;
    public int VolumePeriod { get; set; } = 20;
    public decimal VolumeThresholdMin { get; set; } = 1.0m;
    public decimal VolumeThresholdMax { get; set; } = 2.5m;

    public MaOptimizerConfig ToMaOptimizerConfig() => new()
    {
        FastMaMin = FastMaMin,
        FastMaMax = FastMaMax,
        SlowMaMin = SlowMaMin,
        SlowMaMax = SlowMaMax,
        AtrPeriod = AtrPeriod,
        AtrMultiplierMin = AtrMultiplierMin,
        AtrMultiplierMax = AtrMultiplierMax,
        TakeProfitMultiplierMin = TakeProfitMultiplierMin,
        TakeProfitMultiplierMax = TakeProfitMultiplierMax,
        VolumePeriod = VolumePeriod,
        VolumeThresholdMin = VolumeThresholdMin,
        VolumeThresholdMax = VolumeThresholdMax
    };
}

public class RsiOptimizerConfigSettings
{
    public int RsiPeriodMin { get; set; } = 10;
    public int RsiPeriodMax { get; set; } = 20;
    public decimal OversoldMin { get; set; } = 20m;
    public decimal OversoldMax { get; set; } = 35m;
    public decimal OverboughtMin { get; set; } = 65m;
    public decimal OverboughtMax { get; set; } = 80m;
    public decimal NeutralZoneLow { get; set; } = 45m;
    public decimal NeutralZoneHigh { get; set; } = 55m;
    public int AtrPeriod { get; set; } = 14;
    public decimal AtrMultiplierMin { get; set; } = 1.0m;
    public decimal AtrMultiplierMax { get; set; } = 3.5m;
    public decimal TakeProfitMultiplierMin { get; set; } = 1.5m;
    public decimal TakeProfitMultiplierMax { get; set; } = 3.0m;
    public int TrendFilterMin { get; set; } = 20;
    public int TrendFilterMax { get; set; } = 100;
    public int VolumePeriod { get; set; } = 20;
    public decimal VolumeThresholdMin { get; set; } = 1.0m;
    public decimal VolumeThresholdMax { get; set; } = 2.5m;

    public RsiOptimizerConfig ToRsiOptimizerConfig() => new()
    {
        RsiPeriodMin = RsiPeriodMin,
        RsiPeriodMax = RsiPeriodMax,
        OversoldMin = OversoldMin,
        OversoldMax = OversoldMax,
        OverboughtMin = OverboughtMin,
        OverboughtMax = OverboughtMax,
        NeutralZoneLow = NeutralZoneLow,
        NeutralZoneHigh = NeutralZoneHigh,
        AtrPeriod = AtrPeriod,
        AtrMultiplierMin = AtrMultiplierMin,
        AtrMultiplierMax = AtrMultiplierMax,
        TakeProfitMultiplierMin = TakeProfitMultiplierMin,
        TakeProfitMultiplierMax = TakeProfitMultiplierMax,
        TrendFilterMin = TrendFilterMin,
        TrendFilterMax = TrendFilterMax,
        VolumePeriod = VolumePeriod,
        VolumeThresholdMin = VolumeThresholdMin,
        VolumeThresholdMax = VolumeThresholdMax
    };
}

public class BacktestingSettings
{
    public decimal InitialCapital { get; set; } = 10000m;
    public decimal CommissionPercent { get; set; } = 0.1m;
    public decimal SlippagePercent { get; set; } = 0.05m;

    public BacktestSettings ToBacktestSettings() => new()
    {
        InitialCapital = InitialCapital,
        CommissionPercent = CommissionPercent,
        SlippagePercent = SlippagePercent
    };
}

public class LiveTradingSettings
{
    public string Symbol { get; set; } = "BTCUSDT";
    public string Interval { get; set; } = "FourHour";
    public decimal InitialCapital { get; set; } = 10000m;
    public bool UseTestnet { get; set; } = true;
    public bool PaperTrade { get; set; } = true;
    public int WarmupCandles { get; set; } = 100;
    public string TradingMode { get; set; } = "Spot";
    public decimal FeeRate { get; set; } = 0.001m;
    public decimal SlippageBps { get; set; } = 2m;

    public LiveTraderSettings ToLiveTraderSettings() => new()
    {
        Symbol = Symbol,
        Interval = ParseInterval(Interval),
        InitialCapital = InitialCapital,
        UseTestnet = UseTestnet,
        PaperTrade = PaperTrade,
        WarmupCandles = WarmupCandles,
        TradingMode = TradingMode == "Spot" ? Services.Trading.TradingMode.Spot : Services.Trading.TradingMode.Futures,
        FeeRate = FeeRate,
        SlippageBps = SlippageBps
    };

    private static KlineInterval ParseInterval(string interval) => interval switch
    {
        "1h" or "OneHour" => KlineInterval.OneHour,
        "4h" or "FourHour" => KlineInterval.FourHour,
        "1d" or "OneDay" => KlineInterval.OneDay,
        _ => KlineInterval.FourHour
    };
}

public class OptimizationSettings
{
    public string OptimizeFor { get; set; } = "RiskAdjusted";
    public int[] AdxPeriodRange { get; set; } = [10, 14, 20];
    public decimal[] AdxThresholdRange { get; set; } = [20m, 25m, 30m];
    public int[] FastEmaRange { get; set; } = [10, 15, 20, 25];
    public int[] SlowEmaRange { get; set; } = [40, 50, 60, 80];
    public decimal[] AtrMultiplierRange { get; set; } = [2.0m, 2.5m, 3.0m];
    public decimal[] VolumeThresholdRange { get; set; } = [1.0m, 1.5m, 2.0m];

    public OptimizerSettings ToOptimizerSettings() => new()
    {
        OptimizeFor = ParseOptimizationTarget(OptimizeFor),
        AdxPeriodRange = AdxPeriodRange,
        AdxThresholdRange = AdxThresholdRange,
        FastEmaRange = FastEmaRange,
        SlowEmaRange = SlowEmaRange,
        AtrMultiplierRange = AtrMultiplierRange,
        VolumeThresholdRange = VolumeThresholdRange
    };

    private static OptimizationTarget ParseOptimizationTarget(string target) => target switch
    {
        "RiskAdjusted" => OptimizationTarget.RiskAdjusted,
        "SharpeRatio" => OptimizationTarget.SharpeRatio,
        "SortinoRatio" => OptimizationTarget.SortinoRatio,
        "ProfitFactor" => OptimizationTarget.ProfitFactor,
        "TotalReturn" => OptimizationTarget.TotalReturn,
        _ => OptimizationTarget.RiskAdjusted
    };
}

public class GeneticOptimizerConfigSettings
{
    public int PopulationSize { get; set; } = 100;
    public int Generations { get; set; } = 50;
    public int EliteCount { get; set; } = 5;
    public int TournamentSize { get; set; } = 5;
    public double CrossoverRate { get; set; } = 0.8;
    public double MutationRate { get; set; } = 0.15;
    public int EarlyStoppingPatience { get; set; } = 10;
    public decimal EarlyStoppingThreshold { get; set; } = 0.01m;
    public int? RandomSeed { get; set; }

    public GeneticOptimizerSettings ToGeneticOptimizerSettings() => new()
    {
        PopulationSize = PopulationSize,
        Generations = Generations,
        EliteCount = EliteCount,
        TournamentSize = TournamentSize,
        CrossoverRate = CrossoverRate,
        MutationRate = MutationRate,
        EarlyStoppingPatience = EarlyStoppingPatience,
        EarlyStoppingThreshold = EarlyStoppingThreshold,
        RandomSeed = RandomSeed
    };
}

public class MultiPairLiveTradingSettings
{
    public bool Enabled { get; set; } = false;
    public decimal TotalCapital { get; set; } = 10000m;
    public List<TradingPairConfig> TradingPairs { get; set; } = new();
    public string AllocationMode { get; set; } = "Equal";
    public bool UsePortfolioRiskManager { get; set; } = true;
}

public class TradingPairConfig
{
    public string Symbol { get; set; } = "";
    public string Interval { get; set; } = "FourHour";
    public string Strategy { get; set; } = "ADX";  // ADX, RSI, MA, Ensemble
    public decimal? WeightPercent { get; set; }    // Optional: manual weight (for Weighted mode)

    // Multi-Timeframe Support
    public string Role { get; set; } = "Primary";  // Primary, Filter, Exit
    public string? FilterMode { get; set; }        // For Filter role: "Confirm", "Veto", "Score"

    // Optional per-pair strategy overrides
    public StrategyConfigSettings? StrategyOverrides { get; set; }
}
