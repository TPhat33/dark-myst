# โครงสร้างระบบ

## ภาพรวม

```mermaid
flowchart TD
    A["แอป Unity (iOS / Android)"] --> B["Game API (ASP.NET Core)"]
    B --> C["PostgreSQL — บัญชีและตัวละคร"]
    B --> I["DarkMyst.Expedition — เดินด่านสำรวจ"]
    I --> D["DarkMyst.Combat — คำนวณการต่อสู้"]
    B --> D
    A --> I
    A --> D
    E["Apple / Google / AdMob"] --> F["ตรวจสอบและบันทึกรางวัล"]
    F --> C
    G["Admin (Vue + TS)"] --> B
    A --> H["CDN — ภาพ เสียง และ content pack"]
    G --> H
```

`DarkMyst.Combat` และ `DarkMyst.Expedition` ปรากฏสองที่โดยตั้งใจ — เป็น **assembly เดียวกัน**
ที่คอมไพล์ครั้งเดียว แล้วรันทั้งบนเซิร์ฟเวอร์และในไคลเอนต์ ไคลเอนต์ใช้เพื่อแสดงผลและสนามทดลอง
เซิร์ฟเวอร์ใช้เพื่อตัดสินผลที่จ่ายรางวัลจริง

## ความรับผิดชอบของแต่ละส่วน

| ส่วน | เทคโนโลยี | หน้าที่ |
| --- | --- | --- |
| ตัวเกม | Unity 2022.3 LTS + C# | หน้าจอ การควบคุม ภาพ เสียง แอนิเมชัน |
| กฎการต่อสู้ | `DarkMyst.Combat` (netstandard2.1, ไม่มี dependency) | คำนวณผลและสร้างลำดับเหตุการณ์ |
| ข้อมูลและการปั้น | `DarkMyst.Content` (netstandard2.1 + Newtonsoft) | โหลด content pack, สูตรเลเวล/evolve |
| การสำรวจ (ฟาร์ม) | `DarkMyst.Expedition` (netstandard2.1 + Newtonsoft) | สร้างแผนที่จุดเชื่อมจาก seed, เดินด่านทีละจุด, เรียก `DarkMyst.Combat` ต่อการต่อสู้แต่ละจุด |
| ระบบหลังบ้าน | ASP.NET Core (`server/DarkMyst.Api/`) | บัญชี การสำรวจ evolve รางวัล — ร้านค้ายังไม่ทำ (ระยะ E), ดู [10-backend-spec.md](10-backend-spec.md) |
| ฐานข้อมูล | PostgreSQL | ข้อมูลถาวรและธุรกรรม |
| ไฟล์เนื้อหา | Object storage + CDN | ภาพ เสียง และ content pack แต่ละเวอร์ชัน |
| หน้าจัดการ | Vue + TypeScript | เพิ่มตัวละคร ด่าน ร้านค้า และตรวจข้อมูล |
| ปฏิบัติการ | Managed hosting + logging + backups | ดูแลระบบโดยลดภาระเซิร์ฟเวอร์ |

เริ่มจาก backend **ชุดเดียวที่แบ่งโมดูลภายใน** ยังไม่ต้องแยก microservices หรือใช้ Kubernetes
จนกว่าจะมีตัวเลขผู้เล่นจริงที่บอกว่าจำเป็น

## โครงสร้างรีโป

```
DarkMyst.sln
src/DarkMyst.Combat/      กฎการต่อสู้ — ไม่มี dependency, ไม่รู้จัก Unity
src/DarkMyst.Content/     content pack, สูตรเลเวลและ evolve
src/DarkMyst.Expedition/  ระบบสำรวจ (ฟาร์ม) — ต่อยอด Combat + Content, ไม่รู้จัก Unity
server/DarkMyst.Api/      Game API (ระยะ D) — ดู 10-backend-spec.md
tests/                    เทสต์ของกฎทั้งหมด (123 เคส) + เทสต์ API กับ Postgres จริง (15 เคส)
tools/DarkMyst.SimRunner/ CLI: validate / battle / sweep / expedition / matrix
tools/build-unity-plugins.sh
content/                  ข้อมูลเกมทั้งหมดเป็น JSON — แหล่งความจริงชุดเดียว
unity/DarkMyst/           โปรเจกต์ Unity
docs/                     เอกสารชุดนี้
```

