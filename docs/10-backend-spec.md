# Game API (ระยะ D)

โค้ดอยู่ที่ `server/DarkMyst.Api/` เทสต์อยู่ที่ `tests/DarkMyst.Api.Tests/` (เทสต์จริงกับ
PostgreSQL ที่รันอยู่จริง ไม่มี mock ไม่มี InMemory provider — ดูหัวข้อการทดสอบด้านล่าง)
เอกสารนี้ตอบเกณฑ์ผ่านของระยะ D ([06-roadmap.md](06-roadmap.md)):
**"เน็ตหลุดหรือคำขอซ้ำไม่ทำของหาย/เพิ่มซ้ำ"**

`DarkMyst.Api` **ไม่เขียนกฎเกมขึ้นใหม่แม้แต่บรรทัดเดียว** — อ้างอิง `DarkMyst.Combat`,
`DarkMyst.Content` และ `DarkMyst.Expedition` ชุดเดิมทั้งหมด (ดูกฎข้อ 3 ใน
[01-architecture.md](01-architecture.md)) ทุกครั้งที่ต้องคำนวณสแตท, ผลต่อสู้, สูตร evolve หรือ
แผนที่สำรวจ เซิร์ฟเวอร์เรียกฟังก์ชันเดียวกับที่ไคลเอนต์และ `simrunner` เรียก ไม่มีสำเนาที่สอง

## โครงสร้างโปรเจกต์

```
server/DarkMyst.Api/
  Program.cs                 จุดเริ่ม: DI, migration ตอน startup, endpoint ทั้งหมด
  Accounts/                   บัญชี guest, การเชื่อมบัญชี (linking)
  Content/                    ContentPackRegistry — เก็บ content หลายเวอร์ชันพร้อมกัน
  Data/                        ApiDbContext, entities (มิเรอร์ของโมเดลใน DarkMyst.Content)
  Migrations/                  EF Core code-first migrations
  Evolve/                      ห้าขั้นตอนของ docs/03-evolve-spec.md
  Expeditions/                 server ownership ของ ExpeditionRun (start/choose/resume/abandon)
  Battles/                     สนามทดลอง + การจับ checksum ไม่ตรง (docs/07)
  Teams/                       ทีมที่บันทึกไว้ — เจ้าของ IsInUse ที่ evolve ต้องเช็ค
  Ledger/                      บันทึกการเคลื่อนไหวทอง/อัญมณี/ไอเทม/ตัวละครแบบ append-only
  Summon/                      docs/12-summon-spec.md: pull/spark-redeem/attune/state
  Idempotency/                 กลไกกลางที่ endpoint ที่แก้ข้อมูลทุกตัวต้องผ่าน
  Debug/                       endpoint แจกของสำหรับทดสอบ/สาธิต (ดูหัวข้อ "สิ่งที่ตั้งใจตัดออก")
  Auth/                        bearer-token stub (player) + gate ของ admin (AdminAuth.cs)
  Admin/                       หน้าจัดการเนื้อหา — ดู 11-admin-spec.md
  Telemetry/                   telemetry ระดับตัวละคร (เขียนที่ทุก endpoint ข้างบน, อ่านผ่าน admin)
```

หนึ่งโปรเจกต์ แบ่งโมดูลภายในตามโฟลเดอร์ฟีเจอร์ ไม่ใช่ microservices — ตาม
[01-architecture.md](01-architecture.md): *"เริ่มจาก backend ชุดเดียวที่แบ่งโมดูลภายใน"*

## Endpoint ทั้งหมด

| Endpoint | Auth | Idempotency-Key | หน้าที่ |
| --- | --- | --- | --- |
| `POST /accounts/guest` | ไม่ต้อง | ไม่ต้อง | สร้างบัญชี guest ใหม่ คืน bearer token |
| `GET /accounts/me` | ต้อง | - | สรุปบัญชี (ทอง, จำนวนตัวละคร, เลเวลสูงสุด) |
| `POST /accounts/link/start` | ต้อง | ต้อง | เริ่มเชื่อมกับ identity ภายนอก — อาจได้ `linked` หรือ `conflict` |
| `POST /accounts/link/confirm` | ต้อง | ต้อง | ยืนยันเลือกฝั่งไหนเมื่อมีการชนกัน |
| `POST /evolve/preview` | ต้อง | ไม่ต้อง | เรียก `Evolution.Preview()` อย่างเดียว ไม่เขียนอะไร |
| `POST /evolve/confirm` | ต้อง | ต้อง | ห้าขั้นตอนเต็มในธุรกรรมเดียว |
| `POST /summon/pull` | ต้อง | ต้อง | สุ่ม 1 หรือ 10 ครั้ง หักอัญมณี ให้ตัวละครใหม่หรือ Echo shard |
| `POST /summon/spark-redeem` | ต้อง | ต้อง | แลก Spark 150 แต้มเป็นสายที่เลือก (เศษยกยอด) |
| `POST /summon/attune` | ต้อง | ต้อง | ใช้ Echo shard ไต่ `inheritedBonusPerMille` ถึงเพดาน |
| `GET /summon/state` | ต้อง | - | pity/floor/spark counter และยอด Echo shard ต่อสาย |
| `POST /teams` | ต้อง | ต้อง | บันทึกทีม — ตั้ง `IsInUse` ให้สมาชิก |
| `DELETE /teams/{id}` | ต้อง | ต้อง | ลบทีม — ปลด `IsInUse` ให้ตัวที่ไม่ได้อยู่ทีมอื่น |
| `POST /expeditions/start` | ต้อง | ต้อง | สร้างรันใหม่ ปักเวอร์ชัน content ปัจจุบัน |
| `POST /expeditions/{id}/choose` | ต้อง | ต้อง | เดินไปจุดถัดไปหนึ่งจุด |
| `POST /expeditions/{id}/abandon` | ต้อง | ต้อง | หยุดรันกลางทาง (ดูหัวข้อการตัดสินใจด้านล่าง) |
| `GET /expeditions/{id}` | ต้อง | - | อ่านสถานะปัจจุบัน — คือ "resume" ฝั่งไคลเอนต์ |
| `POST /battle/run` | ต้อง | ไม่บังคับ | สนามทดลอง — ไม่จ่ายรางวัล, ตรวจ checksum ถ้าไคลเอนต์ส่งมา |
| `POST /debug/grant-gold` \| `grant-material` \| `grant-character` | ต้อง | ต้อง | เฉพาะ `Development`/`Debug:AllowGrants` — ดูหัวข้อ "สิ่งที่ตั้งใจตัดออก" |
| `GET /debug/character/{instanceId}` | ต้อง | - | อ่านสถานะตัวละครหนึ่งตัว — เฉพาะ `Development`/`Debug:AllowGrants` เช่นกัน |
| `GET /health` | ไม่ต้อง | - | liveness + เช็คว่าต่อฐานข้อมูลได้ |

