# VolvoTools

Narzędzia do odczytu i zapisu pamięci sterowników ME7 i ME9 za pomocą interfejsów J2534 oraz do rejestrowania danych.

Projekt został pierwotnie napisany przez dream3R na potrzeby Hilton's Tuning Studio (HTS), na forum [Nefarious Motorsports](http://nefariousmotorsports.com/forum). HTS został zdekompilowany i przepisany w języku C++. Później dodano obsługę ogólnych urządzeń J2534, nie tylko DiCE, a także obsługę protokołu UDS.

## Obsługiwane funkcje

- Programowanie i rejestrowanie danych ME7 dla platform P80 i P2.
- Programowanie i rejestrowanie danych ME9 dla platformy P1.
- Programowanie i rejestrowanie danych przez ME9 oraz inne protokoły UDS.
- Programowanie i rejestrowanie danych sterowników ECM Denso dla platformy Volvo P3.

Wielkie podziękowania dla rkam za informacje i udostępnione dane, dla jazdw za wyjaśnienie protokołu TP20 oraz dla maZer.GTi za motywację.

## Budowanie projektu

### Wymagane narzędzia

Zainstaluj system kontroli wersji Git, menedżer pakietów Conan oraz system budowania CMake. Jako środowiska IDE i kompilatora można użyć Visual Studio 2022 Community Edition.

### Kroki kompilacji

1. Wykryj profile środowiska Conan:

   `conan profile detect`

2. Sklonuj repozytorium:

   `git clone https://github.com/asigor/VolvoTools.git`

3. Przejdź do katalogu projektu:

   `cd VolvoTools`

4. Pobierz i zainicjuj podmoduły:

   `git submodule update --init`

5. Zainstaluj zależności:

   `conan install . --build=missing`

6. Wygeneruj pliki projektu CMake:

   `cmake --preset conan-default`

   Jeśli wszystkie kroki zakończą się powodzeniem, w katalogu `build` powinien powstać plik `VolvoTools.sln` wraz z pozostałymi plikami potrzebnymi do kompilacji.

7. Zbuduj konfigurację Release:

   `cmake --build build --config Release`

   Alternatywnie otwórz `VolvoTools.sln` w Visual Studio i skompiluj projekt z poziomu IDE.

## Uwagi dotyczące urządzeń J2534

Aby ograniczyć liczbę otwarć i zamknięć kanałów CAN, kanały miały być przechowywane przez dłuższy czas w obiekcie `J2534Info`. Pojawił się jednak problem: kanał otwarty w jednym wątku powodował awaryjne rozłączenie urządzenia przy próbie użycia go w innym wątku. Zaobserwowano to w adapterze MongoosePro JLR; nie wiadomo, czy inne urządzenia zachowują się podobnie.

W odpowiedzi utworzono obiekt `J2534ChannelProvider`. Pozwala on otworzyć wszystkie kanały obsługiwane przez daną platformę albo otworzyć pojedynczy kanał, aby komunikować się z konkretnym sterownikiem ECU. Pierwszy tryb jest używany we flasherach do przełączania urządzeń w różnych sieciach w stan uśpienia. Drugi służy do pracy z magistralą konkretnego ECU, na przykład w rejestratorach.

## Do uzupełnienia

Należy opisać szczegóły protokołu VAG TP20 napotkane podczas jego implementacji.
