#include <Arduino.h>
#include <WiFi.h>
#include <WiFiClient.h>

#include "config.h"

#if ENABLE_OTA
#include <ArduinoOTA.h>
#endif

// Настройка аппаратного UART
#if USE_HARDWARE_UART2
HardwareSerial CncSerial(2);
#define CNC_UART CncSerial
#else
#define CNC_UART Serial
#endif

// Глобальные объекты
WiFiServer server(TCP_PORT);
WiFiClient client;
uint8_t bridgeBuffer[BRIDGE_BUFFER_SIZE];
bool wasClientConnected = false;

void logTime() {
#if (DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
    unsigned long sec = millis() / 1000;
    unsigned long min = sec / 60;
    sec %= 60;
    Serial.printf("[%02lu:%02lu] ", min, sec);
#endif
}

void setupOTA() {
#if ENABLE_OTA
    ArduinoOTA.setHostname(OTA_HOSTNAME);
    ArduinoOTA.setPassword(OTA_PASSWORD);
    
    ArduinoOTA.onStart([]() {
#if (DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
        logTime();
        Serial.println(F("[OTA] Update started..."));
#endif
        if (client && client.connected()) {
            client.stop();
        }
    });

    ArduinoOTA.onEnd([]() {
#if (DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
        logTime();
        Serial.println(F("\n[OTA] Update finished successfully! Rebooting..."));
#endif
    });

    ArduinoOTA.onError([](ota_error_t error) {
#if (DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
        logTime();
        Serial.printf("[OTA] Error[%u]: ", error);
        if (error == OTA_AUTH_ERROR) Serial.println(F("Auth Failed"));
        else if (error == OTA_BEGIN_ERROR) Serial.println(F("Begin Failed"));
        else if (error == OTA_CONNECT_ERROR) Serial.println(F("Connect Failed"));
        else if (error == OTA_RECEIVE_ERROR) Serial.println(F("Receive Failed"));
        else if (error == OTA_END_ERROR) Serial.println(F("End Failed"));
#endif
    });
    
    ArduinoOTA.begin();
#if (DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
    logTime();
    Serial.printf("[OTA] Ready. Hostname: %s, Port: 3232\n", OTA_HOSTNAME);
#endif
#endif
}

void connectWiFi() {
    WiFi.persistent(false);
    WiFi.mode(WIFI_STA);

#ifdef USE_STATIC_IP
    IPAddress ip(STATIC_IP);
    IPAddress gateway(STATIC_GATEWAY);
    IPAddress subnet(STATIC_SUBNET);
    IPAddress dns(STATIC_DNS);
    WiFi.config(ip, gateway, subnet, dns);
#endif

    WiFi.begin(WIFI_SSID, WIFI_PASSWORD);

#if (DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
    logTime();
    Serial.printf("[WiFi] Connecting to SSID: '%s'", WIFI_SSID);
#endif

    unsigned long startAttemptTime = millis();
    unsigned long lastDot = 0;

    // Ждем подключения до истечения тайм-аута
    while (WiFi.status() != WL_CONNECTED && (millis() - startAttemptTime < WIFI_CONNECT_TIMEOUT)) {
        delay(100);
#if (DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
        if (millis() - lastDot > 500) {
            lastDot = millis();
            Serial.print(F("."));
        }
#endif
    }

#if (DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
    Serial.println();
#endif

    if (WiFi.status() == WL_CONNECTED) {
#if (DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
        logTime();
        Serial.println(F("[WiFi] CONNECTED SUCCESSFULLY!"));
        logTime();
        Serial.printf("[WiFi] IP Address : %s\n", WiFi.localIP().toString().c_str());
        logTime();
        Serial.printf("[WiFi] Subnet Mask: %s\n", WiFi.subnetMask().toString().c_str());
        logTime();
        Serial.printf("[WiFi] Gateway    : %s\n", WiFi.gatewayIP().toString().c_str());
        logTime();
        Serial.printf("[WiFi] Signal RSSI: %d dBm\n", WiFi.RSSI());
        logTime();
        Serial.printf("[WiFi] MAC Address: %s\n", WiFi.macAddress().c_str());
#endif
    } else {
        // Резервная точка доступа
#if (DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
        logTime();
        Serial.println(F("[WiFi] Could not connect to router! Starting Fallback AP..."));
#endif
        WiFi.mode(WIFI_AP);
        WiFi.softAP(AP_SSID, AP_PASSWORD);

#if (DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
        logTime();
        Serial.println(F("[WiFi] Access Point started!"));
        logTime();
        Serial.printf("[WiFi] AP SSID    : %s\n", AP_SSID);
        logTime();
        Serial.printf("[WiFi] AP Password: %s\n", AP_PASSWORD);
        logTime();
        Serial.printf("[WiFi] AP IP      : %s\n", WiFi.softAPIP().toString().c_str());
#endif
    }
}

