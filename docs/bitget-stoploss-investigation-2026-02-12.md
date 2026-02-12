# Bitget stop-loss investigation (2026-02-12)

## Context
Observed runtime error on stop-loss placement:

- `The parameter does not meet the specification d delegateType is error`

This error occurred on `PlaceTpSlOrderAsync` usage for stop loss and caused emergency market close.

## SDK-level analysis (JK.Bitget.Net 3.4.0)

After decompiling `Bitget.Net.Clients.FuturesApiV2.BitgetRestClientFuturesApiTrading`:

1. `PlaceTpSlOrderAsync` sends requests to `/api/v2/mix/order/place-tpsl-order`.
2. Method payload includes `planType`, `triggerPrice`, `size`, `holdSide`, `triggerType`, `executePrice`.
3. Method does **not** expose or send `delegateType`.
4. For some Bitget account/mode combinations, backend validation still reports `delegateType` mismatch.

## Conclusion
For a position that is already open (our flow sets SL right after entry fill), a safer endpoint is:

- `/api/v2/mix/order/place-pos-tpsl` via `SetPositionTpSlAsync`

This endpoint is explicitly position-oriented and does not require the problematic `delegateType` handling path.

## Implemented change
`PlaceStopLossAsync` now places stop-loss via `SetPositionTpSlAsync` with:

- `holdSide = long/short` (derived from trade direction)
- `slTriggerPrice = normalized stop`
- `slTriggerQuantity = normalized quantity`
- `slTriggerType = MarkPrice`

This removes the need for a secondary fallback path in stop-loss placement.

## Why this is better than fallback

- Uses a semantically correct endpoint for already-open position SL.
- Reduces branching/retry complexity.
- Avoids relying on error-string-based control flow for normal behavior.
