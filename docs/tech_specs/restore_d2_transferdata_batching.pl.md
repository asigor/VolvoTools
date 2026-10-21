# Specyfikacja: przywrócenie wysyłania pakietowego w D2ProtocolCommonSteps::transferData

## 1. Cel

Przywrócić szybkość programowania sterowników ECU D2 po refaktoryzacji. Usunąć czterokrotny spadek wydajności spowodowany wysyłaniem każdej ramki CAN osobnym wywołaniem API J2534 zamiast wysyłania ich w pakietach.

## 2. Diagnoza

### 2.1. Obecne wąskie gardło

W `D2ProtocolCommonSteps::transferData()` (`Common/src/protocols/D2ProtocolCommonSteps.cpp:192-219`) pętla wysyła każdą ramkę 8-bajtową osobno:

```
writeDataOffsetAndCheckAnswer(channel, ecuId, chunk.writeOffset);
for (const auto& frame : frames) {
    channel.clearRx();
    if (!channel.send(frame)) { ... } // jedno PassThruWriteMsgs na ramkę
    progressCallback(6);
}
writeDataOffsetAndCheckAnswer(channel, ecuId, chunk.writeOffset);
```

Programowanie typowego ECM (1–4 MB) wymaga dziesiątek tysięcy wywołań API. Urządzenie J2534 lub połączenie USB nie nadąża z ich obsługą.

### 2.2. Implementacja sprzed refaktoryzacji (commit `50f689a`)

Na poziomie tworzenia komunikatów, `D2Messages::createWriteDataMsgs()`, grupowało do 10 elementów danych w jednym `D2Message`:

```cpp
const auto MaxMessagesPerMessage = 10;
std::vector<D2Message> result;
std::vector<D2Message::DataType> resultPayload;

for (size_t i = beginOffset; i < endOffset; i += chunkSize) {
    // Tworzy 8-bajtowy payload: ecuId + 0xA8+size + data
    resultPayload.emplace_back(std::move(payload));
    if (resultPayload.size() >= MaxMessagesPerMessage) {
        result.emplace_back(D2Message(std::move(resultPayload)));
        resultPayload.clear(); // granica pakietu
    }
}
// Ramka kończąca (0xA8) trafia do ostatniego resultPayload
resultPayload.emplace_back(terminator);
result.emplace_back(D2Message(std::move(resultPayload)));
```

Podczas wysyłania:

```cpp
for (const auto binMsg : binMsgs) {
    channel.clearRx();
    const auto passThruMsgs = binMsg.toPassThruMsgs(...);
    unsigned long msgsNum = passThruMsgs.size();
    channel.writeMsgs(passThruMsgs, msgsNum, 50000); // jedno wywołanie API na pakiet
    progressCallback(6 * msgsNum);
}
```

Jedno `writeMsgs` zawierało wiele struktur `PASSTHRU_MSG`. Urządzenie J2534 wysyłało je kolejno na magistralę bez przerwy wynikającej z osobnych wywołań API.

### 2.3. Przyczyna regresji

Nowa funkcja `createWriteDataFrames()` (`D2ProtocolCommonSteps.cpp:117-151`) tworzy te same grupy po 10 elementów, ale spłaszcza je do jednego `std::vector<CanFrame>`. Granice pakietów zostają utracone, więc pętla wysyła ramki pojedynczo.

## 3. Specyfikacja pakietu

Każdy pakiet zawiera **maksymalnie 10 ramek danych**, bez ramki kończącej.

1. Ramki danych są gromadzone do 10 sztuk włącznie.
2. Po osiągnięciu 10 ramek pakiet jest wysyłany.
3. Po zakończeniu pętli pozostałe 1–9 ramek tworzą ostatni pakiet.
4. Jeśli nie ma żadnych ramek danych, nie powstaje żaden pakiet.

Ramka kończąca `0xA8` bez danych, obecna w starej implementacji, **nie jest potrzebna** — ECU prawidłowo przetwarza dane bez niej. Usunięto ją, aby uprościć i przyspieszyć transmisję.

## 4. Wymagania implementacyjne

### 4.1. Zmiana `createWriteDataFrames`

Zmień typ zwracany na:

```cpp
std::vector<std::vector<CanFrame>> createWriteDataFrames(...)
```

Każdy wewnętrzny `std::vector<CanFrame>` reprezentuje osobny pakiet zawierający do 10 ramek danych.

```cpp
std::vector<std::vector<CanFrame>> result;
std::vector<CanFrame> batch;

for (/* każda ramka danych */) {
    batch.emplace_back(D2_CAN_ID, payload, true);
    if (batch.size() >= 10) {
        result.push_back(std::move(batch));
        batch.clear();
    }
}
if (!batch.empty()) {
    result.push_back(std::move(batch));
}
```

