using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Serilog;

namespace SignalBot.Services.Commands;

/// <summary>
/// Handles incoming Telegram bot commands
/// </summary>
public class TelegramCommandHandler
{
    private static readonly IReadOnlyDictionary<string, string> CommandAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["/start"] = "/help",
            ["/pos"] = "/positions"
        };

    private readonly IBotCommands _commands;
    private readonly TelegramBotClient _botClient;
    private readonly long _authorizedChatId;
    private readonly ILogger _logger;

    public TelegramCommandHandler(
        IBotCommands commands,
        string botToken,
        long authorizedChatId,
        ILogger? logger = null)
    {
        _commands = commands;
        _botClient = new TelegramBotClient(botToken);
        _authorizedChatId = authorizedChatId;
        _logger = logger ?? Log.ForContext<TelegramCommandHandler>();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _logger.Information("Starting Telegram command handler");

        // Start receiving updates
        _botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            cancellationToken: ct);

        var me = await _botClient.GetMe(cancellationToken: ct);
        _logger.Information("Telegram bot started: @{BotUsername}", me.Username);
    }

    public void Stop()
    {
        _logger.Information("Stopping Telegram command handler");
        // TelegramBotClient handles cleanup automatically
    }

    private async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken ct)
    {
        try
        {
            if (update.Type != UpdateType.Message || update.Message?.Text == null)
                return;

            var message = update.Message;
            var chatId = message.Chat.Id;

            // Check authorization
            if (chatId != _authorizedChatId)
            {
                _logger.Warning("Unauthorized command attempt from chat {ChatId}", chatId);
                await botClient.SendMessage(
                    chatId,
                    "❌ Unauthorized. This bot is private.",
                    cancellationToken: ct);
                return;
            }

            var text = message.Text.Trim();

            _logger.Information("Received command: {Command} from {ChatId}", text, chatId);

            string response = await ProcessCommandAsync(text, ct);

            await botClient.SendMessage(
                chatId,
                response,
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling Telegram update");
        }
    }

    private Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        CancellationToken ct)
    {
        _logger.Error(exception, "Telegram bot error");
        return Task.CompletedTask;
    }

    private async Task<string> ProcessCommandAsync(string text, CancellationToken ct)
    {
        // Parse command and arguments
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();
        var args = parts.Skip(1).ToArray();

        if (CommandAliases.TryGetValue(command, out var aliasedCommand))
        {
            command = aliasedCommand;
        }

        var handlers = new Dictionary<string, Func<Task<string>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["/help"] = () => Task.FromResult(_commands.GetHelp()),
            ["/status"] = () => _commands.GetStatusAsync(ct),
            ["/positions"] = () => _commands.GetPositionsAsync(ct),
            ["/pause"] = () => _commands.PauseAsync(ct),
            ["/resume"] = () => _commands.ResumeAsync(ct),
            ["/closeall"] = () => _commands.CloseAllAsync(ct),
            ["/close"] = () => args.Length > 0
                ? _commands.ClosePositionAsync(args[0], ct)
                : Task.FromResult("❌ Usage: /close BTCUSDT"),
            ["/resetcooldown"] = () => _commands.ResetCooldownAsync(ct),
            ["/stop"] = () => _commands.EmergencyStopAsync(ct)
        };

        if (handlers.TryGetValue(command, out var handler))
        {
            return await handler();
        }

        return $"❌ Unknown command: {command}\n\nUse /help to see available commands.";
    }
}
