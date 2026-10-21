# Implementacja wyszukiwania kodu PIN

## 1. Cel

Ujednolicić wyszukiwanie PIN-ów dla różnych protokołów (UDS, KWP, D2) i platform, usuwając duplikację oraz sekwencje wpisane na stałe w `VolvoFlasher.cpp`. Zastąpić HFSM2 w `UDSPinFinder` strategią opartą na interfejsie kroków i przenieść wspólny algorytm iteracji do klasy bazowej.

## 2. Obecna architektura

### 2.1. Problemy

- `PinCracker` jest pustą atrapą w `Flasher/flasher/pin/PinCracker.hpp` (ma też literówkę w destruktorze: `~Pinracker()`).
- `PINCrackerBase` został usunięty jako wcześniejsza pusta atrapa i zastąpiony przez `PinCracker`.
- `UDSPinFinder` w `Common/common/protocols/` korzysta z HFSM2: `FallAsleep → KeepAlive → Authorize(loop) → WakeUp → Done/Error`. Automat jest nadmiarowy, bo przejścia są liniowe, a `Authorize::update()` próbuje autoryzacji przy każdym `fsm.update()`.
- `findPin()` w `VolvoFlasher.cpp` korzysta bezpośrednio z surowych struktur `PASSTHRU_MSG` i dubluje logikę UDS.
- `switchToDiagSession()` to osobna funkcja z surowym `startPeriodicMsg`.
- Algorytm jest powielony w `findPin()`, `findPin2()` i częściowo `switchToDiagSession()`.

### 2.2. Obecne zależności

```
VolvoFlasher.cpp
  ├── findPin()            → authByKey()         → bezpośrednio PASSTHRU_MSG
  ├── findPin2()           → UDSPinFinder(HFSM2) → UDSProtocolCommonSteps
  └── switchToDiagSession  → authByKey()         → bezpośrednio PASSTHRU_MSG
```

### 2.3. Różnice między platformami

| Parametr | UDS (P3/SPA/Ford/VAG) | KWP (VAG K-Line) | D2 (P2/P80) |
|---|---|---|---|
| Kanał | ISO15765 (CAN) | TP20 (szeregowy) | Surowy CAN 29-bit |
| Usypianie | Okresowe `0x10 0x02` | Niepotrzebne | Wzorzec wybudzania |
| Keep-alive | `0x3E 0x80` | W sesji | Okresowe `0x86` |
| Sesja | Niepotrzebna do PIN-u | `0x10 0x85` i ponowne połączenie | Załadowanie bootloadera |
| Autoryzacja | `0x27` + `generateKey()` | `0x27` + `generateKeyCommon()` | Przez bootloader |
| Wybudzanie | `0x11 0x11/0x81` | Niepotrzebne | D2 wake |
| Zakres PIN-u | `0x000000–0xFFFFFF` | Zależny od systemu | Zależny od systemu |
| Opóźnienie po błędzie | Około 5 s | Zależne od systemu | Zależne od systemu |

## 3. Architektura docelowa

### 3.1. Zasada

Zastąpić HFSM2 wzorcem **Strategy**. Algorytm wyszukiwania PIN-u jest liniowy i nie potrzebuje automatu stanów:

```
             PinCracker (algorytm)
                    │
             ┌──────┴──────┐
             ▼             ▼
       PinCrackerSteps   callbacki stanu
          (interfejs)
             │
        ┌────┼───────┐
        ▼    ▼       ▼
     UDSSteps KWPsteps D2Steps
```

### 3.2. Interfejs kroków zależnych od platformy