ตารางนี้ไม่รวม endpoint ของหน้าจัดการ (`POST /admin/bootstrap`, `/admin/content/*`,
`/admin/telemetry/*`) — endpoint กลุ่มนั้นผ่าน gate การยืนยันตัวตนคนละแบบโดยสิ้นเชิง
(`X-Admin-Token`, ไม่ใช่ `Authorization: Bearer` ที่ตารางนี้พูดถึง) รายละเอียดเต็มของ
`/admin/content/*` อยู่ที่ [11-admin-spec.md](11-admin-spec.md); ของ `/admin/telemetry/*`
อยู่ในหัวข้อ "Telemetry ระดับตัวละคร" ด้านล่าง

## สัญญา idempotency (idempotency contract)

ทุก endpoint ที่แก้ยอดเศรษฐกิจของผู้เล่น (ทอง, วัตถุดิบ, ตัวละคร, ทีม, สถานะรันสำรวจ) ต้องมี
header `Idempotency-Key` เป็นสตริงที่ไคลเอนต์เลือกเอง (แนะนำ GUID ต่อการกดปุ่มหนึ่งครั้ง) และ
เรียกผ่านกลไกเดียวกันคือ `IdempotencyService.ExecuteAsync` (`Idempotency/IdempotencyService.cs`)
รวมทั้งสามตัว `/debug/grant-*` ด้วย (เดิมสามตัวนี้รับ header มาแล้วเมินเฉย — แก้แล้ว) —
ข้อยกเว้นเดียวที่ตั้งใจคือ `POST /battle/run` ตามที่หัวข้อ "สิ่งที่ตั้งใจตัดออก" ด้านล่างอธิบาย
เหตุผลไว้ (ไม่จ่ายรางวัล ความเสี่ยงจากคำขอซ้ำมีแค่แถว `battle_checksum_mismatches` ซ้ำ):

1. คีย์ `(accountId, endpoint, key)` ถูกจองด้วยแถวสถานะ `InProgress` **ในธุรกรรมเดียวกัน**
   กับที่ operation จริงจะรัน
2. operation รันแล้วผลลัพธ์ (status code + body) ถูกเขียนทับแถวเดิมเป็น `Completed`
   **ก่อน commit เดียวกัน** — ไม่มีช่องว่างที่ operation ทำสำเร็จแต่ผลลัพธ์ยังไม่ถูกบันทึก
3. ถ้า operation โยน exception ธุรกรรมทั้งก้อน (รวมการจองคีย์) ถูก rollback — คีย์กลับไป
   "ไม่เคยถูกใช้" คำขอถัดไปเริ่มสะอาดใหม่ ไม่มีการเขียนครึ่ง ๆ กลาง
4. คำขอสองอันที่แข่งกันด้วยคีย์เดียวกัน — **แม้จะพร้อมกันจริง ๆ** — ชนกันที่ primary key
   insert ของ Postgres เอง: อันที่แพ้ต้องรอ (หรือ fail ด้วย unique violation) แล้วอ่านผลลัพธ์
   ที่อันชนะบันทึกไว้กลับไปแทน ไม่มี application-level lock หรือ polling loop เลย
5. คีย์เดิมที่ส่งมาพร้อม body ที่ต่างไป (hash ของ body ไม่ตรง) ถูกปฏิเสธด้วย 409
   `idempotency_key_reused` — เพราะนั่นคือบั๊กฝั่งไคลเอนต์ ไม่ใช่การ retry ที่ถูกต้อง

ผลคือ: **operation รันสำเร็จได้อย่างมากที่สุดครั้งเดียวต่อคีย์หนึ่งคีย์** ไม่ว่าจะถูกเรียกกี่ครั้ง
หรือพร้อมกันแค่ไหน — มีเทสต์ยืนยันทั้งการเรียกซ้ำตามลำดับและพร้อมกันจริง (ดูหัวข้อการทดสอบ)

## ขอบเขตธุรกรรม (transaction boundaries)

ทุก endpoint ที่แก้ยอดเศรษฐกิจของผู้เล่น (รวมสามตัว `/debug/grant-*`) เปิด **ธุรกรรมเดียว**
ผ่าน `IdempotencyService.ExecuteAsync` แล้วส่ง `ApiDbContext` เดียวกันต่อให้ service ชั้นในใช้
(evolve, expedition, teams, ledger, debug grants) — ข้อยกเว้นเดียวคือ `POST /battle/run`
ตามที่อธิบายไว้ข้างต้น —
ไม่มี service ไหนเปิดธุรกรรมของตัวเองซ้อนเข้าไปอีกชั้น `LedgerService` เองก็ไม่เปิดธุรกรรม
ตั้งใจ เพราะสมมติว่ามันอยู่ในธุรกรรมของ caller เสมอ

จุดที่ต้องล็อกแถวก่อนอ่าน-แล้ว-เขียน (เพื่อกันสองคำขอแย่งวัตถุดิบ/สถานะเดียวกัน) ใช้ raw SQL
`SELECT ... FOR UPDATE` เรียงลำดับด้วย id เสมอ (ป้องกัน deadlock จากสองคำขอที่ล็อกคนละลำดับ):

- **Evolve**: ล็อกตัวที่จะปั้นและวัตถุดิบทุกตัวพร้อมกันก่อนเรียก `Evolution.Preview()` ครั้งที่สอง
  ถ้าวัตถุดิบตัวใดหายไประหว่างรอคิว (อีกคำขอใช้ไปแล้วและ commit ก่อน) จะเจอ "no longer exists"
  ทันทีที่ query กลับมา — นี่คือกรณี "สองคำขอแย่งวัตถุดิบเดียวกัน" ที่ต้องมีผู้ชนะแค่หนึ่งเดียว
