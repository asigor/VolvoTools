# Dokumentacja VolvoTools — tłumaczenie polskie

Ten katalog zawiera polskie wersje dokumentacji technicznej. Oryginalne pliki pozostają bez zmian; tłumaczenia Markdown mają przyrostek `.pl.md`, a tłumaczenia źródeł PlantUML — `.pl.puml`. Nazwy API, symbole, identyfikatory protokołów i fragmenty kodu zachowano w oryginalnej postaci.

## Instrukcje i przegląd projektu

- [README projektu](../README.pl.md)
- [Skróty PowerShell pakietu dystrybucyjnego](../distribution/README.pl.md)

## Diagramy i protokoły

- [Automat stanów odbiornika ISO-TP — Markdown](diagrams/rx_state_machine.pl.md)
- [Automat stanów odbiornika ISO-TP — PlantUML](diagrams/rx_state_machine.pl.puml)
- [Automat stanów nadajnika ISO-TP — Markdown](diagrams/tx_state_machine.pl.md)
- [Automat stanów nadajnika ISO-TP — PlantUML](diagrams/tx_state_machine.pl.puml)
- [Specyfikacja ISO-TP](tech_specs/ISOTP.pl.md)
- [Implementacja protokołu D2](tech_specs/d2_protocol_implementation.pl.md)
- [Utwardzenie parsera odpowiedzi D2](tech_specs/d2_request_parser_hardening.pl.md)
- [Dostawca identyfikatorów CAN](tech_specs/can_id_provider.pl.md)
- [Abstrakcja warstwy transportowej](tech_specs/transport_abstraction.pl.md)
- [Przywrócenie pakietowej transmisji danych D2](tech_specs/restore_d2_transferdata_batching.pl.md)

## Programowanie, odczyt i konfiguracja ECU

- [Wspólna implementacja flashera i czytników D2](tech_specs/d2_flasher_reader_common.pl.md)
- [Automat stanów D2FlasherBase (hFSM2)](tech_specs/d2flasher_hfsm.pl.md)
- [Parametry czytników i flasherów](tech_specs/reader_flasher_params.pl.md)
- [Odczyt oprogramowania z ECU](tech_specs/ecu_reading.pl.md)
- [Częściowe programowanie UDS](tech_specs/uds_partial_flash.pl.md)
- [Wyszukiwanie kodu PIN](tech_specs/pin_crack.pl.md)

## Logowanie

- [Rozszerzenie logowania aplikacji](tech_specs/code_logging_extension.pl.md)

## Zakres i uwagi

Przetłumaczono dokumenty tekstowe znajdujące się w katalogu `docs/`. Oryginały pozostają nienaruszone. Kod źródłowy, nazwy API, identyfikatory, wartości protokołów i przykłady kodu nie są tłumaczone, ponieważ muszą zachować dokładną postać techniczną.

Plik PDF `PrimaryBoot_169.pdf` jest dokumentem binarnym i wymaga osobnego procesu ekstrakcji tekstu oraz weryfikacji tłumaczenia. Nie jest częścią tłumaczeń Markdown wymienionych powyżej.
