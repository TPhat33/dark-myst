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
server/DarkMyst.Api/
  Admin/                 AdminAccountService, AdminContentService, AdminDtos
  Auth/AdminAuth.cs      gate การยืนยันตัวตนของ admin — แยกจาก AccountAuth โดยสิ้นเชิง
  Content/ContentRootPath.cs   directory เดียวกับที่ ContentPackRegistry โหลดจากตอน startup
admin/                   หน้าจัดการ (Vue 3 + TypeScript, Vite)
  src/api.ts             fetch wrapper ที่ type ตรงกับ DTO ฝั่งเซิร์ฟเวอร์
  src/components/        LoginPanel, CharactersWorkspace, CharacterEditForm
  src/lib/diff.ts         diff ฝั่ง client (unit test ได้ ไม่พึ่ง backend)
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
| `GET /admin/content/diff?from=&to=` | ต้อง | - | ต่างกันตรงไหนระหว่างสองเวอร์ชัน (เพิ่ม/ลบ/เปลี่ยนตัวละคร) |

`POST /admin/content/validate` ไม่ต้อง `Idempotency-Key` ด้วยเหตุผลเดียวกับ `POST /evolve/preview`
(docs/10-backend-spec.md): มันไม่เขียนอะไรเลย เรียกซ้ำกี่ครั้งก็ปลอดภัย

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

`AdminContentService.BuildStagedPack` ประกอบ `ContentPack` ในหน่วยความจำจากตัวละครที่แก้แล้ว
บวกไฟล์อื่นทุกไฟล์ที่อ่านตรงจาก `content/` แล้วเรียก `ContentPack.Load` (ซึ่งเรียก
`Validate()` ในตัวเสมอ) — ทั้งปุ่ม validate และปุ่ม publish เดินโค้ดเส้นเดียวกันนี้ทุกครั้ง
ถ้า `Validate()` โยน `ContentException` การเผยแพร่ถูกปฏิเสธด้วย `422 content_invalid` พร้อม
รายการปัญหาทีละข้อ (`ContentException.Problems`) **ไม่มีทางลัดในโค้ดที่ข้ามการเรียกนี้ได้เลย**

### เผยแพร่ทำอะไรบ้าง

1. คำนวณเวอร์ชันใหม่จากเวอร์ชันปัจจุบัน (`AdminContentService.NextUnusedVersion` — ดูหัวข้อ
   บั๊กด้านล่างว่าทำไมไม่ใช่แค่ +1 patch เฉย ๆ)
2. เก็บสำเนาเวอร์ชันที่กำลังจะถูกทับไว้ที่ `content/_history/<เวอร์ชันเดิม>/` ถ้ายังไม่เคยเก็บ
3. เขียน `manifest.json` และไฟล์ตัวละครที่แก้แล้วทับ `content/` จริง — **`content/` คือที่เดียว
   ที่ถูกเขียน** ไม่มีสำเนาที่สอง
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

## ขอบเขตของรอบนี้ — ตัวละครเท่านั้น

หน้าจัดการรอบนี้แก้ได้แค่ `characters.json` ตามลำดับความสำคัญที่กำหนดไว้: กลไก
publish/rollback ที่ถูกต้องมาก่อน, หน้าเว็บที่ใช้งานได้จริงกับเนื้อหาหนึ่งชนิดมาที่สอง,
Playwright มาที่สาม เนื้อหาชนิดอื่น (สกิล ศัตรู ด่าน) ปุ่ม sweep และ diff แบบภาพ อยู่ในหัวข้อ
ถัดไป

ทุกคำขอ validate/publish ส่ง**รายการตัวละครทั้งหมด**กลับไป ไม่ใช่ patch บางส่วน — ผลคือทุก
คำขอถูกตรวจเป็น `ContentPack` เต็มรูปแบบเหมือนแก้ `characters.json` ด้วยมือ ไม่มีสถานะ
"แก้บางส่วน" ที่ server ต้อง merge เอง

## หน้าจัดการ (Vue 3 + TypeScript)

