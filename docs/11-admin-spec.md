# หน้าจัดการเนื้อหา (Admin) — ระยะ D

`docs/05-content-pipeline.md` กำหนด workflow ไว้ตรง ๆ: **แก้ → validate → sweep → ตรวจทาน →
เผยแพร่ → (ย้อนกลับได้)** เอกสารนี้อธิบายเครื่องมือที่สร้างขึ้นเพื่อทำ workflow นั้นให้จบ
(`06-roadmap.md` ระยะ D รายการสุดท้าย) และสิ่งที่ตั้งใจเลื่อนออกไป

**`content/` ยังเป็นแหล่งความจริงชุดเดียวเหมือนเดิม** หน้าจัดการไม่ใช่แหล่งข้อมูลชุดที่สอง —
มันแก้ไฟล์ JSON ชุดเดียวกับที่เซิร์ฟเวอร์และ CI อ่าน ผ่าน `ContentPack.Load()` /
`ContentPack.Validate()` ตัวเดียวกันทุกครั้ง ไม่มีโมเดลเนื้อหาคู่ขนาน ไม่มีตาราง Postgres
ที่เก็บสำเนาเนื้อหาเกม (ตาราง `content_publishes` เก็บแค่ **ใครเผยแพร่อะไรเมื่อไร** — ดู
หัวข้อ audit trail ด้านล่าง)

## โครงสร้าง

```
src/DarkMyst.Sim/        BattleSweepRunner — ไลบรารี sweep ที่ simrunner และ AdminSweepService ใช้ร่วมกัน
server/DarkMyst.Api/
  Admin/                 AdminAccountService, AdminContentService, AdminSweepService, AdminDtos
  Auth/AdminAuth.cs      gate การยืนยันตัวตนของ admin — แยกจาก AccountAuth โดยสิ้นเชิง
  Content/ContentRootPath.cs   directory เดียวกับที่ ContentPackRegistry โหลดจากตอน startup
admin/                   หน้าจัดการ (Vue 3 + TypeScript, Vite)
  src/api.ts             fetch wrapper ที่ type ตรงกับ DTO ฝั่งเซิร์ฟเวอร์
  src/components/        LoginPanel, {Characters,Skills,Enemies,Encounters}Workspace + EditForm ของแต่ละอัน, SweepPanel
  src/lib/diff.ts         diff ฝั่ง client แบบทั่วไป (unit test ได้ ไม่พึ่ง backend)
  e2e/                    Playwright — ดูหัวข้อการทดสอบ
```

## Endpoint ทั้งหมด

| Endpoint | Auth | Idempotency-Key | หน้าที่ |
| --- | --- | --- | --- |
| `POST /admin/bootstrap` | ไม่ต้อง (ปิดนอก `Development`) | ไม่ต้อง | ออก admin token ใหม่ — ดูหัวข้อ gate ด้านล่าง |
| `GET /admin/content/current` | ต้อง (admin) | - | เวอร์ชันปัจจุบัน, `rulesVersion`, ตัวละครทั้งหมด, รายการเวอร์ชันที่ย้อนกลับได้ |
| `POST /admin/content/validate` | ต้อง | ไม่ต้อง | ตรวจรายการตัวละครที่แก้แล้วด้วย `ContentPack.Validate()` เดียวกันทุกที่ ไม่เขียนอะไร |
| `POST /admin/content/publish` | ต้อง | **ต้อง** | เผยแพร่เวอร์ชันใหม่ — ปฏิเสธถ้า validate ไม่ผ่าน ไม่มี flag ข้าม |
| `POST /admin/content/rollback` | ต้อง | **ต้อง** | คืนค่า `content/` กลับไปเวอร์ชันก่อนหน้าที่ยังมี snapshot |
| `GET /admin/content/versions` | ต้อง | - | ประวัติการเผยแพร่/ย้อนกลับ (ใคร เมื่อไร หมายเหตุอะไร) |
| `GET /admin/content/diff?from=&to=` | ต้อง | - | ต่างกันตรงไหนระหว่างสองเวอร์ชัน (เพิ่ม/ลบ/เปลี่ยน — **ตัวละครเท่านั้น**, ดูหัวข้อขอบเขต) |
| `POST /admin/content/sweep` | ต้อง | ไม่ต้อง | รัน battle sweep กับ content ที่เผยแพร่อยู่ตอนนี้ — ดูหัวข้อปุ่ม sweep ด้านล่าง |

`POST /admin/content/validate` และ `POST /admin/content/sweep` ไม่ต้อง `Idempotency-Key` ด้วย
เหตุผลเดียวกับ `POST /evolve/preview` (docs/10-backend-spec.md): ทั้งคู่ไม่เขียนอะไรเลย เรียกซ้ำ
กี่ครั้งก็ปลอดภัย (sweep เขียนแค่หน่วยความจำชั่วคราวของ request เดียว ไม่แตะ `content/` หรือ
ฐานข้อมูลเลย)

