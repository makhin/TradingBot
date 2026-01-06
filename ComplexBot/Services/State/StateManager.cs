using System.Text.Json;
using ComplexBot.Models;
using Binance.Net.Clients;
using Serilog;

namespace ComplexBot.Services.State;

public class StateManager
{
    private readonly string _statePath;
    private readonly ILogger _logger;

    public StateManager(string statePath = "bot_state.json", ILogger? logger = null)
    {
        _statePath = statePath;
        _logger = logger ?? Log.ForContext<StateManager>();
    }

    public async Task SaveState(BotState state, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_statePath, json, cancellationToken);
            _logger.Information("State saved: {PositionCount} positions, Equity: {Equity}", state.OpenPositions.Count, state.CurrentEquity);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save state");
        }
    }

    public async Task<BotState?> LoadState()
    {
        if (!File.Exists(_statePath))
        {
            _logger.Information("No saved state found, starting fresh");
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_statePath);
            var state = JsonSerializer.Deserialize<BotState>(json);
            _logger.Information("State loaded: {PositionCount} positions, Equity: {Equity}", state?.OpenPositions.Count ?? 0, state?.CurrentEquity ?? 0m);
            return state;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load state");
            return null;
        }
    }

    public async Task<StateReconciliationResult> ReconcileWithExchange(BinanceRestClient client, BotState state, string symbol)
    {
        _logger.Information("Reconciling state with exchange for {Symbol}", symbol);

        var result = new StateReconciliationResult
        {
            PositionsConfirmed = new List<SavedPosition>(),
            PositionsMismatch = new List<(SavedPosition Expected, decimal Actual)>(),
            OcoOrdersActive = new List<SavedOcoOrder>(),
            OcoOrdersMissing = new List<SavedOcoOrder>()
        };

        // Check positions
        foreach (var savedPos in state.OpenPositions)
        {
            try
            {
                var balanceResult = await client.SpotApi.Account.GetBalancesAsync();
                if (!balanceResult.Success)
                {
                    _logger.Warning("Failed to get balance: {Error}", balanceResult.Error?.Message);
                    continue;
                }

                var asset = savedPos.Symbol.Replace("USDT", "");
                var actualBalance = balanceResult.Data.FirstOrDefault(b => b.Asset == asset);
                decimal actualQuantity = actualBalance?.Available ?? 0;

                // Allow 1% tolerance for rounding/fees
                if (actualQuantity >= savedPos.RemainingQuantity * 0.99m)
                {
                    _logger.Information("Position confirmed: {Symbol} {Quantity:F5}", savedPos.Symbol, savedPos.RemainingQuantity);
                    result.PositionsConfirmed.Add(savedPos);
                }
                else
                {
                    _logger.Warning("Position mismatch: {Symbol}. Expected {Expected:F5}, Actual {Actual:F5}",
                        savedPos.Symbol,
                        savedPos.RemainingQuantity,
                        actualQuantity);
                    result.PositionsMismatch.Add((savedPos, actualQuantity));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking position {Symbol}", savedPos.Symbol);
            }
        }

        // Check OCO orders
        foreach (var oco in state.ActiveOcoOrders)
        {
            try
            {
                var ocoStatus = await client.SpotApi.Trading.GetOcoOrderAsync(
                    orderListId: oco.OrderListId);

                if (ocoStatus.Success)
                {
                    _logger.Information("OCO order active: {Symbol} #{OrderListId}", oco.Symbol, oco.OrderListId);
                    result.OcoOrdersActive.Add(oco);
                }
                else
                {
                    _logger.Warning("OCO order not found: {Symbol} #{OrderListId}. This may mean the order was filled or cancelled",
                        oco.Symbol,
                        oco.OrderListId);
                    result.OcoOrdersMissing.Add(oco);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking OCO {OrderListId}", oco.OrderListId);
            }
        }

        _logger.Information(
            "Reconciliation complete. Positions: {Confirmed} confirmed, {Mismatched} mismatched. OCO Orders: {Active} active, {Missing} missing",
            result.PositionsConfirmed.Count,
            result.PositionsMismatch.Count,
            result.OcoOrdersActive.Count,
            result.OcoOrdersMissing.Count);

        return result;
    }

    public void DeleteState()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                File.Delete(_statePath);
                _logger.Information("State file deleted");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete state");
        }
    }

    public bool StateExists() => File.Exists(_statePath);
}