`server/DarkMyst.Api/` มีแล้วตั้งแต่ระยะ D นี้ (ดู [10-backend-spec.md](10-backend-spec.md)) —
`admin/` (หน้าจัดการ Vue + TS) ยังไม่มี เป็นงานที่เหลือของระยะ D ตาม
[06-roadmap.md](06-roadmap.md) `server/DarkMyst.Api/` อ้างอิง `src/DarkMyst.Combat`,
`src/DarkMyst.Content` และ `src/DarkMyst.Expedition` ชุดเดิมทั้งหมด ไม่เขียนกฎขึ้นใหม่ฝั่ง
เซิร์ฟเวอร์เลย

## ทำไม Unity ถึงใช้ DLL ไม่ใช่ source

`tools/build-unity-plugins.sh` คอมไพล์สองไลบรารีแล้ววางไฟล์ `.dll` ลง
`unity/DarkMyst/Assets/Plugins/DarkMyst/` วิธีนี้ทำให้พิสูจน์ได้ว่าไคลเอนต์และเซิร์ฟเวอร์
รัน **ไบนารีเดียวกันจริง ๆ** ไม่ใช่ซอร์สสองชุดที่ "น่าจะเหมือนกัน"

`netstandard2.1` เป็นเป้าหมายที่ทั้ง ASP.NET Core และ Unity 2021.2+ (ทั้ง Mono และ IL2CPP)
โหลดได้ ไฟล์ที่ไม่คัดลอกคือ `Newtonsoft.Json.dll` เพราะ Unity มีให้อยู่แล้วผ่าน
`com.unity.nuget.newtonsoft-json` — สำเนาที่สองจะทำให้ build พัง

## การ build สองแพลตฟอร์ม

ใช้โค้ดร่วมกันได้มาก แต่ยังต้อง build, ตั้งค่า store และทดสอบแต่ละระบบแยกกัน
ฝั่ง iOS ใช้ Unity สร้างโปรเจกต์ Xcode แล้ว build ต่อบน macOS

**ต้องสร้าง build ทั้งสองระบบตั้งแต่ระยะ B อย่างสม่ำเสมอ** ไม่รอพอร์ตตอนเกมเสร็จ
และควรมี iPhone กับ Android ระดับกลางไว้ทดสอบจริงตั้งแต่ระยะเดียวกัน

## กฎที่ห้ามแหก

1. `DarkMyst.Combat` **ห้ามอ้างอิง Unity, ห้ามอ้างอิง NuGet ใด ๆ** และห้ามใช้เลขทศนิยม
   `DarkMyst.Expedition` ต่อยอดจาก `DarkMyst.Combat` และ `DarkMyst.Content` ได้ (จึงมี
   Newtonsoft ติดมาเหมือน Content) แต่ **ห้ามอ้างอิง Unity และห้ามใช้เลขทศนิยมเช่นเดียวกัน**
   — แผนที่สำรวจ รางวัล และการสุ่มทุกจุดเป็นเลขจำนวนเต็มล้วน (อัตราส่วนเก็บเป็น per-mille)
   ตลอดทั้งไลบรารี ไม่มีข้อยกเว้น
2. **ห้ามใช้ `System.Random`** ไม่ว่าจะใน `DarkMyst.Combat` หรือ `DarkMyst.Expedition` —
   ใช้ `DeterministicRandom` เท่านั้น การสำรวจหนึ่งรันมีหลายสตรีมของมัน (สร้างแผนที่,
   ต่อสู้ต่อจุด, สุ่มรางวัล/เหตุการณ์ต่อจุด) แต่ทุกสตรีมย้อนไปเป็น `DeterministicRandom`
   เสมอ ไม่มีสตรีมไหนใช้ `string.GetHashCode()` แทน เพราะ .NET สุ่มค่านั้นใหม่ทุกโปรเซส
   (ดู [09-expedition-spec.md](09-expedition-spec.md) หัวข้อ seed)
3. ผลการต่อสู้ที่จ่ายรางวัลจริงต้องมาจากเซิร์ฟเวอร์เสมอ ไคลเอนต์แค่เล่นซ้ำ log
   กฎเดียวกันนี้ใช้กับผลของการสำรวจทั้งรัน ไม่ใช่แค่การต่อสู้แยกจุด
4. ทุกครั้งที่แก้ `CombatRules` ต้องขึ้น `CombatRules.Version`
5. ทุกครั้งที่แก้ `content/` (รวมถึง `stages.json`, ตารางรางวัล และเหตุการณ์) ต้องขึ้น
   `contentVersion` ใน `manifest.json`