## Gate การยืนยันตัวตนของ admin — แยกจาก player โดยสิ้นเชิง

`Auth/AdminAuth.cs` **ไม่ใช้กลไกเดียวกับ `AccountAuth`** โดยตั้งใจ:

- อ่านคนละ header (`X-Admin-Token`) ไม่ใช่ `Authorization: Bearer` — client ที่ forward
  bearer token ของผู้เล่นไปทุก request จะไม่มีอะไรให้ gate นี้อ่านเลยด้วยซ้ำ
- ค้นในคนละตาราง (`admin_accounts`) ไม่ใช่ `accounts` — แม้ client จะ **จงใจ** copy token
  ผู้เล่นไปใส่ header `X-Admin-Token` ก็ยังหาไม่เจอ (token เป็นสุ่ม 256 บิต โอกาสชนกันจริง
  แทบเป็นศูนย์อยู่แล้ว แต่โครงสร้างตารางแยกคือสิ่งที่พิสูจน์ได้จริง ไม่ใช่แค่โอกาสต่ำ)

พิสูจน์ทั้งสองข้อโดยตรงใน `tests/DarkMyst.Api.Tests/AdminAuthTests.cs`

การออก admin token (`POST /admin/bootstrap`) เป็นการ stub ที่ตรงไปตรงมาเหมือนที่
`StubIdentityProvider` stub ตัวตนผู้เล่น — ไม่มีการตรวจสอบจริงกับผู้ให้บริการภายนอกใด ๆ
สิ่งที่ **ไม่ได้** stub คือตัว gate เอง: ทุก endpoint อื่นต้องมี token ที่มีแถวจริงในตารางนี้
เสมอ `POST /admin/bootstrap` เองถูกปิดไว้นอก `Development` ด้วยเงื่อนไขเดียวกับ
`/debug/grant-*` ทุกประการ: `app.Environment.IsDevelopment() || Admin:AllowBootstrap`
(ค่าเริ่มต้นใน `appsettings.json` คือ `false`) **ไม่ใช่ endpoint ที่จะเข้าสู่ production** —
real admin SSO (Apple/Google-style) เป็นงานระยะ E เช่นเดียวกับ real player SSO
(ดูหัวข้อ "สิ่งที่ตั้งใจตัดออก")

## สัญญาการเผยแพร่ (publish) และการย้อนกลับ (rollback)

### ไม่มี flag ข้าม validate

`AdminContentService.BuildStagedPack` ประกอบ `ContentPack` ในหน่วยความจำจากเนื้อหาที่แก้แล้ว
(ตัวละคร และ/หรือ สกิล/ศัตรู/encounter — ดูหัวข้อขอบเขตด้านล่างว่าทำไมโดยปกติมีแค่ชนิดเดียวต่อ
คำขอ) บวกไฟล์อื่นทุกไฟล์ที่อ่านตรงจาก `content/` แล้วเรียก `ContentPack.Load` (ซึ่งเรียก
`Validate()` ในตัวเสมอ) — ทั้งปุ่ม validate และปุ่ม publish เดินโค้ดเส้นเดียวกันนี้ทุกครั้ง
ไม่ว่าจะแก้เนื้อหาชนิดไหน ถ้า `Validate()` โยน `ContentException` การเผยแพร่ถูกปฏิเสธด้วย
`422 content_invalid` พร้อมรายการปัญหาทีละข้อ (`ContentException.Problems`)
**ไม่มีทางลัดในโค้ดที่ข้ามการเรียกนี้ได้เลย**

### เผยแพร่ทำอะไรบ้าง

1. คำนวณเวอร์ชันใหม่จากเวอร์ชันปัจจุบัน (`AdminContentService.NextUnusedVersion` — ดูหัวข้อ
   บั๊กด้านล่างว่าทำไมไม่ใช่แค่ +1 patch เฉย ๆ)
2. เก็บสำเนาเวอร์ชันที่กำลังจะถูกทับไว้ที่ `content/_history/<เวอร์ชันเดิม>/` ถ้ายังไม่เคยเก็บ
3. เขียน `manifest.json` เสมอ บวกไฟล์ของ**เฉพาะชนิดที่คำขอนี้ส่งมา**ทับ `content/` จริง —
   คำขอที่แก้แค่สกิลจะไม่แตะ `characters.json`/`enemies.json`/`encounters.json` เลย (พิสูจน์
   ตรงด้วยเทสต์ `Editing_one_type_leaves_the_others_file_untouched_on_disk`) — **`content/`
   คือที่เดียวที่ถูกเขียน** ไม่มีสำเนาที่สอง
