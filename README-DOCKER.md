# Запуск TradingBot на Raspberry Pi 4 в Docker

## Требования

- Raspberry Pi 4 (рекомендуется модель с 4GB RAM)
- Raspberry Pi OS 64-bit (Bullseye или новее)
- Docker и Docker Compose
- Доступ в интернет

## Установка Docker на Raspberry Pi

### 1. Обновите систему

```bash
sudo apt update && sudo apt upgrade -y
```

### 2. Установите Docker

```bash
curl -fsSL https://get.docker.com -o get-docker.sh
sudo sh get-docker.sh
```

### 3. Добавьте пользователя в группу docker

```bash
sudo usermod -aG docker $USER
```

**Важно:** Перезайдите в систему или выполните `newgrp docker` для применения изменений.

### 4. Установите Docker Compose

```bash
sudo apt install docker-compose-plugin -y
```

Проверьте установку:

```bash
docker --version
docker compose version
```

## Настройка проекта

### 1. Клонируйте репозиторий

```bash
git clone https://github.com/your-repo/TradingBot.git
cd TradingBot
```

### 2. Создайте файл `.env` с API ключами

```bash
cat > .env << 'EOF'
# Timezone
TZ=Europe/Moscow

# Binance Testnet (для тестирования)
BINANCE_TESTNET_KEY=your-testnet-api-key
BINANCE_TESTNET_SECRET=your-testnet-secret

# Binance Mainnet (для реальной торговли)
BINANCE_MAINNET_KEY=your-mainnet-api-key
BINANCE_MAINNET_SECRET=your-mainnet-secret

# Режим работы (true = testnet, false = mainnet)
TRADING_BinanceApi__UseTestnet=true

# Telegram уведомления (опционально)
TELEGRAM_BOT_TOKEN=your-bot-token
TELEGRAM_CHAT_ID=your-chat-id
EOF
```

**Замените значения на свои API ключи!**

**Важно:** Файл `.env` автоматически монтируется в Docker контейнер и загружается `ConfigurationService` при старте приложения. Файл `appsettings.docker.json` больше не требуется.

### 3. Создайте директории для данных

```bash
mkdir -p data/HistoricalData data/logs
```

## Сборка и запуск

### Сборка образа для ARM64

```bash
docker compose build
```

> Первая сборка может занять 10-15 минут на Raspberry Pi 4.

### Интерактивный режим (меню)

Для работы с меню бота (бэктестинг, оптимизация и т.д.):

```bash
docker compose run --rm tradingbot
```

### Автоматические режимы (без меню)

Вы можете запустить бота в определённом режиме без интерактивного меню, используя переменную `TRADING_MODE`:

```bash
# Paper trading (тестовая торговля на testnet)
docker compose run --rm -e TRADING_MODE=live tradingbot

# Бэктестинг
docker compose run --rm -e TRADING_MODE=backtest tradingbot

# Оптимизация параметров
docker compose run --rm -e TRADING_MODE=optimize tradingbot

# Walk-Forward анализ
docker compose run --rm -e TRADING_MODE=walkforward tradingbot

# Monte Carlo симуляция
docker compose run --rm -e TRADING_MODE=montecarlo tradingbot

# Загрузка исторических данных
docker compose run --rm -e TRADING_MODE=download tradingbot
```

**Доступные значения TRADING_MODE:**
- `live` - Paper trading (бумажная торговля на testnet)
- `live-real` - Реальная торговля (требует `CONFIRM_LIVE_TRADING=yes`)
- `backtest` - Бэктестинг стратегии
- `optimize` - Оптимизация параметров
- `walkforward` - Walk-Forward анализ
- `montecarlo` - Monte Carlo симуляция
- `download` - Загрузка исторических данных

### Фоновый режим (live trading)

Для автоматической торговли в фоне (использует настройки из `appsettings.json`):

```bash
docker compose --profile live up -d tradingbot-live
```

**Важно:** Убедитесь, что в `ComplexBot/appsettings.json` настроены параметры в секции `LiveTrading`:
- `Symbol` - торговая пара (например, "BTCUSDT")
- `Interval` - интервал свечей ("FourHour", "OneHour", "OneDay")
- `InitialCapital` - начальный капитал
- `TradingMode` - режим торговли ("Spot" или "Futures")

Проверка логов:

```bash
docker compose logs -f tradingbot-live
```

Остановка:

```bash
docker compose --profile live down
```

### Режим реальной торговли (осторожно!)

Для запуска с реальными деньгами нужно:

1. Установить переменную окружения в `.env`:
   ```bash
   TRADING_BinanceApi__UseTestnet=false
   ```

2. Добавить подтверждение для неинтерактивного режима:
   ```bash
   CONFIRM_LIVE_TRADING=yes
   ```

3. Использовать `TRADING_MODE=live-real` в docker-compose или запустить:
   ```bash
   docker compose run --rm -e TRADING_MODE=live-real -e CONFIRM_LIVE_TRADING=yes tradingbot
   ```

