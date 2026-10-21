# Specyfikacja: częściowe programowanie UDS — wybór bloków do kasowania i zapisu (`calculateBlocksToWrite`)

## 1. Cel

Nie kasować ani nie zapisywać całego obrazu flash, jeśli część bloków w ECU jest już zgodna z obrazem VBF. Sumy kontrolne bloków oblicza sam ECU (routine `0x31 01 FF 01`). Na podstawie różnic tworzony jest **plan** zakresów do skasowania i bloków do zapisania.

Plan powstaje iteracyjnie: kasowanie zakresu niszczy znajdujące się w nim bloki, więc trzeba je ponownie zapisać; może to z kolei wymagać skasowania kolejnych zakresów. Iteracje trwają do ustabilizowania zbiorów, a następnie cały plan jest wykonywany.

Implementacja: `Flasher/src/UDSFlasher.cpp` (`UDSFlasherImpl::calculateBlocksToWrite()` i funkcje pomocnicze); dodano stan HFSM `CheckFlash` pomiędzy `StartBootloader` a `EraseFlash`.

Publiczne API `UDSFlasher` i `UDSProtocolCommonSteps` pozostaje bez zmian.

## 2. Model danych i terminologia

| Termin | Typ / źródło | Znaczenie |
|---|---|---|
| **Blok firmware** (chunk) | `common::VBFChunk{writeOffset, data, crc}`, `_config.flash.chunks` | Jednostka zapisu: `transferChunk` wykonuje dla bloku `0x34` → `0x36`×N → `0x37`. |
| **Zakres kasowania** (erase candidate) | `common::DataBlock{startAddr, length}`, `_eraseCandidates` | Jednostka kasowania: `eraseChunk` wysyła `0x31 01 FF 00` z adresem i długością. |
| **CRC bloku** | `chunk.crc` (`uint32_t`, używane niższe 16 bitów) | Oczekiwana CRC16; dla VBF pochodzi z pliku, a dla HEX/bin jest liczona przez `common::crc16()`. |
| **CRC w ECU** | `0x31 01 FF 01`, odpowiedź `0x71 01 FF 01 10 <crc_hi> <crc_lo>` | Ta sama wartość, którą `transferChunk` przekazuje w TransferExit `0x37`. |
| `W[i]` | `std::vector<bool> writeNeeded` | Czy blok `i` trzeba zapisać. |
| `S[j]` | `std::vector<bool> eraseNeeded` | Czy zakres `j` trzeba skasować. |

### Źródło zakresów kasowania

Funkcja `buildEraseCandidates`:
1. używa `VBFHeader::eraseBlocks`, jeśli lista nie jest pusta (rzeczywiste obrazy Volvo VBF oraz obrazy generowane przez `VBFUtil.cpp` dla ME7/ME9P1/ME9P3/DEMGen2/DEMGen3/DensoP3Restyling);
2. w przeciwnym razie tworzy zakresy 1:1 na podstawie adresów i długości bloków firmware (DensoP3, AW55, TF80 oraz obrazy HEX/bin bez `eraseBlocks`).

Zakresy są **atomowe** — nie wolno ich dzielić, ponieważ nie znamy granic sektorów po stronie ECU. Porównuje się półotwarte przedziały `[startAddr, startAddr + length)` w `uint64_t`; zakresy stykające się końcami nie zachodzą na siebie.

| Obraz | Relacja bloków do zakresów | Przykład |
|---|---|---|
| ME7, ME9P1, ME9P3, DensoP3Restyling | 1:1 | 4 bloki ↔ 4 zakresy |
| DEMGen2 | 1:N | blok `0x10000+0x30000` zachodzi na 3 zakresy |
| DEMGen3 | 1:N | blok `0x18000+0x38000` zachodzi na 4 zakresy |
| DensoP3, AW55, TF80, HEX | Zakresy wyprowadzane z bloków | Z definicji 1:1 |

## 3. Niezmienniki planu

Wynikowe zbiory `W` i `S` muszą spełniać:

1. **Każdy zapisywany blok leży w skasowanym obszarze.** Jeśli `W[i]` jest prawdą, to każdy zakres `j` zachodzący na blok `i` musi mieć `S[j]`.
2. **Każdy skasowany obszar pokryty blokami jest odtwarzany.** Jeśli `S[j]` jest prawdą, każdy blok `i` zachodzący na zakres `j` musi mieć `W[i]`.
3. Niepokrytych przez bloki fragmentów skasowanego zakresu nie da się odtworzyć; jest to dopuszczalne i należy je odnotować w logu.
4. **Minimalność i determinizm:** zbiory stanowią najmniejszy punkt stały i nie zależą od kolejności przeglądania elementów.

W skrócie: dla każdej pary „blok i zakres”, które na siebie zachodzą, musi zachodzić `W[i] == S[j]`.

