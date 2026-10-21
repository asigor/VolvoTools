# Specyfikacja: CanIdProvider — generowanie CAN ID dla różnych protokołów

## 1. Cel

Usunąć identyfikatory CAN wpisane na stałe w `UDSProtocolCommonSteps`, `D2ProtocolCommonSteps` i przyszłych `PinCrackerSteps`. Wprowadzić wspólny mechanizm generowania fizycznych (point-to-point) i funkcjonalnych (broadcast) identyfikatorów CAN zależnie od protokołu, szerokości identyfikatora i konfiguracji ECU.

## 2. Problem

### 2.1. Obecne stałe

```
UDSProtocolCommonSteps.cpp:
  fallAsleep → send({0x7DF, {0x10, 0x02}}, ...)  // na stałe: 11 bitów
  keepAlive  → send({0x7DF, {0x3E, 0x80}}, ...)  // na stałe: 11 bitów
  wakeUp     → send({0x7DF, {0x11, 0x11}}, ...)  // na stałe: 11 bitów
  authorize  → przyjmuje uint32_t canId          // jedyne miejsce bez tego problemu
```

Dla P2_UDS (29 bitów) te stałe są nieprawidłowe: broadcast wymaga `0x18DB33F1`, a adresowanie fizyczne — `0x18DA50F1`.

### 2.2. Trzy formaty identyfikatorów CAN

| Protokół | Fizyczny (do ECU) | Funkcjonalny (broadcast) | Reguła |
|---|---|---|---|
| UDS, 11 bitów | `ecuConfig.canId` z `data.yaml` | `0x7DF` | Stała |
| UDS, 29 bitów | `0x18DA{ps}F1` | `0x18DB{group}F1` | `0x18DA00F1 | (PS << 8)` |
| D2 | `0xFFFFE` | `0xFFFFE` | Stała |
| KWP/TP20 | `ecuConfig.canId` | Nie używany | `data.yaml` |

- `PS` (PDU Specific) to adres ECU dla komunikacji fizycznej (np. `0x50` dla CEM) albo grupa dla broadcastu (np. `0x33` dla układu napędowego).
- `SA` (Source Address) to `0xF1`, czyli adres narzędzia/testera.
- Dla UDS 29-bit: `0x18DA{PS}SA`; dla D2 wszystkie komunikaty używają stałego ID `0xFFFFE`.

## 3. Architektura docelowa

### 3.1. Schemat klas

```
           CanIdProvider (abstrakcyjny)
                │
        ┌───────┼───────────┐
        ▼       ▼           ▼
  CanId11bit  CanId29bit  CanIdD2
  (ISO15765)  (ISO15765)  (CAN/D2)
```

```
BusConfiguration (data.yaml) → CanIdProviderFactory
                                      ↓
                               CanIdProvider
                                    │
                    ┌───────────────┴───────────────────┐
                    ▼                                   ▼
           UDSProtocolCommonSteps               PinCrackerSteps
           (fallAsleep, wakeUp, keepAlive)      (preAuth, postAuth, tryPin)
```

### 3.2. Interfejs

```cpp
// Common/common/CanIdProvider.hpp
#pragma once
#include <cstdint>

namespace common {
class CanIdProvider {
public:
    virtual ~CanIdProvider() = default;
    // CAN ID komunikatów point-to-point (do konkretnego ECU).
    virtual uint32_t getPhysCanId() const = 0;
    // CAN ID komunikatów broadcast/funkcjonalnych.
    virtual uint32_t getFuncCanId() const = 0;
    // Czy ID ma 11 czy 29 bitów.
    virtual bool isExtendedId() const = 0;
};
} // namespace common
```

### 3.3. Implementacje

#### CanId11bit — ISO15765, 11 bitów

```cpp
class CanId11bit final : public CanIdProvider {
public:
    explicit CanId11bit(uint32_t canId) : _physCanId{canId} {}
    uint32_t getPhysCanId() const override { return _physCanId; }
    uint32_t getFuncCanId() const override { return 0x7DF; }
    bool isExtendedId() const override { return false; }
private:
    uint32_t _physCanId; // CANIdentifier z YAML, np. 0x7E0 dla ECM, 0x7E1 dla TCM
};
```

Używany dla P3/SPA/Ford_UDS/VAG, gdy `protocol=ProtocolType::ISO15765`, `canIdBitSize=11`, a `CANIdentifier` jest ustawiony.

