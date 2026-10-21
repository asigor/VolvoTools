# Specyfikacja: refaktoryzacja parametrów czytników i flasherów

## 1. Cel

Zastąpić monolityczną strukturę `FlasherParameters` systemem interfejsów dostawców parametrów (`ReaderParametersProviderBase`, `FlasherParametersProviderBase`). Fabryka ma pobierać od dostawcy tylko parametry wymagane przez konkretny czytnik lub flasher. Celem jest wyeliminowanie błędów w czasie działania wynikających z brakujących pól oraz obejść takich jak `additionalData` i `cmInfo`.

## 2. Dotychczasowa architektura

```cpp
struct FlasherParameters {
    common::CarPlatform carPlatform;
    uint32_t ecuId;
    std::string additionalData;           // obejście: kompresja/szyfrowanie VAG zapisane jako tekst
    std::shared_ptr<SBLProviderBase> sblProvider;
    const common::VBF flash;
    std::unique_ptr<common::CompressorBase> compressor;
    std::unique_ptr<common::EncryptorBase> encryptor;
};
```

Struktura była używana przez wszystkie flashery (`D2FlasherBase`, `UDSFlasher`, `KWPFlasher`) oraz czytnik `D2Reader`.

| Nr | Problem |
|---|---|
| 1 | **Brak wymaganych pól** — `D2Reader` wymaga `sblProvider`, ale pole mogło mieć wartość `nullptr`. |
| 2 | **`additionalData` jako obejście tekstowe** — parametry kompresji i szyfrowania VAG kodowano w łańcuchu znaków. |
| 3 | **`cmInfo` jako obejście tekstowe** — wybór AW55/TF80 odbywał się na podstawie tekstu. |
| 4 | **Jedna struktura dla wszystkich** — flasher VAG nie potrzebuje `sblProvider`, a `D2Reader` nie potrzebuje PIN-u. |
| 5 | **`unique_ptr` w strukturze** — obiekt można przenosić, ale nie kopiować. |

## 3. Docelowa architektura

### 3.1. Pomocnicze typy danych

```cpp
// ParamsTypes.hpp
struct ReadRange { uint32_t startAddr; size_t size; };
using ReadRanges = std::vector<ReadRange>;

struct AuthorizationParams { uint64_t pin; };
struct BootloaderParams { common::VBF bootloader; };  // VBF, a nie SBLProviderBase*
struct EncryptionParams {
    common::CompressionType compression;
    common::EncryptionType encryption;
};
```

### 3.2. Struktury konfiguracji

```cpp
// FlasherConfigs.hpp
struct D2FlasherConfig {
    common::VBF bootloader;     // wcześniej rozstrzygnięty VBF, nie SBLProviderBase*
    const common::VBF flash;
};

struct UDSFlasherConfig {
    std::array<uint8_t, 5> pin;
    common::VBF bootloader;     // wcześniej rozstrzygnięty VBF
    const common::VBF flash;
};

struct KWPFlasherConfig {
    common::VBF bootloader;
    std::array<uint8_t, 5> pin;
    const common::VBF flash;
    common::CompressionType compressionType;
};

struct VAGFlasherConfig {
    std::array<uint8_t, 5> pin;
    const common::VBF flash;
    common::CompressionType compressionType;
    common::EncryptionType encryptionType;
};
```

**Kluczowa różnica względem pierwszej wersji:** `bootloader` jest gotowym obiektem `VBF`, a nie wskaźnikiem `SBLProviderBase*`. Rozpoznanie bootloadera odbywa się przed utworzeniem konfiguracji — w fabryce lub CLI.

### 3.3. Bazowe klasy dostawców parametrów

