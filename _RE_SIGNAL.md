# RE: MAP EVENT "Signal" (MapEventType 0x0F) handler — MAIN.EXE

Reverse-engineering the Signal map-event handler so it can be implemented in UAlbion.
All addresses are radare2 file/linear addresses (DOS LE binary, project `albion_aaa`).

CONFIRMED = I read the actual instructions. INFERRED = reasoning from context. UNKNOWN = stated.

---

## KNOWN FACTS (running list)

1. CONFIRMED — Dispatch. Master map-event dispatcher = `fcn.000312c2`. It reads the event TYPE
   byte from the current frame at `[frame+0x18]` (`0x3136c: mov al, byte [edx+0x18]`),
   bounds-checks `<= 0x1d` (29) at `0x3137f`, does `shl eax,2`, then
   `call dword [eax + 0x13da7c]` at `0x313a1`. Table base = **0x13da7c**.
   Type 0x0F → slot `0x13da7c + 0x0F*4 = 0x13dab8`, whose dword = **0x3af2d**. So the Signal
   handler is `fcn.0003af2d`. (Table max index 0x1d == UAlbion `MapEventType.Script` — the
   binary's table is 1:1 with `src/Formats/MapEvents/MapEventType.cs`; 0x0F == `Signal`.)

2. CONFIRMED — Event-execution stack ("frame"). Global word `0x13d70a` is the current
   event-stack DEPTH (push = `inc` at `0x31532` in `fcn.0003150d`; pop = `dec` at `0x315a3`
   in `fcn.00031581`; reset to 0). The stack is an array at base **0x153160**, stride **0x32
   (50) bytes**, max depth **0x28 (40)** frames (`fcn.0003150d`: `cmp eax,0x28 / jge`, memset
   each frame to 50 bytes via `fcn.00084cf0`). Every handler computes its frame as
   `0x153160 + depth*50`.

3. CONFIRMED — Frame layout (from dispatcher `fcn.000312c2`):
   - `[frame+0x04]`  map event-block handle/pointer base
   - `[frame+0x10]`  current event index within the block (word; 0xffff = end of chain)
   - `[frame+0x14]`  base offset of the in-memory event array
   - `[frame+0x18]`  the EVENT RECORD, copied in by `fcn.00092bcd(dst=frame+0x18, len=0xc)`.
                     So the in-MEMORY event record is **12 bytes** (not the 10 on-disk bytes).
                     `[frame+0x18]` = type byte (0x0F), `[frame+0x19]` = **SignalId**.
   - `[frame+0x00]`, `[frame+0x28]`, `[frame+0x2a]` are extra context slots populated by the
     trigger/driver path (NPC / tile / conversation), NOT by the dispatcher and NOT from the
     on-disk Signal record. See analysis below.

---

## Handler `fcn.0003af2d` — full disassembly trace (CONFIRMED)

```
0x3af2d  prologue; sub esp,0xc
0x3af47  mov ax,[0x13d70a]            ; depth
0x3af4d  imul eax,eax,0x32            ; *50
0x3af50  mov edx,0x153160 ; add edx,eax
0x3af57  mov [var_8h],edx             ; var_8h = frame base
0x3af5a  mov word [0x13daf6],0        ; result struct: clear field
0x3af63  mov word [0x13daf8],0        ; result struct: clear field
0x3af6c  mov eax,[var_8h]; mov ax,[eax]      ; read [frame+0]
0x3af77  cmp eax,1
0x3af7a  jne 0x3b03a                  ; if [frame+0] != 1  -> skip to tail
         ; --- [frame+0] == 1 branch ---
0x3af80  mov ax,[frame+0x28]; mov [var_ch],eax
0x3af8a  switch on [frame+0x28]:
            == 0 -> 0x3afac (the "0" case)
            == 1 -> 0x3b028  (set result 0x13daf6=3, 0x13daf8=0)
            == 2 -> 0x3b03a  (tail, no result change beyond the cleared 0)
            else -> 0x3b03a
         ; --- case [frame+0x28]==0 : 0x3afac ---
0x3afb7  cmp word [0x15e5be],0        ; "conversation/dialogue active" flag
0x3afbf  je 0x3afcb
0x3afc1  cmp word [0x15e5c2],0        ; conversation-context A
0x3afc9  je 0x3afcd
0x3afcb  jmp 0x3afdd                  ; (no/ineligible conversation) -> the "lookup" path
0x3afcd  mov dx,[0x15e5c0]            ; conversation-context B (a target id)
0x3afd4  mov eax,[var_8h]
0x3afd7  cmp dx,[frame+0x2a]          ; does conv-target match [frame+0x2a]?
0x3afdb  je 0x3afdf                   ; match -> result=3
0x3afdd  jmp 0x3aff3                  ; no match -> lookup path
         ; --- result = 3 (matched conversation target) : 0x3afdf ---
0x3afdf  mov word [0x13daf6],3
0x3afe8  mov word [0x13daf8],0
0x3aff1  jmp 0x3b026 -> tail
         ; --- lookup path : 0x3aff3 ---
0x3aff3  mov ax,[frame+0x2a]; and eax,0xffff
0x3afff  call fcn.00035783           ; table lookup (see below); returns idx+1 or 0xffff
0x3b004  mov [var_4h],eax
0x3b00d  cmp eax,0xffff
0x3b012  je 0x3b026                  ; not found -> tail (result stays 0)
0x3b014  mov word [0x13daf6],1       ; found
0x3b020  mov word [0x13daf8],ax      ; store the lookup result (idx+1)
0x3b026  jmp 0x3b03a -> tail
         ; --- tail : 0x3b03a ---
0x3b03a  mov eax,[var_8h]
0x3b03d  mov al,[frame+0x19]         ; <<< SignalId (2nd on-disk byte) read HERE
0x3b042  mov word [0x13dafc],ax      ; store SignalId into result struct +0x08
0x3b048  mov eax,0x13daf4            ; eax = ptr to 0x18-byte result/message struct
0x3b04d  call fcn.0002fce1           ; ENQUEUE that struct onto a subsystem queue
0x3b052  epilogue; ret
```

### Sub-functions
- CONFIRMED `fcn.00035783` — linear search of a **6-entry word table at 0x153cbe** for the
  value in `eax` (`[frame+0x2a]`); returns matchIndex+1, else 0xffff. (Loop `i=0..5`,
  `cmp [0x153cbe + i*2], target`.)
- CONFIRMED `fcn.0002fce1` — takes `eax` = pointer to a 0x18 (24)-byte record, fetches a queue
  slot (`fcn.000311c0`), `memcpy`s 24 bytes in (`fcn.00092bcd len 0x18`), patches a per-state
  field (`+0x14` = one of 0x13d768 / 0x13d73c / 0x13d70c depending on `0x13e142` and
  `0x15e5be`), then enqueues (`fcn.0002fd69`). I.e. it POSTS a 24-byte message to an
  event/UI queue. (Not a global flag store.)

### Result struct `0x13daf4` (24 bytes, the thing that gets enqueued)
- `+0x02` (`0x13daf6`): result CODE — 0 (default/not-found), 1 (lookup hit), or 3 (conversation
  target match / [frame+0x28]==1 special).
- `+0x04` (`0x13daf8`): result VALUE — the lookup index (idx+1) for code 1, else 0.
- `+0x08` (`0x13dafc`): the **SignalId** (`[frame+0x19]`).
- (`0x13daf4`/+0x0c.. were observed as 0 in the static image.)

### The globals it reads
- CONFIRMED `0x15e5be` — a "dialogue/conversation is active" flag: set to 1 at `0x45f3a`,
  cleared to 0 at `0x460d6` (conversation subsystem). Many handlers gate on it.
- CONFIRMED `0x15e5c0` / `0x15e5c2` — a conversation-context pair, written together in
  `fcn.00045d64` (`0x45da9`/`0x45db2`). INFERRED: the current conversation partner / target id
  (0x15e5c0) and a validity flag (0x15e5c2).

---

## ANSWERS TO THE TASK QUESTIONS

### Q1. What does the handler DO with SignalId?
CONFIRMED, and it is the OPPOSITE of what the name "Signal" suggests. The handler does **not**
set a persistent signal/switch keyed by SignalId. SignalId (`[frame+0x19]`) is read only at the
very end (`0x3b03d`) and copied verbatim into a 24-byte message record at `0x13daf4+0x08`, which
is then ENQUEUED to a subsystem via `fcn.0002fce1`. SignalId is a passthrough payload identifying
the signal, not an index that the handler itself acts on.

The handler's actual *logic* operates on the current event-frame's context fields, NOT on the
on-disk Signal record:
- It inspects `[frame+0]` (a frame-state field; must be 1 to take the main branch),
  `[frame+0x28]` (a small enum 0/1/2), and `[frame+0x2a]` (a value it either matches against the
  active-conversation target `0x15e5c0`, or looks up in the 6-entry table at `0x153cbe`).
- From those it computes a result CODE (0/1/3) and a result VALUE (lookup index), packs
  {code, value, SignalId} into the 24-byte message, and posts it.

INFERRED interpretation: "Signal" is a **notification/yield** event. When the script chain hits
a Signal, the engine packages the SignalId (plus the resolved context result) and hands it to the
outer driver/queue (`fcn.0002fce1`'s queue), which lets the conversation/script driver decide what
to do next (e.g. resume a waiting conversation branch, or notify the active dialogue that signal N
fired). The `0x15e5be`/`0x15e5c0` conversation checks strongly tie it to the dialogue system.

### Q2. Where is the signal VALUE stored, and who reads it back?
- It is **NOT** stored in a persistent per-signal flag table. There is no
  `signalTable[SignalId] = …` write anywhere in the handler. CONFIRMED.
- The SignalId and computed result are written into the transient 24-byte struct at `0x13daf4`
  (`+0x02 code`, `+0x04 value`, `+0x08 SignalId`) and immediately ENQUEUED (`fcn.0002fce1`).
  CONFIRMED the writes; CONFIRMED the enqueue. The consumer is whoever drains that queue (the
  event/dialogue driver). I did NOT fully trace the dequeue side, so the exact consumer is
  INFERRED (dialogue/script driver), not byte-confirmed.
- Therefore: Signal is **transient / cross-component messaging**, NOT a cross-zone/cross-map
  persistent flag. The static xref scan found only WRITES to `0x13daf6/f8/fc` (radare's static
  xrefs miss the `mov eax,0x13daf4` base+offset reads that happen after the enqueue copy), which
  is consistent with these being a scratch message buffer rather than a queried global.

### Q3. Related to QuerySwitch / the switch system?
CONFIRMED **NO** — it is a separate mechanism. The switch/word-variable system (used by
`Query`/`Modify`/`QuerySwitch`) is keyed storage that is read back later. This Signal handler
neither reads nor writes that store; it reads frame-context + conversation globals and posts a
one-shot message. The only table it consults is the 6-entry word table at `0x153cbe`
(`fcn.00035783`), which is unrelated to the switches array. So: separate signal/notification
path, not the switch system.

### Q4. C# implementation recommendation for UAlbion
Reality check first: the on-disk `SignalEvent` (`src/Formats/MapEvents/SignalEvent.cs`) carries
only `SignalId`. The branchy logic in the DOS handler depends on `[frame+0]`, `[frame+0x28]`,
`[frame+0x2a]` and the conversation globals — these are populated by the trigger/conversation
driver, not by the event record, and most of that driver context does not exist in UAlbion's
event model. A byte-faithful port is therefore NOT warranted from this record alone.

Recommended approach (in priority order):
1. Treat `SignalEvent` as a **fire-and-forget notification** carrying `SignalId`. The faithful
   minimal behaviour is: raise an in-engine event (e.g. a new `SignalEvent`-derived runtime event
   or a `SignalFiredEvent(SignalId)`) on the `EventExchange` so any interested component
   (primarily the dialogue/conversation manager) can react. This mirrors the original's
   "package SignalId + post to queue" exactly. Use `Enqueue`, not `Raise`, to match the
   deferred-queue semantics of `fcn.0002fce1` (and to respect the `Raise()-skips-own-handlers`
   gotcha).
2. Do **NOT** map it to the GameState switch/ticker store. The handler never touches that store;
   modelling Signal as a switch would be incorrect and could collide with real switches.
3. The conversation-target match (`0x15e5c0`/`0x15e5be`) and the 6-entry lookup at `0x153cbe`
   only matter inside an active conversation. Until UAlbion has a conversation driver that needs
   to consume signals mid-dialogue, the no-op-with-notification behaviour above is sufficient and
   faithful. When that driver exists, the consumer can subscribe to the signal notification and
   resolve it against the active conversation, reproducing the code 1/3 logic.
4. If/when a consumer is built, replicate the result encoding: code 3 when SignalId's context
   matches the active conversation target, code 1 + (tableIndex+1) when it resolves via the
   6-entry table, code 0 otherwise. (The 6-entry table at `0x153cbe` would need to be RE'd from a
   live conversation context to know its contents — currently UNKNOWN.)

So: **no new persistent GameState is required.** Add (at most) a lightweight transient
notification event that the dialogue system can subscribe to. The current UAlbion `SignalEvent`
record is already structurally correct (only SignalId is meaningful); what's missing is a handler
that re-broadcasts it.

---

## CONFIDENCE SUMMARY

HIGH confidence (instructions read):
- 0x0F → handler 0x3af2d via dispatch table at 0x13da7c. CONFIRMED.
- The handler does NOT set a persistent SignalId-keyed flag/switch. CONFIRMED.
- SignalId is `[frame+0x19]`, read once at 0x3b03d, copied into the 24-byte struct at
  0x13daf4+0x08 and enqueued via fcn.0002fce1. CONFIRMED.
- The branching uses frame-context fields (+0, +0x28, +0x2a) and conversation globals
  (0x15e5be/c0/c2), and a 6-entry lookup table at 0x153cbe. CONFIRMED.
- It is NOT the QuerySwitch/switch store. CONFIRMED (no access to that store).

MEDIUM confidence (INFERRED):
- The mechanism is a dialogue/script notification: SignalId is posted to a driver queue so a
  conversation/script can react. Inferred from the conversation-flag gating + enqueue, not from
  tracing the dequeue/consumer.

UNKNOWN / not traced:
- Exact consumer that drains the queue (fcn.0002fce1's target) and how it uses code/value/SignalId.
- Contents/meaning of the 6-entry word table at 0x153cbe (needs a live/RE'd conversation context).
- Precise meaning of frame fields +0, +0x28, +0x2a (which trigger path sets them, and to what).

## IMPLEMENTATION RECOMMENDATION (one line)
Model `SignalEvent` as a transient deferred notification (`Enqueue` a `SignalFired`-style event
carrying `SignalId`) for the dialogue/script system to consume — NOT as a GameState switch and
NOT requiring new persistent state.
