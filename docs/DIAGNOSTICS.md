# Decision and movement diagnostics

## Measurement

The initial measurement used the spatial-hash-order cap, before changing neighbour selection. With `Map.Set(512)`, `Simulation(16384, 3, 1)` and 1,000 steps, the 64-neighbour cap bound 38,777 of 844,757 active decisions (4.59%). Against a full scan, the selected O1, O2, mate and strike targets differed 48, 48, 0 and 4 times respectively.

The measurements below were refreshed after adding continuous depth effects. They used the same map and capacity, one thread, and 1,000 steps for each of seeds 3, 5 and 7. “Cap hit” means more than 64 non-self organisms occupied the hash cells scanned for that decision. Target differences compare the selected target with the result of scanning all organisms in those cells using the same distance, field-of-view and target eligibility rules.

| Seed | Active decisions | Cap hits | O1 changed | O2 changed | Mate changed | Strike changed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 3 | 745,572 | 14,390 | 1,174 | 1,534 | 4 | 834 |
| 5 | 660,500 | 15,875 | 3,084 | 3,160 | 6 | 1,386 |
| 7 | 991,653 | 236,804 | 10,411 | 9,430 | 234 | 41,082 |
| **Total** | **2,397,725** | **267,069 (11.14%)** | **14,669** | **14,124** | **244** | **43,302** |

Because the cap changed targets, capped selection now uses a stable hash priority of `(seed, step, focal serial, neighbour serial)`, with slot index as a tie-breaker. When there are at most 64 candidates, the original spatial-hash order is retained. When the cap binds, the 64 lowest priorities are processed in priority order, so the selected set and aggregation order do not depend on hash-cell traversal order. The priority scan considers candidate IDs in the local hash cells but only the selected 64 receive the more expensive perception processing.

`Decide.Metrics` enables these counters for a measurement run; leave it `null` for ordinary simulation. Metrics are intended for one measured simulation at a time. Action counts below cover all three final runs. “Attempted” is a sampled action bit; “eligible” and “effective” use the per-action definitions in the final column. Calls aggregate the eight sound primitives.

| Action | Attempted | Eligible | Effective | Eligibility and effect |
| --- | ---: | ---: | ---: | --- |
| EAT | 1,329,420 | 1,283,731 | 1,283,516 | A plant bite is available, or prey-eating selected a target; effective on a bite or an in-range prey strike. |
| STRIKE | 73,891 | 12,957 | 12,821 | A live target exists; effective when it is in range (a miss still counts as an executed strike). |
| DRINK | 77,210 | 14,594 | 14,594 | Drinkable water is near and thirst is positive; effective when thirst is relieved. |
| MATE | 113,556 | 989 | 989 | The partner, readiness, sex-system and free-slot checks pass; effective when mating starts, whether or not it conceives. |
| REST | 94,747 | 927 | 927 | Fatigue is positive; effective when fatigue falls. |
| MARK | 79,604 | 79,581 | 79,581 | Always eligible; effective when the action increases scent. |
| LAY | 445,260 | 864 | 864 | The animal is ready and asexual; effective when at least one offspring is produced. |
| CACHE | 75,454 | 31,543 | 31,543 | Seed remains in the gut on land; effective when it is cached. |
| CALL (all sounds) | 361,819 | 188,583 | 188,583 | A sampled sound survives the utterance-length limit and the animal has a voice; effective when emitted. |

SCAN was active (sampled gain above 0.5) 67,194 times and blocked 45,155 ordinary EAT attempts; prey-directed EAT is not blocked by SCAN. SPRINT's intended gain exceeded 0.5 on 42,042 decisions; 31,836 of those moved a positive distance.

## Movement path

Movement now traverses each field cell crossed by a step, wrapping cell indices at the world edge. On reaching a cell outside the animal's realm, it stops just before the boundary; only the traveled fraction contributes to its resulting speed and movement cost. The focused tests cover a one-cell barrier crossed en route to a valid endpoint, an unobstructed edge wrap, a barrier across the wrapped edge, and stopping at a boundary.

No “last move was blocked” sense was added; path validity is fixed without changing the feature or genome layout.
