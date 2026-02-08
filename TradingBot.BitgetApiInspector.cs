using System;
using System.Reflection;
using System.Linq;
using Bitget.Net.Clients;
using Bitget.Net.Enums;

public class ApiInspector
{
    public static void Main()
    {
        // Check KlineInterval enum values
        Console.WriteLine("=== KlineInterval Enum Values ===");
        var klineIntervalType = typeof(Bitget.Net.Enums.KlineInterval);
        foreach (var value in Enum.GetValues(klineIntervalType))
        {
            Console.WriteLine($"  {value}");
        }
        
        // Check BitgetFuturesKlineInterval if it exists
        Console.WriteLine("\n=== BitgetFuturesKlineInterval Enum Values ===");
        try
        {
            var futuresKlineType = typeof(Bitget.Net.Enums.BitgetFuturesKlineInterval);
            foreach (var value in Enum.GetValues(futuresKlineType))
            {
                Console.WriteLine($"  {value}");
            }
        }
        catch { Console.WriteLine("  Type not found"); }
        
        // Check FuturesApiV2.ExchangeData methods
        Console.WriteLine("\n=== FuturesApiV2.ExchangeData Methods ===");
        var restClientType = typeof(BitgetRestClient);
        var futuresApi = restClientType.GetProperty("FuturesApiV2");
        if (futuresApi != null)
        {
            var exchangeData = futuresApi.PropertyType.GetProperty("ExchangeData");
            if (exchangeData != null)
            {
                var methods = exchangeData.PropertyType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name.Contains("Symbol") || m.Name.Contains("Kline") || m.Name.Contains("Account"))
                    .Select(m => $"  {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
                foreach (var method in methods.Distinct())
                {
                    Console.WriteLine(method);
                }
            }
        }
        
        // Check BitgetFuturesKlineUpdate properties
        Console.WriteLine("\n=== BitgetFuturesKlineUpdate Properties ===");
        try
        {
            var klineUpdateType = typeof(Bitget.Net.Objects.Models.V2.BitgetFuturesKlineUpdate);
            foreach (var prop in klineUpdateType.GetProperties())
            {
                Console.WriteLine($"  {prop.PropertyType.Name} {prop.Name}");
            }
        }
        catch { Console.WriteLine("  Type not found"); }
        
        // Check BitgetPositionUpdate properties
        Console.WriteLine("\n=== BitgetPositionUpdate Properties ===");
        try
        {
            var posUpdateType = typeof(Bitget.Net.Objects.Models.V2.BitgetPositionUpdate);
            foreach (var prop in posUpdateType.GetProperties())
            {
                Console.WriteLine($"  {prop.PropertyType.Name} {prop.Name}");
            }
        }
        catch { Console.WriteLine("  Type not found"); }
        
        // Check BitgetPosition properties
        Console.WriteLine("\n=== BitgetPosition Properties ===");
        try
        {
            var posType = typeof(Bitget.Net.Objects.Models.V2.BitgetPosition);
            foreach (var prop in posType.GetProperties())
            {
                Console.WriteLine($"  {prop.PropertyType.Name} {prop.Name}");
            }
        }
        catch { Console.WriteLine("  Type not found"); }
    }
}