## 4. Algorytm

```
calculateBlocksToWrite():
    stateUpdater(CheckFlash)

    jeśli tryb częściowy jest niemożliwy (brak zakresów / zakres o długości zero):
        applyFullRewrite()      # skasuj wszystkie zakresy i zapisz wszystkie bloki
        return

    # 1. Odpytaj ECU — to jedyne źródło informacji o zgodności bloków.
    dla każdego bloku i:
        clearRx()
        ok, crc = getChunkCRC16(block_i)       # 0x31 01 FF 01
        W[i] = !ok || crc != (chunk_i.crc & 0xFFFF)
        zaloguj wynik DEBUG; zlicz różnice i błędy
    zaloguj INFO: flash check finished
    S = ∅

    # 2. Rozszerzaj zbiory do punktu stałego (maks. n + m + 1 iteracji).
    powtarzaj:
        changed = false
        # 2a. Każdy blok do zapisu wymaga skasowania wszystkich zachodzących zakresów.
        dla każdego W[i]:
            dla każdego zachodzącego zakresu j:
                jeśli !S[j]: S[j] = true; changed = true
        # 2b. Skasowanie zakresu niszczy wszystkie bloki, które na niego zachodzą.
        dla każdego S[j]:
            dla każdego zachodzącego bloku i:
                jeśli !W[i]: W[i] = true; changed = true
    dopóki changed

    # 3. Sprawdź niezmienniki. Naruszenie → ERROR i pełny zapis.
    # 4. Zastosuj plan: applyPlan(W, S).
```

**Zbieżność:** obie reguły tylko dodają elementy. Przestrzeń jest skończona (`n+m` bitów), a każda zmieniona iteracja zwiększa `|W|+|S|`. Zatem potrzeba najwyżej `n+m` zmian; końcowy punkt stały jest minimalny i deterministyczny. Złożoność wynosi O((n+m)·n·m); dla rzeczywistych obrazów, gdzie `n,m ≤ 30`, koszt jest pomijalny.

### Zastosowanie planu

- `_blocksToWrite = W`;
- `_blocksToErase` zawiera wyłącznie zakresy wybrane przez `S[j]`, w kolejności rosnących adresów;
- każdy zakres jest kasowany osobnym żądaniem; zakresy stykające się nie są łączone;
- log INFO podsumowuje liczbę i rozmiar zakresów oraz bloków;
- `logUncoveredAreas` raportuje fragmenty zakresów niepokryte blokami;
- maksimum postępu jest przeliczane na `SBL + suma rozmiarów bloków, dla których W[i] == true`.

### Powrót do pełnego zapisu

`applyFullRewrite` wybiera wszystkie bloki i wszystkie zakresy, jeśli:
- `_eraseCandidates` jest pusty — `WARNING no erase ranges declared, writing everything`;
- któryś zakres ma długość zero — `WARNING erase range 0x... has zero length, writing everything`;
- zamykanie zbiorów nie zbiega w `n + m + 1` iteracjach — `ERROR flash plan did not converge in N iterations`;
- niezmiennik jest naruszony — `ERROR flash plan invariant is broken ...`.

**Bezpieczna wartość domyślna:** `_blocksToWrite` początkowo zawiera same wartości `true`, a `_blocksToErase` wszystkie kandydaty. Algorytm zawęża te zbiory, więc wyjątek, nieobsługiwana procedura CRC albo wyłączenie trybu częściowego prowadzi do pełnego kasowania i zapisu, a nie do niekompletnego programowania.

## 5. Integracja

### 5.1. Położenie w HFSM

```
KeepAlive → FallAsleep → StartProgrammingSession → Authorize → LoadBootloader
          → StartBootloader → CheckFlash → EraseFlash → WriteFlash → CheckValidApplication
```

`CheckFlash` (`UDSFlasherImpl::calculateBlocksToWrite()`) znajduje się po `StartBootloader`, ale przed `EraseFlash`. Procedura CRC `0xFF01` jest dostępna po uruchomieniu SBL, a odczyt musi zakończyć się przed pierwszym kasowaniem. CLI pokazuje stan `FlasherState::CheckFlash` jako `Checking flash`.

### 5.2. Wykorzystanie planu

| Metoda | Zastosowanie |
|---|---|
| `eraseFlash()` | Kasuje tylko `_blocksToErase`; log DEBUG zawiera adres i długość każdego zakresu. |
| `writeFlash()` | Pomija bloki z `_blocksToWrite[i] == false`; log DEBUG informuje o pominięciu aktualnego bloku. |
| `getMaximumProgress()` | Początkowo zachowuje dotychczasowe obliczenie SBL + cały obraz; wartość jest ustawiana przed FSM. |

### 5.3. Postęp