```cpp
class PinCrackerSteps {
public:
    virtual ~PinCrackerSteps() = default;

    virtual std::vector<std::unique_ptr<ICanChannel>>
    openChannels(J2534ChannelProvider& provider, uint32_t ecuId) = 0;

    // Wstępna konfiguracja, np. fallAsleep lub sesja programowania.
    virtual bool preAuth(std::vector<std::unique_ptr<ICanChannel>>& channels) { return true; }

    // Uruchomienie i zatrzymanie okresowych komunikatów keep-alive.
    virtual std::vector<unsigned long>
    startKeepAlive(std::vector<std::unique_ptr<ICanChannel>>& channels) { return {}; }
    virtual void stopKeepAlive(std::vector<unsigned long>& ids) { (void)ids; }

    // Próba jednego PIN-u: seed + key; true oznacza sukces.
    virtual bool tryPin(ICanChannel& channel, uint64_t pin) = 0;

    // Sprzątanie po zakończeniu iteracji.
    virtual void postAuth(std::vector<std::unique_ptr<ICanChannel>>& channels) { (void)channels; }

    virtual uint64_t getMinPin() const { return 0; }
    virtual uint64_t getMaxPin() const { return 0xFFFFFF; }
    virtual std::chrono::milliseconds getRetryDelay() const {
        return std::chrono::milliseconds{0};
    }
};
```

### 3.3. `PinCracker` — wspólny algorytm

Klasa przechowuje kierunek iteracji, PIN początkowy, strategię kroków, callback stanu, wątek roboczy oraz referencję do magazynu sprawdzonych PIN-ów. Kopiowanie jest zabronione.

```cpp
class PinCracker {
public:
    enum class State { Initial, PreAuth, Work, PostAuth, Done, Error };
    enum class Direction { Up, Down };

    PinCracker(j2534::J2534& j2534, CarPlatform carPlatform,
               std::unique_ptr<PinCrackerSteps> steps,
               Direction direction, uint64_t startPin,
               std::function<void(State, uint64_t)> stateCallback,
               PinCrackerStorage& storage);
    ~PinCracker();

    PinCracker(const PinCracker&) = delete;
    PinCracker& operator=(const PinCracker&) = delete;

    State getCurrentState() const;
    std::optional<uint64_t> getFoundPin() const;
    bool start();
    void stop();
private:
    void run();
    J2534ChannelProvider _channelProvider;
    std::unique_ptr<PinCrackerSteps> _steps;
    PinCrackerStorage& _storage;
    std::thread _thread;
    Direction _direction;
    uint64_t _startPin;
    std::function<void(State, uint64_t)> _stateCallback;
    mutable std::mutex _mutex;
    State _currentState;
    std::optional<uint64_t> _foundPin;
    std::atomic<bool> _stop{false};
};
```

Algorytm `run()`:
1. Otwiera kanały przez strategię; jeśli to się nie powiedzie, kończy się stanem Error.
2. Ustawia `PreAuth` i wykonuje `preAuth()`; niepowodzenie kończy operację.
3. Uruchamia keep-alive.
4. Iteruje w górę lub w dół od `startPin` do odpowiedniej granicy. Pomija PIN-y już oznaczone w magazynie, ustawia stan `Work`, wywołuje `tryPin()` i zapisuje wynik próby.
5. Po znalezieniu PIN-u lub zatrzymaniu przerywa keep-alive, wykonuje `postAuth()`, wywołuje `flush()` magazynu i ustawia `Done` albo `Error`.

### 3.4. `UDSPinCrackerSteps` — UDS

Strategia:
- `openChannels()`: `provider.getAllChannels(ecuId)`;
- `preAuth()`: `UDSProtocolCommonSteps::fallAsleep(channels)`;
- `startKeepAlive()`: uruchamia `UDSProtocolCommonSteps::keepAlive(channel)`;
- `stopKeepAlive()`: zatrzymuje okresowe komunikaty;
- `tryPin()`: `UDSProtocolCommonSteps::authorize(channel, canId, pin)`;
- `postAuth()`: `UDSProtocolCommonSteps::wakeUp(channels)`;
- `getRetryDelay()`: 5000 ms, zgodnie z dotychczasowym opóźnieniem autoryzacji.

### 3.5. `KWPPinCrackerSteps` — KWP

