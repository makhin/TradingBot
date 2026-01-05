using System.Text.Json;
using ComplexBot.Models;
using Binance.Net.Clients;

namespace ComplexBot.Services.State;

public class StateManager
{
    private readonly string _statePath;

    public StateManager(string statePath = "bot_state.json")
    {
        _statePath = statePath;
    }

    public async Task SaveState(BotState state)
    {
        try
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_statePath, json);
            Console.WriteLine($"💾 State saved: {state.OpenPositions.Count} positions, Equity: ${state.CurrentEquity:F2}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to save state: {ex.Message}");
        }
    }

    public async Task<BotState?> LoadState()
    {
        if (!File.Exists(_statePath))
        {
            Console.WriteLine("📂 No saved state found, starting fresh");
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_statePath);
            var state = JsonSerializer.Deserialize<BotState>(json);
            Console.WriteLine($"📂 State loaded: {state?.OpenPositions.Count ?? 0} positions, Equity: ${state?.CurrentEquity ?? 0:F2}");
            return state;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to load state: {ex.Message}");
            return null;
        }
    }

    public async Task<StateReconciliationResult> ReconcileWithExchange(BinanceRestClient client, BotState state, string symbol)
    {
        Console.WriteLine("🔄 Reconciling state with exchange...");

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
                    Console.WriteLine($"⚠️ Failed to get balance: {balanceResult.Error?.Message}");
                    continue;
                }

                var asset = savedPos.Symbol.Replace("USDT", "");
                var actualBalance = balanceResult.Data.FirstOrDefault(b => b.Asset == asset);
                decimal actualQuantity = actualBalance?.Available ?? 0;

                // Allow 1% tolerance for rounding/fees
                if (actualQuantity >= savedPos.RemainingQuantity * 0.99m)
                {
                    Console.WriteLine($"✅ Position confirmed: {savedPos.Symbol} {savedPos.RemainingQuantity:F5}");
                    result.PositionsConfirmed.Add(savedPos);
                }
                else
                {
                    Console.WriteLine($"⚠️ Position mismatch: {savedPos.Symbol}");
                    Console.WriteLine($"   Expected: {savedPos.RemainingQuantity:F5}, Actual: {actualQuantity:F5}");
                    result.PositionsMismatch.Add((savedPos, actualQuantity));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error checking position {savedPos.Symbol}: {ex.Message}");
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
                    Console.WriteLine($"✅ OCO order active: {oco.Symbol} #{oco.OrderListId}");
                    result.OcoOrdersActive.Add(oco);
                }
                else
                {
                    Console.WriteLine($"⚠️ OCO order not found: {oco.Symbol} #{oco.OrderListId}");
                    Console.WriteLine($"   This may mean the order was filled or cancelled");
                    result.OcoOrdersMissing.Add(oco);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error checking OCO {oco.OrderListId}: {ex.Message}");
            }
        }

        Console.WriteLine($"📊 Reconciliation complete:");
        Console.WriteLine($"   Positions: {result.PositionsConfirmed.Count} confirmed, {result.PositionsMismatch.Count} mismatched");
        Console.WriteLine($"   OCO Orders: {result.OcoOrdersActive.Count} active, {result.OcoOrdersMissing.Count} missing");

        return result;
    }

    public void DeleteState()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                File.Delete(_statePath);
                Console.WriteLine("🗑️ State file deleted");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to delete state: {ex.Message}");
        }
    }

    public bool StateExists() => File.Exists(_statePath);
}
