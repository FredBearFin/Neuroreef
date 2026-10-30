# Decision and movement diagnostics

## Measurement

The initial measurement used the spatial-hash-order cap, before changing neighbour selection. With `Map.Set(512)`, `Simulation(16384, 3, 1)` and 1,000 steps, the 64-neighbour cap bound 38,777 of 844,757 active decisions (4.59%). Against a full scan, the selected O1, O2, mate and strike targets differed 48, 48, 0 and 4 times respectively.

The final measurements used the same map and capacity, one thread, and 1,000 steps for each of seeds 3, 5 and 7. “Cap hit” means more than 64 non-self organisms occupied the hash cells scanned for that decision. Target differences compare the selected target with the result of scanning all organisms in those cells using the same distance, field-of-view and target eligibility rules.

| Seed | Active decisions | Cap hits | O1 changed | O2 changed | Mate changed | Strike changed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 3 | 849,759 | 47,320 | 1,372 | 1,441 | 44 | 2,101 |
| 5 | 680,595 | 19,551 | 3,538 | 3,464 | 12 | 2,049 |
| 7 | 911,547 | 125,050 | 4,289 | 3,722 | 98 | 16,964 |
| **Total** | **2,441,901** | **191,921 (7.86%)** | **9,199** | **8,627** | **154** | **21,114** |

Because the cap changed targets, capped selection now uses a stable hash priority of `(seed, step, focal serial, neighbour serial)`, with slot index as a tie-breaker. When there are at most 64 candidates, the original spatial-hash order is retained. When the cap binds, the 64 lowest priorities are processed in priority order, so the selected set and aggregation order do not depend on hash-cell traversal order. The priority scan considers candidate IDs in the local hash cells but only the selected 64 receive the more expensive perception processing.

`Decide.Metrics` enables these counters for a measurement run; leave it `null` for ordinary simulation. Metrics are intended for one measured simulation at a time. Action counts below cover all three final runs. “Attempted” is a sampled action bit; “eligible” and “effective” use the per-action definitions in the final column. Calls aggregate the eight sound primitives.

| Action | Attempted | Eligible | Effective | Eligibility and effect |
| --- | ---: | ---: | ---: | --- |
| EAT | 1,402,143 | 1,379,484 | 1,379,238 | A plant bite is available, or prey-eating selected a target; effective on a bite or an in-range prey strike. |
| STRIKE | 81,015 | 12,910 | 12,785 | A live target exists; effective when it is in range (a miss still counts as an executed strike). |
| DRINK | 89,852 | 14,229 | 14,229 | Drinkable water is near and thirst is positive; effective when thirst is relieved. |
| MATE | 130,540 | 1,010 | 1,010 | The partner, readiness, sex-system and free-slot checks pass; effective when mating starts, whether or not it conceives. |
| REST | 80,849 | 368 | 368 | Fatigue is positive; effective when fatigue falls. |
| MARK | 77,485 | 77,467 | 77,467 | Always eligible; effective when the action increases scent. |
| LAY | 472,450 | 896 | 896 | The animal is ready and asexual; effective when at least one offspring is produced. |
| CACHE | 116,034 | 59,228 | 59,228 | Seed remains in the gut on land; effective when it is cached. |
| CALL (all sounds) | 390,551 | 227,003 | 227,003 | A sampled sound survives the utterance-length limit and the animal has a voice; effective when emitted. |

SCAN was active (sampled gain above 0.5) 41,509 times and blocked 22,050 ordinary EAT attempts; prey-directed EAT is not blocked by SCAN. SPRINT's intended gain exceeded 0.5 on 14,270 decisions; 12,510 of those moved a positive distance.

## Movement path

Movement now traverses each field cell crossed by a step, wrapping cell indices at the world edge. On reaching a cell outside the animal's realm, it stops just before the boundary; only the traveled fraction contributes to its resulting speed and movement cost. The focused tests cover a one-cell barrier crossed en route to a valid endpoint, an unobstructed edge wrap, a barrier across the wrapped edge, and stopping at a boundary.

No “last move was blocked” sense was added; path validity is fixed without changing the feature or genome layout.
