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
  Ledger/                      บันทึกการเคลื่อนไหวทอง/ไอเทม/ตัวละครแบบ append-only
  Idempotency/                 กลไกกลางที่ endpoint ที่แก้ข้อมูลทุกตัวต้องผ่าน
  Debug/                       endpoint แจกของสำหรับทดสอบ/สาธิต (ดูหัวข้อ "สิ่งที่ตั้งใจตัดออก")
  Auth/                        bearer-token stub (player) + gate ของ admin (AdminAuth.cs)
  Admin/                       หน้าจัดการเนื้อหา — ดู 11-admin-spec.md
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

ตารางนี้ไม่รวม endpoint ของหน้าจัดการ (`POST /admin/bootstrap`, `/admin/content/*`) —
endpoint กลุ่มนั้นผ่าน gate การยืนยันตัวตนคนละแบบโดยสิ้นเชิง (`X-Admin-Token`, ไม่ใช่
`Authorization: Bearer` ที่ตารางนี้พูดถึง) รายละเอียดเต็มอยู่ที่
[11-admin-spec.md](11-admin-spec.md)

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
  evolve และ expedition ได้แบบ end-to-end โดยไม่ต้องมีระบบร้านค้า สามตัวคือ
  `POST /debug/grant-gold` (เพิ่มทอง), `POST /debug/grant-material` (เพิ่มวัตถุดิบ) และ
  `POST /debug/grant-character` (สร้างตัวละครใหม่ให้บัญชี) แต่ละตัวแก้ยอดเศรษฐกิจจริงเหมือน
  endpoint ปกติ จึงเรียกผ่าน `IdempotencyService.ExecuteAsync` เหมือนกันทุกประการ (ไม่ใช่
  ทางลัดที่มองข้ามคีย์ที่ไคลเอนต์ส่งมา) — คู่กันมี `GET /debug/character/{instanceId}` อ่าน
  อย่างเดียว ไว้ให้เทสต์/curl ตรวจสถานะตัวละครหลังการกระทำ ทั้งสี่ endpoint ถูกปิดไว้นอก
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

รวม **129 (กฎเกม รวม `DarkMyst.Sim.Tests`) + 43 (API, integration ทั้งหมดกับ Postgres จริง)
= 172 เคส** — 18 เคสข้างต้นเป็นของ endpoint ในเอกสารนี้ อีก 25 เคสเป็นของหน้าจัดการ (`Admin/`,
gate การยืนยันตัวตนแยกต่างหาก, publish/validate/rollback ของทั้งสี่ชนิดเนื้อหา, ปุ่ม sweep)
อยู่ในไฟล์ทดสอบเดียวกัน (`tests/DarkMyst.Api.Tests/`) แต่รายละเอียดอยู่ที่
[11-admin-spec.md](11-admin-spec.md)
