#!/bin/bash

# Загрузка и установка ngrok
wget https://bin.equinox.io/c/bNyj1mQVY4c/ngrok-v3-stable-linux-amd64.tgz
tar xvzf ngrok-v3-stable-linux-amd64.tgz
chmod +x ngrok

# Настройка ngrok с помощью токена
if [ -n "$NGROK_AUTH_TOKEN" ]; then
    ./ngrok config add-authtoken $NGROK_AUTH_TOKEN
else
    echo "NGROK_AUTH_TOKEN не установлен"
    exit 1
fi

# Запуск ngrok в фоновом режиме
./ngrok http 8080 --log=stdout > ngrok.log &

# Ожидание запуска ngrok
sleep 5

# Получение URL
NGROK_URL=$(curl -s http://localhost:4040/api/tunnels | grep -o '"public_url":"[^"]*' | grep -o 'https://[^"]*')
echo "NGROK_URL=$NGROK_URL" >> .env

echo "ngrok запущен и настроен на URL: $NGROK_URL" 