#### CanId29bit — ISO15765, 29 bitów

```cpp
class CanId29bit final : public CanIdProvider {
public:
    CanId29bit(uint32_t ecuId, uint32_t funcGroup, uint32_t sa = 0xF1)
        : _physCanId{buildPhysCanId(ecuId, sa)}
        , _funcCanId{buildFuncCanId(funcGroup, sa)} {}
    uint32_t getPhysCanId() const override { return _physCanId; }
    uint32_t getFuncCanId() const override { return _funcCanId; }
    bool isExtendedId() const override { return true; }
private:
    static uint32_t buildPhysCanId(uint32_t ps, uint32_t sa) {
        return 0x18DA00F1 | ((ps & 0xFF) << 8); // 0x18DA{PS}SA
    }
    static uint32_t buildFuncCanId(uint32_t group, uint32_t sa) {
        return 0x18DB00F1 | ((group & 0xFF) << 8); // 0x18DB{group}SA
    }
    uint32_t _physCanId;
    uint32_t _funcCanId;
};
```

Używany dla P2_UDS/P1_UDS, gdy `protocol=ProtocolType::ISO15765`, `canIdBitSize=29`, a `CANIdentifier` nie występuje. Parametry z `data.yaml`: `ecuId` pochodzi z `Address` (bajt PS, np. `0x50` dla CEM), `funcGroup` z `busConfiguration.funcGroup` albo domyślnie `0x33`. Pole `CANIdentifier` nie jest używane.

#### CanIdD2 — protokół D2 (CAN 29-bit)

```cpp
class CanIdD2 final : public CanIdProvider {
public:
    static constexpr uint32_t D2_CAN_ID = 0xFFFFE;
    uint32_t getPhysCanId() const override { return D2_CAN_ID; }
    uint32_t getFuncCanId() const override { return D2_CAN_ID; }
    bool isExtendedId() const override { return true; }
};
```

Używany dla P80/P1/P2, gdzie `protocol=ProtocolType::CAN` i `canIdBitSize=29`.

#### CanIdTP20 — KWP/TP20 (ISO14230)

TP20 jest protokołem szeregowym i nie korzysta z CAN ID. Implementacja jest atrapą; metod nie należy wywoływać:

```cpp
class CanIdTP20 final : public CanIdProvider {
public:
    uint32_t getPhysCanId() const override { return 0; }
    uint32_t getFuncCanId() const override { return 0; }
    bool isExtendedId() const override { return false; }
};
```

### 3.4. Fabryka

```cpp
namespace common {
std::unique_ptr<CanIdProvider> createCanIdProvider(
    ProtocolType protocol,
    uint32_t canIdBitSize,
    uint32_t ecuId,       // Address z YAML; dla 29 bitów to bajt PS
    uint32_t canId,       // CANIdentifier; dla 29-bit/D2 = 0
    uint32_t funcGroup = 0x33
);
}
```

Reguły wyboru:
- `ISO15765` + 11 bitów → `CanId11bit(canId)`, ID funkcjonalny `0x7DF`;
- `ISO15765` + 29 bitów → `CanId29bit(ecuId, funcGroup)`, ID fizyczny `0x18DA{ecuId}F1`, funkcjonalny `0x18DB{group}F1`;
- `CAN` + 29 bitów → `CanIdD2()`, oba ID równe `0xFFFFE`;
- `ISO14230` lub `ISO9141` → `CanIdTP20()`.

| Pole | Klucz YAML | UDS 11-bit | UDS 29-bit | D2 |
|---|---|---|---|---|
| `ecuId` | `Address` | Nie używany | Bajt PS, np. `0x50` dla CEM | Ignorowany |
| `canId` | `CANIdentifier` | ID CAN, np. `0x7E0` dla ECM | `0` | `0` |

Dla dostawcy dotyczącego wyłącznie magistrali, gdy potrzebny jest tylko `funcCanId`, można przekazać `ecuId=0, canId=0`; fizyczny ID nie będzie używany. Dla dostawcy konkretnego ECU: UDS 11-bit korzysta z `ecuInfo.canId`, UDS 29-bit z `ecuInfo.ecuId` (adres PS), a D2 zawsze zwraca `0xFFFFE`.

### 3.5. Konfiguracja `data.yaml`

Dla UDS 29-bit należy dodać do `BusConfiguration` pole `funcGroup`; domyślnie jest to `0x33`:

```yaml
busInfo:
  - name: "high_speed"
    protocol: ProtocolType::ISO15765
    baudrate: 500000
    canIdBitSize: 29
    funcGroup: 0x33
    ecuInfo:
      - ecuId: 0x7A
        canId: 0x50
        name: "CEM"
```

Jeśli `funcGroup` nie występuje, fabryka używa `0x33`, zachowując zgodność wsteczną.

## 4. Gdzie używany jest CanIdProvider

### 4.1. Komponenty, które go nie potrzebują

| Komponent | Powód |
|---|---|
| `UDSRequest::process(channel, canId, ...)` | Już przyjmuje `uint32_t canId`. |
| `D2Request::process(channel, ...)` | D2 używa stałego `0xFFFFE`, znanego z `D2Message::CanId`. |
| `UDSProtocolCommonSteps::authorize(channel, canId, pin)` | Już przyjmuje `canId`. |
| `UDSProtocolCommonSteps::transferData(channel, canId, ...)` | Już przyjmuje `canId`. |
| `J2534ChannelProvider` | Otwiera kanały; nie generuje identyfikatorów CAN. |

### 4.2. Komponenty, które go potrzebują

| Komponent | Zastosowanie | Obecny stan |
|---|---|---|
| `UDSPinCrackerSteps::preAuth()` | `fallAsleep` / wejście do sesji programowania z `funcCanId` | Stałe `0x7DF` w UDS common steps |
| `UDSPinCrackerSteps::postAuth()` | `wakeUp` z `funcCanId` | Stałe `0x7DF` |
| `UDSPinCrackerSteps::keepAlive()` | Keep-alive z `funcCanId` | Stałe `0x7DF` |
| `UDSPinCrackerSteps::tryPin()` | Autoryzacja z fizycznym ID ECU | Parametr istnieje; dostawca ujednolici jego źródło |
| `D2PinCrackerSteps` | Wszystkie komunikaty D2 używają `0xFFFFE` | ID jest w `D2Message::CanId`; dostawca ujednolici interfejs |
| `UDSFlasher` | `fallAsleep` / `wakeUp` z `funcCanId` | Stałe w UDS common steps |
| `UDSPinFinder` | `fallAsleep` / keep-alive / `wakeUp` | Stałe w UDS common steps |

### 4.3. Czy można obyć się bez dostawcy?

Tak — można przekazywać wszędzie osobne parametry `uint32_t funcCanId` i `uint32_t physCanId`, ale logika generowania identyfikatorów byłaby rozproszona po kodzie. Przykładowo:

```cpp
// Bez dostawcy: wywołujący musi znać format identyfikatora.
UDSProtocolCommonSteps::fallAsleep(channels, 0x18DB33F1); // P2_UDS
UDSProtocolCommonSteps::fallAsleep(channels, 0x7DF);      // P3

// Z dostawcą: format ID jest ukryty w jednym miejscu.
UDSProtocolCommonSteps::fallAsleep(channels, canIdProvider->getFuncCanId());
```

Alternatywą są funkcje pomocnicze `getFuncCanId(protocol, bitSize, funcGroup)` i `getPhysCanId(protocol, bitSize, ecuCanId)`. Interfejs `CanIdProvider` lepiej jednak wspiera przyszłe protokoły bez zmiany sygnatur wywołań.

## 5. Integracja z istniejącym kodem

### 5.1. UDSProtocolCommonSteps

Metody `authorize`, `transferData`, `transferChunk`, `eraseFlash`, `eraseChunk`, `startRoutine` i `checkValidApplication` już przyjmują `uint32_t canId` i nie wymagają zmian.

Zmienić należy wyłącznie metody z zakodowanym na stałe broadcast ID, np.:

```cpp
static bool fallAsleep(
    const std::vector<std::unique_ptr<ICanChannel>>& channels,
    uint32_t funcCanId);
static std::vector<unsigned long> keepAlive(ICanChannel& channel, uint32_t funcCanId);
static void wakeUp(
    const std::vector<std::unique_ptr<ICanChannel>>& channels,
    uint32_t funcCanId);
```

Wywołanie `startPeriodicMsg({0x7DF, {0x10, 0x02}}, ...)` powinno korzystać z `funcCanId`; analogicznie należy postąpić z `wakeUp` i `keepAlive`.