4. เก็บสำเนาเวอร์ชันใหม่ไว้ที่ `content/_history/<เวอร์ชันใหม่>/` ด้วย (ให้ย้อนกลับมาหาได้อีก
   แม้จะมีการเผยแพร่ทับต่อไปอีกหลายรอบ)
5. โหลด `ContentPack` ใหม่จากดิสก์จริง (ไม่ใช่จากลิสต์ตัวละครในหน่วยความจำของ request) แล้ว
   ลงทะเบียนกับ `ContentPackRegistry` ที่มีอยู่แล้ว ตั้งเป็น `Latest` — เซิร์ฟเวอร์เริ่มเสิร์ฟ
   เวอร์ชันใหม่ทันทีโดยไม่ต้อง restart
6. บันทึกแถว `ContentPublishEntity` (audit trail — ใครเผยแพร่ เวอร์ชันไหน จากเวอร์ชันไหน
   เมื่อไร)

ทั้งหมดนี้เรียกผ่าน `IdempotencyService.ExecuteAsync` ตัวเดียวกับทุก endpoint ที่แก้ข้อมูล
(docs/10-backend-spec.md) — `Idempotency-Key` ซ้ำกับ body เดิมจะได้ผลลัพธ์เดิมกลับมาโดยไม่รัน
ซ้ำ, key ซ้ำกับ body ต่างกันถูกปฏิเสธด้วย `409 idempotency_key_reused` เหมือน endpoint อื่น
ทุกประการ

### ย้อนกลับทำอะไรบ้าง

หาสำเนาเวอร์ชันเป้าหมายจาก `ContentPackRegistry` ที่โหลดอยู่แล้ว หรือถ้าเป็นเวอร์ชันที่หายไป
จากหน่วยความจำแล้ว (เช่น process เพิ่ง restart) โหลดใหม่จาก `content/_history/<เวอร์ชัน>/`
(ซึ่ง `Validate()` ซ้ำอัตโนมัติผ่าน `ContentPack.LoadFromDirectory`) เก็บสำเนาเวอร์ชันปัจจุบัน
ไว้ก่อน (เผื่อย้อนกลับมาใหม่ในอนาคต) แล้วเขียนไฟล์ของเวอร์ชันเป้าหมายทับ `content/` จริง
ตั้ง `Latest` กลับไป และบันทึก audit แถวใหม่ (`Kind = Rollback`)

ย้อนกลับไปเวอร์ชันที่ไม่มี snapshot เลย (ไม่เคยถูกเผยแพร่หรือย้อนกลับผ่านเครื่องมือนี้) ถูก
ปฏิเสธด้วย `409 content_version_unavailable` — โค้ดเดียวกับที่ docs/10-backend-spec.md ใช้กับ
การ resume expedition ข้ามเวอร์ชันที่หายไป

### รายการ "ย้อนกลับไปได้" ไม่ใช่ audit log

`AdminContentCurrentResponse.RollbackableVersions` มาจาก**รายชื่อโฟลเดอร์ snapshot ใน
`content/_history/`** ไม่ใช่จากตาราง `content_publishes` — เพราะเวอร์ชันแรกสุดที่เครื่องมือนี้
เจอตอนติดตั้งครั้งแรกไม่เคยมีใคร "เผยแพร่" มันเลย (มันมีอยู่ก่อนเครื่องมือนี้จะมีตัวตนด้วยซ้ำ)
จึงไม่มีแถว audit ของตัวเอง แต่การเผยแพร่ครั้งแรกจะ snapshot เวอร์ชันเดิมไว้ก่อนเสมอ (ขั้นตอน
เผยแพร่ข้อ 2 ด้านบน) ทำให้มันย้อนกลับไปหาได้ทันทีที่มีการเผยแพร่ครั้งแรกเกิดขึ้น audit log
(`GET /admin/content/versions`) ยังอยู่แยกต่างหากในหน้าจัดการ — ใช้ตอบ "ใครทำอะไรเมื่อไร"
ไม่ใช่ตอบ "ย้อนกลับไปตรงไหนได้บ้าง"

### บั๊กที่พบจากการทดสอบจริง ไม่ใช่จากการอ่านโค้ด

