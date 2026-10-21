# Specyfikacja: wydzielenie wspólnego kodu D2 (FSM i sekwencja bootloadera)

## 1. Cel

Usunąć duplikację między `D2FlasherBase` a `D2ReaderChecksum`. Obie klasy wykonują tę samą sekwencję kroków D2 (`wakeUp → fallAsleep → startPBL → [loadSBL → startSBL] → operacja → wakeUp`), ale implementują ją na różne sposoby: jedna używa HFSM2, druga bezpośrednich wywołań.

## 2. Obecna architektura (przed zmianami)

### 2.1. Zduplikowana logika

| Krok | D2FlasherBase | D2ReaderChecksum / D2ReaderME7 / D2ReaderDEM |
|---|---|---|
| `wakeUp` | `D2FlasherImpl::wakeUpChannels()` | przez wspólny `D2FlasherImpl` |
| `fallAsleep` | `D2FlasherImpl::fallAsleep()` | przez wspólny `D2FlasherImpl` |
| `startPBL` | `D2FlasherImpl::startPBL()` | przez wspólny `D2FlasherImpl` |
| `loadSBL` | `D2FlasherImpl::loadSBL()` | przez wspólny `D2FlasherImpl` |
| `startSBL` | `D2FlasherImpl::startSBL()` | przez wspólny `D2FlasherImpl` |
| operacja | HFSM2 → `eraseCallback` + `writeCallback` | HFSM2 → brak operacji kasowania + czytający callback zapisu |
| `wakeUp` | `D2FlasherImpl::wakeUpFinish()` | przez wspólny `D2FlasherImpl` |

**D2FlasherBase** używa automatu HFSM2 z 11 stanami. Automat i `D2FlasherImpl` znajdują się w jednym pliku `.cpp` (`D2FlasherBase.cpp`, około 370 wierszy).

**D2ReaderChecksum**, **D2ReaderME7** i **D2ReaderDEM** wcześniej powielały sekwencję przez bezpośrednie wywołania `D2ProtocolCommonSteps`, bez HFSM2 i obsługi błędów, z ręcznym ustawianiem `setCurrentState()`.

### 2.2. Pliki

| Plik | Zawartość | Rozmiar |
|---|---|---|
| `Flasher/src/D2FlasherBase.cpp` | `D2FlasherImpl`, stany HFSM2, definicja FSM i `D2FlasherBase::startImpl()` | 372 wiersze |
| `Flasher/src/D2FlasherImpl.hpp` | Tylko deklaracja `D2FlasherImpl` | 65 wierszy |
| `Flasher/src/D2ReaderChecksum.cpp` | Bezpośrednie wywołania D2ProtocolCommonSteps | około 100 wierszy |

## 3. Docelowa architektura (po zmianach)

### 3.1. Przeniesienie D2FlasherImpl do osobnej jednostki translacji

**Nowy plik:** `Flasher/src/D2FlasherImpl.cpp`

Zawiera:
- Pełną implementację `D2FlasherImpl`.
- Struktury stanów HFSM2 (11 stanów).
- Definicję maszyny HFSM2.
- `D2FlasherImpl::run()` — tworzy `FSM::Instance` i wykonuje pętlę `while (!_isDone) { fsm.update(); }`.

### 3.2. D2FlasherBase deleguje pracę do D2FlasherImpl

```cpp
void D2FlasherBase::startImpl(channels)
{
    setCurrentProgress(0);
    D2FlasherImpl impl(channels, _carPlatform, static_cast<uint8_t>(_ecuId), _config.bootloader,
        [this](FlasherState s) { setCurrentState(s); },
        [this](size_t p) { incCurrentProgress(p); },
        [this](ICanChannel& ch, uint8_t id) { eraseStep(ch, id); },
        [this](ICanChannel& ch, uint8_t id) { writeStep(ch, id); });
    impl.setMaximumFlashProgressValue(getMaximumFlashProgress());
    setMaximumProgress(impl.getMaximumProgress());
    impl.run();
}
```

### 3.3. D2ReaderChecksum, D2ReaderME7 i D2ReaderDEM używają D2FlasherImpl