Implementacja planowana (TBD) dla VAG K-Line/TP20:
- `openChannels` — utworzenie `TP20Session`;
- `preAuth` — `KWPProtocolCommonSteps::enterProgrammingSession`;
- `tryPin` — `KWPProtocolCommonSteps::authorize`;
- `postAuth` — rozłączenie sesji.

### 3.6. `D2PinCrackerSteps` — D2

Strategia przyjmuje `CanIdProvider` i identyfikator ECU.
- `preAuth()` i `postAuth()` wysyłają wzorzec wybudzania D2, okresowo używając `funcCanId` (dla D2 `0xFFFFE`).
- Keep-alive jest no-op, ponieważ D2 nie używa `0x3E 0x80`.
- `tryPin()` pozostaje atrapą: D2 nie używa standardowej autoryzacji UDS `0x27`; implementacja wymaga testów dla konkretnego ECU.

### 3.7. Fabryka PinCracker

Fabryka nie powinna zależeć od J2534 — przyjmuje już otwarte kanały `ICanChannel`. Otwieranie kanałów jest odpowiedzialnością wywołującego.

```cpp
std::unique_ptr<PinCracker> createPinCracker(
    std::vector<std::unique_ptr<common::ICanChannel>> channels,
    common::CarPlatform carPlatform,
    uint32_t ecuId,
    PinCracker::Direction direction,
    uint64_t startPin,
    std::function<void(PinCracker::State, uint64_t)> stateCallback,
    PinCrackerStorage& storage);
```

Fabryka odczytuje konfigurację platformy i ECU, dla każdej magistrali wybiera strategię według protokołu (ISO15765 → UDS, CAN 29-bit → D2, pozostałe pomijane), a kanał docelowego ECU identyfikuje na podstawie prędkości magistrali.

`DummyPinCrackerSteps` rejestruje wywołania bez wysyłania ramek CAN i służy do testowania bez fizycznego urządzenia J2534. `createDummyCracker()` tworzy taki obiekt dla testów; bieżąca implementacja wymusza platformę P2. Pomocnik `createPinCrackerStepsForBus(bus, ecuId)` korzysta z `createCanIdProvider(bus)`.

Zalety:
- fabryka nie dołącza nagłówków `<j2534/...>`;
- można użyć dostawcy kanałów J2534, ELM327, ESP32 lub STM32;
- testy mogą korzystać z `MockICanChannel`;
- algorytm można testować bez podłączania do magistrali.

### 3.8. `PinCrackerStorage` — magazyn sprawdzonych PIN-ów

Magazyn pozwala przerwać wyszukiwanie i wznowić je później, unikać ponownego sprawdzania PIN-ów odrzuconych dla kilku ECU na tej samej platformie oraz łączyć wyniki wielu sesji.

```cpp
class PinCrackerStorage {
public:
    virtual ~PinCrackerStorage() = default;
    virtual bool isChecked(uint64_t pin) const = 0;
    virtual void markChecked(uint64_t pin) = 0;
    virtual void markRange(uint64_t from, uint64_t to) = 0;
    virtual void flush() {}
    static std::shared_ptr<PinCrackerStorage> combine(
        std::shared_ptr<PinCrackerStorage> primary,
        std::shared_ptr<PinCrackerStorage> secondary);
};
```

| Implementacja | Przeznaczenie |
|---|---|
| `NullPinCrackerStorage` | Brak zapisywania; wszystkie PIN-y są traktowane jako nieznane |
| `InMemoryPinCrackerStorage` | Przechowywanie sprawdzonych PIN-ów w pamięci, np. w `unordered_set` lub masce bitowej |
| `FilePinCrackerStorage` | Wczytywanie i zapisywanie stanu do pliku |

Dla całej przestrzeni 24-bitowej mapa bitowa zajmuje 2 MB. Format pliku może być binarną mapą bitów lub tekstową listą sprawdzonych zakresów.

W `run()` przed każdą próbą należy pomijać już sprawdzone wartości; po każdej próbie, zarówno udanej, jak i nieudanej, oznaczać PIN jako sprawdzony. Na końcu wywoływać `flush()`. Błąd zapisu nie może spowodować utraty już znalezionego PIN-u. Dla atomowości należy zapisywać do pliku tymczasowego, a następnie zmieniać jego nazwę.