**การคำนวณเวอร์ชันใหม่แบบ +1 patch เฉย ๆ ชนกันได้หลังวงจร เผยแพร่ → ย้อนกลับ → เผยแพร่อีกครั้ง**
เพราะย้อนกลับดึงตัวชี้ "เวอร์ชันล่าสุด" ถอยหลังไปเวอร์ชันเก่ากว่า แล้วการเผยแพร่ครั้งถัดไปบวก
หนึ่งจากเวอร์ชันเก่านั้น ได้เลขเดียวกับเวอร์ชันที่เคยถูกเผยแพร่ไปแล้วและถูกย้อนกลับทิ้ง —
`ContentPackRegistry.Register` ปฏิเสธด้วย "already registered" (เทสต์
`AdminContentTests` จับได้ตอนรันทั้งคลาสพร้อมกัน ไม่ใช่ตอนรันทีละเทสต์) แก้ด้วย
`NextUnusedVersion` ที่ข้ามเลขเวอร์ชันทุกตัวที่เครื่องมือนี้ยังรู้จักอยู่ (ทั้งใน registry
และใน `content/_history/`) ก่อนจะคืนเลขที่ไม่ซ้ำจริง ๆ

## ขอบเขตของรอบนี้ — ตัวละคร, สกิล, ศัตรู, encounter และปุ่ม sweep

รอบก่อนหน้าทำแค่ตัวละคร ตามลำดับความสำคัญที่กำหนดไว้ตอนนั้น (กลไก publish/rollback ที่ถูกต้อง
มาก่อน, หน้าเว็บที่ใช้งานได้จริงกับเนื้อหาหนึ่งชนิดมาที่สอง, Playwright มาที่สาม) รอบนี้ขยาย
ตามลำดับความสำคัญที่ตั้งไว้ใหม่:

1. **ปุ่ม sweep** — ดูหัวข้อถัดไปสำหรับการตัดสินใจออกแบบและขอบเขต
2. **สกิลและศัตรู** — สองชนิดที่แก้บ่อยที่สุดรองจากตัวละคร
3. **Encounter**
4. ~~ด่าน/ตารางรางวัล/เหตุการณ์ (`stages.json`)~~ — **ยังไม่ทำรอบนี้** รูปทรงซับซ้อนที่สุด
   (ชั้น ๆ ในด่าน แต่ละชั้นมีน้ำหนักสุ่มของ 4 ชนิดโหนด และสระ id ที่ต้องมีให้สุ่ม, ตารางรางวัล
   ที่มีน้ำหนักและชนิดรางวัลต่างกัน, เหตุการณ์ที่มีผลลัพธ์หลายแบบ) ยังแก้ผ่าน JSON ตรง ๆ
   เหมือนเดิม — เลือกทำสามอย่างแรกให้ถูกต้องดีกว่าทำสี่อย่างแบบเร่งรีบ

`POST /admin/content/validate`/`publish` รับ `characters`, `skills`, `enemies`, `encounters`
เป็น field แยกกัน (ทุกตัว nullable) — **โดยปกติมีแค่ field เดียวไม่เป็น null ต่อคำขอหนึ่งครั้ง**
(แท็บไหนแก้อยู่ ก็ส่งแค่ field ของแท็บนั้น) field ที่เป็น `null` แปลว่า "ไม่แก้ อ่านจากดิสก์ตรง ๆ"
(`AdminContentService.BuildStagedPack`) field ที่ **ไม่ใช่** null ต้องเป็น**รายการทั้งหมดของชนิด
นั้นเสมอ** ไม่ใช่ patch บางส่วน — สืบทอดกติกาเดิมจากตัวละคร: ทุกคำขอถูกตรวจเป็น `ContentPack`
เต็มรูปแบบเหมือนแก้ไฟล์ JSON ของชนิดนั้นด้วยมือ ไม่มีสถานะ "แก้บางส่วน" ที่ server ต้อง merge เอง
และ publish เขียนทับเฉพาะไฟล์ของชนิดที่ส่งมาเท่านั้น (พิสูจน์ด้วยเทสต์ว่าการแก้สกิลไม่แตะ
`characters.json`/`enemies.json`/`encounters.json` เลย)

`GET /admin/content/diff` (เทียบสองเวอร์ชันที่เผยแพร่แล้ว) **ยังเป็นตัวละครเท่านั้น** — ไม่ได้
ขยายไปสกิล/ศัตรู/encounter รอบนี้ เพราะไม่ใช่เกณฑ์ผ่านที่กำหนดไว้ (แค่ต้องพิสูจน์ edit → validate
→ publish → rollback ต่อชนิด ไม่ใช่ต้องมี version-compare ต่อชนิด) แผงเปรียบเทียบสองเวอร์ชันใน
หน้าเว็บก็ยังอยู่แค่แท็บ Characters ด้วยเหตุผลเดียวกัน — บันทึกไว้เป็นงานที่ตั้งใจตัดออกด้านล่าง

## ปุ่ม sweep — การตัดสินใจออกแบบ

`docs/05-content-pipeline.md` ระบุ sweep เป็นขั้นหนึ่งของ workflow เผยแพร่ (แก้ → validate →
**sweep** → ตรวจทาน → เผยแพร่) รอบก่อนหน้าเลื่อนมันไปเพราะการรัน `simrunner` จากเซิร์ฟเวอร์ต้องมี
การตัดสินใจก่อน: รันเป็น subprocess หรือพอร์ตโค้ดมาเป็น library เรียกตรง — รอบนี้เลือก **พอร์ต
โค้ดมาเป็น library**