## Команды управления

### Просмотр запущенных контейнеров

```bash
docker compose ps
```

### Просмотр логов

#### Docker логи (stdout/stderr)

```bash
# Все сервисы
docker compose logs -f

# Только live trading
docker compose logs -f tradingbot-live

# Последние 100 строк
docker compose logs --tail=100 tradingbot-live
```

#### Файловые логи приложения (Serilog)

Приложение записывает детальные логи в файлы с ротацией по дням:

```bash
# Просмотр сегодняшних логов
tail -f data/logs/tradingbot-$(date +%Y%m%d).log

# Поиск ошибок
grep -i error data/logs/tradingbot-*.log

# Просмотр с фильтрацией по уровню
grep "\[ERR\]" data/logs/tradingbot-*.log  # Только ошибки
grep "\[WRN\]" data/logs/tradingbot-*.log  # Предупреждения
grep "\[INF\]" data/logs/tradingbot-*.log  # Информационные
grep "\[DBG\]" data/logs/tradingbot-*.log  # Отладочные
```

**Уровни логирования:**
- `[DBG]` - DEBUG: Детальная отладочная информация (пишется только в файлы)
- `[INF]` - INFO: Информационные сообщения (консоль и файлы)
- `[WRN]` - WARNING: Предупреждения (консоль и файлы)
- `[ERR]` - ERROR: Ошибки (консоль и файлы)
- `[FTL]` - FATAL: Критические ошибки (консоль и файлы)

**Настройки ротации:**
- Новый файл создается каждый день
- Хранятся последние 30 файлов
- Максимальный размер файла: 100MB
- При достижении лимита создается новый файл с суффиксом

**Расположение:** `./data/logs/tradingbot-YYYYMMDD.log`

### Перезапуск контейнера

```bash
docker compose restart tradingbot-live
```

### Пересборка после изменений

```bash
docker compose build --no-cache
docker compose up -d
```

### Очистка неиспользуемых образов

```bash
docker system prune -a
```

## Мониторинг ресурсов

### Использование CPU и памяти

```bash
docker stats
```

### Проверка health-статуса

```bash
docker inspect --format='{{.State.Health.Status}}' tradingbot
```

## Автозапуск при загрузке системы

Docker контейнеры с `restart: unless-stopped` автоматически запустятся при перезагрузке Raspberry Pi, если Docker сервис включён:

```bash
sudo systemctl enable docker
```

## Оптимизация для Raspberry Pi

### Рекомендуемые настройки

1. **Swap файл** (если 2GB RAM модель):
   ```bash
   sudo dphys-swapfile swapoff
   sudo sed -i 's/CONF_SWAPSIZE=.*/CONF_SWAPSIZE=2048/' /etc/dphys-swapfile
   sudo dphys-swapfile setup
   sudo dphys-swapfile swapon
   ```

2. **Охлаждение**: Используйте радиаторы или активное охлаждение для длительной работы.

3. **Питание**: Используйте качественный блок питания на 3A.

### Ограничение ресурсов

В `docker-compose.yml` уже настроены лимиты:
- Максимум 2GB RAM для интерактивного режима
- Максимум 1GB RAM для live trading

Вы можете изменить их в секции `deploy.resources`.

## Бэкап данных

Все данные хранятся в директории `./data/`:

```bash
# Создание бэкапа
tar -czvf backup-$(date +%Y%m%d).tar.gz data/

# Восстановление
tar -xzvf backup-20240115.tar.gz
```

## Устранение проблем

### Ошибка "permission denied"

```bash
sudo chown -R $USER:$USER data/
```

### Контейнер падает с OOM

Увеличьте swap или уменьшите лимиты памяти в `docker-compose.yml`.

### Не подключается к Binance

1. Проверьте API ключи в `.env`
2. Убедитесь, что для testnet используются testnet ключи
3. Проверьте доступ в интернет: `curl -I https://api.binance.com`

### Медленная сборка

Используйте кросс-компиляцию на более мощной машине:

```bash
# На x64 машине
docker buildx build --platform linux/arm64 -t tradingbot:arm64 --load .

# Сохранение образа
docker save tradingbot:arm64 | gzip > tradingbot-arm64.tar.gz

# На Raspberry Pi
gunzip -c tradingbot-arm64.tar.gz | docker load
```

## Безопасность

1. **Никогда не коммитьте** `.env` файл в git
2. Используйте **testnet** для тестирования
3. Начинайте с **paper trading** (`PaperTrade: true`)
4. Ограничьте права API ключей на Binance (только чтение + торговля, без вывода)
5. Регулярно обновляйте образы и систему

## Получение API ключей

### Binance Testnet
1. Перейдите на https://testnet.binance.vision/
2. Авторизуйтесь через GitHub
3. Сгенерируйте API ключи

### Binance Mainnet
1. Перейдите на https://www.binance.com/
2. Настройки -> API Management
3. Создайте новый ключ с ограничениями по IP (рекомендуется)
