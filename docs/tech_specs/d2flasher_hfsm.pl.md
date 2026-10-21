# Specyfikacja: przeniesienie D2FlasherBase na automat stanów (hFSM2)

## 1. Cel

Zastąpić liniowy mechanizm sterowania oparty na try/catch w `D2FlasherBase::startImpl()` automatem stanów z użyciem biblioteki hFSM2, podobnie jak w `UDSFlasher` i `KWPFlasher`. Wydzielić FSM do osobnej jednostki translacji, aby można go było ponownie wykorzystać w `D2ReaderChecksum`.

## 2. Obecna architektura

```
D2FlasherBase::startImpl()
  └─ kod sekwencyjny:
       wakeUp → fallAsleep → startPBL → [loadSBL → startSBL] → eraseStep → writeStep → wakeUp + setDIMTime
  try { ... } catch(...) { wakeUp + setDIMTime + Error }
```

**Uczestnicy:**

| Klasa | Rola |
|---|---|
| `D2FlasherBase` | Koordynator. `startImpl()` to sekwencja liniowa i try/catch. |
| `D2Flasher` | Dziedziczy po `D2FlasherBase`. Nadpisuje `eraseStep()` (wywołanie `eraseFlash`) i `writeStep()` (wywołanie `transferData`). |
| `D2Reader` | Dziedziczy po `D2FlasherBase`. Nadpisuje `eraseStep()` (pusta operacja) i `writeStep()` (odczyt bajt po bajcie). |

**Problemy:**
- try/catch jest jedynym mechanizmem obsługi błędów;
- trudno dodać kroki warunkowe lub powtarzane;
- `wakeUp` i `setDIMTime` są powielone na ścieżce poprawnej i w bloku catch;
- brak szczegółowej kontroli stanów.

## 3. Docelowa architektura

### 3.1. Architektura ogólna

```
D2FlasherBase::startImpl()
  └─ tworzy D2FlasherImpl (kontekst)
  └─ impl.run()  ← uruchamia FSM i czeka na Done/Error

D2ReaderChecksum::startImpl()
  └─ tworzy D2FlasherImpl (kontekst, erase jako no-op)
  └─ impl.run()  ← ten sam FSM

D2FlasherImpl::run()
  └─ FSM::Instance fsm{ *this }
  └─ while(!_isDone) { fsm.update() }
```

**Najważniejsza zmiana:** `D2FlasherImpl` znajduje się w osobnych plikach (`Flasher/src/D2FlasherImpl.{hpp,cpp}`) i jest używany zarówno przez `D2FlasherBase`, jak i `D2ReaderChecksum`.

### 3.2. `D2FlasherImpl` — kontekst automatu

```cpp
// Flasher/src/D2FlasherImpl.hpp
class D2FlasherImpl {
public:
    D2FlasherImpl(
        const std::vector<std::unique_ptr<ICanChannel>>& channels,
        common::CarPlatform carPlatform,
        uint8_t ecuId,
        const common::VBF& bootloader,
        const std::function<void(FlasherState)>& stateUpdater,
        const std::function<void(size_t)>& progressUpdater,
        const std::function<void(ICanChannel&, uint8_t)>& eraseCallback,
        const std::function<void(ICanChannel&, uint8_t)>& writeCallback
    );

    void run();  // uruchamia FSM i czeka na Done/Error

    void setMaximumFlashProgressValue(size_t value);
    size_t getMaximumProgress() const;

    bool isFailed() const;
    bool isSBLRequired() const;

    void wakeUpChannels();
    void fallAsleep();
    void startPBL();
    void loadSBL();
    void startSBL();
    void eraseFlash();
    void writeFlash();
    void wakeUpFinish();
    void setDIMTime();
    void done();
    void error();

private:
    void setFailed(const std::string& msg);
    // ... _channels, _carPlatform, _ecuId, _bootloader, _isFailed, _isDone, ...
};
```

`eraseCallback` i `writeCallback` to lambdy przekazywane z `startImpl()`. W przypadku `D2FlasherBase` wywołują `eraseStep`/`writeStep`, a w `D2ReaderChecksum` pierwsza jest no-op, zaś druga realizuje odczyt bajt po bajcie.

### 3.3. Stany automatu

```hfsm2
PeerRoot<
  Composite<              // Główny plan (StartWork)
    struct StartWork,
    struct FallAsleep,
    struct StartPBL,
    struct LoadSBL,       // Opcjonalny etap SBL
    struct StartSBL,
    struct EraseFlash,
    struct WriteFlash
  >,
  Composite<              // Zakończenie (Finish)
    struct Finish,
    struct WakeUpFinish,
    struct SetDIMTime,
    struct Done,
    struct Error
  >
>
```

**StartWork** konfiguruje plan:

```
plan.change<FallAsleep, StartPBL>();
plan.change<StartPBL, LoadSBL>();
plan.change<LoadSBL, StartSBL>();
plan.change<StartSBL, EraseFlash>();
plan.change<EraseFlash, WriteFlash>();
```

**WakeUpChannels** nie jest osobnym stanem — wybudzenie odbywa się wewnątrz `run()`, przed uruchomieniem FSM (zob. `D2FlasherImpl`).

`LoadSBL` i `StartSBL` są no-op, gdy `!isSBLRequired()` (puste `enter()` prowadzi przez `BaseState::update` do `succeed`).

**Finish** przechodzi w zależności od `planSucceeded`/`planFailed` przez `WakeUpFinish → SetDIMTime → Done/Error`.

### 3.4. Zaktualizowane `startImpl()` w D2FlasherBase