```cpp
// ReaderParametersProviderBase.hpp
class ReaderParametersProviderBase {
public:
    ReaderParametersProviderBase(common::CarPlatform carPlatform, uint32_t ecuId, const std::string& cmInfo);
    common::CarPlatform getCarPlatform() const;
    uint32_t getEcuId() const;
    const std::string& getCmInfo() const;
    virtual ReadRanges getReadRanges() const = 0;      // wektor zakresów
    virtual std::optional<AuthorizationParams> getAuthParams() const;
    virtual std::optional<BootloaderParams> getBootloaderParams() const;
};

// FlasherParametersProviderBase.hpp
class FlasherParametersProviderBase {
public:
    FlasherParametersProviderBase(common::CarPlatform carPlatform, uint32_t ecuId, const std::string& cmInfo);
    common::CarPlatform getCarPlatform() const;
    uint32_t getEcuId() const;
    const std::string& getCmInfo() const;
    virtual const common::VBF& getFlashData() const = 0;
    virtual std::optional<AuthorizationParams> getAuthParams() const;
    virtual std::optional<BootloaderParams> getBootloaderParams() const;
    virtual std::optional<EncryptionParams> getEncryptionParams() const;
};
```

### 3.4. FlasherBase

```cpp
class FlasherBase: public FlasherCallbackHolder {
public:
    FlasherBase(j2534::J2534 &j2534, common::CarPlatform carPlatform, uint32_t ecuId);
    static size_t getProgressFromVBF(const common::VBF& vbf);
    common::CarPlatform getCarPlatform() const;
    uint32_t getEcuId() const;
    // ... start, state, progress, callbacks
protected:
    const common::CarPlatform _carPlatform;
    const uint32_t _ecuId;
};
```

Każdy flasher przechowuje własną strukturę konfiguracji. `D2FlasherBase` deleguje wykonanie do `D2FlasherImpl` (osobny plik `.cpp` z HFSM2):

```
D2FlasherBase → D2FlasherImpl::run() → HFSM2: WakeUp → FallAsleep → StartPBL → LoadSBL → StartSBL → [callback] → WakeUp → Done
```

### 3.5. ReaderBase

```cpp
class ReaderBase: public FlasherCallbackHolder {
public:
    ReaderBase(j2534::J2534& j2534, common::CarPlatform carPlatform, uint32_t ecuId, ReadRanges ranges);
    const std::vector<std::vector<uint8_t>>& buffers() const;  // wektor buforów
    FlasherState getCurrentState() const;
    void start();
protected:
    const common::CarPlatform _carPlatform;
    const uint32_t _ecuId;
    const ReadRanges _ranges;                     // wektor zakresów
    common::J2534ChannelProvider _channelProvider;
    std::vector<std::vector<uint8_t>> _buffers;   // bufor dla każdego zakresu
    virtual void startImpl(std::vector<std::unique_ptr<ICanChannel>>& channels) = 0;
};
```

### 3.6. D2FlasherImpl — wspólny HFSM2 dla D2ReaderChecksum i D2FlasherBase

```cpp
class D2FlasherImpl {
public:
    D2FlasherImpl(channels, carPlatform, ecuId, bootloader,
                  stateUpdater, progressUpdater, eraseCallback, writeCallback);
    void run();   // uruchamia FSM i czeka na Done/Error
    // ... setMaximumFlashProgressValue, getMaximumProgress, isFailed ...
};
```

Użycie:
- `D2FlasherBase` — callbacki `eraseStep`/`writeStep` (programowanie);
- `D2ReaderChecksum` — erase jako no-op i callback odczytu.

### 3.7. Fabryki

```cpp
// ReaderFactory
static std::unique_ptr<ReaderBase> create(j2534::J2534&, const ReaderParametersProviderBase&);

// FlasherFactory
static std::unique_ptr<FlasherBase> create(j2534::J2534&, const FlasherParametersProviderBase&);
```

Dobór implementacji:
- D2 ECM → `D2ReaderChecksum` (zakresy);
- D2 TCM (`cmInfo=="aw55_p2"`) → `D2ReaderAW55` (zakresy);
- D2 TCM (`cmInfo=="tf80_p2"`) → `D2ReaderTF80` (zakresy);
- UDS → `UDSReader` (zakresy + PIN);
- programowanie D2 → `D2Flasher` (`BootloaderParams`);
- programowanie UDS → `UDSFlasher` (`BootloaderParams` + opcjonalny PIN);
- VAG → `UDSFlasher` (PIN + `EncryptionParams`).

