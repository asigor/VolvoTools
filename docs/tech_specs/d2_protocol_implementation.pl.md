# Specyfikacja implementacji protokołu D2

## Cel

Opisać format i działanie protokołu D2 używanego do diagnostyki i programowania sterowników Volvo P80/P1/P2 przez CAN. Dokument dotyczy istniejącej implementacji w C++20 dla Windows/CMake.

## Przegląd

D2 jest zastrzeżonym protokołem Volvo działającym nad CAN z rozszerzonymi identyfikatorami 29-bitowymi. Wszystkie komunikaty diagnostyczne używają `D2Message::CanId = 0xFFFFE` i ośmiobajtowego payloadu.

```
D2Message (serializacja danych logicznych do CanFrame)
    ↓
D2Request (wysyłanie przez ICanChannel i parsowanie odpowiedzi)
    ↓
ICanChannel (J2534 / Mock)
```

Operacje bootloadera korzystają z odrębnego, prostego protokołu: surowej ramki 8-bajtowej bez prefiksów i numeracji serii.

```
D2ProtocolCommonSteps → makeBootloaderFrame → ICanChannel
```

## Format ramki CAN

```cpp
struct CanFrame {
    uint32_t id;
    std::vector<uint8_t> data; // payload: 0–8 bajtów
    bool isExtendedId;
};
```

Ramki D2 mają rozszerzone CAN ID `0xFFFFE` i payload o długości 8 bajtów.

| Bajt | Znaczenie |
|---|---|
| `[0]` | Nagłówek protokołu (prefiks / seriesId) |
| `[1..7]` | Dane, maksymalnie 7 bajtów; pozostałe bajty są zerowane |

## Nagłówki (bajt 0)

Nagłówek koduje trzy niezależne flagi oraz długość danych lub numer ramki serii.

| Bit | Maska | Znaczenie |
|---|---|---|
| 7 | `0x80` | Pierwsza ramka |
| 6 | `0x40` | Ostatnia ramka |
| 4 | `0x10` | Flaga serii — odpowiedź wieloramkowa jest kontynuowana |
| 3..0 | `0x0F` | Długość danych albo numer ramki serii |

### Kodowanie długości

| Flaga serii `0x10` | Młodszy nibble | Długość danych |
|---|---|---|
| Wyłączona | `8 + len` (8–15) | `len - 8` (0–7) |
| Włączona | `len` (0–7) | `len` (0–7) |

Przykładowo `0xCB` dla żądania `{0xB9, 0xFB}` oznacza pierwszą i ostatnią ramkę z 3 bajtami danych; `0x97` oznacza pierwszą ramkę odpowiedzi CEM z flagą serii i 7 bajtami danych.

### Formy nagłówka

| Wartość | `0x80` | `0x40` | `0x10` | Znaczenie |
|---|---:|---:|---:|---|
| `0xC8 + len` | 1 | 1 | 0 | Odpowiedź jednoramkowa |
| `0x88 + len` | 1 | 0 | 0 | Pierwsza ramka wieloramkowa |
| `0x48 + len` | 0 | 1 | 0 | Ostatnia ramka serii |
| `0x09..0x0F`, `0x08` | 0 | 0 | 0 | Kontynuacja serii, baza `0x08` |
| `0x90 + len` | 1 | 0 | 1 | Pierwsza ramka tekstowej odpowiedzi CEM |
| `0x11..0x17`, `0x10` | 0 | 0 | 1 | Kontynuacja serii, baza `0x10` |
| `0x50..0x57` | 0 | 1 | 1 | Koniec podbloku, nie koniec odpowiedzi |

Odpowiedź kończy wyłącznie ramka z bitem `0x40`, ale bez flagi serii: `0xC8..0xCF` (odpowiedź jednoramkowa) lub `0x48..0x4F` (ostatnia ramka serii). `0x50..0x57` kończy podblok i oznacza dalszy ciąg.

### Identyfikatory serii (seriesId)

Bazę wyznacza bit `0x10` pierwszej ramki:

| Baza | Sekwencja | Zastosowanie |
|---|---|---|
| `0x08` | `0x09 → 0x0A → … → 0x0F → 0x08 → …` | Odpowiedzi przy odczycie pamięci i danych ECU |
| `0x10` | `0x11 → 0x12 → … → 0x17 → 0x10 → …` | Tekstowe odpowiedzi CEM, np. VIN `B9 FB` i konfiguracja `B9 FC` |

