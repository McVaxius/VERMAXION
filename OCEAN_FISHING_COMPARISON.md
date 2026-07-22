# Ocean Fishing implementation comparison

VERMAXION now owns the Ocean Fishing flow directly. This records the supported scope and reliability behavior.

| Area | Current VERMAXION behavior |
|---|---|
| Character choice | Builds one queue per registration window: enabled `AlwaysFish` characters first, then enabled characters with XA Database Fisher levels ascending. The full XADB roster is authoritative for every character, including the current character. Unknown levels are excluded unless `AlwaysFish` is set. |
| Fallback | Missing unlock, Fisher gearset, or usable lure excludes that character and advances immediately. ADS and travel failures retry the same character twice after 3 and 10 seconds. |
| Stop boundaries | No new attempt starts with less than 60 seconds remaining. Registration closure, queue confirmation, and any post-queue failure disable fallback. |
| External state | A named YesAlready lease plus AutoRetainer multi-mode and AutoHook snapshots are held through registration and the voyage. Cleanup retries until restoration is verified; Full Stop/disposal performs forced best-effort restoration with diagnostics. |
| Registration | Boarding and embark strings come from active-language `custom/006/CtsIkdEntrance_00663` rows 4 and 10. |
| Travel | Lifestream handles Limsa travel. Locked Arcanists' Guild shard 43 receives one navigation/attunement attempt, is verified before use, then falls back to direct Dryskthota navigation. |
| Boat positioning | On voyage entry, one read-only vnavmesh scan probes 32 directions every 0.5 yalms up to 20 yalms. VERMAXION chooses the nearest derived edge with no player within 2 yalms, otherwise the edge with greatest player clearance. A failed scan snapshots the furthest player's position; with no other player it stays at the entry position. The entry-to-destination direction is retained as the outward facing. |
| Fishing sessions | At each seven-minute session start, Versatile Lure is set once and `/ahstart` is sent immediately, then every 3 seconds until Fishing/Gathering acknowledgement. Retries continue during initial movement and resume in place after an interruption. AutoHook presets remain unmanaged. |
| Voyage movement boundary | Fishing/Gathering acknowledgement immediately stops vnavmesh and permanently locks movement for that voyage. Before fishing has ever started, one alternative from the entry scan is allowed only when the first destination remains unfishable for 10 seconds. Route changes, later fishability failures, and crowd changes never reposition the character. Stored outward facing is reapplied whenever owned boat navigation stops. |
| Voyage completion | `DutyCompleted`, `IKDResult`, and disappearance of a previously observed duty context are accepted. Zone transitions and result settlement have bounded waits. |
| Inventory cleanup | Per-character opt-in `/ays discard`, followed by navigation near Limsa's Merchant & Mender and opt-in `/ays itemsell`. AutoRetainer must be readable and idle; cleanup warnings do not block return. |
| Return | A configured return is successful only after Lifestream activity settles or territory changes. It retries once after 30 seconds and fails after 120 seconds. |
| Deliberately excluded | Questionable integration, local/self repair, dynamic bait selection, and AutoHook preset management. ADS remains the only repair provider. |

Manual and AutoRetainer post-process triggers remain the only initial start paths. Once a run starts, bounded recovery is automatic for that registration window and is not persisted.