## 4. Zestawienie plików

### 4.1. Nowe pliki

| Nr | Plik | Opis |
|---|---|---|
| 1 | `Flasher/flasher/ParamsTypes.hpp` | `ReadRange`, `ReadRanges`, `AuthorizationParams`, `BootloaderParams`, `EncryptionParams` |
| 2 | `Flasher/flasher/ReaderParametersProviderBase.hpp` | Bazowy dostawca parametrów czytników |
| 3 | `Flasher/flasher/FlasherParametersProviderBase.hpp` | Bazowy dostawca parametrów flasherów |
| 4 | `Flasher/flasher/FlasherConfigs.hpp` | Konfiguracje `D2FlasherConfig`, `UDSFlasherConfig`, `KWPFlasherConfig` |
| 5 | `Flasher/flasher/ReaderBase.hpp` | Klasa bazowa czytnika (`ReadRanges` + `_buffers`) |
| 6 | `Flasher/src/ReaderBase.cpp` | Implementacja `ReaderBase` |
| 7 | `Flasher/flasher/ReaderFactory.hpp` | Fabryka czytników |
| 8 | `Flasher/src/ReaderFactory.cpp` | Implementacja `ReaderFactory` |
| 9 | `Flasher/flasher/D2ReaderChecksum.hpp` | Odczyt D2 przez checksum; zastępuje `D2Reader` |
| 10 | `Flasher/src/D2ReaderChecksum.cpp` | Korzysta z `D2FlasherImpl` |
| 11 | `Flasher/flasher/UDSReader.hpp` | Odczyt UDS przez usługę `0x23` |
| 12 | `Flasher/src/UDSReader.cpp` | Implementacja `UDSReader` |
| 13 | `Flasher/flasher/D2ReaderAW55.hpp` | Odczyt sterownika TCM AW55 |
| 14 | `Flasher/src/D2ReaderAW55.cpp` | Implementacja |
| 15 | `Flasher/flasher/D2ReaderTF80.hpp` | Odczyt sterownika TCM TF80 |
| 16 | `Flasher/src/D2ReaderTF80.cpp` | Implementacja |
| 17 | `Flasher/flasher/FlasherFactory.hpp` | Przepisana sygnatura |
| 18 | `Flasher/src/FlasherFactory.cpp` | Implementacja `FlasherFactory` |
| 19 | `Flasher/src/D2FlasherImpl.hpp` | HFSM2 i kroki D2 w osobnej jednostce translacji |
| 20 | `Flasher/src/D2FlasherImpl.cpp` | Implementacja `D2FlasherImpl` i stany FSM |

### 4.2. Modyfikowane pliki

| Nr | Plik | Zmiana |
|---|---|---|
| 21 | `Flasher/flasher/FlasherBase.hpp` | Usunięto `FlasherParameters`; konstruktor przyjmuje `(j2534, CarPlatform, ecuId)`. |
| 22 | `Flasher/src/FlasherBase.cpp` | Konstruktor bez `FlasherParameters`. |
| 23 | `Flasher/flasher/D2FlasherBase.hpp` | Konstruktor przyjmuje `(j2534, CarPlatform, ecuId, D2FlasherConfig&&)`. |
| 24 | `Flasher/src/D2FlasherBase.cpp` | Przeniesiono D2FlasherImpl i HFSM2 do `D2FlasherImpl.cpp`; `startImpl` wywołuje `impl.run()`. |
| 25 | `Flasher/flasher/D2Flasher.hpp` | Konstruktor przyjmuje `(j2534, CarPlatform, ecuId, D2FlasherConfig&&)`. |
| 26 | `Flasher/src/D2Flasher.cpp` | Używa `getConfig().flash` zamiast `getFlasherParameters().flash`. |
| 27 | `Flasher/flasher/UDSFlasher.hpp` | Usunięto `UDSFlasherParameters`; konfiguracja `UDSFlasherConfig&&`. |
| 28 | `Flasher/src/UDSFlasher.cpp` | `UDSFlasherImpl` dostosowano do `UDSFlasherConfig`. |
| 29 | `Flasher/flasher/KWPFlasher.hpp` | Usunięto `KWPFlasherParameters`; konfiguracja `KWPFlasherConfig&&`. |
| 30 | `Flasher/src/KWPFlasher.cpp` | `KWPFlasherImpl` dostosowano do `KWPFlasherConfig`. |
| 31 | `VolvoFlasher/src/VolvoFlasher.cpp` | `readFlash()`, `UDSFlash()` i `D2Flash()` tworzą konfigurację bezpośrednio. |