**เหตุผล:** การรัน subprocess จากใน request path ของเว็บเซิร์ฟเวอร์เป็นคำถามด้าน resource และ
security ที่ไม่คุ้มจะเปิด — ต้องคุม argv ที่ผ่านไปให้ปลอดภัย (command injection), คุมจำนวน
process ที่ spawn พร้อมกันแยกจากการคุมความถี่ระดับโค้ด, และ parse stdout กลับมาเป็นข้อมูลแทนที่
จะได้ object ตรง ๆ ในหน่วยความจำเดียวกัน ในขณะที่โค้ด sweep ของ `simrunner` (ลูปยิงการต่อสู้
หลาย seed แล้วนับผล) กับสิ่งที่ปุ่มเว็บต้องการนั้นแทบจะเหมือนกันทุกประการอยู่แล้ว — เก็บเป็นสอง
implementation แยกกันแล้วต้องคอยแก้ให้ตรงกันด้วยมือมีความเสี่ยงกว่าการรวมเป็นตัวเดียว

**สิ่งที่ทำ:** ดึงลูป sweep ออกมาเป็น `src/DarkMyst.Sim/BattleSweepRunner.cs` — ไลบรารีใหม่
(netstandard2.1 อ้างอิงแค่ `DarkMyst.Combat`/`DarkMyst.Content` ไม่แตะ `DarkMyst.Expedition` เพราะ
`simrunner sweep` เดิมก็ไม่เคยแตะเช่นกัน — sweep คือลูปต่อสู้เดี่ยว ไม่ใช่การเล่นทั้งด่านสำรวจ)
`tools/DarkMyst.SimRunner` เองก็เรียกไลบรารีนี้แล้วพิมพ์ข้อความแบบเดิมทุกตัวอักษร (พิสูจน์ด้วย
มือ: รัน `sweep --encounter enc_tutorial_hounds --repeat 20 --seed 5` เทียบกับก่อนแก้)
`server/DarkMyst.Api/Admin/AdminSweepService.cs` เรียกไลบรารีเดียวกันจากฝั่งเซิร์ฟเวอร์

**การจำกัดขอบเขต (bound) สองชั้น** เพราะ sweep กินซีพียูมาก (ยิงการต่อสู้เต็มรูปแบบซ้ำหลายร้อย
ครั้งต่อคำขอ) และเข้าถึงได้จากปุ่มในเบราว์เซอร์ ไม่เหมือนบรรทัดคำสั่งที่ต้องพิมพ์เอง:

1. **จำกัดจำนวน repeat** — `Admin:MaxSweepRepeat` (ค่าเริ่มต้น 500 ใน `appsettings.json` — เลข
   repeat ที่มากที่สุดที่ `docs/07-testing-plan.md` มีตัวอย่างให้รันมือ) คำขอที่ขอมากกว่านี้ถูก
   ปฏิเสธด้วย `400 sweep_repeat_too_large` **ก่อน**การต่อสู้แม้แต่ครั้งเดียวจะรัน ไม่ใช่ถูกตัด
   (clamp) แบบเงียบ ๆ — หน้าเว็บอ่านค่า `max` จาก response แล้วปรับช่องกรอกให้เองแทนที่จะให้กด
   ซ้ำแล้วโดนปฏิเสธซ้ำ
2. **รันได้ทีละหนึ่ง sweep ทั้งกระบวนการ (process-wide)** — `SemaphoreSlim` ตัวเดียว รอด้วย
   timeout ศูนย์ (`WaitAsync(0, ct)`) กดปุ่มซ้ำสองครั้ง เปิดสองแท็บ หรือมี admin สองคนกดพร้อมกัน
   คำขอที่สองถูกปฏิเสธทันทีด้วย `409 sweep_in_progress` **ไม่ใช่เข้าคิวรอ** และไม่ใช่ปล่อยให้รัน
   ควบคู่กันแล้วซีพียูโดนใช้ซ้ำสอง — เลือกปฏิเสธตรง ๆ เพราะที่สเกลทีมเล็กของเครื่องมือนี้ กด
   ปุ่มใหม่อีกทีหลัง sweep แรกเสร็จนั้นถูกกว่าการสร้างกลไกคิวที่ไม่มีใครต้องการจริง ๆ

`AdminSweepService.RunAsync` ยังรับ `CancellationToken` ผ่านต่อไปยัง `BattleSweepRunner.Run`
(เช็กทุกก่อนจะยิงการต่อสู้ครั้งถัดไป) — ถ้า client ตัดการเชื่อมต่อ (ปิดแท็บ, เน็ตหลุด) ระหว่าง
sweep ยาว ๆ กำลังรัน ลูปจะหยุดกลางทางแทนที่จะรันจนจบทั้ง repeat โดยไม่มีใครรออยู่แล้ว

