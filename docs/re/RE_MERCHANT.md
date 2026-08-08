# RE — Albion MERCHANT economy (MAIN.EXE)

> Goal: decode the per-shop buy%/sell% multiplier, exact buy/sell price formulas,
> merchant purse, stock model, and the unsellable-item gate. Tool: radare2
> (`radare2 -p albion_aaa`). DOS LE binary.
> The SR repo does NOT contain lifted game logic (only platform glue) — so all
> findings are from radare2 disassembly + cross-reference to UAlbion's decoded data.

## KNOWN FACTS (addresses + offsets)

### Confirmed (radare2, MAIN.EXE)

- **Item record = 40 bytes (0x28).** Resolver `fcn.0004a521` `GetItemDataPointer(slotPtr)`:
  reads `word[slot+4]` = itemId, computes `itemBase + (id-1)*0x28`, base global = `[0x15e5c8]`.
  Matches UAlbion `ItemData` exactly: `Flags` ushort @ +0x1B, **`Value` ushort @ +0x20**, size 0x28.
- **Inventory slot = 6 bytes**, item id at slot+4 (`fcn.0004a521`).
- **SHOP PRICE PERCENT lives in global `word [0x17800c]`.** It is a per-shop multiplier,
  NOT stored in the merchant XLD or the item. ONE multiplier is used for BOTH buy and sell.
- **Source of the percent = PlaceActionEvent.Unk6.** The PlaceAction dispatcher
  `fcn.000666e9` decodes the event record into globals at `0x66740`:
  `byte[rec+2]→[0x178014]`, `byte[rec+3]→[0x178016]`, `byte[rec+4]→[0x178012]`,
  `byte[rec+5]→[0x17800e]`, **`word[rec+6]→[0x17800c]`** (=UAlbion `Unk6`),
  `word[rec+8]→[0x178010]` (=UAlbion `Unk8`). The dispatcher record `rec+2` aligns
  with UAlbion's first payload field `Unk2` (1-byte node/type header difference).
- **The single write to `[0x17800c]` is at `0x6677c`** (the dispatcher) — it is set
  once when the Merchant/service PlaceAction opens, then read by every price calc.

### Price formula (BYTE-EXACT) — `price = max(1, Value * percent / 100)`

At `0x68174` (a BUY price calc, identical idiom repeated ~12× across 0x66xxx-0x68xxx):
```
ax  = word[item+0x20]          ; item.Value (= base resell value * 10)
dx  = word[0x17800c]           ; shop percent (PlaceAction.Unk6)
edx = Value * percent          ; imul edx, eax
eax = edx / 100                ; idiv 100 (signed; sar edx,0x1f for sign-extend)
if (eax <= 1) eax = 1          ; price floored to 1   (cmp eax,1; jle → set 1)
price = eax
```
So **buy price = max(1, item.Value * Unk6 / 100)**, integer (truncating) division.
With Unk6 in the believed 38..130 range this gives the "38–130% of Value" behaviour.
Because Value is already "base resell × 10", a percent of 100 returns Value (10× the
displayed gold price; gold itself is in tenths, so it nets the displayed price).



- Item `Value` field: 16-bit, at item-record offset TBD. In UAlbion `ItemData.Value`
  is "base resell value" stored in the same unit as party gold (gold-tenths).
- Merchant XLD record (per UAlbion serdes `Inventory.SerdesMerchant`): ONLY 24 backpack
  item slots, NO gold field, NO multiplier field, NO per-item price field. So a
  per-shop multiplier (if it exists) is NOT in the merchant XLD — it must be a
  constant or a separate MAIN.EXE table indexed by MerchantId.
- `MerchantEvent` (map event) carries only {MerchantId, PartyMember} — no multiplier arg.

## SELL / BUY COMPLETION, STOCK, PURSE, GATE

### Buy completion (merchant → party) — `0x68561`..`0x686dd` (qty-buy callback at 0x683bf)
1. unit price = `max(1, Value*pct/100)`; `qty` chosen via `min(stock byte[slot+0], totalGold/price, maxStack)` (max-affordable calc at 0x683bf, maxStack via `fcn.00049969` per item id).
2. `cost = qty * unitPrice` (`0x6857d`), then `call fcn.00067b9c` (`0x68580`) — the TrySpendGold helper: `total=GetTotalGold; if(cost>total) fail; else newTotal=total-cost; ModifyGold(newTotal)` (with a yes/no confirm popup; returns 0 if cancelled).
3. item handed to party via `fcn.0004928a` (`0x6865c`).
4. **STOCK DECREMENT** (`0x68661`, byte-exact, verified):
```
al = byte[slot+0]            ; current stock
if (al == 0xFF) skip          ; 0xFF = INFINITE stock sentinel (never depletes)
else byte[slot+0] -= qty
if (byte[slot+0] == 0) clearSlot(fcn.0004a4ee)
```