```cpp
void D2FlasherBase::startImpl(std::vector<std::unique_ptr<ICanChannel>>& channels) {
    setCurrentProgress(0);
    D2FlasherImpl impl(channels, _carPlatform, static_cast<uint8_t>(_ecuId), _config.bootloader,
        [this](FlasherState s) { setCurrentState(s); },
        [this](size_t p) { incCurrentProgress(p); },
        [this](ICanChannel& ch, uint8_t id) { eraseStep(ch, id); },
        [this](ICanChannel& ch, uint8_t id) { writeStep(ch, id); }
    );

    impl.setMaximumFlashProgressValue(getMaximumFlashProgress());
    setMaximumProgress(impl.getMaximumProgress());
    impl.run();   // zamiast ręcznej pętli fsm.update()
}
```

### 3.5. `startImpl()` w D2ReaderChecksum — ponowne użycie tego samego FSM

```cpp
void D2ReaderChecksum::startImpl(std::vector<std::unique_ptr<ICanChannel>>& channels) {
    D2FlasherImpl impl(channels, _carPlatform, static_cast<uint8_t>(_ecuId), common::VBF(),
        [this](FlasherState s) { setCurrentState(s); },
        [this](size_t p) { incCurrentProgress(p); },
        [](ICanChannel&, uint8_t) {},  // erase — no-op
        [this](ICanChannel& ch, uint8_t id) { /* odczyt bajt po bajcie przez checksum */ }
    );

    size_t totalSize = /* suma _ranges.size */;
    impl.setMaximumFlashProgressValue(totalSize);
    setMaximumProgress(impl.getMaximumProgress());
    impl.run();
}
```

### 3.6. Obliczanie postępu

`getMaximumProgress()`:

```
stepCost * 4 + getProgressFromVBF(_bootloader) + getMaximumFlashProgressValue()
```

Gdzie `stepCost = 100` (koszt kroków `wakeUp`, `fallAsleep`, `startPBL` i `setDIMTime`).

## 4. Pliki objęte zmianami

### 4.1. Nowe pliki

| Nr | Plik | Opis |
|---|---|---|
| 1 | `Flasher/src/D2FlasherImpl.hpp` | Deklaracja `D2FlasherImpl` |
| 2 | `Flasher/src/D2FlasherImpl.cpp` | Implementacja `D2FlasherImpl`, maszyna hFSM2, 10 stanów i `run()` |

### 4.2. Modyfikowane pliki

| Nr | Plik | Zmiana |
|---|---|---|
| 3 | `Flasher/src/D2FlasherBase.cpp` | Usunięto implementację `D2FlasherImpl`, hFSM2 i struktury stanów. `startImpl()` deleguje wykonanie do `impl.run()`. |
| 4 | `Flasher/flasher/D2FlasherBase.hpp` | Usunięto nieużywany kod i `#include "FlasherCallback.hpp"`. |

### 4.3. Pliki korzystające ze wspólnej implementacji

| Nr | Plik | Zmiana |
|---|---|---|
| 5 | `Flasher/src/D2ReaderChecksum.cpp` | Tworzy `D2FlasherImpl` z erase jako no-op i wywołuje `impl.run()`. |
| 6 | `Flasher/src/D2ReaderME7.cpp` | Tworzy `D2FlasherImpl` z erase jako no-op i odczytem w callbacku write (bajt po bajcie przez `createReadOffsetMsg2`). |
| 7 | `Flasher/src/D2ReaderDEM.cpp` | Tworzy `D2FlasherImpl` z erase jako no-op i odczytem w callbacku write (bajt po bajcie przez `createReadOffsetMsgDEM`). |

## 5. Testowanie

Testy w `Flasher/test/` (`D2FlasherTest.cpp`, 11 przypadków) nie uległy zmianie. Automat hFSM2 pozostał taki sam — został jedynie przeniesiony do osobnego pliku `.cpp`.

## 6. Status implementacji

| Nr | Zadanie | Status |
|---|---|---|
| 1 | Utworzenie `D2FlasherImpl`, hFSM2 i struktur stanów | ✓ |
| 2 | Wydzielenie do plików `D2FlasherImpl.hpp` / `.cpp` | ✓ |
| 3 | Przepisanie `D2FlasherBase::startImpl()` na `impl.run()` | ✓ |
| 4 | Usunięcie nieużywanego kodu z nagłówka | ✓ |
| 5 | Ponowne użycie `D2FlasherImpl` w `D2ReaderChecksum`, `D2ReaderME7` i `D2ReaderDEM` | ✓ |
| 6 | Dodanie katalogu `Flasher/test/` z testami | ✓ |
| 7 | Kompilacja bez błędów | ✓ |
| 8 | Uruchomienie testów: 11/11 zaliczonych | ✓ |

## 7. Kryteria akceptacji

1. ✓ `D2FlasherBase::startImpl()` nie zawiera liniowego kodu z try/catch — korzysta z hFSM2.
2. ✓ `FlasherState` jest aktualizowany na każdym etapie.
3. ✓ W razie błędu: `Finish → WakeUpFinish → SetDIMTime → Error`.
4. ✓ Po sukcesie: `Finish → WakeUpFinish → SetDIMTime → Done`.
5. ✓ Postęp aktualizuje się podczas `wakeUp`, `fallAsleep`, `startPBL`, `loadSBL`, `writeFlash` i `setDIMTime`.
6. ✓ `D2Flasher`, `D2ReaderChecksum`, `D2ReaderME7` i `D2ReaderDEM` korzystają z jednego hFSM2.
7. ✓ Kompilacja bez błędów.
8. ✓ Test `FlasherTests.exe` zalicza 11/11 przypadków.