## หน้าจัดการ (Vue 3 + TypeScript)

`admin/` เป็นแอปเดียว ไม่มี router, ไม่มี state library แยก — หลัง login แล้วเป็นแถบแท็บ
(Characters / Skills / Enemies / Encounters / Sweep) แต่ละแท็บโหลด/แก้/เผยแพร่ของตัวเองอิสระ:

- **LoginPanel.vue** — เก็บ admin token ไว้ใน `localStorage`, วางของเดิมได้หรือกด "Create dev
  admin" (เรียก `/admin/bootstrap`, ใช้ได้เฉพาะตอน `Admin:AllowBootstrap` เปิดอยู่)
- **CharactersWorkspace.vue** + **CharacterEditForm.vue** — เหมือนรอบก่อน: แก้สแตทพื้นฐาน/
  growth, รายชื่อสกิล (คั่นด้วยจุลภาค), flavor text, แผง pending-changes, validate/publish,
  รายการ "roll back to", audit log, และตัวเทียบสองเวอร์ชัน (from/to — ตัวละครเท่านั้น)
- **SkillsWorkspace.vue** + **SkillEditForm.vue** — แก้ trigger, ความน่าจะเป็นที่จะติด (‰),
  cooldown/initial cooldown, leader flag เป็นฟอร์มจริง; `effects` (รายการ ~20 field ต่อชิ้น
  คาบเกี่ยว 5 enum ที่ไม่เกี่ยวกัน) แก้เป็นข้อความ JSON ดิบแทน — parse ฝั่ง client ก่อนส่งไปเลย
  (เห็น error ทันทีถ้า JSON ผิดรูป) แล้วยังผ่าน `ContentPack.Validate()` เดียวกันทุกครั้งเหมือน
  field อื่น ทำฟอร์มโครงสร้างเต็มรูปแบบให้ทุก effect kind ไม่ทันในรอบนี้
- **EnemiesWorkspace.vue** + **EnemyEditForm.vue** — โครงสร้างเต็มรูปแบบเหมือนตัวละคร (ชื่อ,
  affinity แบบ dropdown, สแตท, รายชื่อสกิล)
- **EncountersWorkspace.vue** + **EncounterEditForm.vue** — โครงสร้างเต็มรูปแบบ พร้อมตาราง
  เพิ่ม/ลบ unit (enemy id, slot, stat scale ‰)
- **SweepPanel.vue** — เลือก encounter (จากรายการ encounter จริงของ pack), roster (ค่าเริ่มต้น
  คือตัวละครห้าตัวแรกของ pack), level/seed/repeat, กดรัน แล้วเห็นอัตราชนะ/ความยาวเฉลี่ย/อัตรา
  รอดต่อตัวละครจริงจาก `POST /admin/content/sweep` — ปรับช่อง repeat ให้เองถ้าเซิร์ฟเวอร์ปฏิเสธ
  เพราะเกินขีดจำกัด
- **lib/diff.ts** — `diffItem`/`diffList` (ทั่วไปสำหรับอะไรก็ได้ที่มี `id`) บวก
  `diffCharacter`/`diffCharacterList` เดิม (ตอนนี้แค่ห่อ `diffItem`/`diffList`) ใช้ตัวเดียวกัน
  ทุกแท็บสำหรับแผง pending-changes และเทสต์หน่วย (`tests/diff.spec.ts`, `tests/diffList.spec.ts`)

`VITE_API_BASE_URL` (ค่าเริ่มต้น `http://localhost:5099`) กำหนดว่าแอปคุยกับ API ตัวไหน — ทำให้
build เดียวกันชี้ไปเซิร์ฟเวอร์ dev, เซิร์ฟเวอร์ที่ CI สร้างขึ้น หรือเซิร์ฟเวอร์ที่ Playwright
คุมเองได้โดยไม่ต้อง build ใหม่

## การทดสอบ