Po sprawdzeniu CRC maksimum jest ponownie obliczane jako `SBL + rozmiar bloków wybranych do zapisu`. Ponieważ `incCurrentProgress` jest wywoływane tylko dla faktycznie zapisanych bajtów, pasek postępu osiąga 100%. Jeśli nic się nie różni, maksimum odpowiada objętości SBL, więc postęp kończy się prawidłowo.

### 5.4. Odczyt CRC — `UDSProtocolCommonSteps::getChunkCRC16`

- Przed żądaniem wywoływane jest `channel.clearRx()`, aby stara odpowiedź nie została uznana za odpowiedź na bieżące zapytanie.
- Format odpowiedzi: `71 01 <routineId> <szerokość CRC w bitach> <dane>`; dla CRC16: `71 01 FF 01 10 <crc_hi> <crc_lo>`. Bajt `0x10` oznacza 16 bitów, a następne dwa bajty zawierają CRC w kolejności big-endian.
- Poprzednia implementacja czytała `response[4..5]`, czyli szerokość i starszy bajt CRC. Na sterowniku Denso P3 po liftingu powodowało to fałszywe wykrywanie różnic we wszystkich 24 blokach zamiast w dwóch.
- Sprawdzane są `response.size() >= 7` i cały prefiks `71 01 FF 01 10`. Niezgodność powoduje wpis ERROR z zrzutem odpowiedzi i wynik `false`, przez co blok zostaje zapisany. Inna szerokość CRC jest odrzucana.
- Sparsowana wartość i surowa odpowiedź są zapisywane na poziomie DEBUG.
- Timeout wynosi 1000 ms, jak wcześniej. Odpowiedź negatywna ECU oznacza `false`, więc blok jest zapisywany. NRC `0x78` (Response Pending) nie jest błędem — `UDSRequest::process` czeka na odpowiedź końcową.

## 6. Przypadki brzegowe

| Sytuacja | Zachowanie |
|---|---|
| Wszystkie bloki są zgodne | `S = ∅`, `W = ∅`; nic nie jest kasowane ani zapisywane, wykonywane jest `checkValidApplication`, postęp 100%. |
| Jeden blok zachodzi na kilka zakresów | Kasowane są wszystkie zachodzące zakresy (DEMGen2/Gen3). |
| Zakres obejmuje kilka bloków, z których część była zgodna | Zgodne bloki również są zapisywane, jeśli kasowanie je zniszczy. |
| Ponownie dodany blok zachodzi na kolejny zakres | Ten zakres zostaje dodany w następnej iteracji. |
| Żaden blok nie zachodzi na zakres | Zakres nie jest wybierany do kasowania. |
| Blok nie zachodzi na żaden zakres | Jeśli się różni, jest zapisywany bez kasowania. |
| Zakres jest pokryty blokami tylko częściowo | Pozostały fragment jest kasowany i zgłaszany w INFO; np. końcówki bloku Denso nie wolno zapisywać poza granicą obrazu. |
| Odczyt CRC bloku się nie powiódł | Blok uznaje się za różny i zapisuje. |
| Odczyt CRC nie działa dla żadnego bloku | Wszystkie bloki trafiają do zapisu i odpowiednie zakresy do kasowania — pełny zapis jak przed wprowadzeniem mechanizmu. |
| Obraz nie zawiera bloków | Puste zbiory; nic nie jest wykonywane. |

## 7. Logowanie

| Poziom | Przykład / komunikat | Znaczenie |
|---|---|---|
| DEBUG | `flash block 0x... - crc 0x... matches, skip` | Blok zgodny |
| DEBUG | `flash block 0x... - crc 0x... != expected 0x..., will write` | Blok różny |
| DEBUG | `flash block 0x... - crc request failed, will write` | Nie udało się odczytać CRC |
| DEBUG | `getChunkCRC16 response: ... -> crc 0x...` | Surowa odpowiedź i wynik CRC |
| INFO | `flash check finished: N blocks, K differ, F crc requests failed` | Podsumowanie sprawdzania |
| WARNING | `all N blocks differ from the image on the ECU...` | Wszystkie bloki różne; możliwy pusty ECU lub niezgodna procedura CRC |
| INFO | `flash plan: erase X of Y ranges (B bytes), write K of N blocks (B bytes)` | Podsumowanie planu |
| INFO | `erase range ... is not covered by flash blocks...` | Fragment bez odpowiadającego bloku |
| DEBUG | `erase range 0x..., size N` / `flash block ... is up to date, skip writing` | Wykonanie planu |
| WARNING | Brak zakresów lub długość zero | Tryb częściowy wyłączony |
| ERROR | Brak zbieżności, naruszony niezmiennik lub nieoczekiwana odpowiedź CRC | Bezpieczny powrót do pełnego zapisu |

