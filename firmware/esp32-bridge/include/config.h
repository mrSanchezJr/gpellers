#pragma once

#include <Arduino.h>

// ==========================================
// НАСТРОЙКИ СЕТИ WI-FI
// ==========================================
// Данные вашей домашней / рабочей Wi-Fi сети:
#define WIFI_SSID           "kyvartirad34"
#define WIFI_PASSWORD       "pa33-3e7"

// Резервная точка доступа (AP), если не удалось подключиться к роутеру:
#define AP_SSID             "Inectra-CNC-Bridge"
#define AP_PASSWORD         "12345678"
#define WIFI_CONNECT_TIMEOUT 15000  // Тайм-аут попытки подключения к роутеру (мс)

// Настройка статического IP (рекомендуется для ЧПУ, чтобы IP не менялся):
// Раскомментируйте USE_STATIC_IP, если хотите зафиксировать IP модуля в подсети.
//#define USE_STATIC_IP
#ifdef USE_STATIC_IP
    #define STATIC_IP       192, 168, 1, 150
    #define STATIC_GATEWAY  192, 168, 1, 1
    #define STATIC_SUBNET   255, 255, 255, 0
    #define STATIC_DNS      192, 168, 1, 1
#endif

// ==========================================
// НАСТРОЙКИ TCP СЕРВЕРА
// ==========================================
// TCP порт, к которому подключается управляющая программа C# (G-write)
#define TCP_PORT            8088

// ==========================================
// НАСТРОЙКИ UART (ПОДКЛЮЧЕНИЕ К INECTRA MSC-3US)
// ==========================================
// Скорость порта MSC-3US (по умолчанию для Inectra MSC-3US - 115200)
#define UART_BAUD_RATE      115200

// Выбор аппаратного UART контроллера ESP32:
// 1 = Использовать Hardware Serial2 (ESP32 UART2). РЕКОМЕНДУЕТСЯ!
//     В этом случае пины 16 и 17 подключаются к Inectra,
//     а стандартный USB порт (Serial0 на COM10) остается свободным
//     для отладки, мониторинга и легкой перепрошивки кабелем!
// 0 = Использовать Hardware Serial (ESP32 UART0, GPIO3=RX, GPIO1=TX).
#define USE_HARDWARE_UART2  1

// Пины для UART2 (при USE_HARDWARE_UART2 = 1):
// ESP32 RX (пин 16) -> подключать к TX контроллера Inectra MSC-3US
// ESP32 TX (пин 17) -> подключать к RX контроллера Inectra MSC-3US
#define INECTRA_RX_PIN      16
#define INECTRA_TX_PIN      17

// Размер кольцевого буфера RX UART (в байтах)
#define UART_RX_BUFFER_SIZE 2048

// Размер буфера перекачки пакетов UART <-> TCP
#define BRIDGE_BUFFER_SIZE  2048

// ==========================================
// ОТЛАДКА И ДИАГНОСТИКА
// ==========================================
// Выводить ли отладочные сообщения в USB Serial (COM10).
// Работает безопасно, когда USE_HARDWARE_UART2 = 1.
#define DEBUG_TO_SERIAL     true

// Выводить ли в USB Serial отметки о проходящих пакетах
#define DEBUG_DATA_TRAFFIC  false

// Поддержка прошивки "по воздуху" (ArduinoOTA)
#define ENABLE_OTA          true
#define OTA_HOSTNAME        "inectra-esp32"
#define OTA_PASSWORD        "admin"