Magazyn może być współdzielony przez wiele obiektów `PinCracker`; magazyn plikowy pozwala wznowić wyszukiwanie po ponownym uruchomieniu programu.

## 4. Pliki objęte zmianami

### Nowe pliki

| Plik | Opis |
|---|---|
| `Flasher/flasher/pin/PinCrackerSteps.hpp` | Interfejs kroków |
| `Flasher/flasher/pin/PinCracker.hpp` | Klasa bazowa z algorytmem |
| `Flasher/src/pin/PinCracker.cpp` | Implementacja `run()` |
| `Flasher/flasher/pin/UDSPinCrackerSteps.hpp` | Strategia UDS |
| `Flasher/src/pin/UDSPinCrackerSteps.cpp` | Implementacja UDS |
| `Flasher/flasher/pin/PinCrackerStorage.hpp` | Interfejs magazynu |
| `Flasher/src/pin/PinCrackerStorage.cpp` | Implementacje pomocnicze i łączenie magazynów |
| `Flasher/src/pin/FilePinCrackerStorage.cpp` | Magazyn plikowy |
| `Flasher/flasher/pin/PinCrackerFactory.hpp` | Fabryka |

### Modyfikowane lub usuwane

- Usunąć `Common/common/protocols/UDSPinFinder.hpp` i `Common/src/protocols/UDSPinFinder.cpp`, ponieważ zastępuje je strategia.
- W `VolvoFlasher/src/VolvoFlasher.cpp` zastąpić `findPin2()` użyciem `PinCracker`; `findPin()` usunąć lub przenieść na tę samą implementację.
- Przepisać dotychczasowe atrapy `PinCracker.hpp/.cpp`.
- Zaktualizować wpisy w `CMakeLists.txt`.
- Dodać implementacje D2; implementacja KWP może być osobnym etapem.

Bez zmian pozostają `UDSProtocolCommonSteps`, `D2ProtocolCommonSteps`, `KWPProtocolCommonSteps` i dostawca kanałów.

## 5. Kolejność wdrożenia

1. Utworzyć interfejs kroków, `PinCracker` oraz magazyn PIN-ów.
2. Zaimplementować kroki UDS i D2 oraz fabrykę.
3. Przełączyć `VolvoFlasher` na `createPinCracker()`.
4. Usunąć stare `UDSPinFinder` i martwe wpisy w CMake.
5. Opcjonalnie dodać strategię KWP dla TP20.

## 6. Kryteria akceptacji

1. `PinCrackerSteps` abstrahuje wszystkie kroki zależne od platformy.
2. `PinCracker::run()` otwiera kanały, wykonuje `preAuth`, keep-alive, pętlę prób, zatrzymuje keep-alive i wykonuje `postAuth`.
3. `UDSPinCrackerSteps` używa istniejących `UDSProtocolCommonSteps`.
4. `tryPin()` dla UDS wywołuje `authorize()`.
5. `VolvoFlasher` używa `PinCracker` zamiast `UDSPinFinder`.
6. Szybkość wyszukiwania nie jest niższa od dotychczasowej; dostępne są callbacki stanu i pomiar PIN/s.
7. Magazyn udostępnia `isChecked()`, `markChecked()) i `markRange()`.
8. `NullPinCrackerStorage` nie dodaje istotnego narzutu.
9. Magazyny pamięciowe i opcjonalnie plikowe zachowują sprawdzone PIN-y.
10. Algorytm pomija wartości zapisane w magazynie i oznacza każdą próbę.
11. Fabryka wybiera strategię i kanał ECU na podstawie konfiguracji platformy.
12. Nową strategię KWP można dodać bez zmiany `PinCracker`.
13. Stary `UDSPinFinder` jest usunięty, a atrapa `PinCracker` zastąpiona implementacją.
14. Projekt buduje się bez błędów.