- **Expedition choose/abandon**: ล็อกแถว `expedition_runs` ด้วย `FOR UPDATE` ก่อน clone
  แล้ว resolve บนสำเนา (`ExpeditionRun.Resume` สร้าง state ใหม่เสมอ ไม่แชร์ reference กับแถวเดิม)
  — เขียนทับแถวจริงก็ต่อเมื่อ `Choose()` คืนค่าโดยไม่โยน exception เท่านั้น ตรงตาม
  "known limitations" ที่ [09-expedition-spec.md](09-expedition-spec.md) กำหนดให้ชั้นเซิร์ฟเวอร์
  ต้องทำเอง

หมายเหตุ: `OwnedCharacterEntity.IsInUse` เป็น **flag ที่บำรุงรักษาเอง** (maintained,
ไม่ใช่คำนวณสดจาก join ทุกครั้งที่อ่าน) `TeamService` และ `ExpeditionService` เป็นสองที่เดียว
ที่ตั้ง/ปลดค่านี้ ในธุรกรรมเดียวกับการเปลี่ยนสมาชิกภาพที่ทำให้ค่ามันเปลี่ยน เหตุผล: การอ่าน
ตัวละครหนึ่งตัว (เช่นตอน preview evolve) ไม่ควรต้อง join ตารางทีมกับตารางรันทุกครั้ง —
ต้นทุนคือทุกจุดที่ปลดการใช้งานต้องจำเรียก `ReleaseIfUnusedElsewhereAsync`/ตรรกะเทียบเท่า
ให้ครบ ซึ่งมีเทสต์คลุมทั้งฝั่งทีมและฝั่งสำรวจ

## Evolve: ห้าขั้นตอนของ docs/03-evolve-spec.md

`EvolveService.EvolveAsync` ทำครบทั้งห้าข้อในธุรกรรมเดียว:

1. ตรวจความเป็นเจ้าของ (subject และวัตถุดิบทุกตัว)
2. ตรวจ `IsLocked`/`IsInUse` — ทำโดย `Evolution.Preview()` เอง (blocker list)
3. เรียก `Evolution.Preview()` — **ฟังก์ชันเดียวกับที่ `/evolve/preview` ใช้** เรียกอีกครั้งกับแถว
   ที่เพิ่งล็อกสด ๆ ก่อนหักอะไรจริง (ป้องกันไคลเอนต์ถือ preview เก่ามาหลอกระบบ)
4. หักทอง/ไอเทม/วัตถุดิบ และเขียนตัวใหม่ **ในธุรกรรมเดียว** (ผ่าน `LedgerService`)
5. บันทึก `EvolveHistoryEntity` — คีย์วัตถุดิบที่หายไปยังชี้กลับมาที่ประวัติได้เสมอ

`EvolvePreviewResponse.StatsAfter` มาจาก `Progression.ComputeStats(preview.Result)` ตัวเดียวกับ
ที่ `EvolvedCharacterResponse.StatsAfter` ใช้ — ไม่มีสูตรคู่ขนาน

## Summon: pull / spark-redeem / attune (docs/12-summon-spec.md)

`Summon/SummonService.cs` ทำเหมือน `EvolveService`: หนึ่ง operation เต็ม ๆ ต่อการเรียกหนึ่งครั้ง
อยู่ใน transaction เดียวที่ `IdempotencyService.ExecuteAsync` เปิดไว้แล้ว และไม่คำนวณกฎ pity/floor/
tier เองเลยแม้แต่บรรทัดเดียว — ทุกครั้งเรียก `DarkMyst.Sim.SummonEngine.ResolvePull` ซึ่งเป็นฟังก์ชัน
เดียวกับที่ `simrunner summon` วัดผล (ดูหัวข้อถัดไปว่าทำไมถึงอยู่ใน `DarkMyst.Sim` ไม่ใช่โค้ดใหม่ที่
คัดลอกกฎมาเขียนซ้ำ)

- **`POST /summon/pull`** — `{ count: 1 | 10 }` หักอัญมณีตามราคา
  `SummonService.PullPriceGemsPerPull` **(ยังไม่ล็อก — ดูย่อหน้าถัดไป)** แล้ววนสุ่มทีละครั้งด้วย
  `DeterministicRandom` ที่ seed จาก `RandomNumberGenerator` สดใหม่ทุกคำขอ (คาดเดาไม่ได้ต่อบัญชี
  ต่อคำขอ ไม่ใช่ seed ตายตัวแบบที่ battle ใช้เพื่อ replay ได้ — สองจุดประสงค์ต่างกัน) ต่อการสุ่มหนึ่ง
  ครั้ง: อัปเดต pity/floor counter ใน `SummonStateEntity`, ถ้าเป็นสายใหม่ (เช็คจาก `AccountLineEntity`
  — ดูย่อหน้าถัดไป) สร้าง `OwnedCharacterEntity` ขั้น 1 เลเวล 1 ผ่าน `LedgerService`, ถ้าซ้ำให้บวก
  Echo shard เข้า `EchoShardEntity` ตามอัตราของ `SummonRules.Proposed.DuplicateShardsForRarity`, และ
  เขียน `character_obtained` (source: `"summon"`) + `summon_pulled` ต่อการสุ่มหนึ่งครั้ง R5 ที่ยังไม่มี
  สายจริงพับลงมาเป็น R4 อัตโนมัติโดย `SummonEngine.PickFromPool` เอง (เหมือนที่ `simrunner summon`
  ทำ) ไม่มีทางโยน exception Spark แต้มไม่ถูกใช้อัตโนมัติแม้ถึงเพดาน — response คืน `canRedeemSpark`
  ให้ผู้เล่นกดแลกเองผ่าน endpoint ถัดไป
- **`POST /summon/spark-redeem`** — `{ lineId }` ปฏิเสธด้วย 409 ถ้า `SparkPoints` ต่ำกว่า
  `SummonRules.Proposed.SparkThreshold` (150) หรือ `lineId` ไม่ใช่สายขั้น-1 ที่สุ่มได้จริง สำเร็จแล้วหัก
  แต้ม Spark **เท่ากับ threshold พอดี** เศษที่เหลือยกไปสะสมต่อ ไม่รีเซ็ตเป็น 0 ให้ตัวละครหรือ shard
  เหมือน pull ทุกประการ แล้วเขียน `character_obtained` (source: `"spark"`)
