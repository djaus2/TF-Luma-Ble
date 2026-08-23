#ifndef BTSTACK_CONFIG_H
#define BTSTACK_CONFIG_H

// Project-local BTstack config for Raspberry Pi Pico W using Arduino-Pico.
// Keep BT Classic disabled and enable BLE only for the peripheral server use case.
#define ENABLE_BLE 1
#define ENABLE_CLASSIC 0

// Lightweight configuration for a small embedded BLE peripheral.
#define ENABLE_LOGGING 0
#define ENABLE_PRINTF 0
#define ENABLE_SDP 0
#define ENABLE_L2CAP 1
#define BTSTACK_SUPPORTS_LE_PERIPHERAL 1
#define BTSTACK_SUPPORTS_LE_CENTRAL 1

#define MAX_NR_GATT_CLIENTS 1
#define MAX_NR_GATT_SERVICES 4
#define MAX_NR_GATT_CHARACTERISTICS 8
#define MAX_NR_GATT_DESCRIPTORS 8
#define MAX_NR_HCI_CONNECTIONS 1
#define MAX_NR_L2CAP_CHANNELS 1
#define MAX_NR_LE_DEVICE_DB_ENTRIES 8

#endif