`admin/` เป็นแอปเดียว ไม่มี router, ไม่มี state library แยก — ขอบเขตคือหนึ่งหน้าจอหลังผ่าน login
เดียว จึงยังไม่ต้องมี:

- **LoginPanel.vue** — เก็บ admin token ไว้ใน `localStorage`, วางของเดิมได้หรือกด "Create dev
  admin" (เรียก `/admin/bootstrap`, ใช้ได้เฉพาะตอน `Admin:AllowBootstrap` เปิดอยู่)
- **CharactersWorkspace.vue** — รายชื่อตัวละคร, แผง "pending changes" (คำนวณฝั่ง client ด้วย
  `lib/diff.ts` ก่อนแม้แต่จะเรียก validate), ปุ่ม validate/publish, รายการ "roll back to",
  audit log, และตัวเทียบสองเวอร์ชัน (from/to)
- **CharacterEditForm.vue** — แก้สแตทพื้นฐาน/growth, รายชื่อสกิล (คั่นด้วยจุลภาค), flavor text
- **lib/diff.ts** — diff ฝั่ง client แบบเดียวกับ `AdminContentService.DiffJson` ฝั่งเซิร์ฟเวอร์
  ใช้ตัวเดียวกันทั้งแผง pending-changes และเทสต์หน่วย (`tests/diff.spec.ts`)

`VITE_API_BASE_URL` (ค่าเริ่มต้น `http://localhost:5099`) กำหนดว่าแอปคุยกับ API ตัวไหน — ทำให้
build เดียวกันชี้ไปเซิร์ฟเวอร์ dev, เซิร์ฟเวอร์ที่ CI สร้างขึ้น หรือเซิร์ฟเวอร์ที่ Playwright
คุมเองได้โดยไม่ต้อง build ใหม่

## การทดสอบ

| ชุด | จำนวน | ครอบคลุม |
| --- | --- | --- |
| `AdminAuthTests` (dotnet, Postgres จริง) | 5 | player token เข้า admin gate ไม่ได้ทั้งสองแบบ (header ผิด, header ถูกแต่ token ผิด), token หายไปเลยถูกปฏิเสธ, admin token จริงผ่าน, สอง admin ได้ token ไม่ซ้ำกัน |
| `AdminContentTests` (dotnet, Postgres จริง) | 8 | validate ผ่าน/ไม่ผ่าน, publish เขียนเวอร์ชันใหม่ลงดิสก์+registry+audit จริง, publish idempotent ตาม key ซ้ำ, publish เนื้อหาเสียถูกปฏิเสธและไม่แตะดิสก์, rollback คืนดิสก์+registry จริง (รวมพิสูจน์ว่าเวอร์ชันตั้งต้นย้อนกลับได้แม้ไม่มี audit แถว), rollback ไปเวอร์ชันที่ไม่มี snapshot ถูกปฏิเสธด้วย 409, diff รายงาน field ที่เปลี่ยนถูกต้อง |
| `admin/tests/*.spec.ts` (vitest) | 9 | `diffCharacter`/`diffCharacterList` (field เดี่ยว, array เป็น leaf เดียว, null vs blank) และ `CharacterEditForm.vue` (v-model แก้ object จริง ทั้งฟิลด์บนสุดและ nested stat, แปลง skill ids ไปกลับ) |
| `admin/e2e/*.spec.ts` (Playwright, Chromium จริง) | 2 | วงจรเต็ม แก้→validate→publish→rollback พิสูจน์ด้วยการอ่านไฟล์ `content/` บนดิสก์จริง (ไม่ใช่แค่เชื่อหน้าเว็บ) ว่าเปลี่ยนแล้วเปลี่ยนกลับ; publish เนื้อหาเสียถูกปฏิเสธทั้งใน UI และ response, player bearer token เข้า admin API ไม่ได้ทั้งสองรูปแบบ header |

รวมของหน้าจัดการ: **13 เคส dotnet ใหม่ (154 รวมทั้งโปรเจกต์) + 9 เคส vitest + 2 เคส Playwright**