```cpp
void D2ReaderChecksum::startImpl(channels)
{
    D2FlasherImpl impl(channels, _carPlatform, static_cast<uint8_t>(_ecuId), common::VBF{},
        [this](FlasherState s) { setCurrentState(s); },
        [this](size_t p) { incCurrentProgress(p); },
        [](ICanChannel&, uint8_t) {},  // kasowanie — brak operacji
        [this](ICanChannel& channel, uint8_t ecuId) {
            for (size_t r = 0; r < _ranges.size(); ++r) {
                auto& buffer = _buffers[r];
                buffer.clear();
                const auto& range = _ranges[r];
                for (uint32_t i = 0; i < range.size; ++i) {
                    const auto currentPos = range.startAddr + i;
                    common::D2ProtocolCommonSteps::jumpTo(channel, ecuId, currentPos);
                    // ... odczyt sumy kontrolnej -> buffer.push_back(data[2])
                }
            }
        });
    size_t totalSize = 0;
    for (const auto& range : _ranges) totalSize += range.size;
    impl.setMaximumFlashProgressValue(totalSize);
    setMaximumProgress(impl.getMaximumProgress());
    impl.run();
}
```

### 3.4. Korzyści

| Obszar | Przed zmianą | Po zmianie |
|---|---|---|
| Kopie sekwencji bootloadera | 4 (`D2FlasherBase` + `D2ReaderChecksum` + `D2ReaderME7` + `D2ReaderDEM`) | 1 (`D2FlasherImpl`) |
| HFSM2 | Wewnątrz `D2FlasherBase.cpp` | W osobnym `D2FlasherImpl.cpp` |
| Obsługa błędów w czytnikach | Brak (bezpośrednie wywołania) | `HFSM2 planFailed` |
| `setCurrentState` w czytnikach | Sterowanie ręczne | Przez callback `stateUpdater` |

## 4. Zmieniane pliki

| Nr | Plik | Zmiana |
|---|---|---|
| 1 | `Flasher/src/D2FlasherImpl.cpp` | **Nowy** — D2FlasherImpl, HFSM2 i FSM |
| 2 | `Flasher/src/D2FlasherImpl.hpp` | Dodanie metody `run()`; kroki publiczne |
| 3 | `Flasher/src/D2FlasherBase.cpp` | Usunięcie D2FlasherImpl i HFSM2; `startImpl()` wywołuje `impl.run()` |
| 4 | `Flasher/src/D2ReaderChecksum.cpp` | Zastąpienie bezpośrednich wywołań przez `D2FlasherImpl::run()` |
| 5 | `Flasher/src/D2ReaderME7.cpp` | Zastąpienie bezpośrednich wywołań przez `D2FlasherImpl::run()` |
| 6 | `Flasher/src/D2ReaderDEM.cpp` | Zastąpienie bezpośrednich wywołań przez `D2FlasherImpl::run()` |

## 5. Kolejność wdrożenia

### Krok 1: utworzenie D2FlasherImpl.cpp

1. Przenieść D2FlasherImpl, HFSM2 i FSM z `D2FlasherBase.cpp` do `D2FlasherImpl.cpp`.
2. Dodać `D2FlasherImpl::run()`.
3. Skompilować projekt.

### Krok 2: uproszczenie D2FlasherBase.cpp

4. Usunąć D2FlasherImpl, stany HFSM2 i definicję FSM.
5. Użyć `impl.run()` zamiast `FSM::Instance fsm{ impl }; while(...) { fsm.update(); }`.
6. Skompilować projekt.

### Krok 3: uproszczenie D2ReaderChecksum, D2ReaderME7 i D2ReaderDEM

7. W D2ReaderChecksum zastąpić bezpośrednie wywołania przez `D2FlasherImpl::run()`.
8. Wprowadzić tę samą zmianę w D2ReaderME7.
9. Wprowadzić tę samą zmianę w D2ReaderDEM.
10. Skompilować projekt.

## 6. Kryteria ukończenia

1. ✓ `D2FlasherImpl` znajduje się w osobnych plikach `.cpp` i `.hpp` oraz używa HFSM2.
2. ✓ `D2FlasherImpl::run()` uruchamia FSM i oczekuje na stan Done/Error.
3. ✓ `D2FlasherBase::startImpl()` deleguje wykonanie do `impl.run()`.
4. ✓ `D2ReaderChecksum::startImpl()` używa `D2FlasherImpl` (brak operacji kasowania, callback odczytu w miejscu zapisu).
5. ✓ `D2ReaderME7::startImpl()` używa `D2FlasherImpl` (brak kasowania, odczyt przez `createReadOffsetMsg2`).
6. ✓ `D2ReaderDEM::startImpl()` używa `D2FlasherImpl` (brak kasowania, odczyt przez `createReadOffsetMsgDEM`).
7. ✓ Wszystkie czytniki obsługują błędy za pośrednictwem HFSM2.
8. ✓ Czytniki nie zawierają bezpośrednich wywołań `wakeUp/fallAsleep/startPBL/loadSBL/startSBL`.
9. ✓ Kompilacja kończy się bez błędów.
10. ✓ Funkcjonalność D2Flash i D2Read pozostaje zachowana.