| ชุด | จำนวน | ครอบคลุม |
| --- | --- | --- |
| `AdminAuthTests` (dotnet, Postgres จริง) | 5 | player token เข้า admin gate ไม่ได้ทั้งสองแบบ (header ผิด, header ถูกแต่ token ผิด), token หายไปเลยถูกปฏิเสธ, admin token จริงผ่าน, สอง admin ได้ token ไม่ซ้ำกัน |
| `AdminContentTests` (dotnet, Postgres จริง) | 8 | validate ผ่าน/ไม่ผ่าน (ตัวละคร), publish เขียนเวอร์ชันใหม่ลงดิสก์+registry+audit จริง, publish idempotent ตาม key ซ้ำ, publish เนื้อหาเสียถูกปฏิเสธและไม่แตะดิสก์, rollback คืนดิสก์+registry จริง (รวมพิสูจน์ว่าเวอร์ชันตั้งต้นย้อนกลับได้แม้ไม่มี audit แถว), rollback ไปเวอร์ชันที่ไม่มี snapshot ถูกปฏิเสธด้วย 409, diff รายงาน field ที่เปลี่ยนถูกต้อง |
| `AdminContentTypesTests` (dotnet, Postgres จริง) | 7 | edit→publish→rollback ต่อชนิด (สกิล, ศัตรู, encounter) พิสูจน์ด้วยการอ่าน `content/*.json` บนดิสก์ตรง ๆ; publish ปฏิเสธสกิลที่ตัวละครยังอ้างถึงอยู่, ศัตรูที่ไม่มี guaranteed turn action, encounter ที่ไม่มี unit; การแก้ชนิดหนึ่งไม่แตะไฟล์ของชนิดอื่นเลยบนดิสก์ |
| `AdminSweepTests` (dotnet, Postgres จริง) | 5 | sweep จริงคืนตัวเลขจริง, repeat เกิน cap ถูกปฏิเสธก่อนรันแม้แต่ครั้งเดียว, sweep ที่สองขณะตัวแรกยังรันถูกปฏิเสธ (พิสูจน์แบบไม่พึ่งจังหวะเวลาจริง — เรียก `AdminSweepService` ตรง ไม่ผ่าน HTTP), encounter ที่ไม่มีจริงถูกปฏิเสธเป็น `content_invalid`, player bearer token เข้าไม่ได้ |
| `admin/tests/*.spec.ts` (vitest) | 24 | `diffCharacter`/`diffCharacterList`/`diffItem`/`diffList` (field เดี่ยว, array เป็น leaf เดียว, null vs blank, ชนิดทั่วไปที่มี `id`) และฟอร์มแก้ไขทั้งสี่ (`CharacterEditForm`, `SkillEditForm` รวม parse/error ของช่อง effects JSON, `EnemyEditForm` รวมค่า Affinity dropdown ที่ถูกต้อง, `EncounterEditForm` รวมเพิ่ม/ลบ unit) — v-model แก้ object จริงทุกตัว |
| `admin/e2e/*.spec.ts` (Playwright, Chromium จริง) | 10 | วงจรเต็ม แก้→validate→publish→rollback ต่อชนิด (ตัวละคร, สกิล, ศัตรู, encounter) พิสูจน์ด้วยการอ่านไฟล์ `content/` บนดิสก์จริง; publish เนื้อหาเสียถูกปฏิเสธต่อชนิดทั้งใน UI และ response; player bearer token เข้า admin API ไม่ได้ทั้งสองรูปแบบ header; sweep จริงคืนตัวเลขจริง; repeat เกิน cap ถูกปฏิเสธและช่องกรอกถูกปรับให้เท่ากับ max จริงจากเซิร์ฟเวอร์ แล้วรันซ้ำที่ค่านั้นสำเร็จ |

รวมของหน้าจัดการ: **25 เคส dotnet (172 รวมทั้งโปรเจกต์) + 24 เคส vitest + 10 เคส Playwright**

ทุกเทสต์ Playwright รันกับกระบวนการจริงล้วน: `DarkMyst.Api` (dotnet run จริง),
PostgreSQL ฐานข้อมูลทิ้งต่อรัน (`admin/e2e/run-api.sh` สร้าง/ลบเอง ไม่แตะฐานข้อมูล dev
ที่ใช้ร่วมกัน), สำเนา `content/` แยกต่างหาก (ไม่แตะ `content/` จริงในรีโปเด็ดขาด), Vite dev
server จริง, Chromium จริง (`/opt/pw-browsers/chromium` ในแซนด์บ็อกซ์นี้, ดาวน์โหลดจริงใน CI —
ดู `admin/playwright.config.ts`)

## สิ่งที่ตั้งใจตัดออกในรอบนี้ (deliberately deferred)

- **real admin SSO (Apple/Google-style)** — `POST /admin/bootstrap` เป็น stub ตรงไปตรงมาแบบ
  เดียวกับที่ `StubIdentityProvider` stub ฝั่งผู้เล่น เลื่อนไปพร้อมกับ real player SSO ที่ระยะ E
  (docs/10-backend-spec.md, docs/06-roadmap.md) — **gate เองไม่ใช่ของที่เลื่อน** มันถูกสร้างให้
  ถูกต้องตั้งแต่รอบนี้ (ดูหัวข้อ gate ด้านบน)
