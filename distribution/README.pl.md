# Skróty PowerShell dla VolvoTools

Skrypty te należy umieścić obok plików wykonywalnych pobranych z pakietu GitHub Actions. Wywołują opcje wiersza poleceń zaimplementowane w programach `VolvoFlasher` i `VolvoLogger`; nie modyfikują samych aplikacji.

## Przykłady

Odczyt PIN-u z CEM (bezpośredni odpowiednik przykładu wiersza poleceń):

```powershell
.\Read-CEM-Pin.ps1
```

Wyszukiwanie PIN-u dla platformy P1:

```powershell
.\Start-PinSearch-P1.ps1
```

To samo działanie co polecenie `VolvoFlasher.exe -d "sm2 pro" -f "P1" pin`.

Zmiana urządzenia lub sterownika ECU:

```powershell
.\Start-PinSearch-P1.ps1 -Device "sm2 pro" -Platform P1 -Ecu 7A -StartPin 0
```

Wyszukiwanie PIN-u w kierunku malejących wartości (przekazuje aplikacji flagę `-d`):

```powershell
.\Start-PinSearch-P1.ps1 -Down
```

Wybudzenie sieci CAN:

```powershell
.\Start-Wakeup-P1.ps1 -Device "sm2 pro"
```

Uruchomienie rejestratora. Jeśli jest dostępny, podaj plik zmiennych używany przez VolvoLogger:

```powershell
.\Start-Logger-P1.ps1 -Device "sm2 pro" -Variables ".\data\variables.json" -Output ".\logs\p1.csv"
```

Programowanie sterownika jest działaniem jawnym — plik firmware musi być wskazany:

```powershell
.\Start-Flash-P1.ps1 -InputFile ".\data\firmware.bin" -Device "sm2 pro" -Platform P1
```

## Wartości domyślne

Plik `config/defaults.json` opisuje domyślne ustawienia pakietu: urządzenie `sm2 pro`, platformę `P1`, prędkość 500000 bodów, ECU `7A` oraz wyszukiwanie PIN-u rozpoczynające się od zera i przebiegające w górę. Skrypty pobierają te wartości z `config/defaults.json`. Parametry przekazane do skryptu nadpisują je na czas danego uruchomienia.

## Bezpieczeństwo i ograniczenia

- Polecenie wyszukiwania PIN-u aktywnie komunikuje się ze sterownikiem ECU. Używaj go wyłącznie w pojeździe lub module, do którego diagnozowania masz uprawnienia. Najpierw potwierdź wybór urządzenia i platformy.
- Nie dodano automatycznego skrótu do programowania ECU, ponieważ plik firmware/SBL i parametry zależne od celu muszą zostać wybrane jawnie.
- Plik zmiennych rejestratora ma format specyficzny dla aplikacji. Skrypt nie tworzy po cichu pliku zastępczego, ponieważ nieprawidłowy plik może uniemożliwić rejestrowanie danych.
- Skrypty wymagają Windows PowerShell 5.1 lub nowszego albo PowerShell 7 działającego w systemie Windows.
