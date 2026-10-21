# Specyfikacja: abstrakcja warstwy transportowej (J2534 → ICanChannel)

## 1. Cel

Usunąć bezpośrednią zależność `*ProtocolCommonSteps`, flasherów i loggerów od `j2534::J2534Channel`, zastępując ją abstrakcyjnym kanałem CAN. Dzięki temu będzie można podłączać alternatywne transporty (ESP32, STM32, ELM327) bez zmieniania logiki protokołów.

## 2. Problem

Obecny kod jest ściśle powiązany z J2534:

```
J2534 DLL (PassThru)
    ↓
j2534::J2534 / J2534Channel   ← cały kod jest z nim związany
    ↓
BaseMessage (j2534)           ← toPassThruMsgs() jest specyficzne dla J2534
    ↓
CanMessage ← D2Message        ← dziedziczą po BaseMessage
UDSMessage                    ← dziedziczy po BaseMessage
    ↓
UDSProtocolCommonSteps        ← J2534Channel& + uint32_t canId
D2ProtocolCommonSteps         ← J2534Channel& + uint8_t ecuId
KWPProtocolCommonSteps
UDSRequest / D2Request        ← process(J2534Channel&)
TP20Session / TP20RequestProcessor
    ↓
FlasherBase / UDSFlasher / KWPFlasher / D2Flasher
    ↓
VolvoFlasher / VolvoLogger
```

**Miejsca zależne od J2534:**
- 11 metod `UDSProtocolCommonSteps` przyjmujących `const J2534Channel&` i `uint32_t canId`;
- 8 metod `D2ProtocolCommonSteps` przyjmujących `const J2534Channel&` i `uint8_t ecuId`;
- `UDSRequest::process(const J2534Channel&)` — wysyłanie i odbieranie UDS;
- hierarchia `D2Request` / `RequestProcessorBase`;
- `TP20Session` / `TP20RequestProcessor`;
- `FlasherBase`, który posiada `J2534ChannelProvider` zwracający `unique_ptr<J2534Channel>`;
- dziedziczenie `BaseMessage ← CanMessage ← D2Message` oraz `BaseMessage ← UDSMessage`, używane wyłącznie ze względu na `toPassThruMsgs()`;
- struktura J2534 `PASSTHRU_MSG`, wykorzystywana we wszystkich krokach.

## 3. Docelowa architektura

```
J2534 DLL     ELM327 (AT)     ESP32 (WiFi/UART)    STM32 (USB)   ← transport
    ↓              ↓                 ↓                  ↓
J2534Channel  Elm327Adapter    Esp32CanAdapter    Stm32CanAdapter ← adaptery
    ↓              ↓                 ↓                  ↓
    └──────────────┴─────────────────┴──────────────────┘
                              ↓
                         ICanChannel                  ← wspólny interfejs
                              ↓
                    *ProtocolCommonSteps
                    UDSRequest / D2Request
                    TP20Session / RequestProcessorBase
                              ↓
                    FlasherBase / *Flasher
                    VolvoFlasher / VolvoLogger
```

Kluczowa zmiana: `*ProtocolCommonSteps` i `*Flasher` nie zależą już od `j2534::J2534Channel`. Szczegóły J2534 zostają zamknięte w `J2534ChannelAdapter`.

## 4. Nowe typy

### 4.1. `CanFrame` — wspólna reprezentacja ramki CAN

```cpp
// Common/common/CanFrame.hpp
#pragma once

#include <cstdint>
#include <vector>

struct CanFrame {
    uint32_t id = 0;               // identyfikator arbitrażu CAN (11 lub 29 bitów)
    std::vector<uint8_t> data;     // dane (payload)
    bool isExtendedId = false;     // true = 29 bitów, false = 11 bitów
};
```

Zastępuje `PASSTHRU_MSG` jako jednostkę wymiany danych. Publiczne nagłówki komponentów `Common`, `Flasher` i `Logger` nie powinny zawierać struktur J2534.

### 4.2. `ICanChannel` — wspólny interfejs kanału

