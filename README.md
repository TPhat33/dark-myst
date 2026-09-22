# Dark Myst

เกม 2D dark fantasy แนวจัดทีม–ต่อสู้–ฟาร์ม–evolve บนมือถือ (iOS + Android)
โปรเจกต์ Unity ชุดเดียว พร้อมกฎเกมที่เขียนเป็นไลบรารี C# ซึ่งไคลเอนต์และเซิร์ฟเวอร์ใช้ร่วมกัน

รีโปนี้คือผลของ **ระยะ A (ล็อกแบบเกม)** ตามแผน และเป็นจุดเริ่มของระยะ B
กฎการต่อสู้และกฎ evolve ถูกล็อก เขียนเป็นเอกสาร และเขียนเป็นเทสต์ที่รันได้จริง

## เริ่มต้น

```bash
dotnet test                                              # กฎเกม (129) + API กับ Postgres จริง (43) = 172 เคส
dotnet run --project tools/DarkMyst.SimRunner -- validate # ตรวจ content pack
./tools/build-unity-plugins.sh                            # เตรียมโปรเจกต์ Unity

cd admin && npm ci && npm run build && npm test           # หน้าจัดการ: build + เทสต์หน่วย (24 เคส)
npm run test:e2e                                          # Playwright กับ API/เซิร์ฟเวอร์จริง (10 เคส)
```

ต้องมี .NET SDK 8 สำหรับส่วนกฎเกมและ backend, Unity 2022.3 LTS สำหรับตัวเกม, PostgreSQL 16
ที่รันอยู่ (สำหรับเทสต์ `DarkMyst.Api.Tests` และการรัน `server/DarkMyst.Api` เอง — ดู
[docs/10-backend-spec.md](docs/10-backend-spec.md)) และ Node 22 สำหรับหน้าจัดการ (`admin/`
— ดู [docs/11-admin-spec.md](docs/11-admin-spec.md))

## ลองดูการต่อสู้จริง

```bash
# ดู log ทีละเหตุการณ์
dotnet run --project tools/DarkMyst.SimRunner -- battle \
  --encounter enc_boss_ashen_revenant --seed 20260920

# วัดสมดุล: รัน 300 seed แล้วดูอัตราชนะและตัวที่รอด
dotnet run --project tools/DarkMyst.SimRunner -- sweep \
  --encounter enc_boss_ashen_revenant --repeat 300 --level 12

# ลองทีมอื่น
dotnet run --project tools/DarkMyst.SimRunner -- sweep \
  --encounter enc_boss_ashen_revenant --repeat 300 --level 12 \
  --roster chr_ashen_knight_i,chr_grave_warden_i,chr_thorn_maiden_i,chr_mire_hexer_i,chr_pale_stalker_i

# ลองการสำรวจ (farm) หนึ่งรอบ ดูทุกจุดที่เจอ
dotnet run --project tools/DarkMyst.SimRunner -- expedition \
  --stage stg_ashfields --seed 20260920

# วัดสมดุลการสำรวจ: อัตราจบด่านและรางวัลเฉลี่ยจาก 300 seed
dotnet run --project tools/DarkMyst.SimRunner -- expedition \
  --stage stg_ashfields --repeat 300 --level 12

# วัดความหลากหลายของโรสเตอร์: หลายทีมสู้กับหลายด่านพร้อมกัน พิมพ์เป็นตาราง
dotnet run --project tools/DarkMyst.SimRunner -- matrix --level 12 --repeat 300 \
  --rosters "balanced=chr_ashen_knight_i,chr_grave_warden_i,chr_ember_adept_i,chr_tide_oracle_i,chr_pale_stalker_i;no_healer=chr_ashen_knight_i,chr_grave_warden_i,chr_thorn_maiden_i,chr_mire_hexer_i,chr_pale_stalker_i"
```

ทีมสุดท้ายไม่มีตัวฮีล อัตราชนะ 0% — การจัดทีมมีผลจริง ไม่ใช่แค่ความรู้สึก

## โครงสร้าง

```
src/DarkMyst.Combat/       กฎการต่อสู้ — ไม่มี dependency, ไม่รู้จัก Unity, เลขจำนวนเต็มล้วน
src/DarkMyst.Content/      content pack, สูตรเลเวลและ evolve
src/DarkMyst.Expedition/   ระบบสำรวจ (ฟาร์ม) — แผนที่จุดเชื่อม บัฟเฉพาะรอบ ต่อยอดจากทั้งสองข้างบน
src/DarkMyst.Sim/          ไลบรารี battle sweep ที่ simrunner และปุ่ม sweep ของหน้าจัดการใช้ร่วมกัน
server/DarkMyst.Api/       Game API (ระยะ D) — บัญชี evolve การสำรวจ อ้างอิงไลบรารีข้างบนทั้งหมด
admin/                     หน้าจัดการเนื้อหา (Vue 3 + TypeScript) — แก้ตัวละคร/สกิล/ศัตรู/encounter, sweep, validate/publish/rollback ผ่าน Game API
tests/                     เทสต์ของกฎทั้งหมด + เทสต์ API กับ Postgres จริง
tools/DarkMyst.SimRunner/  CLI: validate / battle / sweep / expedition / matrix
content/                   ข้อมูลเกมทั้งหมดเป็น JSON — แหล่งความจริงชุดเดียว
unity/DarkMyst/            โปรเจกต์ Unity (2022.3 LTS)
docs/                      เอกสารออกแบบ
```