Logowanie odbywa się w module `flasher` (`UDSFlasher.cpp`) i `common` (`UDSProtocolCommonSteps.cpp`). Poziom DEBUG włącza `VOLVOLOG_DEBUG=flasher,common`.

## 8. Przykłady

**Relacja 1:1 (ME9P3, 4 bloki i 4 zakresy).** Różni się tylko pierwszy blok. Po zbieżności plan kasuje 1 z 4 zakresów i zapisuje 1 z 4 bloków.

**Relacja 1:N (DEMGen2).** Blok `0x10000+0x30000` zachodzi na zakresy zaczynające się pod `0x10000`, `0x20000` i `0x30000`, każdy o długości `0x10000`. Jeśli blok jest różny, wszystkie trzy zakresy są kasowane. Niezależny blok `0x6000+0x2000` nie jest dotykany.

**Kasowanie obejmuje blok wcześniej zgodny.** Zakres `0x10000+0x20000` obejmuje blok A (`0x10000+0x10000`, różny) i B (`0x20000+0x10000`, zgodny). Ponieważ kasowanie niszczy B, plan dodaje go do zapisu. Ostatecznie kasowany jest zakres i zapisywane są A oraz B.

**Przekroczenie granicy zakresów.** Różny blok C (`0x1F000+0x2000`) przecina zakresy `0x10000+0x10000` i `0x20000+0x10000`; zgodny blok B (`0x20000+0x1000`) zostaje dodany do zapisu, ponieważ jest niszczony przez kasowanie.

## 9. Pliki

| Plik | Zmiana |
|---|---|
| `Flasher/src/UDSFlasher.cpp` | Typy zakresów, wyznaczanie kandydatów do kasowania, plan częściowego zapisu, maski kasowania/zapisu, stan HFSM `CheckFlash`, callback maksimum postępu |
| `Common/src/protocols/UDSProtocolCommonSteps.cpp` | `getChunkCRC16`: czyszczenie RX przed żądaniem i walidacja odpowiedzi |
| `VolvoFlasher/src/VolvoFlasher.cpp` | Wyświetlanie stanu `CheckFlash` jako „Checking flash” |
| `docs/tech_specs/uds_partial_flash.md` | Dokumentacja mechanizmu |
| `AGENTS.md` | Lista specyfikacji technicznych |

## 10. Kryteria akceptacji

1. Plan kasowania i zapisu jest iteracyjnie rozszerzany do punktu stałego i spełnia niezmienniki z §3.
2. Zgodne bloki nie są zapisywane; zakresy bez różniących się bloków nie są kasowane.
3. Bloki niszczone przez kasowanie są dodawane do zapisu, a wynikające z tego dodatkowe zakresy — do kasowania.
4. `CheckFlash` znajduje się po `StartBootloader`, a przed `EraseFlash`.
5. Maksimum postępu jest przeliczane po sprawdzeniu CRC; przy braku zmian postęp nie pozostaje na zerze.
6. Każda awaria bezpiecznie prowadzi do pełnego zapisu z przyczyną w logu.
7. Sąsiadujące zakresy nie są łączone; kasowanie odbywa się według rosnących adresów.
8. Niepokryte fragmenty są raportowane na poziomie INFO, a nie traktowane jako błąd.
9. Publiczne API `UDSFlasher` i `UDSProtocolCommonSteps` nie zmienia się.
10. Kompilacja Release x64 kończy się bez błędów; x86 jest sprawdzane przez CI.

## 11. Ograniczenia i świadomie pominięte funkcje

1. **Zakresy kasowania nie są dzielone.** Nie znamy fizycznej granulacji sektora ECU, a dodatkowy sektor mógłby usunąć blok, którego nie planowano zapisywać.
2. **Nie odtwarza się danych z niepokrytych fragmentów.** W obsługiwanych obrazach takie fragmenty nie występują albo są oczekiwaną końcówką ostatniego zakresu Denso.
3. **Zakresy o zerowej długości** (sam adres `erase = 0x...;` w VBF) wyłączają tryb częściowy, ponieważ nie wiadomo, jaki obszar zostałby skasowany.
4. **CRC sprawdzane jest tylko dla bloków firmware.** SBL jest ładowany i uruchamiany przed sprawdzeniem; jego bloków nie pomija się.
5. **Timeout CRC wynosi 1000 ms.** Jeśli konkretny ECU oblicza CRC dłużej, blok zostanie zapisany. Jest to bezpieczne, choć może powodować dodatkową pracę.
6. **Zamknięcie zbiorów nie ma osobnych testów jednostkowych**, ponieważ logika znajduje się wewnątrz `UDSFlasherImpl` w pliku `.cpp`. W razie potrzeby można ją wydzielić do czystej funkcji operującej na zakresach i wynikach CRC.