### 4.3. Usunięte pliki

| Nr | Plik | Powód |
|---|---|---|
| 32 | `Flasher/flasher/FlasherParameters.hpp` | Struktura usunięta z `FlasherBase.hpp`. |
| 33 | `Flasher/flasher/UDSMemoryReader.hpp` | Zastąpiony przez `UDSReader`, a następnie usunięty; odczyt pamięci realizuje `UDSReaderMemory` przez `MemoryReaderFactory`. |
| 34 | `Flasher/src/UDSMemoryReader.cpp` | Zastąpiony przez `UDSReader`, a następnie usunięty. |
| 35 | `Flasher/flasher/D2Reader.hpp` | Zastąpiony przez `D2ReaderChecksum`. |
| 36 | `Flasher/src/D2Reader.cpp` | Zastąpiony przez `D2ReaderChecksum`. |

## 5. Najważniejsze decyzje architektoniczne

| Decyzja | Uzasadnienie |
|---|---|
| `BootloaderParams` zawiera `VBF`, a nie `SBLProviderBase*` | Bootloader jest rozpoznawany przed utworzeniem konfiguracji; konfiguracje nie zależą od `SBLProviderBase`. |
| `ReaderBase` przyjmuje `ReadRanges` (wektor) | Obsługa wielu zakresów odczytu z osobnymi buforami. |
| `D2FlasherImpl` to osobny plik `.cpp` z HFSM2 | Jedna implementacja FSM współdzielona przez `D2FlasherBase` i `D2ReaderChecksum`. |
| Struktury konfiguracji bez `unique_ptr` | Można je kopiować i przechowywać przez wartość. |
| Bazowe klasy dostawców z implementacjami domyślnymi (`std::nullopt`) | Klasa pochodna nadpisuje tylko metody, których potrzebuje. |

## 6. Kryteria akceptacji

1. ✓ `ParamsTypes.hpp` zawiera `ReadRange`, `ReadRanges`, `AuthorizationParams`, `BootloaderParams` i `EncryptionParams`.
2. ✓ `ReaderParametersProviderBase` ma nie-wirtualne gettery oraz wirtualne `getReadRanges()`, `getAuthParams()`, `getBootloaderParams()`.
3. ✓ `FlasherParametersProviderBase` analogicznie udostępnia `getFlashData()` i `getEncryptionParams()`.
4. ✓ `FlasherConfigs.hpp` zawiera osobne struktury dla D2, UDS, KWP i VAG, z polem `bootloader` typu `VBF`.
5. ✓ `FlasherBase` nie przechowuje `_flasherParameters`; ma `_carPlatform` i `_ecuId`.
6. ✓ `ReaderBase` zapewnia wątek, postęp, `ReadRanges` i `_buffers`.
7. ✓ `D2FlasherImpl` jest osobną jednostką translacji z HFSM2 i metodą `run()`.
8. ✓ `ReaderFactory::create()` wybiera implementację według `(platform, ecuId, cmInfo)`.
9. ✓ `FlasherFactory::create()` wybiera implementację według `(platform, ecuId)`.
10. ✓ `D2Reader` zastąpiono przez `D2ReaderChecksum`, a `UDSMemoryReader` przez `UDSReader`.
11. ✓ Usunięto `FlasherParameters`, `UDSFlasherParameters` i `KWPFlasherParameters`.
12. ✓ Budowanie przez `cmake --build build --config Release` kończy się bez błędów.