### 5.2. Osobny dostawca dla każdej magistrali

Na platformach z dwiema magistralami (P2_UDS, P1_UDS) każda może korzystać z innego protokołu. Potrzebny jest zatem dostawca dla każdej magistrali oraz osobny dostawca dla docelowego ECU.

Przykład P2_UDS, gdzie celem jest CEM:
- magistrala A (high-speed), ISO15765 29-bit: `CanId29bit(funcGroup=0x33)`, broadcast `0x18DB33F1`;
- magistrala B (low-speed), CAN 29-bit D2: `CanIdD2()`, broadcast `0xFFFFE`;
- ECU CEM na magistrali A: `CanId29bit(ps=0x50)`, fizyczny ID `0x18DA50F1`.

Zalecany schemat:
1. Otworzyć wszystkie kanały przez `getAllChannels(ecuId)`.
2. Dla każdego kanału pobrać konfigurację magistrali z `data.yaml` i utworzyć dostawcę funkcjonalnego ID (dla trybu tylko-magistrala przekazać `ecuId=0, canId=0`).
3. Dla ECU utworzyć osobnego dostawcę na podstawie `busInfo` i `ecuInfo`: UDS 11-bit korzysta z `CANIdentifier`, UDS 29-bit z `Address` (bajt PS), D2 ignoruje oba pola.
4. Wywoływać `fallAsleep`/`wakeUp` na każdej magistrali z jej własnym `getFuncCanId()`; `authorize` wywoływać na kanale ECU z `getPhysCanId()`.

### 5.3. PinCrackerSteps

`UDSPinCrackerSteps` otrzymuje `CanIdProvider` magistrali przez konstruktor. `preAuth`, `postAuth` i keep-alive używają `getFuncCanId()`; próba PIN-u używa fizycznego ID ECU, jeśli dostawca został utworzony dla ECU.

### 5.4. D2ProtocolCommonSteps

D2 korzysta ze stałego identyfikatora `0xFFFFE`. Dostawca ujednolica jego pozyskiwanie i pozwala wspólnemu kodowi nie rozróżniać szczegółów formatu ID.

## 6. Pliki objęte zmianami

### Nowe
- `Common/common/CanIdProvider.hpp` — interfejs i implementacje dostawców;
- opcjonalnie `Common/src/CanIdProvider.cpp` — implementacja fabryki, jeśli nie pozostanie w nagłówku.

### Modyfikowane
- `Common/common/BusConfiguration.hpp` (lub odpowiedni typ konfiguracji) — opcjonalne pole `funcGroup`;
- `Common/src/BusConfiguration.cpp` — odczyt `funcGroup` z YAML;
- `Common/common/protocols/UDSProtocolCommonSteps.hpp` i `.cpp` — przekazywanie funkcjonalnego ID;
- `Flasher/flasher/pin/PinCrackerSteps.hpp` i implementacje kroków;
- fabryka oraz kod tworzący `PinCrackerSteps`, by budować dostawcę dla właściwej magistrali i ECU.

### Bez zmian
- `UDSRequest`, `D2Request` i `J2534ChannelProvider` nie potrzebują dostawcy wyłącznie po to, by przenosić ID;
- istniejące wywołania, które już otrzymują jawny `canId`, mogą zachować sygnatury.

## 7. Kolejność wdrożenia

1. Dodać `CanIdProvider` i fabrykę, nie zmieniając jeszcze istniejącego kodu.
2. Zmienić metody UDS, które używają broadcast ID na stałe.
3. Wstrzyknąć dostawców do implementacji `PinCrackerSteps`.
4. Opcjonalnie dodać `funcGroup` do `data.yaml`; gdy pole nie występuje, używać `0x33`.

## 8. Kryteria akceptacji

1. Identyfikatory UDS 11-bit pochodzą z `CANIdentifier`, a broadcast to `0x7DF`.
2. Identyfikatory UDS 29-bit są budowane z adresu PS i grupy funkcjonalnej.
3. D2 używa `0xFFFFE` jako fizycznego i funkcjonalnego ID.
4. TP20 nie korzysta z CAN ID.
5. Na każdej magistrali może być użyty osobny dostawca.
6. Brak `funcGroup` w YAML nie łamie zgodności — stosowana jest wartość `0x33`.
7. Metody już przyjmujące `canId` nie otrzymują zbędnej warstwy abstrakcji.
