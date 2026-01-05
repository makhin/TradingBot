namespace ComplexBot.Services.Trading;

internal record OrderResult(bool Success, decimal FilledQuantity, decimal AveragePrice, string? ErrorMessage);