- **ด่านสำรวจ/ตารางรางวัล/เหตุการณ์ (`stages.json`) ในหน้าจัดการ** — รูปทรงซับซ้อนที่สุดในทุก
  ชนิดเนื้อหา (ชั้นในด่านแต่ละชั้นมีน้ำหนักสุ่ม 4 ชนิดโหนด, สระ id ที่ต้องมีให้สุ่มต่อชนิด,
  ตารางรางวัลที่มีน้ำหนักและชนิดรางวัลหลายแบบ, เหตุการณ์ที่มีผลลัพธ์หลายแบบ) ยังแก้ผ่าน JSON
  ตรง ๆ เหมือนเดิม — ตามลำดับความสำคัญของรอบนี้ (ปุ่ม sweep > สกิล/ศัตรู > encounter > นี่)
- **diff แบบภาพ (rendered side-by-side) และ diff ของสกิล/ศัตรู/encounter** — `GET
  /admin/content/diff` เทียบสองเวอร์ชันได้แค่ตัวละครเหมือนรอบก่อน เป็นรายการ field ที่เปลี่ยน
  พร้อมค่าก่อน/หลัง ไม่ใช่ตัวเปรียบเทียบภาพสวยงาม และยังไม่ขยายไปสามชนิดใหม่ — ไม่ใช่เกณฑ์ผ่าน
  ที่กำหนดไว้ (edit→validate→publish→rollback ต่อชนิด ไม่ใช่ version-compare ต่อชนิด)
- **ฟอร์มโครงสร้างเต็มรูปแบบสำหรับ skill effects** — `SkillEditForm.vue` แก้ trigger/cooldown/
  activation chance/leader flag เป็นฟอร์มจริง แต่ `effects` (~20 field ต่อชิ้น คาบเกี่ยว 5 enum:
  `EffectKind`, `TargetSelector`, `DamageKind`, `Stat`, `StackRule`) แก้เป็นข้อความ JSON ดิบ
  แทน — parse และแสดง error ฝั่ง client ก่อนส่งไปเลย แต่ไม่ใช่ dropdown/ฟอร์มย่อยต่อ field
- **การ retire snapshot เก่าออกจาก `content/_history/`** — เหมือนที่
  `ContentPackRegistry` ไม่เคยปลดเวอร์ชันออกจากหน่วยความจำ (docs/10-backend-spec.md) ทุก
  เวอร์ชันที่เคยเผยแพร่ยังอยู่ในดิสก์ตลอดไป ปลอดภัยที่สเกลนี้ ผิดที่สเกลใหญ่กว่านี้
- **การล็อกระดับฐานข้อมูลสำหรับ publish/rollback พร้อมกันข้าม process** — ใช้แค่ process-wide
  lock (`SemaphoreSlim` เดียว ไม่ใช่ `SELECT ... FOR UPDATE` แบบที่ `EvolveService`/
  `ExpeditionService` ใช้กับสถานะผู้เล่น) เพราะนี่คือการกระทำที่ทำน้อยครั้งโดยทีมเล็ก ไม่ใช่
  การแข่งขันความถี่สูงของผู้เล่นจำนวนมาก ถ้าเซิร์ฟเวอร์ขยายเป็นหลาย process การเผยแพร่สอง
  ครั้งพร้อมกันจากคนละ process ยังชนกันได้ — บันทึกไว้ตรง ๆ ไม่ใช่ปกปิด (ปุ่ม sweep ใหม่ก็ใช้
  `SemaphoreSlim` แบบเดียวกัน ข้อจำกัดเดียวกันนี้จึงใช้กับมันด้วย)
- **การ retire snapshot เก่าออกจาก `content/_history/`** — เหมือนที่
  `ContentPackRegistry` ไม่เคยปลดเวอร์ชันออกจากหน่วยความจำ (docs/10-backend-spec.md) ทุก
  เวอร์ชันที่เคยเผยแพร่ยังอยู่ในดิสก์ตลอดไป ปลอดภัยที่สเกลนี้ ผิดที่สเกลใหญ่กว่านี้
- **การล็อกระดับฐานข้อมูลสำหรับ publish/rollback พร้อมกันข้าม process** — ใช้แค่ process-wide
  lock (`SemaphoreSlim` เดียว ไม่ใช่ `SELECT ... FOR UPDATE` แบบที่ `EvolveService`/
  `ExpeditionService` ใช้กับสถานะผู้เล่น) เพราะนี่คือการกระทำที่ทำน้อยครั้งโดยทีมเล็ก ไม่ใช่
  การแข่งขันความถี่สูงของผู้เล่นจำนวนมาก ถ้าเซิร์ฟเวอร์ขยายเป็นหลาย process การเผยแพร่สอง
  ครั้งพร้อมกันจากคนละ process ยังชนกันได้ — บันทึกไว้ตรง ๆ ไม่ใช่ปกปิด