ทั้งสองเทสต์ Playwright รันกับกระบวนการจริงล้วน: `DarkMyst.Api` (dotnet run จริง),
PostgreSQL ฐานข้อมูลทิ้งต่อรัน (`admin/e2e/run-api.sh` สร้าง/ลบเอง ไม่แตะฐานข้อมูล dev
ที่ใช้ร่วมกัน), สำเนา `content/` แยกต่างหาก (ไม่แตะ `content/` จริงในรีโปเด็ดขาด), Vite dev
server จริง, Chromium จริง (`/opt/pw-browsers/chromium` ในแซนด์บ็อกซ์นี้, ดาวน์โหลดจริงใน CI —
ดู `admin/playwright.config.ts`)

## สิ่งที่ตั้งใจตัดออกในรอบนี้ (deliberately deferred)

- **real admin SSO (Apple/Google-style)** — `POST /admin/bootstrap` เป็น stub ตรงไปตรงมาแบบ
  เดียวกับที่ `StubIdentityProvider` stub ฝั่งผู้เล่น เลื่อนไปพร้อมกับ real player SSO ที่ระยะ E
  (docs/10-backend-spec.md, docs/06-roadmap.md) — **gate เองไม่ใช่ของที่เลื่อน** มันถูกสร้างให้
  ถูกต้องตั้งแต่รอบนี้ (ดูหัวข้อ gate ด้านบน)
- **เนื้อหาชนิดอื่นนอกจากตัวละคร** — สกิล ศัตรู ด่าน encounter ยังแก้ผ่านหน้าจัดการนี้ไม่ได้
  ต้องแก้ JSON ตรง ๆ เหมือนเดิม เหตุผลคือลำดับความสำคัญของรอบนี้ (กลไก publish/rollback ที่
  ถูกต้อง > เนื้อหาชนิดเดียวที่ใช้งานได้จริง > Playwright > ที่เหลือ)
- **ปุ่มรัน `sweep` จากหน้าจัดการ** — `docs/05-content-pipeline.md` ระบุไว้เป็นงานระยะ D
  แต่ยังไม่ได้ต่อเข้าหน้าเว็บ `tools/DarkMyst.SimRunner -- sweep` ยังใช้ได้จากบรรทัดคำสั่งตาม
  ปกติ (docs/07-testing-plan.md) — การเชื่อมปุ่มเว็บเข้ากับ `simrunner` ต้องมีวิธีรันมันจาก
  เซิร์ฟเวอร์ (subprocess หรือพอร์ตโค้ดมาเป็น library เรียกตรง) ซึ่งยังไม่ได้ออกแบบ
- **diff แบบภาพ (rendered side-by-side)** — `GET /admin/content/diff` และแผง diff ในหน้าเว็บ
  เป็นรายการ field ที่เปลี่ยนพร้อมค่าก่อน/หลัง ไม่ใช่ตัวเปรียบเทียบภาพสวยงาม
- **การ retire snapshot เก่าออกจาก `content/_history/`** — เหมือนที่
  `ContentPackRegistry` ไม่เคยปลดเวอร์ชันออกจากหน่วยความจำ (docs/10-backend-spec.md) ทุก
  เวอร์ชันที่เคยเผยแพร่ยังอยู่ในดิสก์ตลอดไป ปลอดภัยที่สเกลนี้ ผิดที่สเกลใหญ่กว่านี้
- **การล็อกระดับฐานข้อมูลสำหรับ publish/rollback พร้อมกันข้าม process** — ใช้แค่ process-wide
  lock (`SemaphoreSlim` เดียว ไม่ใช่ `SELECT ... FOR UPDATE` แบบที่ `EvolveService`/
  `ExpeditionService` ใช้กับสถานะผู้เล่น) เพราะนี่คือการกระทำที่ทำน้อยครั้งโดยทีมเล็ก ไม่ใช่
  การแข่งขันความถี่สูงของผู้เล่นจำนวนมาก ถ้าเซิร์ฟเวอร์ขยายเป็นหลาย process การเผยแพร่สอง
  ครั้งพร้อมกันจากคนละ process ยังชนกันได้ — บันทึกไว้ตรง ๆ ไม่ใช่ปกปิด