## แนวคิดหลัก

**กฎการต่อสู้แยกจากภาพโดยสิ้นเชิง** `BattleSimulator` รับ `BattleRequest` แล้วคืน
ลำดับเหตุการณ์ที่จบแล้ว Unity แค่เล่นซ้ำ ความเร็วแอนิเมชันจึงไม่มีทางเปลี่ยนผลการต่อสู้
และเซิร์ฟเวอร์ตรวจสอบผลได้ด้วย engine ตัวเดียวกัน

**ผลต้องซ้ำได้เสมอ** เลขจำนวนเต็มล้วน (อัตราส่วนเก็บเป็น per-mille), RNG ที่ระบุอัลกอริทึมเอง,
ลำดับวนซ้ำคงที่ ทุกผลมี `Checksum` และ `RngCalls` ติดมาเพื่อจับความเพี้ยนระหว่างสองฝั่ง

**เนื้อหาเป็นข้อมูล** ตัวละครและสกิลใหม่ที่ใช้กลไกเดิมเพิ่มได้ด้วย JSON อย่างเดียว
มีเพียงกลไกที่เปลี่ยนกติกาจริง ๆ ที่ต้องเขียนโค้ด

**หลังบ้านเป็น C# เพื่อใช้กฎร่วมกัน** หน้าจอ preview สแตทก่อน evolve ที่ไม่ตรงกับผลจริง
คือวิธีที่เร็วที่สุดในการทำลายความเชื่อมั่นในเกมที่สร้างบนการฟาร์ม `server/DarkMyst.Api/`
เรียก `DarkMyst.Combat`/`DarkMyst.Content`/`DarkMyst.Expedition` ตัวเดียวกับไคลเอนต์เสมอ
ไม่เขียนกฎขึ้นใหม่ฝั่งเซิร์ฟเวอร์เลย (ดู [docs/10-backend-spec.md](docs/10-backend-spec.md))
หน้าจัดการเนื้อหา (`admin/`) สร้างด้วย Vue + TypeScript ตามที่วางแผนไว้ แก้ไฟล์ `content/`
ชุดเดียวกันผ่าน Game API เดียวกันนี้เสมอ ไม่ใช่แหล่งข้อมูลชุดที่สอง (ดู
[docs/11-admin-spec.md](docs/11-admin-spec.md))

## เอกสาร

เริ่มที่ [docs/00-overview.md](docs/00-overview.md)

| | |
| --- | --- |
| [01 โครงสร้างระบบ](docs/01-architecture.md) | เทคโนโลยีและการแบ่งความรับผิดชอบ |
| [02 กฎการต่อสู้](docs/02-combat-spec.md) | สูตร ลำดับ สถานะ การเลือกเป้าหมาย |
| [03 ตัวละครและ evolve](docs/03-evolve-spec.md) | การสืบทอดสแตท และสิ่งที่เซิร์ฟเวอร์ต้องปฏิเสธ |
| [04 เศรษฐกิจและรายได้](docs/04-economy-spec.md) | ยังไม่ล็อก — รอตัวเลขจากระยะ C |
| [05 การเพิ่มเนื้อหา](docs/05-content-pipeline.md) | โครงข้อมูล การตรวจสอบ การกำหนดเวอร์ชัน |
| [06 Roadmap](docs/06-roadmap.md) | ระยะ A–F และเกณฑ์ผ่าน |
| [07 แผนทดสอบ](docs/07-testing-plan.md) | สิ่งที่ต้องพิสูจน์ และตัวเลขที่วัดได้แล้ว |
| [08 ตัวชี้วัด](docs/08-metrics.md) | เหตุการณ์ที่ต้องเก็บและเหตุผล |
| [09 ระบบสำรวจ](docs/09-expedition-spec.md) | แผนที่จุดเชื่อม การสุ่มที่ตรวจสอบได้ บัฟเฉพาะรอบ |
| [10 Game API](docs/10-backend-spec.md) | Endpoint, สัญญา idempotency, ขอบเขตธุรกรรม, การเชื่อมบัญชี |
| [11 หน้าจัดการ](docs/11-admin-spec.md) | Endpoint, gate การยืนยันตัวตนของ admin, สัญญาการเผยแพร่/ย้อนกลับ |

## ขั้นถัดไป

เปิดโปรเจกต์ Unity สร้าง scene ต่อสู้ ผูก `BattleReplay` กับ UI แล้ว
**build ลงเครื่อง iOS และ Android จริง** — นั่นคือเกณฑ์ผ่านที่เหลืออยู่ของระยะ B
ดูรายการใน [docs/06-roadmap.md](docs/06-roadmap.md)
