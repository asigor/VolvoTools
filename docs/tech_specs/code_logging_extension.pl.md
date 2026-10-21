# Specyfikacja: rozszerzenie logowania aplikacji (easyloggingpp)

## 1. Cel

Rozszerzyć istniejącą infrastrukturę logowania opartą na easyloggingpp o wyjście konsolowe, rotację plików logów, poziomy dla poszczególnych modułów, obsługę awarii, śledzenie operacji protokołów, migrację do logowania modułowego oraz pomiary wydajności. Obecny mechanizm raportowania postępu przez `std::cout` w `VolvoFlasher` **pozostaje bez zmian** — dodane mają być jedynie wpisy `LOG(ERROR)` dla błędów krytycznych.

## 2. Architektura

### 2.1. Stan pierwotny

```
startup → common::initLogger("application.log")
                   ↓
    el::Configurations
      Format: "%datetime %level %msg"
      Filename: "application.log"
      (brak konsoli, brak rotacji, jeden plik dla wszystkiego)
```

**Komponenty:**

| Komponent | Plik | Rola |
|---|---|---|
| `Common` | `Common/common/Util.hpp:101` | Deklaracja `initLogger()` |
| `Common` | `Common/src/Util.cpp:779-786` | Implementacja `initLogger()` |
| `Common` | `Common/src/protocols/UDSProtocolCommonSteps.cpp` | Ponad 20 wywołań `LOG(INFO)` / `LOG(ERROR)` |
| `Common` | `Common/src/encryption/EncryptorFactory.cpp` | Jedno `LOG(WARNING)` |
| `Flasher` | `Flasher/src/FlasherBase.cpp` | Dwa `LOG(ERROR)` |
| `Logger` | `Logger/src/Logger.cpp` | Jedno `LOG(ERROR)` |
| `VolvoFlasher` | `VolvoFlasher/src/VolvoFlasher.cpp` | `initLogger("application.log")` |
| `VolvoLogger` | `VolvoLogger/src/VolvoLogger.cpp` | `initLogger("application.log")` |

### 2.2. Stan docelowy (zrealizowane kroki A i B)

```
startup → common::initLogger("application.log")
                   ↓
         ┌─────────┴──────────┐
         ↓                    ↓
  FileAppender (z rotacją)    ConsoleAppender (automatycznie w Debug)
         ↓                    ↓
  application.log (10 MB)     stdout (wszystkie poziomy w Debug, INFO+ w Release)
         ↓
  + loggery modułowe ──────── el::Loggers::getLogger("common"/"flasher"/"logger")
  + VOLVOLOG_DEBUG ────────── zmienna środowiskowa włączająca DEBUG/TRACE
```

**Najważniejsze wdrożone zmiany:**
- Format zmieniono z `%datetime %level %msg` na `%datetime %level %fbase:%line %msg`.
- Rotacja: `MaxLogFileSize` ustawiono na 10 MB.
- Konsola jest dostępna po ustawieniu `enableConsole = true`.
- Kompilacja Debug (`_DEBUG`): konsola i wszystkie poziomy logowania.
- Kompilacja Release: tylko INFO/WARNING/ERROR/FATAL.
- Zarejestrowano moduły `common`, `flasher` i `logger`.
- `VOLVOLOG_DEBUG=common,flasher` włącza poziomy DEBUG/TRACE dla wskazanych modułów.
- Sygnatura `initLogger(file, enableConsole=false, debugMode=false)` zachowuje zgodność wsteczną.
- Wejścia i wyjścia metod w `UDSProtocolCommonSteps` są logowane przez `LOG(TRACE)`.

## 3. Lista problemów

| Nr | Problem | Status |
|---|---|---|
| 1 | **Brak logów w konsoli** | ✓ Krok A |
| 2 | **Brak rotacji logów** | ✓ Krok A |
| 3 | **Jeden plik logu, brak poziomów per moduł** | ✓ Krok B |
| 4 | **Brak DEBUG/TRACE w kodzie** | ✓ Krok B (zmienna środowiskowa i wejścia/wyjścia UDS) |
| 5 | **Brak logowania w `D2ProtocolCommonSteps`** | ✓ Krok 3 |
| 6 | **Brak logowania w `KWPProtocolCommonSteps`** | ✓ Krok 4 |
| 7 | **Błędy `setFailed()` w flasherach HFSM2 nie są logowane** | ✓ Krok 5 |
| 8 | **`VolvoFlasher`: 59 użyć `std::cout`, brak `LOG`** | ✓ Krok 2 (błędy krytyczne) |
| 9 | **Brak logowania w `J2534ChannelAdapter`** | ✓ Krok 8 |
| 10 | **Brak logowania w `TP20Session`** | ✓ Krok 9 |
| 11 | **Logowanie wydajności** | ✓ Krok 10 |
| 12 | **Obsługa awarii (crash handler)** | ✗ Zablokowana przez recepturę Conan dla easyloggingpp |
| 13 | **Brak użycia CLOG** | ✓ Krok 6 — używany jest `LOG_MODULE` |
| 14 | **Brak flagi `-v/--verbose` w CLI** | ✓ Krok 7 — dodano w obu programach |
| 15 | **Format JSON** | ✗ Odłożone |
| 16 | **`TIMED_SCOPE`** | ✗ Odłożone (receptura Conan) |

