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
| Voyage completion | `DutyCompleted`, `IKDResult`, and disappearance of a previously observed duty context are accepted. Zone transitions and result settlement have bounded waits. |
| Inventory cleanup | Per-character opt-in `/ays discard`, followed by navigation near Limsa's Merchant & Mender and opt-in `/ays itemsell`. AutoRetainer must be readable and idle; cleanup warnings do not block return. |
| Return | A configured return is successful only after Lifestream activity settles or territory changes. It retries once after 30 seconds and fails after 120 seconds. |
| Deliberately excluded | Questionable integration, local/self repair, dynamic bait selection, and AutoHook preset management. ADS remains the only repair provider. |

Manual and AutoRetainer post-process triggers remain the only initial start paths. Once a run starts, bounded recovery is automatic for that registration window and is not persisted.
