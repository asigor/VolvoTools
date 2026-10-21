# Specyfikacja: utwardzenie parsera odpowiedzi w `D2Request::process()`

## 1. Cel

Usunąć błędy i podatności parsera odpowiedzi protokołu D2 w `Common/src/protocols/D2Request.cpp`: nieskończoną pętlę, brak walidacji `seriesId` i niezdefiniowane zachowanie przy pustym `requestId`. Zaostrzyć parsowanie tak, aby naruszenia protokołu po rozpoznaniu odpowiedzi dla właściwego żądania powodowały wyjątek. Przebudować pętlę na jawny automat stanów i pokryć nowe zachowanie testami. Publiczne API `process()` **pozostaje bez zmian**.

## 2. Architektura

### 2.1. Format odpowiedzi

Pierwsza ramka ma 8 bajtów CAN: 1 bajt nagłówka i 7 bajtów danych.

```
[0] = header
[1] = ecuId                         // musi zgadzać się z żądanym
[2] = requestId[0] + 0x40           // musi zgadzać się z żądanym
[3..2+restRequestSize] = requestId[1..]
pozostałe bajty = dane odpowiedzi
```

Gdzie `restRequestSize = requestId.size()-1`.

| Bity nagłówka | Zakres / sekwencja | Znaczenie |
|---|---|---|
| `0x80=1, 0x40=1, 0x10=0` | `0xC8..0xCF` | Pojedyncza ramka (pierwsza i ostatnia) |
| `0x80=1, 0x40=0, 0x10=0` | `0x88..0x8F` | Pierwsza ramka serii wieloramkowej |
| `0x80=0, 0x40=1, 0x10=0` | `0x48..0x4F` | Ostatnia ramka serii; długość = `header - 0x48` |
| `0x80=0, 0x40=0, 0x10=0` | `0x09→…→0x0F→0x08→…` | Ramka pośrednia, baza seriesId `0x08` |
| `0x80=1, 0x40=0, 0x10=1` | `0x90..0x97` | Pierwsza ramka serii CEM; `0x97` oznacza 7 bajtów |
| `0x80=0, 0x40=0, 0x10=1` | `0x11→…→0x17→0x10→…` | Ramka pośrednia, baza seriesId `0x10` |
| `0x80=0, 0x40=1, 0x10=1` | `0x50..0x57` | Koniec podbloku, **nie** koniec odpowiedzi |

Długość danych w pierwszej/ostatniej ramce wynosi `8 + len` bez flagi serii, a przy fladze serii — samo `len`. Odpowiedź kończy wyłącznie ramka z bitem `0x40`, ale bez flagi serii.

**Ograniczenia formatu:**
- Echo `requestId` zajmuje `requestId.size() + 1` bajtów strumienia i może wykraczać poza pierwszą ramkę CAN. Przy długim `requestId` echo jest kontynuowane w kolejnych ramkach; dane właściwe zaczynają się dopiero po zebraniu całego echa. Nie ma limitu długości `requestId`.
- Każda ramka serii przenosi najwyżej 7 bajtów danych.