```cpp
// Common/common/ICanChannel.hpp
#pragma once

#include "CanFrame.hpp"
#include <vector>

class ICanChannel {
public:
    virtual ~ICanChannel() = default;

    // Wysyłanie (timeout w milisekundach; parametr dodany dla pakietów D2)
    virtual bool send(const CanFrame& frame, unsigned long timeout = 1000) = 0;
    virtual bool send(const std::vector<CanFrame>& frames, unsigned long timeout = 1000) = 0;

    // Odbieranie (timeout w milisekundach)
    virtual bool receive(CanFrame& frame, unsigned long timeout) = 0;
    virtual bool receive(std::vector<CanFrame>& frames, unsigned long timeout) = 0;

    // Czyszczenie buforów
    virtual void clearRx() = 0;
    virtual void clearTx() = 0;

    // Wiadomości okresowe: keep-alive (0x3E 0x80) i sekwencje wybudzania.
    // Jeśli sprzęt nie obsługuje timera, adapter emuluje okresowość w osobnym wątku.
    virtual bool startPeriodicMsg(const CanFrame& frame,
                                  unsigned long intervalMs,
                                  unsigned long& msgId) = 0;
    virtual bool stopPeriodicMsg(unsigned long msgId) = 0;

    // Filtry CAN — sprzętowe lub programowe filtrowanie po CAN ID.
    virtual bool startMsgFilter(unsigned long filterType,
                                const CanFrame& mask,
                                const CanFrame& pattern,
                                unsigned long& filterId) = 0;
    virtual bool stopMsgFilter(unsigned long filterId) = 0;

    // Sterowanie kanałem
    virtual bool setConfig(unsigned long parameter, unsigned long value) = 0;
    virtual bool ioctl(unsigned long ioctlId, const void* input, void* output) = 0;
};
```

Adapter zwraca `false` dla nieobsługiwanej operacji. Kod wywołujący potrafi już obsługiwać taki przypadek (obecnie `J2534Channel` zwraca `STATUS_NOT_SUPPORTED`).

### 4.3. `J2534ChannelAdapter` — adapter istniejącego urządzenia PassThru

```cpp
// Common/common/J2534ChannelAdapter.hpp
class J2534ChannelAdapter final : public ICanChannel {
public:
    explicit J2534ChannelAdapter(std::unique_ptr<j2534::J2534Channel> channel);
    ~J2534ChannelAdapter() override;

    bool send(const CanFrame& frame) override;
    bool send(const std::vector<CanFrame>& frames) override;
    bool receive(CanFrame& frame, unsigned long timeout) override;
    bool receive(std::vector<CanFrame>& frames, unsigned long timeout) override;
    void clearRx() override;
    void clearTx() override;
    bool startPeriodicMsg(const CanFrame&, unsigned long, unsigned long&) override;
    bool stopPeriodicMsg(unsigned long) override;
    bool startMsgFilter(unsigned long, const CanFrame&, const CanFrame&, unsigned long&) override;
    bool stopMsgFilter(unsigned long) override;
    bool setConfig(unsigned long parameter, unsigned long value) override;
    bool ioctl(unsigned long, const void*, void*) override;
};
```

Wszystkie metody interfejsu `ICanChannel` są mapowane na wywołania `J2534Channel`.

**Konwersja `CanFrame → PASSTHRU_MSG`:**

```
PASSTHRU_MSG.Data[0..3] = CAN ID (big-endian, zależnie od ProtocolID)
PASSTHRU_MSG.Data[4..N] = CanFrame.data
PASSTHRU_MSG.DataSize   = CanFrame.data.size() + 4
PASSTHRU_MSG.ProtocolID = _protocolId (zapisany podczas tworzenia adaptera)
PASSTHRU_MSG.Flags      = _txFlags (zapisane podczas tworzenia adaptera)
```

**Konwersja odwrotna `PASSTHRU_MSG → CanFrame`:**

```
CanFrame.id = PASSTHRU_MSG.Data[0..3]
CanFrame.data = PASSTHRU_MSG.Data[4..DataSize]
CanFrame.isExtendedId = (PASSTHRU_MSG.Flags & CAN_29BIT_ID) != 0
```