- **`POST /summon/attune`** — `{ instanceId, shardsToSpend }` เลือกแบบ "ใช้เท่าที่ขอ" ไม่ใช่ "ใช้
  ทั้งหมดที่มี" (คอมโพสได้ง่ายกว่า) ปฏิเสธถ้าไม่ใช่เจ้าของ, ไม่ใช่ขั้น 1 ของสาย, หรือ `shardsToSpend`
  เกินยอดที่มี สำเร็จแล้วเพิ่ม `InheritedBonusPerMille` ทีละ `shardsToSpend / ShardsPerPerMille`
  (หารปัดลง) **ถูก clamp ด้วยเพดานที่อ่านจาก content
  (`ContentPack.Progression.Evolve.InheritedBonusCapPerMille`) เสมอ ไม่ hardcode 300** shard ที่เกิน
  ความจำเป็น (ทั้งเศษต่ำกว่า `ShardsPerPerMille` และส่วนที่เกินเพดาน) **ไม่ถูกหักออกจากยอดคงเหลือ** —
  ยังธนาคารอยู่ให้ใช้ครั้งหน้า เขียน event `attune_completed` ใหม่ (ดูตารางด้านล่าง)
- **`GET /summon/state`** — อ่านอย่างเดียว ไม่ต้อง Idempotency-Key คืน pity/floor/spark และยอด Echo
  shard ต่อสายทั้งหมดของบัญชี

**อัญมณี (`AccountEntity.Gems`)** เพิ่มเข้ามาพร้อมรอบนี้ — เป็นสกุลเงินแรกในเอพีไอที่มี endpoint จริง
ใช้จ่าย (ก่อนหน้านี้มีแต่ทอง) บำรุงรักษาเหมือน `Gold` ทุกประการผ่าน `LedgerService.ApplyGems` (เพิ่ม
`LedgerKind.Gems`) `/debug/grant-gems` เป็นตัวยืนแทนการซื้อจริง เกตด้วยเงื่อนไขเดียวกับ
`/debug/grant-*` ตัวอื่น (ดูหัวข้อ "สิ่งที่ตั้งใจตัดออก")

**ราคา `SummonService.PullPriceGemsPerPull = 150` ต่อครั้ง (10 ครั้ง = 10 เท่า ไม่มีส่วนลด) ยังไม่ล็อก**
— มีคอมเมนต์ `NOT LOCKED` ติดอยู่ที่ตัวค่าคงที่ใน `Summon/SummonService.cs` ชี้กลับมาที่
[docs/04-economy-spec.md](04-economy-spec.md) หัวข้อ "ตัวเลขที่ต้องมีก่อนล็อกเอกสารนี้" ข้อ 4
("สัดส่วนเวลาที่ลดลงถ้าจ่ายเงิน") — ตัวเลขนี้มีไว้ให้ endpoint มีอะไรให้หักทดสอบได้เท่านั้น ไม่ใช่ราคา
จริงที่ตัดสินใจแล้ว

**ทำไม `DarkMyst.Sim.SummonEngine` ไม่ใช่โค้ดคัดลอก:** `SummonSimulator` (batch statistics tool ของ
`simrunner summon`) เดิมมีตรรกะ "สุ่มหนึ่งครั้ง" ฝังอยู่ในลูปของมันเอง ถูกแยกออกมาเป็น
`SummonEngine.ResolvePull` (ฟังก์ชัน static บริสุทธิ์ รับ pity/floor counter + RNG คืน tier/character
ที่สุ่มได้จริงพร้อม counter หลังสุ่ม) แล้วให้ `SummonSimulator` เองเรียกกลับเข้าไปแทนของเดิม — ยืนยัน
ด้วย diff เอาต์พุตของ `simrunner summon` ก่อน/หลังว่าไบต์ต่อไบต์เหมือนเดิมทุกประการ บวกเทสต์ล็อก
(`SummonSimulatorTests.Run_output_for_Proposed_is_locked_at_a_fixed_seed`) `server/DarkMyst.Api` อ้าง
โปรเจกต์ `DarkMyst.Sim` อยู่แล้ว (`AdminSweepService` ใช้ `DarkMyst.Sim.SweepResult`) จึงเรียก
`SummonEngine` ตรง ๆ ไม่ต้องสร้างไลบรารีใหม่ — ผลคือมีกฎ pity/floor/tier ที่ implement ไว้ที่เดียวใน
รีโปทั้งหมด ทั้ง `simrunner summon` และ `POST /summon/pull` เรียกฟังก์ชันเดียวกัน

**ทำไม `AccountLineEntity` แทนที่จะ join `owned_characters` สด ๆ ทุกครั้ง:** ต้องเช็ค "เคยได้สายนี้
มาก่อนไหม" ทุกการสุ่มหนึ่งครั้ง การ join ตัวละครทุกตัวที่บัญชีถืออยู่กับ content pack เพื่อ resolve
`lineId` เป็น O(จำนวนตัวละครที่ถือ) ต่อการสุ่มหนึ่งครั้ง และโตขึ้นเรื่อย ๆ ตามอายุบัญชี จึงเลือกตาราง
เล็ก ๆ ที่ดูแลเองตอนที่แจกตัวละครขั้น-1 ตัวแรกของสาย (ที่ summon/spark-redeem เท่านั้น —
`Debug/DebugGrants.cs` ตั้งใจไม่แตะตารางนี้ เพราะเป็นตัวยืนสำหรับทดสอบ ไม่ใช่เส้นทางแจกจริง) แทน —
ดู `Data/Entities/AccountLineEntity.cs` remarks

## Expedition: สิ่งที่ server ต้องทำเองตามที่ library บอกไว้

`ExpeditionService` implement clone-then-commit pattern ตามที่
[09-expedition-spec.md](09-expedition-spec.md) กำหนด (ดูหัวข้อขอบเขตธุรกรรมด้านบน) และเพิ่ม:

- **รางวัลถูกให้จริงเมื่อรันจบเท่านั้น** (`Cleared`, `Failed`, หรือ `Abandoned` ที่ยัง
  `InProgress` อยู่) ไม่ใช่ทีละจุด — ของที่ "banked" ระหว่างทางยังเป็นแค่ตัวเลขใน
  `ExpeditionRunState` จนกว่ารันจะจบ ถึงตอนนั้นจึงแปลงเป็น `LedgerEntryEntity` จริงครั้งเดียว
  ป้องกันกรณีรันค้างครึ่งทางแล้วรางวัลถูกให้ไปแล้วบางส่วนซ้ำอีกทีตอน resume