## Format żądania

Ramki CAN są budowane z trzech części: `ecuId` (1 bajt), `requestId` (N bajtów) i `params` (M bajtów). Łącznie: `1 + N + M` bajtów danych.

Żądania wysyłane przez narzędzie używają formatu bez flagi serii. Flaga `0x10` została zaobserwowana w odpowiedziach ECU.

### Pojedyncza ramka (do 7 bajtów danych)

```
[0] = 0xC8 + dataSize
[1] = ecuId
[2..1+N] = requestId
[2+N..1+N+M] = params
pozostałe bajty do [7] = 0x00 (dopełnienie)
```

### Wiele ramek (więcej niż 7 bajtów)

- Pierwsza: `[0] = 0x88 + payloadSize`; dalsze bajty zawierają `ecuId` oraz początek `requestId/params`.
- Środkowe: `[0] = seriesId` (np. `0x09 → 0x0A`); payload zawiera kolejne dane.
- Ostatnia: `[0] = 0x48 + payloadSize`; payload zawiera końcowe dane.

Implementacja znajduje się w `generateCanFrames` w `Common/src/protocols/D2Message.cpp`. Dane są dzielone na fragmenty po maksymalnie 7 bajtów. Bajt 0 jest wyliczany na podstawie tego, czy ramka jest pierwsza, ostatnia czy środkowa. Licznik serii zwiększa się cyklicznie modulo 8; pierwsza ramka środkowa musi mieć `0x09`.

| Pierwsza | Ostatnia | Prefiks | Znaczenie |
|---:|---:|---|---|
| 1 | 1 | `0xC8 + payloadSize` | Pojedyncza ramka |
| 1 | 0 | `0x88 + payloadSize` | Pierwsza ramka serii |
| 0 | 1 | `0x48 + payloadSize` | Ostatnia ramka |
| 0 | 0 | `0x09, 0x0A, …, 0x0F, 0x08, …` | Ramka środkowa |

## Format odpowiedzi

Odpowiedź parsuje `D2Request::process` w `Common/src/protocols/D2Request.cpp`, używając stanów `WaitFirst` i `WaitSeries`.

### Strumień danych odpowiedzi

Odpowiedź jest interpretowana jako jeden strumień złożony z bajtów danych pierwszej ramki i wszystkich ramek serii:

```
[0] = ecuId
[1] = requestId[0] + 0x40
[2..requestId.size()] = requestId[1..]
[requestId.size()+1..] = właściwe dane odpowiedzi
```

Echo zajmuje `requestId.size() + 1` bajtów i może wykraczać poza pierwszą ramkę. Parser zbiera je i sprawdza, zanim zwróci właściwy payload.

Długi identyfikator jest wykorzystywany przy odczycie pamięci TF80: `D2Messages::createReadTCMTF80DataByAddr` używa `{0xB4, 0x21, 0x34, addr(4)}` (8 bajtów), a echo ma 9 bajtów i przechodzi przez dwie ramki. Zachowanie potwierdzono na sprzęcie. ME7 używa podobnego wzorca `createReadDataByAddrMsg`; AW55 korzysta z `createReadDataByOffsetMsg` i nie wymaga zmian.

### Pierwsza ramka

- `header & 0x80` musi być ustawione.
- `data[1]` musi odpowiadać `ecuId), a `data[2]` — `requestId[0] + 0x40` albo markerowi błędu `0x7F`.
- Po rozpoznaniu markera dopuszczalne są nagłówki `0x88..0x8F`, `0xC8..0xCF), a także format z flagą serii `0x90..0x97) lub `0xD0..0xD7`. W innym przypadku zgłaszany jest błąd.
- Baza `seriesId` wynosi `0x10), gdy ustawiono flagę serii, w przeciwnym razie `0x08`.
- Ramka jednoramkowa musi zawierać całe echo.

### Ramki kontynuacji

- Ramka środkowa musi mieć oczekiwany `seriesId`; nieprawidłowa kolejność powoduje wyjątek.
- `0x50..0x57) oznacza koniec podbloku, ale odpowiedź trwa dalej.
- `0x48..0x4F) bez flagi serii oznacza ostatnią ramkę; długość danych to `header - 0x48`.
- Powtórzona pierwsza ramka (np. `0x8F` po `0x55` w odpowiedzi CEM) jest pomijana jako duplikat.
- Jeśli seria kończy się przed zebraniem całego echa, parser zgłasza błąd.