void setup() {
#if (DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
    Serial.begin(115200);
    delay(200);
    Serial.println();
    Serial.println(F("=================================================="));
    Serial.println(F("   Inectra MSC-3US WiFi Serial Bridge (ESP-32S)   "));
    Serial.println(F("=================================================="));
    logTime();
    Serial.printf("[SYSTEM] Chip Model   : %s Rev %d\n", ESP.getChipModel(), ESP.getChipRevision());
    logTime();
    Serial.printf("[SYSTEM] CPU Frequency: %d MHz\n", ESP.getCpuFreqMHz());
    logTime();
    Serial.printf("[SYSTEM] Flash Size   : %d KB\n", ESP.getFlashChipSize() / 1024);
    logTime();
    Serial.printf("[SYSTEM] Free Heap    : %d bytes\n", ESP.getFreeHeap());
    logTime();
    Serial.printf("[UART2] RX Pin: %d, TX Pin: %d, Baud: %d\n", INECTRA_RX_PIN, INECTRA_TX_PIN, UART_BAUD_RATE);
#endif

#if USE_HARDWARE_UART2
    CncSerial.setRxBufferSize(UART_RX_BUFFER_SIZE);
    CncSerial.begin(UART_BAUD_RATE, SERIAL_8N1, INECTRA_RX_PIN, INECTRA_TX_PIN);
#else
    CNC_UART.setRxBufferSize(UART_RX_BUFFER_SIZE);
    CNC_UART.begin(UART_BAUD_RATE);
#endif

    connectWiFi();
    setupOTA();

    // Запуск TCP сервера
    server.begin();
    server.setNoDelay(true);

#if (DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
    logTime();
    Serial.printf("[TCP] Server listening on port: %d\n", TCP_PORT);
    Serial.println(F("--------------------------------------------------"));
    logTime();
    Serial.println(F("[READY] Bridge is active. Waiting for TCP client..."));
    Serial.println(F("--------------------------------------------------"));
#endif
}

void loop() {
#if ENABLE_OTA
    ArduinoOTA.handle();
#endif

    // Проверка новых входящих TCP подключений
    if (server.hasClient()) {
        WiFiClient newClient = server.accept();
        newClient.setNoDelay(true);

        if (!client || !client.connected()) {
            if (client) client.stop();
            client = newClient;
            wasClientConnected = true;
#if (DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
            logTime();
            Serial.printf("[TCP] Client connected from %s:%d\n", 
                          client.remoteIP().toString().c_str(), 
                          client.remotePort());
#endif
        } else {
            // Если уже было соединение, замещаем зависшее новым
            client.stop();
            client = newClient;
#if (DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
            logTime();
            Serial.printf("[TCP] Reconnected client from %s:%d\n", 
                          client.remoteIP().toString().c_str(), 
                          client.remotePort());
#endif
        }
    }

    // Проверяем отключение клиента
    if (wasClientConnected && (!client || !client.connected())) {
        wasClientConnected = false;
#if (DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
        logTime();
        Serial.println(F("[TCP] Client disconnected."));
#endif
    }

    // Если клиент активен - пересылаем данные
    if (client && client.connected()) {
        // 1. TCP (программа C# G-write на ПК) -> UART (контроллер Inectra)
        size_t availableTCP = client.available();
        if (availableTCP > 0) {
            size_t bytesToRead = (availableTCP < sizeof(bridgeBuffer)) ? availableTCP : sizeof(bridgeBuffer);
            size_t bytesRead = client.read(bridgeBuffer, bytesToRead);
            if (bytesRead > 0) {
                CNC_UART.write(bridgeBuffer, bytesRead);
#if (DEBUG_DATA_TRAFFIC && DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
                logTime();
                Serial.printf("[TCP -> CNC] (%u bytes)\n", bytesRead);
#endif
            }
        }

        // 2. UART (контроллер Inectra) -> TCP (программа C# G-write на ПК)
        size_t availableSerial = CNC_UART.available();
        if (availableSerial > 0) {
            size_t bytesToRead = (availableSerial < sizeof(bridgeBuffer)) ? availableSerial : sizeof(bridgeBuffer);
            size_t bytesRead = CNC_UART.readBytes(bridgeBuffer, bytesToRead);
            if (bytesRead > 0) {
                client.write(bridgeBuffer, bytesRead);
                client.flush(); // Отправляем немедленно для минимальной задержки статуса ЧПУ
#if (DEBUG_DATA_TRAFFIC && DEBUG_TO_SERIAL && USE_HARDWARE_UART2)
                logTime();
                Serial.printf("[CNC -> TCP] (%u bytes)\n", bytesRead);
#endif
            }
        }
    } else {
        // Очищаем буфер UART, пока никто не подключен по TCP
        while (CNC_UART.available()) {
            CNC_UART.read();
        }
    }
}