- **Abandon คือการเก็บของที่สะสมไว้ พาตัวกลับบ้าน ไม่ใช่การเสียของทั้งหมด** — เอกสารไม่ได้กำหนด
  เรื่องนี้ตรง ๆ นี่คือการตัดสินใจของรอบนี้: เหตุผลเดียวกับที่
  `StageData.KeepRewardsOnDefeat` เลือก `true` เป็นค่าเริ่มต้น (docs/09) — การเสียของทั้งหมด
  จากการหยุดเล่นกลางทาง ไม่ได้ทำให้คนอยากกลับมาฟาร์มต่อ
- **Abandon บนรันที่จบไปแล้ว (Cleared/Failed) ถูกปฏิเสธ** ไม่ใช่ทำเงียบ ๆ ให้กลายเป็น
  "Abandoned" ทับสถานะเดิม — รันที่จบแล้วได้รับรางวัลของมันไปแล้วตอน `Choose()` ที่ทำให้มันจบ
- **การ resume ที่เวอร์ชัน content หายไปแล้วถูกปฏิเสธด้วย 409 `content_version_unavailable`**
  ไม่ใช่ 500 และไม่ใช่ refusal ทั่วไปแบบ `expedition_refused` — client แยกแยะได้ว่าเกิดอะไรขึ้นจริง
  (`ContentPackRegistry.Get` โยน `ContentVersionUnavailableException` ที่ปล่อยให้ลอยขึ้นไปตรง ๆ
  แทนที่จะถูกครอบด้วย refusal ทั่วไป)

## Battle: สนามทดลองและการจับผลเพี้ยน

`POST /battle/run` ใช้ `Progression.BuildUnit` / `Progression.BuildEncounterTeam` และ
`BattleSimulator.Run` ตรง ๆ — **ไม่คำนวณความเสียหายเอง** ไม่จ่ายรางวัลอะไรเลย (นี่คือ
"สนามทดลองที่คำนวณบนเครื่องได้ ไม่จ่ายรางวัล" ของ docs/04-economy-spec.md ที่ย้ายมาฝั่ง
เซิร์ฟเวอร์เพื่อให้ตรวจ checksum ได้) ถ้าไคลเอนต์ส่ง `clientChecksum` มาด้วยและไม่ตรงกับที่
เซิร์ฟเวอร์คำนวณได้ แถวหนึ่งถูกบันทึกลง `battle_checksum_mismatches` (คำขอทั้งก้อนถูกเก็บไว้
เพื่อสืบสวนย้อนหลังได้จริง ไม่ใช่แค่นับจำนวน) ตาม docs/07-testing-plan.md

## Telemetry ระดับตัวละคร

ต่อยอดจากหลักการใน [08-metrics.md](08-metrics.md) และ [12-summon-spec.md](12-summon-spec.md)
หัวข้อ "Telemetry ระดับตัวละคร" — เป้าหมายคือหาตัวละครที่แรงเกิน/อ่อนเกินไว้ปรับสมดุลทีหลัง
(กรอบความนิยม × ความแรงของ docs/12) **ไม่ใช่การทำแดชบอร์ด** จึงเก็บเฉพาะ event ดิบ ไม่มีเลข
สรุปที่คำนวณไว้ล่วงหน้าเก็บถาวรที่ไหนเลย

### ตาราง `telemetry_events`