### Limity i semantyka błędów

- `maxResponseSize = 64 KiB), `maxFrameCount = maxResponseSize / 7 + 4`. Przekroczenie limitu powoduje `std::runtime_error`.
- Pusty `requestId` powoduje `std::invalid_argument`; długość identyfikatora nie jest ograniczona.
- Obce ramki, puste ramki, zbyt krótkie dane i niedopasowany marker są pomijane.
- Naruszenia protokołu po rozpoznaniu odpowiedzi, nieprawidłowy nagłówek/seriesId, brak końca serii, przekroczenie limitów i timeout powodują wyjątek.
- CAN ID odpowiedzi nie jest walidowany: rzeczywiste odpowiedzi D2 mogą przychodzić z ID innym niż `0xFFFFE`.

## Format błędu

Negatywna odpowiedź diagnostyczna zawiera marker `0x7F` w pozycji `data[2]`, następnie echo numeru usługi i kod błędu:

```
[0] = header
[1] = ecuId
[2] = 0x7F
[3] = requestId[0]
[4] = kod błędu
```

Region echa ma 3 bajty strumienia `[ecuId, 0x7F, requestId[0]]); pełne echo `requestId` nie występuje. Po zebraniu regionu parser natychmiast zgłasza `D2Error` z kodem `result[3]`; jeśli bajtu kodu brakuje, używa `D2Error(0)`. Obsługa błędu jest częścią `D2Request::process`; dawna funkcja `checkD2Error` została usunięta.

## Rzeczywista komunikacja: odczyt VIN (`B9 FB`)

Dane pochodzą z logu VIDA z sesji `IdentifyVehicle → Read VIN`. VIN to `YV1MK685972006675`; komunikacja odbywa się przez D2.

### Adresowanie

| Magistrala | Konfiguracja J2534 | Żądanie | Odpowiedź |
|---|---|---|---|
| CAN HS 500k, 29 bitów | `PassThruConnect(CAN=0x05, CAN_29BIT_ID, 500000)` | `CB 50 B9 FB` | Blok tekstowy z VIN |
| CAN MS 125k, 29 bitów | `PassThruConnect(CAN_PS=0x8004, CAN_29BIT_ID, 125000)` | `CB 40 B9 FB` | `7F B9 12` — subFunctionNotSupported |

CEM obsługuje odczyt VIN przez magistralę HS pod adresem `0x50`; na magistrali 125 kbit/s adres `0x40` odpowiada odmową obsługi podfunkcji.

### Żądanie

```
---> 00,0F,FF,FE | CB 50 B9 FB 00 00 00 00
     CAN ID 0xFFFFE; 0xCB = first+last, 3 bajty danych
     ecuId 0x50 (CEM), requestId B9 FB, dopełnienie zerami
```

Żądanie jest identyczne bajt po bajcie z ramką generowaną przez `D2Message::getFrames()`.

### Odpowiedź i payload