## 4. Plan wdrożenia

### Krok A (✓ wykonany): podstawowa infrastruktura

- `initLogger()`: konsola, rotacja, obsługa `_DEBUG` i format wpisów.
- Bez zmian w `VolvoFlasher.cpp` i `VolvoLogger.cpp`.

### Krok B (✓ wykonany): loggery modułowe i poziomy

- Rejestracja modułów `common`, `flasher` i `logger`.
- Zmienna środowiskowa `VOLVOLOG_DEBUG`.
- Wejścia/wyjścia metod `UDSProtocolCommonSteps` logowane przez `LOG(TRACE)`.

### Krok 1: obsługa awarii

**Plik:** `Common/src/Util.cpp`

Dodać do `initLogger()`:

```cpp
el::Helpers::setCrashHandler(el::CrashHandler::installCrashHandler());
```

Obsługuje sygnały takie jak SIGSEGV, SIGFPE i SIGABRT.

**Kryterium:** po wykonaniu `*(int*)0 = 0;` w logu pojawia się zrzut informacji o awarii. Ten krok jest obecnie zablokowany przez recepturę Conan.

### Krok 2 (✓ wykonany): VolvoFlasher — LOG(ERROR) dla krytycznych błędów

**Plik:** `VolvoFlasher/src/VolvoFlasher.cpp`

W około 12 miejscach dodano `LOG_MODULE(ERROR) << ...` równolegle do `std::cout`:
- błędy seed/key (`readMessages`, `authByKey`);
- wyjątki w blokach catch w funkcji main;
- błędy `startPeriodicMsg` (`findPin`, `findPin2`).

Raportowanie postępu, lista urządzeń i `FlasherState` pozostały bez zmian.

**Kryterium:** krytyczne błędy trafiają również do `application.log`, a wyjście `std::cout` pozostaje dostępne.

### Krok 3 (✓ wykonany): D2ProtocolCommonSteps — TRACE wejścia/wyjścia

**Plik:** `Common/src/protocols/D2ProtocolCommonSteps.cpp`

Dodano `LOG_MODULE(TRACE)` na wejściu i wyjściu wszystkich ośmiu publicznych metod: `fallAsleep`, `startPBL`, `wakeUp`, `transferData`, `eraseFlash`, `jumpTo`, `startRoutine`, `setDIMTime`.

**Kryterium:** każda operacja loguje rozpoczęcie i zakończenie.

### Krok 4 (✓ wykonany): KWPProtocolCommonSteps — TRACE wejścia/wyjścia

**Plik:** `Common/src/protocols/KWPProtocolCommonSteps.cpp`

Dodano `LOG_MODULE(TRACE)` dla ośmiu publicznych metod: `authorize`, `enterProgrammingSession`, dwóch przeciążeń `transferData`, dwóch przeciążeń `eraseFlash`, `requestDownload` i `startRoutine`.

**Kryterium:** każda operacja loguje rozpoczęcie i zakończenie.

### Krok 5 (✓ wykonany): flasher-y HFSM2 — LOG(ERROR) w setFailed()

**Pliki:**
- `Flasher/src/D2FlasherBase.cpp`
- `Flasher/src/UDSFlasher.cpp`
- `Flasher/src/KWPFlasher.cpp`

W metodzie `setFailed()` każdej implementacji dodano `LOG_MODULE(ERROR) << message`.

**Kryterium:** wszystkie błędy z flasherów HFSM2 trafiają do `application.log`.

### Krok 6: wspólny makrodefiniowany logger modułowy

We wszystkich plikach należy stosować ten sam makrodefiniowany interfejs `LOG_MODULE(level)`. Nazwę modułu określa się przez `#define`:

```cpp
// Common/common/Util.hpp (lub osobny nagłówek logowania)
#undef LOG_MODULE
#define LOG_MODULE(level) CLOG(level, LOG_MODULE_NAME)

// W każdym pliku .cpp:
#define LOG_MODULE_NAME "common"
#include "common/Util.hpp"

// Użycie:
LOG_MODULE(TRACE) << "transferChunk enter";
```

| Plik | `LOG_MODULE_NAME` |
|---|---|
| `UDSProtocolCommonSteps.cpp` | `"common"` |
| `D2ProtocolCommonSteps.cpp` | `"common"` |
| `KWPProtocolCommonSteps.cpp` | `"common"` |
| `TP20Session.cpp` | `"common"` |
| `J2534ChannelAdapter.cpp` | `"common"` |
| `EncryptorFactory.cpp` | `"common"` |
| `FlasherBase.cpp` | `"flasher"` |
| `D2FlasherBase.cpp` | `"flasher"` |
| `UDSFlasher.cpp` | `"flasher"` |
| `KWPFlasher.cpp` | `"flasher"` |
| `VolvoFlasher.cpp` | `"flasher"` |
| `Logger.cpp` | `"logger"` |

**Kryterium:** `VOLVOLOG_DEBUG=common` włącza DEBUG tylko dla modułu Common. Kod używa `LOG_MODULE(level)`, a nazwę modułu ustawia jedna linia `#define LOG_MODULE_NAME "…"` na początku pliku `.cpp`.

### Krok 7: flaga CLI -v/--verbose

**Pliki:** `VolvoFlasher/src/VolvoFlasher.cpp`, `VolvoLogger/src/VolvoLogger.cpp`.

Dodać flagę do argparse:

```cpp
program.add_argument("-v", "--verbose")
    .help("Enable verbose (debug) logging")
    .default_value(false)
    .implicit_value(true);
```

Następnie przekazać ją do `initLogger`:

```cpp
common::initLogger("application.log", verbose, verbose);
```

**Kryterium:** `VolvoFlasher -v` włącza logowanie w konsoli oraz poziomy DEBUG/TRACE.

### Krok 8: DEBUG w J2534ChannelAdapter

**Plik:** `Common/src/J2534ChannelAdapter.cpp`

Dodać `LOG(DEBUG)` w przypadku błędów `send`, `receive` i `startPeriodicMsg`, np.:

```cpp
bool J2534ChannelAdapter::send(const CanFrame& frame) {
    PASSTHRU_MSG msg = toPassThruMsg(frame);
    unsigned long numMsgs = 1;
    auto rc = _channel->writeMsgs(&msg, numMsgs, 0);
    if (rc != STATUS_NOERROR) {
        LOG(DEBUG) << "J2534ChannelAdapter::send failed, rc=" << rc;
        return false;
    }
    return true;
}
```

Analogiczne logowanie dodać dla `receive`, `startPeriodicMsg`, `stopPeriodicMsg` i `startMsgFilter`.

**Kryterium:** przy `VOLVOLOG_DEBUG=common` widoczne są błędy wywołań J2534.

### Krok 9: TRACE w TP20Session

**Plik:** `Common/src/protocols/TP20Session.cpp`

Dodać `LOG(TRACE)` dla:
- wejść do i wyjść ze stanów HFSM2 (około 7 stanów);
- przekroczeń limitu czasu w `connect`, `sendRequest` i `readResponse`.

```cpp
void TP20Session::sendRequest() {
    LOG(TRACE) << "TP20Session::sendRequest enter";
    ...
    if (timeout) {
        LOG(DEBUG) << "TP20Session::sendRequest timeout";
    }
    LOG(TRACE) << "TP20Session::sendRequest exit";
}
```

**Kryterium:** przy `VOLVOLOG_DEBUG=common` widoczne są wszystkie przejścia stanów TP20Session.

### Krok 10: logowanie wydajności (std::chrono)

**Pliki:** `Common/common/Util.hpp`, `Common/src/protocols/UDSProtocolCommonSteps.cpp`, `Flasher/src/FlasherBase.cpp`, `Logger/src/Logger.cpp`.

Dodać do `Util.hpp` makro mierzące czas trwania operacji, oparte na `std::chrono::steady_clock`:

```cpp
#define LOG_SCOPE_DURATION(msg) \\
    auto ELPP_UNIQUE_NAME(_perfStart) = std::chrono::steady_clock::now(); \\
    ... defer { \\
        auto _elapsed = std::chrono::duration_cast<std::chrono::milliseconds>( \\
            std::chrono::steady_clock::now() - ELPP_UNIQUE_NAME(_perfStart)).count(); \\
        LOG(INFO) << msg << " took " << _elapsed << " ms"; \\
    }
```

Alternatywą jest ScopeGuard lub lokalny obiekt zapisujący czas w destruktorze.

**Miejsca pomiaru:**

| Miejsce | Operacja |
|---|---|
| `UDSProtocolCommonSteps::transferChunk()` | Czas zapisu pojedynczego bloku |
| `UDSProtocolCommonSteps::transferData()` | Czas całego transferu |
| `UDSProtocolCommonSteps::authorize()` | Czas autoryzacji |
| `UDSProtocolCommonSteps::eraseFlash()` | Czas kasowania pamięci |
| `FlasherBase::start()` | Czas całego programowania |
| `Logger::logFunction()` | Czas jednego cyklu odpytywania ECU |

**Kryterium:** `application.log` zawiera wpisy w rodzaju `transferChunk took 1234 ms`.

## 5. Zestawienie plików

| Nr | Plik | Krok |
|---|---|---|
| 1 | `Common/common/Util.hpp` | A |
| 2 | `Common/src/Util.cpp` | A, 1 |
| 3 | `Common/src/protocols/UDSProtocolCommonSteps.cpp` | B, 10 |
| 4 | `Common/src/protocols/D2ProtocolCommonSteps.cpp` | 3, 6 |
| 5 | `Common/src/protocols/KWPProtocolCommonSteps.cpp` | 4, 6 |
| 6 | `Common/src/protocols/TP20Session.cpp` | 9 |
| 7 | `Common/src/J2534ChannelAdapter.cpp` | 8 |
| 8 | `Flasher/src/D2FlasherBase.cpp` | 5, 6 |
| 9 | `Flasher/src/UDSFlasher.cpp` | 5, 6 |
| 10 | `Flasher/src/KWPFlasher.cpp` | 5, 6 |
| 11 | `Flasher/src/FlasherBase.cpp` | 6, 10 |
| 12 | `Logger/src/Logger.cpp` | 6, 10 |
| 13 | `VolvoFlasher/src/VolvoFlasher.cpp` | 2, 6, 7 |
| 14 | `VolvoLogger/src/VolvoLogger.cpp` | 7 |

## 6. Kryteria akceptacji

1. ✓ Format `application.log`: `%datetime %level %fbase:%line %msg`.
2. ✓ Kompilacja Debug: konsola i wszystkie poziomy.
3. ✓ Kompilacja Release: tylko plik, poziom INFO i wyższe.
4. ✓ Rotacja plików przy rozmiarze 10 MB.
5. ✓ `VOLVOLOG_DEBUG=flasher` włącza DEBUG dla Flasher.
6. ✓ Wejścia/wyjścia `UDSProtocolCommonSteps` logowane przez `LOG(TRACE)`.
7. ☐ Krok 1 — crash handler: zablokowany przez recepturę Conan.
8. ✓ Krok 2 — krytyczne komunikaty `std::cout` w VolvoFlasher są powielane przez `LOG_MODULE(ERROR)`.
9. ✓ Krok 3 — wejścia/wyjścia D2ProtocolCommonSteps przez `LOG_MODULE(TRACE)`.
10. ✓ Krok 4 — wejścia/wyjścia KWPProtocolCommonSteps przez `LOG_MODULE(TRACE)`.
11. ✓ Krok 5 — `setFailed()` we flasherach HFSM2 używa `LOG_MODULE(ERROR)`.
12. ✓ Krok 6 — makro `LOG_MODULE` i filtrowanie per moduł działają.
13. ✓ Krok 7 — `--verbose` w VolvoFlasher i VolvoLogger włącza szczegółowe logowanie.
14. ✓ Krok 8 — błędy J2534ChannelAdapter logowane przez `LOG_MODULE(DEBUG)`.
15. ✓ Krok 9 — wejścia/wyjścia i timeouty TP20Session logowane przez `LOG_MODULE(TRACE/DEBUG)`.
16. ✓ Krok 10 — `LOG_SCOPE_DURATION` dodano do kluczowych operacji.
17. ✓ Raportowanie postępu VolvoFlasher przez `std::cout` pozostało bez zmian.
18. ✓ `SA2.cpp` pozostał bez zmian.
19. ✓ Projekt kompiluje się bez błędów.
