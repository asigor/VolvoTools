# Automat stanów nadajnika ISO-TP (HFSM2)

## Stany automatu nadajnika

| Stan | Opis |
|---|---|
| `Idle` | Oczekiwanie na polecenie wysłania. |
| `SendSingleFrame` | Wysłanie SF — krótkiej wiadomości o długości do 7 bajtów. |
| `SendFirstFrame` | Wysłanie FF — pierwszej ramki długiej wiadomości. |
| `WaitFlowControl` | Oczekiwanie na FC od odbiorcy. |
| `SendConsecutiveFrame` | Wysłanie kolejnej ramki CF z uwzględnieniem BlockSize i STmin. |
| `TxCompleted` | Wysyłanie zakończone powodzeniem. |
| `TxError` | Stan błędu, np. przekroczenie limitu czasu, wyjątek lub nieprawidłowa FC. |

## Diagram przejść

```mermaid
stateDiagram-v2
    [*] --> Idle : wejście początkowe
    
    Idle --> SendSingleFrame : SendRequest && size <= 7
    Idle --> SendFirstFrame : SendRequest && size > 7
    
    SendSingleFrame --> TxCompleted : ramka wysłana
    
    SendFirstFrame --> WaitFlowControl : FF wysłana
    
    WaitFlowControl --> SendConsecutiveFrame : RxFrame(FC, CTS)
    WaitFlowControl --> WaitFlowControl : RxFrame(FC, WAIT), restart timera
    WaitFlowControl --> TxError : RxFrame(FC, OVERFLOW)
    WaitFlowControl --> TxError : Timeout(N_Bs)
    
    SendConsecutiveFrame --> SendConsecutiveFrame : TxConfirmation OK && są dalsze dane && blok nie jest pełny
    SendConsecutiveFrame --> WaitFlowControl : TxConfirmation OK && blockSize > 0 && blockCounter >= blockSize
    SendConsecutiveFrame --> TxCompleted : TxConfirmation OK && wysłano wszystkie dane
    SendConsecutiveFrame --> TxError : TxConfirmation FAIL
    SendConsecutiveFrame --> TxError : Timeout(N_Cs)
    SendConsecutiveFrame --> WaitFlowControl : ReadyToSend po upływie STmin
    
    TxCompleted --> Idle : automatycznie
    
    Idle --> Idle : Reset
    WaitFlowControl --> Idle : Reset
    SendConsecutiveFrame --> Idle : Reset
    TxError --> Idle : Reset
    TxError --> TxError : każde inne zdarzenie
```

## Legenda zdarzeń

| Zdarzenie | Opis |
|---|---|
| `SendRequest` | Użytkownik wywołuje `send()`. |
| `RxFrame(FC)` | Odebrano ramkę Flow Control (CTS, WAIT lub OVERFLOW). |
| `TxConfirmation` | Potwierdzenie wysłania ramki CAN (powodzenie/błąd). |
| `Timeout(N_Bs)` | Upłynął czas oczekiwania na FC. |
| `Timeout(N_Cs)` | Upłynął czas między ramkami CF zgodnie z STmin. |
| `ReadyToSend` | Wewnętrzne zdarzenie generowane po przerwie STmin. |
| `Reset` | Użytkownik wywołuje `reset()`. |