Odpowiedź zaczyna się od `0x97), a kolejne ramki używają serii z bazą `0x10`. W strumieniu znajdują się metadane, tekst oddzielony CRLF oraz dopełnienie `0xFF`. Po usunięciu echa payload zawiera m.in.:

- metadane `57 00 31 82` i `17 96`;
- wiersz `002`;
- wiersz VIN `YV1MK685972006675`;
- kolejne pola tekstowe oraz dopełnienie `0xFF`.

Najpewniejsze wydobywanie VIN-u polega na podziale tekstu po CRLF i znalezieniu wiersza składającego się z 17 znaków `[0-9A-Z]`. Stałe przesunięcie 11 bajtów działa tylko wtedy, gdy pierwszy wiersz ma dokładnie postać `002`.

### Czasy i komunikaty pomocnicze

- Odpowiedź przychodzi zwykle w 10–20 ms; VIDA używa limitu 60–100 ms, a `D2Request` — 500 ms.
- Przed żądaniem VIDA uruchamia okresowy komunikat keep-alive D2. Na wybudzonym samochodzie odpowiedź często przychodzi również bez niego.
- `B9 FB` nie wymaga otwarcia sesji diagnostycznej: jedno żądanie daje jedną odpowiedź.

## Format raw — polecenia jednoramkowe

Stosowany w `D2ProtocolCommonSteps` i `D2RawMessages` do operacji bootloadera.

```
[0] = ecuId
[1] = command (np. 0xA8+len, 0xC0, 0xC8, 0xF8, 0xA0, 0x9C, 0xB4, 0xBC)
[2..7] = parametry / dane
```

Ramkę tworzy `makeBootloaderFrame(ecuId, {command, ...})` lub jedna z metod `D2RawMessages::create*`. Odpowiedź jest porównywana z oczekiwanymi bajtami `response[1..]`; numer polecenia może się różnić, np. żądanie `0xC0` otrzymuje odpowiedź `0xC6`.

## Polecenia D2

### Polecenia raw dla bootloadera

| Polecenie | Funkcja | Przeznaczenie |
|---|---|---|
| `0xC0` | `createStartPrimaryBootloaderMsg` | Uruchomienie Primary Bootloader; odpowiedź `0xC6` |
| `0xC8` | `createWakeUpECUMsg` | Wybudzenie ECU |
| `0xA8+len` | `createWriteDataFrames` | Zapis danych, długość 1–6 |
| `0xA8` | `createSBLTransferCompleteMsg` | Koniec transferu SBL |
| `0xF8` | `createEraseMsg` | Kasowanie flash; odpowiedź `0xF9` + status |
| `0xA0` | `createJumpToMsg` | Skok pod adres / uruchomienie |
| `0x9C` | `createSetMemoryAddrMsg` | Ustawienie adresu pamięci |
| `0xB4` | `createCalculateChecksumMsg` | Obliczenie sumy kontrolnej |
| `0xBC` | `createReadOffsetMsg2` | Odczyt według przesunięcia |
| `0x86` | `goToSleepCanRequest` | Usypianie magistrali, okresowo wysyłane z ID `0xFF` |

### Ustrukturyzowane żądania diagnostyczne

| `requestId` | Parametry | Funkcja | Znaczenie |
|---|---|---|---|
| `{0xB9, 0xFB}` | brak | `requestVIN` | Odczyt VIN z CEM; tekst, VIN w drugim wierszu |
| `{0xB9, 0xFC}` | brak | `requestVehicleConfiguration` | Konfiguracja pojazdu |
| `{0xA6, 0xF0, 0x00, 0x01}` | brak | `requestMemory` | Żądanie pamięci ECM_ME |
| `{0xAA, 0x00}` | brak | `unregisterAllMemoryRequest` | Anulowanie rejestracji pamięci |
| `{0xA7}` | adres 3-bajtowy, 1, rozmiar | `readDataByOffset` | Odczyt według przesunięcia |
| `{0xBB}` | adres 3-bajtowy, rozmiar | `readDataByAddr` | Odczyt pod adresem |
| `{0xAA, 0x50}` | adres, rozmiar | `registerAddrRange` | Rejestracja zakresu adresów |
| `{0xBA}` | adres, dane | `writeDataByAddr` | Zapis bajtu pod adresem |
| `{0xB0, 0x07, 0x01, 0xFF}` | czas hi/lo | `setCurrentTime` | Ustawienie czasu DIM |

## Echo żądania w odpowiedzi ECU

Pierwsza ramka odpowiedzi potwierdza żądanie:

```
response[1] = ecuId
response[2] = requestId[0] + 0x40
response[3..] = requestId[1..]
```

Po echo następują dane odpowiedzi.

## Sekwencje protokołu

### Wybudzanie

1. Wysłanie ramki wake-up `[0xFF, 0xC8, 0,0,0,0,0,0]` z ID `0xFF`.
2. Pauza.
3. Okresowe wysyłanie `[0xFF, 0x86, 0,0,0,0,0,0]` co 5 ms przez 3 s.
4. Kolejna pauza.

### Ładowanie bootloadera

1. Wysłać `{0xC0}` (start PBL), oczekiwać potwierdzenia `{0xC6}`.
2. Jeśli wymagany jest SBL, przesłać obraz SBL przez `transferData`.
3. Wysłać `0xA0` z adresem, aby uruchomić SBL.

### Programowanie

1. Ustawić adres pamięci przez `0x9C) i sprawdzić echo.
2. Wysyłać dane pakietami po 10 ramek `0xA8+len`.
3. Na końcu ustawić adres końcowy i wysłać `0xB4` do obliczenia sumy kontrolnej.
4. ECU odpowiada `0xB1` i sumą kontrolną.

## Implementacja

### Klasy

