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

### 2. Создайте файл конфигурации

```bash
cp appsettings.docker.json.example appsettings.docker.json
```

Отредактируйте `appsettings.docker.json` под свои нужды.

### 3. Создайте файл `.env` с API ключами

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

### 4. Создайте директории для данных

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

### Фоновый режим (live trading)

Для автоматической торговли в фоне:

```bash
docker compose --profile live up -d tradingbot-live
```

Проверка логов:

```bash
docker compose logs -f tradingbot-live
```

Остановка:

```bash
docker compose --profile live down
```

## Команды управления

### Просмотр запущенных контейнеров

```bash
docker compose ps
```

### Просмотр логов

```bash
docker compose logs -f
```

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