### 4.2. Zmiana pętli wysyłania w `transferData`

Zamiast wysyłać każdą ramkę oddzielnie:

```cpp
for (const auto& frame : frames) {
    channel.clearRx();
    channel.send(frame);
    progressCallback(6);
}
```

wysyłaj całe pakiety:

```cpp
for (auto& batch : batches) {
    channel.clearRx();
    if (!channel.send(batch, 50000)) {
        throw std::runtime_error("write msgs error");
    }
    progressCallback(6 * batch.size());
}
```

Postęp naliczany jest za każdą ramkę danych w pakiecie — 6 jednostek na ramkę, tak jak w obecnej pętli wysyłającej pojedyncze ramki.

### 4.3. Czyszczenie RX

`channel.clearRx()` należy wywoływać przed każdym pakietem, tak jak w starej implementacji.

### 4.4. Limit czasu wysyłania

W starej implementacji limit czasu `writeMsgs` wynosił 50 000 ms. Obecne `channel.send()` nie pozwala go skonfigurować — `J2534ChannelAdapter::send()` wywołuje `writeMsgs` z domyślnym limitem **1000 ms** (zob. `J2534Channel.hpp:23-30`).

Przy wysyłaniu do 10 ramek CAN w jednym wywołaniu `PassThruWriteMsgs` limit 1000 ms może być za krótki. Należy:

1. Dodać parametr `timeout` do `ICanChannel::send()`:

```cpp
virtual bool send(const CanFrame& frame, unsigned long timeout = 1000) = 0;
virtual bool send(const std::vector<CanFrame>& frames, unsigned long timeout = 1000) = 0;
```

Domyślna wartość 1000 ms zachowuje zgodność z dotychczasowymi wywołaniami.

2. Zaktualizować `J2534ChannelAdapter::send()`, aby przekazywała parametr do `_channel->writeMsgs()`: `writeMsgs(msgs, numMsgs, timeout)`.

3. W `transferData` użyć limitu 50 000 ms dla wysyłania pakietowego, jak w poprzedniej implementacji.

## 5. Pliki do zmiany

| Nr | Plik | Zmiana |
|---|---|---|
| 1 | `Common/common/ICanChannel.hpp` | Dodać `unsigned long timeout = 1000` do obu przeciążeń `send()`. |
| 2 | `Common/common/J2534ChannelAdapter.hpp` | Zaktualizować deklaracje `send()`. |
| 3 | `Common/src/J2534ChannelAdapter.cpp` | Przekazywać `timeout` do `writeMsgs()`. |
| 4 | `Common/src/protocols/D2ProtocolCommonSteps.cpp` | Zmienić typ zwracany przez `createWriteDataFrames` i pętlę wysyłania w `transferData`. |

Jeśli istnieją inne implementacje `ICanChannel` (ELM327, ESP32, STM32), ich metody `send()` również trzeba zaktualizować — dodać parametr `timeout`, ignorując go lub wykorzystując zależnie od potrzeb.

## 6. Szacowany efekt

Typowy obraz ECM zawiera 1–4 MB danych, czyli około 180 tys.–700 tys. ramek CAN.

| Tryb | Liczba wywołań API | Szacowany czas dla 1 MB |
|---|---:|---:|
| Obecny, ramka po ramce | ok. 175 000 | ok. 175 s (3 min) |
| Pakietowy, pakiety po 10 | ok. 16 000 (z ramkami kończącymi) | ok. 40 s |

Oczekiwane przyspieszenie: **około 4×**.

## 7. Kryteria akceptacji

1. ✓ `createWriteDataFrames` zwraca `std::vector<std::vector<CanFrame>>` z prawidłowymi granicami pakietów.
2. ✓ Każdy pakiet zawiera maksymalnie 10 ramek danych; ramka kończąca nie występuje.
3. ✓ `clearRx()` jest wywoływane przed każdym pakietem.
4. ✓ Callback postępu jest wywoływany raz na pakiet i nalicza 6 jednostek za każdą ramkę.
5. ✓ `ICanChannel::send()` przyjmuje `unsigned long timeout = 1000`.
6. ✓ `J2534ChannelAdapter::send()` przekazuje `timeout` do `writeMsgs()`.
7. ✓ Wysyłanie pakietowe w `transferData` korzysta z limitu 50 000 ms.
8. ✓ Kompilacja kończy się bez błędów i ostrzeżeń.
9. ✓ Rzeczywista szybkość programowania ECU D2 wraca do poziomu sprzed refaktoryzacji.