| Klasa | Plik | Odpowiedzialność |
|---|---|---|
| `CanFrame` | `Common/common/CanFrame.hpp` | Ramka CAN: ID, dane, flaga rozszerzonego ID |
| `CanMessage` | `Common/common/CanMessage.hpp` | Klasa bazowa z `_data` i `getFrames()` |
| `D2Message` | `Common/common/protocols/D2Message.hpp` | Serializacja `ecuId + requestId + params` do ramek |
| `D2Request` | `Common/common/protocols/D2Request.hpp` | Wysyłanie żądań i parsowanie odpowiedzi |
| `D2Messages` | `Common/common/protocols/D2Messages.hpp` | Fabryka żądań diagnostycznych |
| `D2RawMessages` | `Common/common/protocols/D2Messages.hpp` | Fabryka surowych poleceń bootloadera |
| `D2ProtocolCommonSteps` | `Common/common/protocols/D2ProtocolCommonSteps.hpp` | Wysokopoziomowe sekwencje bootloadera |
| `D2Error` | `Common/common/protocols/D2Error.hpp` | Wyjątek błędu D2 |

Zależności: `D2Message → CanMessage`; `D2Request → D2Message, ICanChannel`; `D2ProtocolCommonSteps → D2Message, D2Messages, ICanChannel, VBF`. `D2RawMessages` tworzy surowe `CanFrame`, bez użycia `D2Message`.

- **D2Message** serializuje dane logiczne do ramek, dobierając prefiksy i `seriesId`.
- **D2Request** wysyła ramki, odbiera odpowiedź, waliduje echo i wykrywa błędy `0x7F`.
- **D2ProtocolCommonSteps** koordynuje sekwencje bootloadera.
- **D2RawMessages** tworzy gotowe ramki bootloadera w formacie `[ecuId, command, params...]`.

## Znane problemy i poprawki

### Niepełny model nagłówków odpowiedzi — poprawiono

Początkowo parser obsługiwał tylko `0x88..0x8F`, `0xC8..0xCF), serię `0x09..0x0F,0x08) i koniec `0x48..0x4F`. Rzeczywista odpowiedź CEM na `B9 FB` zaczynała się od `0x97` i kontynuowała przez `0x11..0x17,0x10`, co wywoływało błąd. Dodano flagę serii, wybór bazy numeracji z pierwszej ramki i obsługę powtórzonej pierwszej ramki `0x8F`. Starsze formaty nadal działają. Testy: `RealCemVinResponseFromVidaLog` i `LastFrameWithSeriesFlagIsNotTheEnd`.

### Inkrementacja seriesId — poprawiono

Wcześniej licznik był zwiększany za wcześnie, więc pierwsza ramka środkowa miała `0x0A` zamiast `0x09`. Prawidłowa sekwencja to `0x09 → 0x0A → …`.

### Kolejność params i requestId w createPayload — poprawiono

W `D2Message.cpp` zmieniono wstawianie parametrów z początku wektora na koniec. Poprawny układ to `[ecuId, requestId..., params...]`.

### Obsługa błędów w surowych ramkach

`writeMessagesAndCheckAnswer` porównuje echo odpowiedzi z oczekiwanymi bajtami i nie interpretuje błędów diagnostycznych D2. Tak ma pozostać: bootloader używa innego formatu odpowiedzi niż diagnostyczny marker `0x7F`, który obsługuje `D2Request::process`.

### Wysyłanie poleceń diagnostycznych poza D2Message/D2Request — poprawiono

Wcześniej niektóre polecenia diagnostyczne były wysyłane jako surowe ramki, bez prawidłowego prefiksu, segmentacji multi-frame i obsługi odpowiedzi. Poprawiono m.in. `setDIMTime`, `D2ReaderChecksum` i `D2ReaderME7`, aby korzystały z fabryk wiadomości. Naprawiono też błąd użycia zakresu `{begin(), end()}`.

## Różnice między wysyłaniem i odbiorem

| Pole | Żądanie | Odpowiedź |
|---|---|---|
| `[0]` | Prefiks / seriesId | Nagłówek |
| `[1]` | `ecuId` | Echo `ecuId` |
| `[2]` | `requestId[0]` | `requestId[0] + 0x40` |
| `[3..]` | Pozostałe `requestId` i parametry | Echo pozostałego ID, potem dane |
| Flaga `0x10` | Nie używana | Używana w wieloramkowych odpowiedziach tekstowych CEM |

To różnica protokołu, a nie błąd.

## D2 a protokół bootloadera

Oba używają CAN ID `0xFFFFE`, ale są odrębnymi protokołami.