### Sell completion (party → merchant) — `0x2c046`..`0x2c153` (verified)
1. unit price = `max(1, Value*pct/100)` — **same percent, same formula as buy** (`fcn.000689da` shows the gold popup; aborts if cancelled).
2. remove qty from player inventory (`fcn.00048fb2`).
3. **deposit item into merchant inventory** via `fcn.00068756` (`0x2c105`) — increments the merchant slot stock byte (`add byte[slot],al` at `0x6882c`, capped at 99/0x63) so the player can buy it back.
4. **GOLD CREDIT** (`0x2c10a`..`0x2c120`, byte-exact, verified):
```
edx = qty * unitPrice
eax = GetTotalGold()         ; fcn.00038804
eax += edx
ModifyGold(eax)              ; fcn.000388a0  -- UNCONDITIONAL
```

### MERCHANT PURSE: DOES NOT EXIST (CONFIDENCE: HIGH)
The sell credits party gold unconditionally — no gold-pool read, no cap, no "merchant
out of money" branch. The merchant-screen globals were all audited: `[0x178018]` =
merchant 24-slot inventory pointer, `[0x178010]` = merchant id, `[0x17800c]` = price %,
`[0x17800e]` = repair %, `[0x178012/14/16/1c]` = UI text targets / counters — none holds
a decremented gold pool. The premise of a "merchant purse capping sells" is FALSE for Albion.

### STOCK: finite per-slot count byte, PERSISTED (CONFIDENCE: HIGH)
- `byte[slot+0]` = stock count of that merchant ware (slot is 6 bytes). Buy decrements,
  player-sell increments (cap 99); a slot hitting 0 is cleared.
- `0xFF` stock = infinite (never decremented) — for staple-stock merchants.
- Persistence: merchant init `fcn.00067d2f` loads block type 0x22 keyed by merchant id
  (`[0x178010]`) into `[0x178018]`; merchant exit `fcn.00067e07` SAVES it back. So stock
  changes survive in the savegame (this matches UAlbion serializing merchant inventories
  in SavedGame).

### UNSELLABLE / "VITAL ITEM" GATE (CONFIDENCE: HIGH)
At `0x64e92` (transfer-validation loop), byte-exact:
```
al = byte[item+0x1B]   ; item.Flags low byte
al &= 0x02             ; PlotItem bit (== UAlbion ItemFlags.PlotItem = 1<<1)
if (al) blockedCount++
... if (blockedCount) show refusal message [0x15e0bc]
```
So the original blocks selling any item with **Flags bit 1 (0x0002 = PlotItem)** — exactly
what UAlbion's `OnSellToMerchant` already checks. (A separate buy-time gate `fcn.00037f19`
tests a CHARACTER state bit at char+0x31c bit 24 → msg 0x244; that is char-state, not item.)

## IMPLEMENTATION RECOMMENDATION (for MerchantPricing + InventoryManager)

1. **Replace the 100%/33% placeholders with the single per-shop percent.** Both buy and
   sell use `price = max(1, item.Value * percent / 100)` (truncating integer division,
   floored to 1). There is NO separate sell rate — selling an item back to the SAME shop
   returns the SAME unit price you'd pay to buy it (Albion shops have no buy/sell spread;
   the only economic loss to the player is that wares restock and prices are fixed by shop).
   - `MerchantPricing.Price(value, percent)` already does `value*percent/100`; just add the
     `Math.Max(1, ...)` floor and drop the separate DefaultSellPercent. Use ONE percent for both.
2. **Source the percent from `PlaceActionEvent.Unk6`** (the merchant/service event that opens
   the shop). Rename `Unk6` → `PricePercent` for the Merchant/ScrollMerchant/RepairItem/Food
   place-action types. Thread it from the MerchantEvent/PlaceAction handler into
   `OnBuyFromMerchant`/`OnSellToMerchant` (currently they call `BuyPrice(item.Value)` with the
   default). NOTE: the percent is NOT in the merchant XLD and NOT in MerchantEvent today —
   it lives in the map's PlaceAction script event; the UI must capture Unk6 when the shop opens.
   Until that plumbing exists, a defensible default percent ≈ 100 keeps prices == displayed Value.
3. **Add merchant STOCK** (finite per-slot counts): decrement merchant slot on buy, increment
   on player-sell (cap 99), treat count 0xFF as infinite, clear slot at 0. UAlbion already
   serializes merchant inventories, so this only needs the count plumbing in
   OnBuyFromMerchant/OnSellToMerchant (it currently uses TryGiveItems which already moves
   counts — verify the merchant slot Amount decrements/increments and that 0xFF/large amounts
   are treated as unlimited).
