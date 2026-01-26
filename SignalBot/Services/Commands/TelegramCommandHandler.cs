using SignalBot.Configuration;
using SignalBot.Services;
using System;
using System.Threading;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Serilog;

namespace SignalBot.Services.Commands;

/// <summary>
/// Handles incoming Telegram bot commands
/// </summary>
public class TelegramCommandHandler : ServiceBase
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
    private readonly IReadOnlySet<long> _authorizedUserIds;
    private readonly TelegramCommandRetrySettings _retrySettings;
    private readonly string _symbolExample;
    private int _consecutiveErrors;

    public TelegramCommandHandler(
        IBotCommands commands,
        string botToken,
        long authorizedChatId,
        IReadOnlyCollection<long> authorizedUserIds,
        TelegramCommandRetrySettings retrySettings,
        IOptions<SignalBotSettings> settings,
        ILogger? logger = null)
        : base(logger)
    {
        _commands = commands;
        _botClient = new TelegramBotClient(botToken);
        _authorizedChatId = authorizedChatId;
        _authorizedUserIds = new HashSet<long>(authorizedUserIds ?? Array.Empty<long>());
        _retrySettings = retrySettings;
        var suffix = string.IsNullOrWhiteSpace(settings.Value.Trading.DefaultSymbolSuffix)
            ? "USDT"
            : settings.Value.Trading.DefaultSymbolSuffix.Trim().ToUpperInvariant();
        _symbolExample = $"BTC{suffix}";
    }

    protected override async Task OnStartAsync(CancellationToken ct)
    {
        // Start receiving updates
        _botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            cancellationToken: ct);

        var me = await _botClient.GetMe(cancellationToken: ct);
        _logger.Information("Telegram bot started: @{BotUsername}", me.Username);
    }

    protected override Task OnStopAsync(CancellationToken ct)
    {
        // TelegramBotClient handles cleanup automatically
        return Task.CompletedTask;
    }

    private async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken ct)
    {
        try
        {
            Interlocked.Exchange(ref _consecutiveErrors, 0);

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

            var userId = message.From?.Id;
            if (_authorizedUserIds.Count > 0 &&
                (!userId.HasValue || !_authorizedUserIds.Contains(userId.Value)))
            {
                _logger.Warning(
                    "Unauthorized command attempt from user {UserId} in chat {ChatId}",
                    userId,
                    chatId);
                await botClient.SendMessage(
                    chatId,
                    "❌ Unauthorized. Your account is not allowed to use this bot.",
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
        var attempt = Interlocked.Increment(ref _consecutiveErrors);
        var delay = GetRetryDelay(attempt);

        if (delay > TimeSpan.Zero && !ct.IsCancellationRequested)
        {
            _logger.Warning("Retrying Telegram bot polling in {Delay} (attempt {Attempt})", delay, attempt);
            return Task.Delay(delay, ct);
        }

        return Task.CompletedTask;
    }

    private TimeSpan GetRetryDelay(int attempt)
    {
        if (_retrySettings.BaseDelaySeconds <= 0)
        {
            return TimeSpan.Zero;
        }

        var maxDelaySeconds = _retrySettings.MaxDelaySeconds <= 0
            ? _retrySettings.BaseDelaySeconds
            : _retrySettings.MaxDelaySeconds;
        var delaySeconds = _retrySettings.BaseDelaySeconds * Math.Pow(2, attempt - 1);
        var boundedDelaySeconds = Math.Min(delaySeconds, maxDelaySeconds);

        return TimeSpan.FromSeconds(boundedDelaySeconds);
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
                : Task.FromResult($"❌ Usage: /close {_symbolExample}"),
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
