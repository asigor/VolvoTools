# Dokumentacja VolvoTools — tłumaczenie polskie

Ten katalog zawiera polskie wersje wybranych dokumentów projektu. Oryginalne pliki pozostają bez zmian; tłumaczenia mają przyrostek `.pl.md`.

## Instrukcje i przegląd projektu

- [README projektu](../README.pl.md)
- [Skróty PowerShell dla pakietu dystrybucyjnego](../distribution/README.pl.md)

## Diagramy i protokoły

- [Automat stanów odbiornika ISO-TP](diagrams/rx_state_machine.pl.md)
- [Automat stanów nadajnika ISO-TP](diagrams/tx_state_machine.pl.md)
- [Specyfikacja ISO-TP](tech_specs/ISOTP.pl.md)

## Architektura flashera i odczytu ECU

- [Wspólna implementacja flashera i czytników D2](tech_specs/d2_flasher_reader_common.pl.md)
- [Przeniesienie D2FlasherBase na automat stanów hFSM2](tech_specs/d2flasher_hfsm.pl.md)
- [Parametry czytników i flasherów](tech_specs/reader_flasher_params.pl.md)
- [Odczyt oprogramowania z ECU](tech_specs/ecu_reading.pl.md)

## Zakres i uwagi

To jest rozwijany zestaw tłumaczeń dokumentacji, a nie kompletna polska wersja całego repozytorium. W repozytorium znajduje się więcej specyfikacji technicznych, a także plik PDF `PrimaryBoot_169.pdf`. Plików binarnych, kodu źródłowego, nazw API, identyfikatorów protokołów ani danych technicznych nie należy tłumaczyć automatycznie jak zwykłej prozy. Tłumaczenia dokumentacji zachowują nazwy symboli i przykłady kodu, aby pozostały użyteczne dla programistów.
