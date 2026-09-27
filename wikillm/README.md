# wikillm — Obsidian LLM Wiki

Vault นี้ใช้ plugin **Karpathy LLM Wiki** (v1.27.2) สร้าง wiki แบบมีโครงสร้าง (entity/concept pages
พร้อมลิงก์ย้อนกลับไปยังแหล่งอ้างอิง) จากโน้ตโปรเจกต์ ProjectF โดยให้ LLM ทำงานผ่าน **Ollama ในเครื่อง**

```
wikillm/
├── sources/          ← โน้ต input (เอาไฟล์ .md มาวางที่นี่เพื่อให้ ingest ได้)
└── wiki/             ← OUTPUT ทั้งหมดของ plugin (ห้ามวางโน้ต input ตรงนี้)
    ├── entities/     หน้า entity ที่ LLM สร้าง/รวมกันเอง
    ├── concepts/     หน้า concept
    ├── sources/      หน้า provenance 1 หน้าต่อ 1 โน้ตที่ ingest (สรุป + ลิงก์กลับ)
    └── schema/       config.md = กฎการสร้าง wiki (แก้ได้) + suggestions.md จาก lint
```

> ⚠️ Plugin **exclude ทุกไฟล์ที่อยู่ใต้ `wiki/`** จากการ ingest — วางโน้ต input ที่ `sources/` เท่านั้น
> (ยืนยันจาก `isExcludedFromSourcePicker()` ในโค้ด plugin)

## เงื่อนไขก่อนใช้ (ปัจจุบันพร้อมหมดแล้ว)

1. **Ollama ต้องรันอยู่** — ถ้าปิดเครื่อง/ปิดโปรแกรมไว้ ให้เปิด "Ollama" จาก Start Menu
   หรือรัน: `ollama serve` ตรวจว่าพร้อมด้วย
   ```bash
   curl http://localhost:11434/api/version     # ต้องตอบ {"version":"..."}
   ollama list                                  # ต้องมี ornith-1.5:9b
   ```
2. **Model ตาม config**: `ornith-1.5:9b` (สำรอง: `gemma4:e2b-it-q8_0`) — เปลี่ยนได้ที่
   Settings → Karpathy LLM Wiki → Model
3. **Plugin เปิดอยู่**: Settings → Community plugins → Karpathy LLM Wiki (ปัจจุบันเปิดอยู่
   พร้อมกับ smart-connections)

ค่า config ปัจจุบัน: provider `ollama`, endpoint `http://localhost:11434/v1`,
wikiFolder `wiki`, ภาษา wiki อังกฤษ, **auto-watch เปิด (โหมด auto-ingest) กับโฟลเดอร์
`sources/`**, periodic lint ปิด

## 1. เพิ่มแหล่งข้อมูล (Import sources)

เอาไฟล์ `.md` มาวางใน `wikillm/sources/` เท่านั้น ปัจจุบันมี 5 ไฟล์:

| ไฟล์ | สาระสำคัญ |
|---|---|
| `knowledge.md` | กฎเหล็ก on-chain (determinism, Bencodex, signer validation) |
| `DEVLOG.md` | บันทึกเทคนิคจาก planet-clicker (Libplanet/MagicOnion) |
| `Game Design Document.md` | ดีไซน์เกม |
| `SIGN-IN.md` | งาน sign-in + two-node peer sync test |
| `9-27-2026-1-30pm.md` | โครงสร้างโปรเจกต์ + แผน Stage 1b |

## 2. Ingest (สร้าง/อัปเดตหน้า wiki จากโน้ต)

เปิด **Command palette** (`Ctrl+P`) แล้วพิมพ์ `ingest`:

| คำสั่ง | ใช้เมื่อ |
|---|---|
| **Karpathy LLM Wiki: Ingest single source** | เลือกไฟล์เดียวจากรายการ |
| **Karpathy LLM Wiki: Ingest from folder** | ingest ทั้งโฟลเดอร์ (เช่น `sources/`) — ไฟล์ที่ ingest ไปแล้วจะถูกข้าม |
| **Karpathy LLM Wiki: Ingest active file** | ไฟล์ที่เปิดอยู่ตอนนี้ |
| **Karpathy LLM Wiki: Cancel ingestion** | ยกเลิกกลางทาง (รอบเดียวจบ ไม่ค้าง) |

สิ่งที่เกิดตอน ingest หนึ่งไฟล์: LLM อ่านโน้ต → สกัด entity/concept → สร้างหน้าใหม่หรือ
**merge เข้าหน้าเดิม** (เพิ่ม `sources:` array, เก็บ quote เดิม, ไม่ทับของเก่า) → เขียนหน้า
provenance ลง `wiki/sources/` → อัปเดต `wiki/index.md` กับ `wiki/log.md`

