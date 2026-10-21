# Automat stanów odbiornika ISO-TP (HFSM2)

## Stany automatu odbiornika

| Stan | Opis |
|---|---|
| `RxIdle` | Oczekiwanie na pierwszą ramkę (SF lub FF). |
| `ReceiveSF` | Odebrano SF — natychmiast wywoływane jest `onReceived`. |
| `ReceiveFF` | Odebrano FF — wysyłana jest ramka FC i rozpoczyna się składanie wiadomości. |
| `SendFC` | Ponowne wysłanie Flow Control, jeśli jest to wymagane. |
| `ReceiveCF` | Oczekiwanie na kolejne ramki Consecutive Frame (CF) i ich składanie. |
| `RxCompleted` | Wiadomość została w całości złożona. |
| `RxError` | Stan błędu, np. błąd protokołu lub przekroczenie limitu czasu. |

## Diagram przejść

```mermaid
stateDiagram-v2
    [*] --> RxIdle : wejście początkowe
    
    RxIdle --> ReceiveSF : RxFrame(SF)
    RxIdle --> ReceiveFF : RxFrame(FF)
    RxIdle --> RxError : RxFrame(other)
    
    ReceiveSF --> RxCompleted : zapisz dane, wywołaj onReceived
    
    ReceiveFF --> ReceiveCF : wyślij FC(CTS, BS, STmin), uruchom timer N_Ar
    ReceiveFF --> RxError : BufferOverflow (wiadomość jest za duża)
    
    ReceiveCF --> RxCompleted : odebrano wszystkie CF, wywołaj onReceived
    ReceiveCF --> ReceiveCF : oczekiwane są kolejne CF (ponowne wejście, restart timera N_Cr)
    ReceiveCF --> RxError : InvalidSequenceNumber
    ReceiveCF --> RxError : Timeout(N_Cr)
    ReceiveCF --> RxError : RxFrame(other)
    
    SendFC --> ReceiveCF : ponownie wyślij FC i wróć do oczekiwania na CF
    
    RxCompleted --> RxIdle : automatycznie
    
    RxIdle --> RxIdle : Reset
    ReceiveFF --> RxIdle : Reset
    ReceiveCF --> RxIdle : Reset
    RxError --> RxIdle : Reset
    RxError --> RxError : każde inne zdarzenie
```

## Legenda zdarzeń

| Zdarzenie | Opis |
|---|---|
| `RxFrame(SF)` | Odebrano Single Frame (ramkę pojedynczą). |
| `RxFrame(FF)` | Odebrano First Frame (ramkę pierwszą). |
| `RxFrame(CF)` | Odebrano Consecutive Frame (ramkę kolejną). |
| `Timeout(N_Ar)` | Upłynął czas oczekiwania na pierwszą ramkę CF. |
| `Timeout(N_Cr)` | Upłynął dopuszczalny odstęp czasu między ramkami CF. |
| `Reset` | Użytkownik wywołuje `reset()`. |