คอลัมน์: `id` (identity), `account_id` (FK → `accounts`, **`ON DELETE CASCADE`** — ลบบัญชีแล้ว
telemetry ของบัญชีนั้นหายไปด้วย ตรงตาม docs/08-metrics.md ข้อ 4 "ต้องลบได้จริงเมื่อผู้เล่นขอลบ
บัญชี"), `type` (string), `occurred_at` (UTC ฝั่งเซิร์ฟเวอร์เสมอ ไม่รับจากไคลเอนต์), `content_version`,
`rules_version`, `payload` (`jsonb`) ดัชนีสองตัว: `(type, occurred_at)` สำหรับ export ตาม
ช่วงเวลา และ `(account_id, occurred_at)` สำหรับ cascade/query ต่อบัญชี **append-only** — ไม่มี
โค้ด update หรือ delete แถวนี้ที่ไหนเลยนอกจาก cascade ข้างต้น

`Telemetry/TelemetryWriter.cs` คือที่เดียวที่สร้างแถวนี้ — `Add(...)` แค่แปะ entity ลง
`ApiDbContext` ตัวเดียวกับที่ endpoint นั้นใช้อยู่แล้ว **ไม่เรียก `SaveChangesAsync` เอง**
เหมือน `LedgerService` ทุกประการ ดังนั้น event จะ commit พร้อมกับการเปลี่ยนสถานะเกมที่มันพูดถึง
เสมอ ใน transaction เดียวกัน (มักจะเป็น transaction ของ `IdempotencyService.ExecuteAsync`) —
event มีอยู่ก็ต่อเมื่อการเปลี่ยนสถานะนั้น commit จริง ไม่มีช่องว่างให้มี event ลอย ๆ ที่ไม่มี
อะไรเกิดขึ้นจริง หรือมีการเปลี่ยนสถานะที่ไม่มี event อธิบาย คำขอซ้ำที่ `IdempotencyService`
เจอทาง fast path (คืนผลที่เคย commit ไปแล้ว) จึงไม่เรียก `operation()` ซ้ำ และไม่มี event ซ้ำ
ตามไปด้วย — พิสูจน์ตรงด้วยเทสต์ (`TelemetryTests.Replaying_the_same_evolve_confirm_request_does_not_duplicate_the_event`)
เช่นเดียวกัน การ refuse (เช่น evolve ที่ blocker ไม่ผ่าน) จบก่อนถึงจุดที่เขียน event เสมอ —
ไม่มี event ของการกระทำที่ไม่ได้เกิดขึ้นจริง

### รายการ event

| `type` | เขียนที่ไหน | payload |
| --- | --- | --- |
| `character_obtained` | ทุกจุดที่สร้าง `OwnedCharacterEntity` ใหม่: รางวัล expedition (`ExpeditionService.SettleAsync`) และ `POST /debug/grant-character` | `{instanceId, characterId, lineId, rarity, evolveStage, source: "expedition"\|"debug", runId?, stageId?}` — `source` เป็น string เผื่อขยายเป็น `"summon"` ในอนาคตโดยไม่ต้อง migrate |
| `expedition_started` | `POST /expeditions/start` | `{runId, stageId, members: [{instanceId, characterId, lineId, evolveStage, level, focus}]}` — สัญญาณ "ถูกเลือกลงทีม ต่อด่าน" ของ docs/12 |
| `battle_finished` | ทุกการต่อสู้ที่เซิร์ฟเวอร์เป็นคนตัดสิน: node การต่อสู้ใน expedition (`ExpeditionService.ChooseAsync`) และ `POST /battle/run` | `{context: "expedition"\|"battle_run", runId?, stageId?, encounterId, outcome: "win"\|"loss"\|"draw", turns, members: [...]}` — draw นับแยกจาก loss ในนี้ (ต่างจากกติกาความคืบหน้าของ expedition ที่ถือว่า draw คือแพ้) เพื่อไม่ให้ telemetry กลืนข้อมูลจริงทิ้ง |
| `evolve_completed` | `POST /evolve/confirm` (เฉพาะที่สำเร็จ) | `{instanceId, fromCharacterId, toCharacterId, lineId, fromStage, toStage, inheritedBonusPerMilleAfter, materialInstanceIds, sameLineMaterialCount}` |
| `team_saved` | `POST /teams` | `{teamId, members: [...]}` |
| `summon_pulled` | `POST /summon/pull` (ทุกครั้งที่สุ่ม แม้อยู่ใน batch 10 ครั้ง) | `{bannerId, pullIndex, pityCounterBefore, tier, characterId, lineId, isDuplicate, shardsGranted}` — `bannerId` เป็น `"default"` เสมอในรอบนี้ (Banner ใบเดียว) `pityCounterBefore` คือค่า pity counter **ก่อน**การสุ่มครั้งนี้ (ค่าที่ป้อนให้ `SummonEngine.ResolvePull`) |
| `attune_completed` | `POST /summon/attune` (ทุกครั้งที่สำเร็จ แม้ใช้ shard ไป 0 เพราะเต็มเพดานแล้ว) | `{instanceId, lineId, shardsSpent, inheritedBonusPerMilleBefore, inheritedBonusPerMilleAfter}` — `shardsSpent` คือจำนวนที่ถูกหักจริง (คูณด้วย `ShardsPerPerMille` เสมอ) ซึ่งอาจน้อยกว่า `shardsToSpend` ที่ขอ (ส่วนต่างยังธนาคารอยู่ ไม่ได้ถูกหัก) |

`lineId` คือ `CharacterData.LineId` ที่มีอยู่แล้วในเนื้อหา (จัดกลุ่มทุกขั้น evolve ของสายเดียวกัน
เข้าด้วยกัน) — resolve จาก content pack ของเวอร์ชันที่ตัวละครนั้นถืออยู่ตอนเขียน event
(`Telemetry/TelemetryContentResolver.cs`) ถ้าเวอร์ชันนั้นหาไม่เจอ (กรณีสุดขั้วที่ทฤษฎีจะไม่เกิด
เพราะ `ContentPackRegistry` ยังไม่ retire เวอร์ชันไหนเลยในรอบนี้) จะ fallback ไปเวอร์ชันล่าสุด
แทนที่จะทำให้การเขียนเกมพัง — telemetry เป็นช่องทางรอง (side channel) ไม่ใช่กฎเกม จึงไม่ยอมให้
การหาสายตัวละครพังแล้วลากการกระทำจริงของผู้เล่นพังตามไปด้วย

`summon_pulled` และ `attune_completed` เขียนจริงแล้วตั้งแต่รอบที่สร้าง `POST /summon/*` — ดูหัวข้อ
"Summon: pull / spark-redeem / attune" ด้านบน

### Admin — อ่าน telemetry

Gate เดียวกับ `/admin/content/*` (`AdminAuth`, `X-Admin-Token` แยกจาก token ผู้เล่นโดยสิ้นเชิง —
ดู [11-admin-spec.md](11-admin-spec.md)) endpoint กลุ่มนี้ **อ่านอย่างเดียว** ไม่มีทางเขียนแถว
`telemetry_events` เลย

- `GET /admin/telemetry/events?type=&since=&until=&limit=&after=` — export ดิบ, keyset
  pagination บน `id` (แถวใหม่ไม่ทำให้หน้าถัดไปข้ามหรือซ้ำ ต่างจาก offset pagination)
  `limit` ค่าเริ่มต้น 500 สูงสุด 5000
- `GET /admin/telemetry/lines?contentVersion=` — ต่อหนึ่งสาย stage-I: จำนวนที่ได้มา, บัญชีที่
  ถืออยู่จริง (distinct), อัตราถูกเลือกลงทีมต่อด่าน, อัตราชนะเมื่อมี vs ไม่มีสายนั้นในทีม
  (ต่อ encounter และรวม พร้อม `n`, `accounts` — จำนวนบัญชี distinct ที่อยู่เบื้องหลัง `n`
  การต่อสู้นั้น — และเลเวลเฉลี่ยของทั้งสองกลุ่ม เพื่อให้เห็น level confounding — ตรงกับที่
  `docs/12` เตือนไว้ว่าต้องมีทั้งความนิยมและความแรงถึงจะบัฟถูกตัว) **นับเฉพาะ
  `battle_finished` ที่ `context == "expedition"`** — `/battle/run` (`context: "battle_run"`)
  เป็นสนามทดลองที่เล่นซ้ำได้ไม่จำกัดและไม่จ่ายรางวัล ถ้านับรวมด้วย ผู้เล่นคนเดียวสแปมทีมเดียว
  กับสนามนั้นจะเอียงอัตราชนะทั้งกลุ่มได้ event ของ `battle_run` ยังถูกเขียนและนับรวมเป็น
  "เคยใช้งาน" สำหรับตัวเลขได้มาแต่ไม่เคยใช้ด้านล่าง แค่ไม่เข้าอัตราชนะ `lowSample: true`
  เมื่อฝั่งใดฝั่งหนึ่ง `n < 30` **หรือ** `accounts < 5` — ตัวเลข `n` สูงอย่างเดียวหลอกได้ถ้ามัน
  มาจากผู้เล่นหนักไม่กี่คนเล่นซ้ำ ๆ, จำนวน evolve สำเร็จ, จำนวนครั้งที่เป็น evolve แรกของบัญชี,
  จำนวนที่ได้มาแต่ไม่เคยใช้ (ไม่เคยอยู่ใน `expedition_started`/`battle_finished` เลย — รวม
  `battle_run` ด้วย) พร้อม median วันจากได้มาถึงใช้ครั้งแรก (เฉพาะที่เคยถูกใช้) — **ทุกตัวเลข
  คำนวณสดจาก event ดิบตอนอ่าน ไม่มีอะไรถูกเก็บล่วงหน้า** ตามหลักการข้อ 1 ของ
  docs/08-metrics.md `contentVersion` ค่าเริ่มต้นคือเวอร์ชันปัจจุบัน
  (`ContentPackRegistry.LatestVersion`) และ response จะบอกเสมอว่าครอบคลุม
  เวอร์ชันไหนบ้าง (ไม่มีการผสมหลายเวอร์ชันเข้าด้วยกันแบบเงียบ ๆ)

## การเชื่อมบัญชี (account linking)

`docs/04-economy-spec.md` กำหนดกรณีที่ต้องมีขั้นตอนชัดเจน: guest บนเครื่องใหม่เชื่อมกับ
บัญชีถาวรที่มีเซฟเดิมอยู่แล้ว — `LinkingService` แบ่งเป็นสองขั้น:

1. `StartAsync` — ถ้า identity ไม่เคยถูกเชื่อมมาก่อน เชื่อมทันที (`Linked`) ถ้าเชื่อมกับ
   บัญชีอื่นอยู่แล้ว **ไม่แตะทั้งสองบัญชี** สร้าง `PendingLinkEntity` แล้วคืนสรุปทั้งสองเซฟ
   (`AccountSaveSummary`: เลเวลสูงสุด, จำนวนตัวละคร, เวลาเล่นล่าสุด — ตรงตามที่ docs/04
   กำหนดให้แสดง) ให้ผู้เล่นเลือก
2. `ConfirmAsync` — บัญชีที่แพ้ถูกตั้งเป็น `Superseded` และ token ถูกล้าง (authenticate ไม่ได้
   อีกต่อไป) **แต่ไม่มีแถวไหนถูกลบเลย** — ตัวละคร วัตถุดิบ ทอง ของบัญชีที่แพ้ยังอยู่ครบ
   ตรงตาม "เก็บเซฟที่ถูกทิ้งไว้ระยะหนึ่งเผื่อเลือกผิด"

Bearer token ของบัญชีที่ชนะถูกส่งกลับตรง ๆ ใน response ของ `confirm` — ดูหัวข้อ
"สิ่งที่ตั้งใจตัดออก" ว่าทำไม

## Bearer-token stub

`AccountAuth.RequireAccountAsync` (`Auth/AccountAuth.cs`) เช็คแค่ว่า token (สุ่ม 256 บิต
เก็บในตาราง `accounts`) ตรงกับแถวไหนไหม — ไม่มีการเซ็นหรือ decode JWT ใด ๆ `IIdentityProvider`
เป็น interface ที่ `StubIdentityProvider` เชื่อ external id ที่ไคลเอนต์ส่งมาตรง ๆ (ไม่มีการ
ตรวจสอบกับ Apple/Google จริง) **จุดที่ตั้งใจให้แทนที่ได้โดยไม่กระทบส่วนอื่น:**

- `IIdentityProvider.Resolve` — เปลี่ยนเป็นตรวจ token จริงกับ Apple/Google เมื่อพร้อม
  ทุกอย่างหลังจากนั้น (`LinkingService`) ไม่เปลี่ยน เพราะมันเห็นแค่ `ExternalIdentity`
  ที่ resolve แล้วเท่านั้น
- `AccountAuth` — เปลี่ยนจาก "token สุ่มที่เก็บในตาราง" เป็นตรวจลายเซ็น JWT ได้โดยไม่ต้อง
  แก้ endpoint ไหนเลย เพราะทุก endpoint เรียกผ่านฟังก์ชันเดียวนี้

## สิ่งที่ตั้งใจตัดออกในรอบนี้ (deliberately deferred)

- **ไม่มีระบบซื้อในแอปจริง** (`docs/04-economy-spec.md` เอง: ตัวเลขเศรษฐกิจยังไม่ล็อกจนกว่าจะ
  ได้ข้อมูลจากระยะ C) — `Debug/DebugGrants.cs` เป็นตัวแทนชั่วคราวสำหรับให้ทดสอบ/สาธิตวงจร
  evolve, expedition และตอนนี้รวม summon ได้แบบ end-to-end โดยไม่ต้องมีระบบร้านค้า สี่ตัวคือ
  `POST /debug/grant-gold` (เพิ่มทอง), `POST /debug/grant-gems` (เพิ่มอัญมณี — เพิ่มเข้ามาพร้อมกับ
  summon เพราะเป็น endpoint แรกที่ใช้อัญมณีจริง), `POST /debug/grant-material` (เพิ่มวัตถุดิบ) และ
  `POST /debug/grant-character` (สร้างตัวละครใหม่ให้บัญชี — **ไม่**เขียน `AccountLineEntity`
  ตั้งใจ ดูหัวข้อ Summon ด้านบน) แต่ละตัวแก้ยอดเศรษฐกิจจริงเหมือน
  endpoint ปกติ จึงเรียกผ่าน `IdempotencyService.ExecuteAsync` เหมือนกันทุกประการ (ไม่ใช่
  ทางลัดที่มองข้ามคีย์ที่ไคลเอนต์ส่งมา) — คู่กันมี `GET /debug/character/{instanceId}` อ่าน
  อย่างเดียว ไว้ให้เทสต์/curl ตรวจสถานะตัวละครหลังการกระทำ ทั้งห้า endpoint ถูกปิดไว้นอก
  `Development` ด้วยเงื่อนไขเดียวกันใน Program.cs: `app.Environment.IsDevelopment() ||
  Debug:AllowGrants` — ค่าเริ่มต้นใน `appsettings.json` คือ `Debug:AllowGrants=false`
  (`appsettings.Development.json` เปิดเป็น `true` สำหรับรันแบบ dev/localhost เท่านั้น)
  **ไม่ใช่ endpoint ที่จะเข้าสู่ production** — เป็นตัวยืนแทนร้านค้า/gacha ที่ระยะ E จะสร้างจริง
  เท่านั้น
- **ไม่มี server-side verification ของ Apple/Google/AdMob จริง** — `IIdentityProvider` และ
  webhook รับซื้อ/โฆษณาเป็นงานของระยะ E ตาม roadmap
- **ไม่มีการ retire content version ออกจากหน่วยความจำ** — `ContentPackRegistry` เก็บทุกเวอร์ชัน
  ที่เคยโหลดไว้ตลอดอายุ process (ดู remarks ในไฟล์นั้น) ปลอดภัยที่สเกลนี้ ผิดที่สเกลใหญ่กว่านี้
- **ไม่มี endpoint login ผ่าน identity provider แยกจาก link** — token ของบัญชีที่ชนะการเชื่อม
  ถูกส่งกลับตรง ๆ ใน response ของ `/accounts/link/confirm` แทน เพราะยังไม่มีขั้นตอน "เข้าสู่ระบบ
  ด้วยบัญชีถาวรที่มีอยู่แล้ว" ที่เป็นอิสระจากการเชื่อม — งานของระยะ E เมื่อ real auth เข้ามา
- **battle/run ไม่บังคับ Idempotency-Key** — มันไม่แก้ยอดเศรษฐกิจใด ๆ (ไม่จ่ายรางวัล) ความเสี่ยง
  เดียวคือแถว `battle_checksum_mismatches` ซ้ำ ซึ่งไม่กระทบความถูกต้องของบัญชีผู้เล่น

## การทดสอบ

`tests/DarkMyst.Api.Tests/` รันกับ PostgreSQL จริงเสมอ ไม่มี mock/InMemory:

- **ฐานข้อมูลทิ้งได้ต่อคลาสเทสต์** (`Infra/PostgresTestDatabase.cs`) — สร้างด้วย
  `CREATE DATABASE` จาก role ที่มีสิทธิ์ `CREATEDB` แล้ว migrate สดทุกครั้ง (พิสูจน์เกณฑ์ผ่าน
  ข้อ 4 "migration ใช้กับฐานข้อมูลเปล่าได้" ไปในตัวทุกครั้งที่รันเทสต์)
- **โฮสต์จริงผ่าน `WebApplicationFactory<Program>`** (`Infra/ApiFactory.cs`) — คือ
  `Program.cs` ตัวเดียวกับที่ `dotnet run` ใช้ ทั้ง migration ตอน startup, exception mapping,
  และทุก endpoint
- เคสยากที่ยืนยันด้วยการยิง request จริงพร้อมกัน (`Task.WhenAll`, ไม่ใช่ทีละคำขอ):
  คำขอซ้ำ (idempotency) ทั้งเรียงลำดับและพร้อมกันจริง, evolve สองคำขอแย่งวัตถุดิบเดียวกัน,
  choose สองคำขอซ้ำกันบนรันเดียวกัน, การ resume ข้ามเวอร์ชัน content
- **การกระทบยอด (reconciliation)**: รวมยอด `ledger_entries` แล้วเทียบกับยอดทอง/ไอเทม/
  การมีอยู่ของตัวละครจริง หลังลำดับการกระทำผสม (grant → evolve → expedition)
- **`/health`** (`HealthTests.cs`): เคสฐานข้อมูลต่อได้ยิงผ่าน HTTP จริงเหมือนเคสอื่นทั้งหมด
  ส่วนเคสต่อไม่ได้เรียก `HealthCheck.CheckAsync` ตรง ๆ ด้วย `ApiDbContext` ที่ชี้ไปยัง
  connection string ที่ไม่มีอะไรฟังอยู่ (ไม่ใช่ mock — เป็น `ApiDbContext` จริงที่ต่อไม่ติดจริง) —
  ไม่ผ่าน `WebApplicationFactory` เพราะ Program.cs รัน migration ตอน startup ก่อน endpoint ไหน
  จะพร้อมรับคำขอ ฐานข้อมูลที่ต่อไม่ได้จริง ๆ ทำให้ startup ล้มเหลวไปก่อนที่ `/health` จะถูกเรียกได้
  ด้วยซ้ำ — ข้อจำกัดที่มีอยู่แล้วในการออกแบบ startup ไม่ใช่สิ่งที่รอบนี้แก้

`tests/DarkMyst.Api.Tests/` เพียงโปรเจกต์เดียวมี **80 เคส** ณ ตอนที่เพิ่ม summon เข้ามา
(`SummonTests.cs` — 17 เคสใหม่ ครอบคลุมการสุ่มตรงกับ `SummonEngine` ที่ seed กำหนดได้เฉพาะเทสต์,
ตัวซ้ำได้ shard ไม่ได้ตัวละครที่สอง, คำขอซ้ำ/พร้อมกันจริงไม่แจกซ้ำ/หักซ้ำ, เงินไม่พอ refuse สะอาด,
spark-redeem ใต้เพดาน/เกินเพดาน/เศษยกยอด, attune ตรวจความเป็นเจ้าของ/ขั้น/เพดาน 300‰ และธนาคาร
เศษต่ำกว่า 4 shard, `GET /summon/state` ตรงกับลำดับการกระทำ) ก่อนหน้านั้นมี 63 เคสตอนที่เพิ่ม
telemetry เข้ามา (`TelemetryTests.cs`, `AdminTelemetryLinesTests.cs` — 16 เคส ครอบคลุมทุก event
type, คำขอซ้ำ/การ refuse ไม่เขียน event ซ้ำ/ไม่เขียนเลย, gate ของ `/admin/telemetry/*`, pagination
ไม่มีช่องว่างไม่ซ้ำ, เลขคำนวณของ `/admin/telemetry/lines` ตรงกับ fixture ที่สร้างมือ และ cascade
delete) ตัวเลขรวมทั้งรีโป (กฎเกมใน `DarkMyst.Sim.Tests` ฯลฯ) เปลี่ยนบ่อยกว่าที่เอกสารนี้จะตามทัน
— รันคำสั่ง `dotnet test` เองเพื่อดูตัวเลขปัจจุบันแทนที่จะเชื่อเอกสาร ตามหลักที่ `HANDOFF.md`
ยึดถือ อีก 25 เคสในโปรเจกต์เดียวกันเป็นของหน้าจัดการ (`Admin/`, gate การยืนยันตัวตนแยกต่างหาก,
publish/validate/rollback ของทั้งสี่ชนิดเนื้อหา, ปุ่ม sweep) รายละเอียดอยู่ที่
[11-admin-spec.md](11-admin-spec.md)