`PASSTHRU_MSG` pozostaje wyłącznie w `J2534ChannelAdapter.cpp` oraz zewnętrznej zależności `j2534/`.

### 4.4. Zmiana `J2534ChannelProvider`

Obecnie `J2534ChannelProvider` tworzy i zwraca `unique_ptr<J2534Channel>`. Po zmianie:

```cpp
// Common/common/J2534ChannelProvider.hpp
class J2534ChannelProvider {
public:
    std::vector<std::unique_ptr<ICanChannel>> getAllChannels(uint32_t ecuId) const;
    std::unique_ptr<ICanChannel> getChannelForEcu(uint32_t ecuId) const;
    // ...
};
```

Każdy utworzony `J2534Channel` jest opakowywany przez `J2534ChannelAdapter`. Dla protokołów D2 nie tworzy się kanałów ISO9141 (K-Line) — sprawdzenie `getProtocolId() != ISO9141` z `D2ProtocolCommonSteps` znika razem z bezpośrednią obsługą kanału. Logika `UDSProtocolCommonSteps::openChannels` (otwarcie kanału UDS i kanału low-speed) zostaje przeniesiona do `J2534ChannelProvider`.

## 5. Zmiany w poszczególnych warstwach

### 5.1. `*ProtocolCommonSteps`

| Sygnatura | Przed zmianą | Po zmianie |
|---|---|---|
| `UDSProtocolCommonSteps::openChannels` | `(J2534&, baudrate, canId)` → `vector<unique_ptr<J2534Channel>>` | Usunięta; otwieranie kanałów przechodzi do `J2534ChannelProvider` / `ChannelProviderFactory`. |
| Pozostałe metody | `(const J2534Channel& channel, uint32_t canId, ...)` | `(ICanChannel& channel, ...)`; `canId` jest używany wewnątrz metody jako `CanFrame.id`. |
| `D2ProtocolCommonSteps::fallAsleep` | `startPeriodicMsgs(D2Message(...), 5)` | `startPeriodicMsg({0x7DF, {0x10, 0x02}}, 5, id)` |
| `D2ProtocolCommonSteps::wakeUp` | `writeMsgs(D2Message::wakeUpCanRequest, ...)` | `send(wakeUpCanFrame)`; sprawdzanie `ISO9141` zostaje usunięte. |

Wywołania `channels[i]->getProtocolId()`, `channel.getProtocolId()` i `channel.getTxFlags()` zostają usunięte. Adapter przechowuje ProtocolID i TxFlags, a konwersja `CanFrame → PASSTHRU_MSG` korzysta z nich wewnętrznie.

### 5.2. `UDSRequest` / `D2Request`

Sygnatury zmieniają się z `process(const j2534::J2534Channel&)` na `process(ICanChannel&)`, zachowując parametry limitu czasu i ponowień:

```cpp
class UDSRequest {
    std::vector<uint8_t> process(ICanChannel& channel, size_t timeout = 1000);
    std::vector<uint8_t> process(ICanChannel& channel,
                                  const std::vector<uint8_t>& checkData,
                                  size_t retryCount = 1, size_t timeout = 1000);
};
```

Wewnętrzne `channel.writeMsgs(_message, numMsgs)` zastępuje się przez `channel.send({canId, payload})`. Zamiast przechowywać `UDSMessage`, obiekt przechowuje bezpośrednio `uint32_t _canId` i `std::vector<uint8_t> _data`.

### 5.3. `KWPProtocolCommonSteps` / `TP20Session` / `RequestProcessorBase`

`KWPProtocolCommonSteps` przyjmuje `RequestProcessorBase&`; ten abstrakcyjny interfejs pozostaje bez zmian. Zmieniają się implementacje:
- `UDSRequestProcessor` przechodzi z `J2534Channel&` na `ICanChannel&`;
- `TP20RequestProcessor` przechodzi z `J2534Channel&` (przez `TP20Session`) na `ICanChannel&`.