4. **Do NOT add a merchant purse** — it does not exist in the original; selling credits party
   gold unconditionally. (Remove the "merchant purse caps sells" item from the roadmap.)
5. **Keep the PlotItem (0x0002) unsellable gate** — already correct in OnSellToMerchant.
6. **Units**: item.Value is "base resell × 10"; party gold is in tenths. percent=100 ⇒ unit
   price == Value (tenths), displayed as `price/10` gold. UAlbion uses the same tenths unit,
   so no unit conversion is needed — just apply the percent and the max(1,...) floor.

## CONFIDENCE SUMMARY
- Price formula `max(1, Value*pct/100)`, buy==sell percent: **HIGH** (byte-exact, 3 sites + sell path).
- Percent source = PlaceActionEvent.Unk6 (global [0x17800c], dispatcher 0x66740/0x6677c): **HIGH**.
- Stock = slot count byte, 0xFF=infinite, persisted: **HIGH** (verified decrement 0x68675 / increment 0x6882c / persist 0x67d2f/0x67e07).
- No merchant purse: **HIGH** (sell credit unconditional 0x2c11e/0x2c120; all merchant globals audited).
- Unsellable = Flags&0x0002 (PlotItem): **HIGH** (verified 0x64e92).

## COULD NOT DETERMINE
- Literal Unk6 percent values per individual merchant (they live in map/event data, not
  MAIN.EXE; need to dump the PlaceAction events from the map XLDs to enumerate the real
  38..130 spread — out of scope for the EXE).
- Whether RepairItem/Food/Heal use Unk6 the same way as Merchant for THEIR pricing — they
  read the same `[0x17800c]` global (so structurally yes), but their per-action formulas
  (e.g. repair = Σ over equipment) were not fully traced here.

## KEY ADDRESSES (quick reference)

| What | Address |
|---|---|
| Price percent global (PlaceAction.Unk6) | `word [0x17800c]` |
| Percent set (only write) | `0x6677c` in dispatcher `fcn.000666e9` (decode block @ `0x66740`) |
| PlaceAction dispatch table (13 handlers) | `0x13eaf0`; Merchant=idx7 & ScrollMerchant=idx9 → `0x6750c` |
| Merchant screen entry | `fcn.00067cfc` (UI loop `fcn.0007545c`, callback table `0x13eb3c`) |
| Item-data resolver `GetItemDataPointer` | `fcn.0004a521` (item base `[0x15e5c8]`, size 0x28) |
| Item.Value / Item.Flags | `word[item+0x20]` / `byte[item+0x1B]` |
| Price formula sites (`max(1,Value*pct/100)`) | `0x68174`, `0x68328`, `0x68418` (+ sell `0x2c0..`) |
| GetTotalPartyGold / ModifyPartyGold | `fcn.00038804` / `fcn.000388a0` |
| TrySpendGold (buy debit + confirm) | `fcn.00067b9c` |
| Buy completion (stock dec `0x68675`, item add `0x6865c`) | `0x68561`..`0x686dd` |
| Sell completion (gold credit `0x2c11e/0x2c120`) | `0x2c046`..`0x2c153` |
| Merchant-inventory deposit (stock inc `0x6882c`) | `fcn.00068756` |
| Merchant stock load / save (persisted) | `fcn.00067d2f` / `fcn.00067e07` |
| Unsellable gate (`Flags & 0x02`) | `0x64e92`, refusal msg `[0x15e0bc]` |

## METHOD LOG

- SR repo (`the local SR checkout`) confirmed to contain ONLY platform glue (per docs/re/SR_INDEX.md);
  lifted game logic is not committed → all RE done in radare2 against `<game-install>/MAIN.EXE`.
- radare2 search gotcha: with project `albion_aaa`, `/x` returns nothing unless you set
  `e search.in=io.maps.x` AND seek into code first (`s 0x10000; /x ...`); the data map is at 0x10000000.
- Found item resolver via `fcn.0004a521` (imul 0x28 / base [0x15e5c8]) → established Value@+0x20.
- Searched `mov ax,word[reg+0x20]` reads → 3 merchant sites all using identical `Value*[0x17800c]/100`.
- Traced `[0x17800c]` to its sole writer in the PlaceAction dispatcher → Unk6.
- Verified buy stock-decrement, sell gold-credit (no purse), and the `Flags&0x02` unsellable
  gate by direct disassembly; a sub-agent corroborated buy/sell completion + stock persistence.