| Cecha | D2 — diagnostyka | Bootloader — programowanie |
|---|---|---|
| Format | Wieloramkowy, prefiksy i seriesId, echo requestId | Jedna ramka: `[0]=ecuId, [1]=command, [2..7]=params` |
| Multi-frame | Tak | Nie |
| Echo requestId | Tak, `requestId[0] + 0x40` | Nie; sprawdzane jest echo bajtów polecenia |
| Obsługa błędów | `D2Request::process`, marker `0x7F` | Brak odrębnego parsera błędów |
| Użycie | `D2Request`, `D2Messages`, czytniki D2, Logger | `D2ProtocolCommonSteps` |
| Budowanie ramki | `D2Message::getFrames()` | `makeBootloaderFrame` / `D2RawMessages` |

**Nie używaj `D2Message` ani `D2Request` do poleceń bootloadera** — format i reguły są inne.

## Zrealizowane zmiany: przejście na D2Message / D2Request

Celem było użycie `D2Message` do budowania ramek i `D2Request` do wysyłania/odbierania diagnostyki; polecenia bootloadera pozostają surowymi ramkami.

| Zmiana | Status |
|---|---|
| Scentralizowano CAN ID w `D2Message::CanId = 0xFFFFE` | ✓ |
| `makeD2RawFrame` przemianowano na `makeBootloaderFrame` | ✓ |
| `setDIMTime` korzysta z `D2Messages::setCurrentTime` | ✓ |
| `D2ReaderChecksum` korzysta z `D2Message::makeD2RawMessage` | ✓ |
| `D2ReaderME7` korzysta z `D2Messages::createReadOffsetMsg2` | ✓ |
| Naprawiono błędne użycie zakresu `{begin(), end()}` | ✓ |
| `D2Message::getFrames()` zwraca `vector<CanFrame>` | ✓ |
| `CanMessage` ma czysto wirtualne `getFrames()` | ✓ |
| `D2RawMessages` tworzy polecenia bootloadera | ✓ |
| Uproszczono `generateCanFrames` do jednej formuły prefiksu | ✓ |
| Naprawiono sekwencję `seriesId` | ✓ |

## Specyfikacja testów D2Message i D2Request

### Cel i infrastruktura

Testy serializacji `D2Message` i obsługi odpowiedzi `D2Request` korzystają z Boost.Test oraz `MockICanChannel`.

Pliki: `Common/test/D2MessageTest.cpp`, `Common/test/D2RequestTest.cpp` i konfiguracja CMake. Typowe asercje: `BOOST_CHECK_EQUAL`, `BOOST_CHECK`, `BOOST_CHECK_THROW`, `BOOST_REQUIRE`.

### Testy D2Message

Powinny obejmować:
- konstruktory raw, ustrukturyzowane oraz move;
- gettery `getEcuId()` i `getRequestId()`, w tym wartości dla raw;
- serializację single-frame dla 1 i 7 bajtów, prefiks `0xC8 + dataSize), ECU ID, requestId, parametry i zerowe dopełnienie;
- serializację multi-frame dla 8, 14, 15 i 57 bajtów;
- prefiksy pierwszej, środkowej i ostatniej ramki;
- sekwencję `seriesId` `0x09..0x0F), zawijanie `0x0F → 0x08 → 0x09) i brak luki w pierwszym identyfikatorze środkowym;
- przypadki graniczne: dokładnie 7 bajtów, 8 bajtów, puste parametry i samo requestId.

### Testy D2Request

Powinny obejmować:
- poprawne odpowiedzi jedno- i wieloramkowe;
- kontrolę echa ECU ID i requestId;
- niezgodne echo, długie echo rozłożone na ramki oraz echo na granicy ramki;
- odpowiedzi błędów D2 i niezgodne ECU ID;
- timeout, puste/krótkie ramki;
- niestandardowy timeout i opóźnienie między wysyłanymi ramkami;
- scenariusze integracyjne serializacji i parsowania oraz długiego requestId.

### CMake i status

Testy korzystają z C++20, Boost.Test, easyloggingpp oraz `MockICanChannel`. Cel `CommonTests` uruchamia `D2MessageTest.cpp` i `D2RequestTest.cpp`, a test CTest nazywa się `D2Common`.

Status opisany w źródle: testy `D2MessageTest` (18 przypadków), `D2RequestTest` i mock są zaimplementowane; kompilacja Release kończy się bez błędów.