### Auto-watch (เปิดอยู่)

ไฟล์ใหม่หรือไฟล์ที่แก้ไขใน `sources/` จะถูก **ingest อัตโนมัติ** เมื่อไม่มีการแก้ไข
ต่อเนื่องไป 5 วินาที (debounce — แก้ไฟล์ติด ๆ กันจะถูกรวมเป็นรอบเดียว) โดยไม่ต้องกดอะไร
สถานะการทำงานดูได้จาก Notice มุมจอ Obsidian ข้อยกเว้น: ไฟล์ที่อยู่ใน `wiki/`
(ถูก exclude เสมอ) สำหรับโน้ตชุดแรก 5 ไฟล์ ให้รัน **Ingest from folder** ด้วยมือ
หนึ่งครั้ง (auto-watch จับเฉพาะไฟล์ที่เปลี่ยนหลังจาก Obsidian เปิดอยู่)

ดูประวัติการทำงานย้อนหลัง: **View operation history** · ตารางเวลา (วัดจริง): ingest 1 โน้ต
ใช้ **5–12 นาที** (โน้ตสั้นที่สกัดได้ 3 entities = 626 วินาที ที่ ~35 tok/s — มี LLM เรียก
หลายรอบ: source page + entity/concept ทีละหน้า) ชุดแรก 5 โน้ตควรรันแบบ folder เดียวจบ
แล้วปล่อยให้ทำงานไปเรื่อย ๆ

## 3. Query (ถาม-ตอบจาก wiki)

`Ctrl+P` → **Karpathy LLM Wiki: Query wiki** → พิมพ์คำถาม เช่น

- "Block policy ของ ProjectF กำหนดอะไรบ้าง"
- "บทเรียนจาก peer sync ใน SIGN-IN.md มีอะไร"

คำตอบจะอ้างหน้า wiki ที่เกี่ยว (คลิกลิงก์ไปอ่านต่อได้) ประวัติแชทเก็บสูงสุด 30 รอบ
เคลียร์ได้ที่ Settings

## 4. Regenerate / บำรุงรักษา

| คำสั่ง | ทำอะไร |
|---|---|
| **Karpathy LLM Wiki: Regenerate index** | สร้าง `wiki/index.md` ใหม่จากหน้าที่มีอยู่จริง (เร็ว, ไม่เรียก LLM หนัก) |
| **Karpathy LLM Wiki: Lint wiki** | ตรวจสุขภาพ wiki ทั้งชุด: หน้ากำพร้า, ลิงก์เสีย, หน้าซ้ำซ้อน (merge), ข้อความขัดแย้ง → ผลลัพธ์อยู่ที่ `wiki/schema/suggestions.md` และ `wiki/log.md` |
| **Recreate Welcome note** | สร้างหน้าต้อนรับใหม่ (ถ้าลบทิ้ง) |

เมื่อไหร่ควร lint: หลัง ingest ครบชุดใหญ่ หรือทุกครั้งที่ merge หน้าเยอะ ๆ
(periodic lint ตั้งไว้ "off" อยู่ — เปิด schedule ได้ใน Settings ถ้าต้องการ)

## การแก้ปัญหาเร็ว

| อาการ | สาเหตุ/ทางแก้ |
|---|---|
| Ingest แล้วค้าง/หมดเวลา | Ollama ไม่ได้รัน → เปิด Ollama แล้วลองใหม่ (ตรวจด้วย curl ด้านบน) |
| คำสั่ง ingest ไม่มีใน palette | plugin ถูก disable → เปิดที่ Community plugins |
| ไฟล์ใหม่ไม่ถูก ingest | ไฟล์อยู่ใน `wiki/` (ถูก exclude) หรือนามสกุลไม่ใช่ `.md` |
| ตอบช้ามาก | model ใหญ่เกิน → ลองเปลี่ยนเป็น `gemma4:e2b-it-q8_0` ใน Settings |
| LLM สร้างหน้าซ้ำ | รัน **Lint wiki** เพื่อให้ merge หน้าที่คล้ายกัน |

## เรื่อง git ของ vault นี้

- **Track**: โน้ตใน `sources/`, `wiki/**` (ผลงาน wiki), `manifest.json`/`data.json` ของ plugin,
  `styles.css`, ไฟล์ตั้งค่า `.obsidian/*.json` หลัก
- **Ignore**: `workspace.json` (สถานะหน้าต่าง Obsidian), `.smart-env/` (embedding cache ของ
  smart-connections), `plugins/*/main.js` (bundle plugin — ติดตั้งใหม่ผ่าน Community plugins
  ได้ ตั้งค่าไม่หายเพราะ `data.json` ถูก track)