**Format błędu:** rzeczywista odpowiedź negatywna ma marker `0x7F) w `data[2]`, następnie echo numeru usługi `requestId[0]` i kod błędu. Pełne echo `requestId` nie występuje. Region echa ma więc 3 bajty strumienia: `[ecuId, 0x7F, requestId[0]]`; kod błędu jest w `result[3]`. Starsza funkcja `checkD2Error` sprawdzała inne pozycje i nie wykrywała błędu dla długiego `requestId`.

### 2.2. Stan przed zmianą

Parser wysyłał wszystkie ramki żądania, a następnie odbierał odpowiedzi w `while(true)`. Sprawdzał echo w pierwszej ramce, pomijał niedopasowane ramki i kopiował dane do wyniku. Flagi `firstMessage`/`inSeries` oraz brak limitów pozwalały na nieskończone zbieranie danych.

### 2.3. Stan po zmianie

1. Puste `requestId` powoduje wyjątek.
2. Odpowiedź jest parsowana jako automat stanów `WaitFirst` → `WaitSeries`.
3. Pierwsza ramka musi zawierać marker ECU i poprawny nagłówek.
4. Echo jest zbierane i walidowane w kolejnych ramkach.
5. `seriesId` jest sprawdzany i inkrementowany cyklicznie.
6. Obowiązują limity rozmiaru odpowiedzi i liczby ramek.
7. Wynik zawiera wyłącznie payload po usunięciu echa.

**Komponenty:**

| Komponent | Plik | Rola |
|---|---|---|
| `D2Request` | `Common/src/protocols/D2Request.cpp` | Odbiór, składanie echa, obsługa błędów |
| `Util` | `Common/src/Util.cpp` | Usunięto `checkD2Error`; detekcja błędów jest w parserze |
| Testy | `Common/test/D2RequestTest.cpp` | Testy Boost.Test, `-DBUILD_TESTS=ON` |
| `D2Message` | `Common/src/protocols/D2Message.cpp:28-53` | Źródło reguł ramek; bez zmian |
| Dokumentacja | `docs/tech_specs/d2_protocol_implementation.md` | Synchronizacja opisu formatu odpowiedzi i błędów |

## 3. Zidentyfikowane problemy

| Nr | Problem | Rozwiązanie |
|---|---|---|
| 1 | `while(true)` bez limitu ramek i rozmiaru; uszkodzony strumień mógł bez końca powiększać wynik | Limity `maxResponseSize` i `maxFrameCount` |
| 2 | Brak walidacji `seriesId`; utrata, duplikacja lub zmiana kolejności ramek cicho uszkadzała dane | Błędny identyfikator serii powoduje wyjątek |
| 3 | Brak walidacji pierwszego nagłówka; ramka pośrednia mogła być potraktowana jako początkowa | Wymagany bit `0x80` i dozwolone zakresy nagłówków |
| 4 | Puste `requestId` powodowało underflow `size_t` i odczyt `requestId[0]` poza zakresem | Walidacja we wszystkich konstruktorach |
| 5 | Echo długiego `requestId` nie było składane między ramkami | Inkrementalne składanie echa |
| 6 | Martwy warunek `response.data.size() < 1` i nieczytelne flagi sterujące | Jawny automat stanów |
| 7 | Niezgodny format błędu między implementacją, specyfikacją i testami | Usunięto `checkD2Error`; detekcja jest w parserze i zgodna z zachowaniem sprzętu |
| 8 | Sprawdzanie CAN ID odpowiedzi blokowało prawidłowy ruch | Sprawdzanie usunięto: rzeczywiste odpowiedzi D2 mogą mieć ID różne od `0xFFFFE` |

## 4. Zrealizowane kroki

### Krok 1 — walidacja pustego `requestId`

We wszystkich czterech konstruktorach `D2Request` pusty identyfikator żądania powoduje `std::invalid_argument("D2Request: empty requestId")`. Nie ma ograniczenia długości — echo jest składane z wielu ramek.

### Krok 2 — limity odpowiedzi

- `maxResponseSize = 64 * 1024` — maksymalnie 64 KiB wyniku;
- `maxFrameCount = maxResponseSize / 7 + 4` — maksymalna liczba odebranych ramek;
- przekroczenie limitu powoduje `std::runtime_error("D2 response too large")` lub `std::runtime_error("too many frames")`.

Czytniki D2 pobierają dane porcjami po 132 bajty, co odpowiada około 20 ramkom serii na żądanie. Limit 64 KiB zapewnia duży zapas dla rzeczywistych odpowiedzi.

### Krok 3 — walidacja `seriesId`

Po pierwszej ramce serii oczekiwany identyfikator wynosi `0x09`. Każda ramka pośrednia musi mieć dokładnie oczekiwany nagłówek; w przeciwnym razie rzucany jest wyjątek. Identyfikator zmienia się cyklicznie: `0x09 → … → 0x0F → 0x08 → …`. Dla ostatniej ramki dopuszczalny jest zakres `0x48..0x4F`, a zadeklarowana długość musi mieścić się w ramce.

### Krok 4 — walidacja pierwszej ramki

- Bit `0x80` musi być ustawiony.
- `data[1]` musi odpowiadać `ecuId`.
- `data[2]` musi być równe `0x7F` albo `requestId[0] + 0x40`.
- Niedopasowany marker oznacza obcy ruch i prowadzi do `continue`.
- Po rozpoznaniu markera nagłówek musi należeć do `0x88..0x8F` lub `0xC8..0xCF`; dla nieprawidłowego nagłówka rzucany jest wyjątek.
- Odpowiedź jednoramkowa musi zawierać całe echo.

### Krok 5 — semantyka pomijania i wyjątków

CAN ID nie jest sprawdzany: rzeczywiste odpowiedzi D2 mogą przychodzić z identyfikatorem innym niż `0xFFFFE`, a kontrola blokowała poprawny ruch. Niedopasowany marker, pusta lub zbyt krótka ramka oraz obcy ruch są pomijane. Naruszenia wykryte po rozpoznaniu odpowiedzi dla właściwego ECU, przekroczenia limitów, błędny `seriesId` i timeout powodują wyjątek.

### Krok 6 — automat stanów i składanie echa

Parser używa stanów `WaitFirst` i `WaitSeries`. Dane wszystkich ramek są dołączane do jednego bufora. Echo jest walidowane przyrostowo, a po zakończeniu odpowiedzi usuwane jednym `result.erase`. Zmniejsza to złożoność kodu i unika kopiowania całego payloadu. Błędy odbioru i parsowania są logowane przez `LOG_MODULE(ERROR)`.

### Krok 7 — rzeczywisty format odpowiedzi negatywnej

Potwierdzone na sprzęcie: odpowiedź błędu ma marker `0x7F` w `data[2]), po nim echo numeru usługi `requestId[0]) i kod błędu. Format: `[header, ecuId, 0x7F, requestId[0], code]`. Region echa ma 3 bajty strumienia, a kod jest w `result[3]`. Gdy kodu brak, rzucany jest `D2Error(0)`. Usunięto `checkD2Error`, a testy i dokumentacja zostały zsynchronizowane.

### Krok 8 — testy

Dodano testy obejmujące:
- serie dłuższe niż 8 ramek i zawijanie `seriesId`;
- brakujące, zduplikowane i przestawione ramki;
- brak ramki końcowej;
- przekroczenie limitów rozmiaru/liczby ramek;
- pusty `requestId`;
- długie echo rozłożone na kilka ramek i echo kończące się na granicy ramki;
- odpowiedź jednoramkową z niepełnym echem;
- serię kończącą się przed zakończeniem echa;
- odpowiedzi negatywne z długim `requestId` oraz bez bajtu kodu błędu;
- prawidłową odpowiedź z CAN ID różnym od `0xFFFFE`;
- nagłówki pierwszej ramki bez wymaganego bitu.

Wszystkie przypadki mają być uruchamiane z `-DBUILD_TESTS=ON`.

### Krok 9 — aktualizacja dokumentacji

Zaktualizowano opis formatu odpowiedzi i błędów w `d2_protocol_implementation.md`, a także listy specyfikacji w plikach projektu.

### Krok 10 — obsługa flagi serii w odpowiedziach CEM

Rzeczywisty ruch CEM wykorzystuje flagę serii `0x10`: pierwsza ramka może mieć nagłówek `0x97`, a dalsze `0x11..0x17` i `0x10`. Starszy parser znał tylko format `0x88..0x8F` oraz `0x09..0x0F), przez co odrzucał odpowiedzi.