Analogicznie `TP20Session` i `TP20RequestProcessor` zastępują `J2534Channel&` przez `ICanChannel&`.

### 5.4. Flasher-y (hFSM2)

`UDSFlasherImpl` i `KWPFlasherImpl` przechowują `ICanChannel&) zamiast `J2534Channel&`. Metody `startImpl()` przyjmują `vector<unique_ptr<ICanChannel>>&`.

### 5.5. `FlasherBase`

```cpp
// Przed:
common::J2534ChannelProvider _j2534ChannelProvider;

// Po:
common::J2534ChannelProvider _channelProvider; // typ dostawcy pozostaje, zmienia się typ zwracany
```

Metoda `start()` wywołuje `_channelProvider.getAllChannels(ecuId)`, które zwraca `vector<unique_ptr<ICanChannel>>`.

## 6. Pełna lista plików do zmiany

### Nowe pliki

| Nr | Plik | Opis |
|---|---|---|
| 1 | `Common/common/CanFrame.hpp` | Struktura ramki CAN |
| 2 | `Common/common/ICanChannel.hpp` | Abstrakcyjny interfejs kanału |
| 3 | `Common/common/J2534ChannelAdapter.hpp` | Adapter J2534 → ICanChannel |
| 4 | `Common/src/J2534ChannelAdapter.cpp` | Implementacja adaptera |

### Modyfikowane pliki

| Nr | Plik | Zmiana |
|---|---|---|
| 1 | `Common/common/J2534ChannelProvider.hpp` | Zwraca `unique_ptr<ICanChannel>`. |
| 2 | `Common/src/J2534ChannelProvider.cpp` | Opakowuje kanały w adapter i nie tworzy ISO9141. |
| 3 | `Common/common/protocols/UDSProtocolCommonSteps.hpp` | `J2534Channel&` → `ICanChannel&`. |
| 4 | `Common/src/protocols/UDSProtocolCommonSteps.cpp` | `PASSTHRU_MSG` → `CanFrame`; wysyłanie bezpośrednio zamiast `UDSMessage`. |
| 5 | `Common/common/protocols/D2ProtocolCommonSteps.hpp` | `J2534Channel&` → `ICanChannel&`. |
| 6 | `Common/src/protocols/D2ProtocolCommonSteps.cpp` | `PASSTHRU_MSG` → `CanFrame); wysyłanie bezpośrednio zamiast `D2Message`. |
| 7 | `Common/common/protocols/UDSRequest.hpp` | `process(const J2534Channel&)` → `process(ICanChannel&)`. |
| 8 | `Common/src/protocols/UDSRequest.cpp` | `channel.writeMsgs(_message)` → `channel.send(CanFrame{canId, _data})`. |
| 9 | `Flasher/flasher/D2FlasherBase.hpp` | Zmiana sygnatury `startImpl`. |
| 10 | `Flasher/src/D2FlasherBase.cpp` | Użycie `ICanChannel&`. |
| 11 | `Flasher/src/UDSFlasher.cpp` | Typ kanału w kontekście hFSM2 i `startImpl`. |
| 12 | `Flasher/src/KWPFlasher.cpp` | Typ kanału w kontekście hFSM2 i `startImpl`. |
| 13 | `Flasher/flasher/FlasherBase.hpp` | Typ zwracany przez `J2534ChannelProvider`. |
| 14 | `Flasher/src/FlasherBase.cpp` | Dostosowanie do nowego typu. |

### Pliki bez zmian

- `VolvoFlasher/src/VolvoFlasher.cpp` — `main()` korzysta z `FlasherBase` przez istniejące metody abstrakcyjne.
- `VolvoLogger/src/*` — analogicznie.
- `j2534/` — zewnętrzna zależność.
- `BaseMessage`, `CanMessage`, `UDSMessage`, `D2Message` — pozostają jako kod zgodności; korzysta z nich tylko `J2534ChannelAdapter`.
- `CanMessagesTransceiver` można przenieść później.
- `keys.cpp`, `data.yaml`, `docs/`.

## 7. Kolejność wdrożenia

1. **Podstawowe typy:** dodać `CanFrame.hpp` i `ICanChannel.hpp`, bez zmiany istniejącego kodu.
2. **Adapter:** dodać `J2534ChannelAdapter.hpp/.cpp` oraz konwersję `CanFrame ↔ PASSTHRU_MSG`.
3. **`J2534ChannelProvider`:** zmienić typy zwracane i opakowywać kanały w adapter.
4. **Kroki protokołów i żądania:** zaktualizować `UDSProtocolCommonSteps`, `D2ProtocolCommonSteps`, `UDSRequest`, ewentualnie `D2Request`, `TP20Session` oraz procesory żądań.
5. **Flashery:** dostosować `FlasherBase`, `D2FlasherBase`, `UDSFlasher` i `KWPFlasher`.
6. **Budowanie i weryfikacja:** `cmake --build build --config Release`.

## 8. Przykłady konwersji

### `UDSRequest::process()`

Przed zmianą `UDSRequest` przechowywał `UDSMessage` (dziedziczący po `BaseMessage`) i wysyłał go przez `channel.writeMsgs(_message)`. Po zmianie przechowuje `uint32_t _canId` i `std::vector<uint8_t> _data`, a następnie wysyła `CanFrame`:

```cpp
std::vector<uint8_t> UDSRequest::process(ICanChannel& channel, size_t timeout)
{
    CanFrame request{_canId, _data};
    if (!channel.send(request))
        throw std::runtime_error("Failed to send");

    CanFrame response;
    if (!channel.receive(response, timeout))
        throw std::runtime_error("Failed to receive");

    return parseResponse(response.data);
}
```

### `UDSProtocolCommonSteps::eraseChunk()`

Po zmianie metoda przyjmuje `ICanChannel&`, wysyła żądanie przez `channel.send({canId, {...}})`, a odpowiedź odczytuje przez przeniesioną na `ICanChannel` funkcję `readMessageCheckAndGet`.

## 9. Status implementacji

Wszystkie sześć etapów zostało zrealizowanych:

| Nr | Etap | Status |
|---|---|---|
| 1 | `Common/common/CanFrame.hpp` — struktura | ✓ |
| 2 | `Common/common/ICanChannel.hpp` — interfejs, w tym parametr `timeout` w `send` | ✓ |
| 3 | `Common/common/J2534ChannelAdapter.hpp` — deklaracja | ✓ |
| 4 | `Common/src/J2534ChannelAdapter.cpp` — konwersja `CanFrame ↔ PASSTHRU_MSG` | ✓ |
| 5 | `J2534ChannelProvider` zwraca `unique_ptr<ICanChannel>` | ✓ |
| 6–15 | Kroki protokołów, żądania i flashery używają `ICanChannel&` | ✓ |

Dodatkowo `send()` otrzymało parametr `timeout` (domyślnie 1000 ms), aby obsługiwać pakietową transmisję w protokole D2 (zob. `restore_d2_transferdata_batching.md`).

## 10. Kryteria akceptacji

1. ✓ Nagłówki `*ProtocolCommonSteps` nie zawierają `#include <j2534/...>`.
2. ✓ Nagłówki `UDSRequest`, `D2Request` i `TP20Session` nie zawierają `#include <j2534/...>`.
3. ✓ `PASSTHRU_MSG` występuje wyłącznie w `J2534ChannelAdapter.cpp` i zewnętrznej zależności `j2534/`.
4. ✓ `BaseMessage`, `CanMessage`, `UDSMessage` i `D2Message` nie są importowane w `Common/common/protocols/`, `Flasher/` ani `Logger/`. Wyjątkiem jest `J2534ChannelAdapter`, jedyna klasa potrzebująca `BaseMessage` i `PASSTHRU_MSG`.
5. ✓ Projekt buduje się bez błędów przez `cmake --build build --config Release`.
6. ✓ Funkcje programowania, odczytu, wyszukiwania PIN-u i logowania zostały zachowane (porównanie stanów i callbacków podczas testów z adapterem J2534).