- Bazę numeracji serii wyznacza flaga w pierwszej ramce: `0x08` lub `0x10`.
- Koniec odpowiedzi stanowi wyłącznie ramka z bitem końcowym bez flagi serii. Nagłówek `0x55) oznacza koniec podbloku, a nie całej odpowiedzi.
- Długość danych jest kodowana inaczej zależnie od flagi serii.
- Powtórzona pierwsza ramka w obrębie serii może być pominięta jako duplikat z dopełnieniem `0xFF`.
- Dodano testy oparte na rzeczywistym logu odczytu VIN CEM.

## 5. Pliki objęte zmianami

| Plik | Zakres |
|---|---|
| `Common/src/protocols/D2Request.cpp` | Walidacja, limity, automat stanów, format błędów i obsługa obu formatów nagłówków |
| `Common/src/Util.cpp`, `Common/common/Util.hpp` | Usunięcie `checkD2Error` |
| `Common/test/D2RequestTest.cpp` | Testy regresyjne i przypadki brzegowe |
| `Flasher/test/MockICanChannel.hpp` | Dostosowanie mocka do interfejsu `ICanChannel` |
| `docs/tech_specs/d2_protocol_implementation.md` | Synchronizacja specyfikacji |
| `docs/tech_specs/d2_request_parser_hardening.md` | Ten dokument |

## 6. Kryteria akceptacji

1. Puste `requestId` powoduje `std::invalid_argument`, bez niezdefiniowanego zachowania.
2. Echo `requestId` dowolnej długości jest składane z kolejnych ramek.
3. Przekroczenie `maxResponseSize` lub `maxFrameCount` powoduje `std::runtime_error`.
4. Nieprawidłowy, utracony lub zduplikowany `seriesId` powoduje wyjątek.
5. Nieprawidłowa pierwsza ramka po rozpoznaniu markera powoduje wyjątek.
6. Obcy ruch jest pomijany; CAN ID odpowiedzi nie jest sprawdzany.
7. Błąd D2 jest wykrywany po markerze `0x7F` i echo numeru usługi, bez zależności od długości `requestId`.
8. Seria dłuższa niż 8 ramek jest prawidłowo parsowana.
9. Nowe i istniejące testy przechodzą z `-DBUILD_TESTS=ON`.
10. Zachowano działanie odpowiedzi jedno- i wieloramkowych.
11. Publiczna sygnatura `D2Request::process()` nie uległa zmianie.
12. Projekt kompiluje się bez błędów.
13. Parser obsługuje oba formaty nagłówków; odpowiedź kończy się wyłącznie ramką końcową bez flagi serii.

## 7. Uwagi implementacyjne

1. Pusty `requestId` jest weryfikowany we wszystkich czterech konstruktorach, aby błąd występował przed wysłaniem żądania. Limit długości usunięto.
2. Format błędu potwierdzono podczas debugowania sprzętowego. Pełne echo `requestId` nie występuje w odpowiedzi negatywnej; `checkD2Error` usunięto.
3. `MockICanChannel.hpp` dostosowano do aktualnego interfejsu; wcześniej brak przeciążenia `receive` uniemożliwiał kompilację testów.
4. W teście `MultiFrameResponse2Frames` nagłówek ostatniej ramki poprawiono z `0x49` na `0x4A`, ponieważ deklarował wcześniej niezgodną długość.
5. Test długiego echa zastąpiono testami echa rozłożonego na ramki, echa na granicy ramki, identyfikatora pięciobajtowego i odpowiedzi błędu dla długiego ID.
6. Ostateczna implementacja gromadzi cały strumień w `result`, sprawdza echo przyrostowo i usuwa je jednym `erase`. Zachowanie parsera pozostaje takie samo.
7. Nie sprawdza się CAN ID odpowiedzi: testy wykazały, że rzeczywiste odpowiedzi D2 mają ID różne od `0xFFFFE`. Filtracja jest protokołowa: nagłówek, marker, echo i `seriesId`.
8. Odczyt TF80 wykorzystuje dłuższy `requestId`, którego echo przechodzi przez kilka ramek. Odczyt na rzeczywistym sprzęcie potwierdził, że ECU zwraca pełne echo adresu; usunięto wcześniejsze ręczne przesunięcia danych.
9. Flaga serii `0x10) została dodana na podstawie rzeczywistych odpowiedzi CEM. Starsze formaty nadal są obsługiwane; powtórzona pierwsza ramka z dopełnieniem `0xFF` jest pomijana jako duplikat.
