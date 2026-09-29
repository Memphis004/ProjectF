# Devlog — planet-clicker (Libplanet Unity)

บันทึกการพัฒนาและแก้ไขปัญหา พร้อมเหตุผลและวิธีทดสอบ — เรียงตามลำดับเวลาของงาน

> **หมายเหตุ:** หัวข้อ 1–7 ด้านล่างเป็นบันทึกยุค planet-clicker (Libplanet 0.38,
> โปรเจกต์เก่า) — เก็บไว้เป็นประวัติและบทเรียน ส่วนงานปัจจุบัน (ProjectF,
> Libplanet 5.5.3 + MagicOnion 7) เริ่มที่หัวข้อ 8 เป็นต้นไป

สภาพแวดล้อม: Unity **2022.3.62f2** (Windows 11), Libplanet **0.38** (DLL ใน `Assets/LibplanetUnity/Packages/`)

---

## 1. แก้ปัญหา peer sync สองเครื่อง (สอง instance)

### ปัญหาที่เจอ

เมื่อเปิดเกมสอง instance พร้อมกัน (เช่น 1 ใน Editor + 1 .exe) พบ:

- ทั้งสองฝั่งใช้ **identity เดียวกัน** — ตอน sync กันทาง peer จะมองว่าเป็น node เดียวกัน ทำให้เห็นเป็น "1 player" หรือข้อมูลกระโดดแปลก ๆ
- บล็อกที่ขุดใหม่ append ไม่ได้เพราะ chain tip ขยับไปก่อน → `InvalidBlockIndexException`
- เครื่องที่เข้าทีหลัง (join ทีหลัง) crash ตอน preload เพราะ store ของตัวเองมีแค่ genesis → `ChainIdNotFoundException`
- การ parse peer string เดิมใช้ library `CommandLineParser` ซึ่งใน environment นี้ **throw `InvalidOperationException` ตอนสร้าง Options instance** → ต้องเขียน parser เอง

### สิ่งที่แก้

**`Assets/LibplanetUnity/Agent.cs`** (เขียนใหม่ส่วนใหญ่):

- **Identity เดียวต่อ instance**: `SetPendingPrivateKey(PrivateKey)` / `HasPendingPrivateKey` เพื่อให้ sign-in screen ส่ง key เข้ามาก่อน init แล้วใช้ key ตัวเดียวกันทั้ง sign transaction และเป็น swarm identity (ใน `NodeConfig`)
  - ⚠️ กับดัก Libplanet 0.38: `PrivateKey` **overload `==`/`!=` แล้ว NRE ตอน null** — ห้ามใช้ `== null`, ต้องใช้ `ReferenceEquals(x, null)` เท่านั้น
- **Options ใหม่** (`Assets/LibplanetUnity/Helper/Options.cs`): DTO เต็มรูปแบบ + hand-rolled `CommnadLineParser.GetCommandLineOptions()` (switch parser, ไม่ใช้ CommandLine library แล้ว) รองรับ:
  - `--private-key <hex>` — กำหนด identity ตอน launch
  - `--host <ip>` (default `127.0.0.1`) / `--port <n>`
  - `--peer "<pubkey-hex>,<host>,<port>"` — bootstrap peer (seed) รองรับหลายตัว
  - `--no-miner` — ปิดการขุด (สำหรับ non-seed node)
  - `--auto-click` — คลิกอัตโนมัติทุก 0.2 วิ (ไว้ทดสอบ)
- **Genesis bootstrap**: node ที่ไม่เคยขุดจะไม่มี canonical chain ใน store → ตอน init ถ้า `store.GetCanonicalChainId() is null` จะ append genesis ลง store ก่อน (แก้ root cause ของ `ChainIdNotFoundException`)
- **Preload ไม่ fatal**: ถ้า `PreloadAsync` ยัง throw (เช่น fork จาก genesis-only chain) จะ log warning `"...(continuing anyway)"` แล้วเริ่ม swarm ต่อ เพราะ block/tx sync ยังทำงานผ่าน demand-driven + broadcast
- **CoMiner กัน tip race**: ถ้า mining fail ด้วย `InvalidBlockIndexException` หรือ `OperationCanceledException` (ตรง ๆ หรือเป็น InnerException) → log `"Tip changed while mining. Retrying against the new tip."` แล้ว `continue` ขุดใหม่กับ tip ล่าสุดทันที แทนที่จะปล่อย pass นั้นหายไปเฉย ๆ
  - อัปเดตตามหลัง: `Debug.LogException(ex)` เดิมถูกเรียก **ไม่มีเงื่อนไข** ทำให้ case ที่จัดการแล้วยังพิมพ์ **Error แดง** รก Console → แก้เป็นพิมพ์แดงเฉพาะ exception ที่ไม่ใช่ benign case (tip-race/OCE log ขาวธรรมดา)

### กฎ topology ที่สำคัญ

> **มี miner ได้เครื่องเดียวต่อเครือข่าย (seed node)** ถ้ามีสอง miner จะ fork chain และ block ที่พก tx ถูก orphan
> เครื่องอื่นรันด้วย `--no-miner` + `--peer` ชี้มาที่ seed

---

## 2. Sign-in screen (Address + Secret)

เพิ่ม `Assets/Scenes/SignIn.unity` (ขึ้นก่อน Game) + `Assets/_Script/SignInController.cs` โดยดูแบบจาก [planetarium/gamejam-laylas-island `IntroCanvas.cs`](https://github.com/planetarium/gamejam-laylas-island)

- **Dropdown** เลือก address จาก keystore (แสดงแบบสั้น `0x…`) + ตัวเลือก **"Enter private key"** (paste raw hex 64 ตัว รับทั้งมี/ไม่มี `0x` prefix, validate สด ๆ) + **"Create New One"** (สร้าง key ใหม่เข้า keystore)
- **Secret field** = passphrase สำหรับถอดรหัส keystore (แบบเดียวกับ laylas-island): ใช้ `ProtectedPrivateKey.Protect` / `Unprotect` + `Web3KeyStore.DefaultKeyStore` จาก DLL Libplanet 0.38 ที่ bundle มาอยู่แล้ว
- ใส่ passphrase ผิด → แสดง error inline และ disable ปุ่ม (ไม่ crash)
- กด Sign-in สำเร็จ → `Agent.SetPendingPrivateKey(key)` → callback เข้า `Game.OnSignInCompleted()` (หรือโหลด scene `Game` ถ้าเปิด SignIn ตรง ๆ)
- ข้ามหน้า sign-in อัตโนมัติถ้ามี `--private-key` ใน command line (เหมาะกับ E2E test / nodeA-nodeB)
- Scene สร้างด้วย editor script `Assets/LibplanetUnity/Editor/SignInSceneBuilder.cs` (เมนู **Tools → Libplanet → Build Sign-In Scene**) — สร้าง Canvas + TMP dropdown + InputField + Button และ wire private `[SerializeField]` ผ่าน `SerializedObject` ให้เอง

ผลลัพธ์: เปิด .exe สอง instance ของ **build เดียวกัน** แล้ว sign-in ด้วย secret คนละอันได้ → identity ต่างกัน ไม่ชนกัน

---

## 3. Sync indicator บนหน้าจอเกม

เพิ่มให้เห็นสถานะ sync ได้ทันทีไม่ต้องเปิด log:

- **`Agent.cs`** เปิด read-only properties (ปลอดภัยต่อ main thread):
  - `TipIndex` → `long` (index ของ chain tip, `-1` ถ้า chain ยังไม่พร้อม)
  - `PeerCount` → จำนวน peer ใน `Swarm<T>.Peers` (try/catch กันช่วง startup/teardown)
  - `IsMiner` → ขุดหรือไม่ (ไม่ได้รันด้วย `--no-miner`)
  - `IsSwarmRunning` → swarm พร้อมหรือยัง
- **`Game.cs`**: เพิ่ม `[SerializeField] Text syncText` + poll ทุก 1 วิ (ใน `FixedUpdate`, เริ่มหลัง sign-in เท่านั้น — เพราะ `MonoSingleton.instance` จะ **auto-create** Agent GameObject ถ้ายังไม่มี จึงต้องมี flag `_signedIn` กันไม่ให้ polling ไป trigger การสร้างก่อนเวลา)
- **UI**: `SyncStatusText` มุมบนซ้ายของ Panel ใน Game.unity (Text UI เดิม, ฟอนต์ LegacyRuntime, ขาว) แสดงเช่น:

  ```
  Block #846 (miner, 1 peer)
  Block #846 (peer, 1 peer)
  ```

  ตอนเริ่มแสดง `Sync: starting...` ก่อน sign-in/init เสร็จ
- การชี้ reference ทำผ่าน editor script (SerializedObject → `syncText`) แล้วบันทึก scene — ตรวจใน YAML แล้ว: `syncText: {fileID: 534789279}`

---

## 4. การ build และเครื่องมือที่ใช้

- Build player ด้วย editor script (`/tmp/build_player.json` → `script-execute`): scenes `[Assets/Scenes/SignIn.unity, Assets/Scenes/Game.unity]` → `Build/Windows/PlanetClicker.exe` (Development build)
- Build ล่าสุด **Succeeded, errors: 0** — ยืนยันว่าโค้ดใหม่อยู่ใน build จริงโดย grep string แบบ UTF-16 ใน `Assembly-CSharp.dll` (เช่น `"Tip changed while mining"`, `"Sync: starting"`, `"Block #"`) — วิธีเดียวที่เวิร์กเพราะ .NET strings เก็บเป็น UTF-16 และเครื่องนี้ไม่มี `strings` binary
- ⚠️ ประสบการณ์การใช้เครื่องมือ:
  - MCP tool call ตรง ๆ มัก fail (`Invalid parameters` / timeout) → ใช้ `npx unity-mcp-cli run-tool <tool> --input-file <json>` แทน
  - CLI มี internal timeout ~60 วิ — build ยาวอาจโดน "timeout" แต่ build ยังรันต่อจนเสร็จใน Editor → เช็คผลจากไฟล์ output หรือเรียกใหม่
  - หลังแก้ไฟล์นอก Unity ต้อง `assets-refresh` ก่อนใช้ `script-execute` กับโค้ดใหม่
  - `write_file` กับไฟล์ยาวบางครั้งเนื้อหาเสีย — เขียนแล้วต้อง grep ตรวจเสมอ / แก้ด้วย `str_replace` จะปลอดภัยกว่า

---

## 5. วิธีทดสอบ E2E (สอง node, เครื่องเดียว)

Script พร้อมใช้: `run-two-nodes.ps1` หรือทำมือตามขั้นตอนนี้:

```bash
# 1) สร้าง key สองอัน (openssl / .NET / sign-in screen "Create New One")
openssl rand -hex 32 > /tmp/pc_keyA.hex
openssl rand -hex 32 > /tmp/pc_keyB.hex

# 2) เครื่อง A = seed/miner
./Build/Windows/PlanetClicker.exe \
  --private-key $(cat /tmp/pc_keyA.hex) \
  --port 39710 \
  --storage-path "C:/UnityProjects/planet-clicker/pc_test_A_storage" \
  -logFile "C:/UnityProjects/planet-clicker/pc_test_A.log"

# 3) รอจน log ขึ้น "The address of this node:" → ได้ peer string รูป <pubkey>,127.0.0.1,39710

# 4) สำเนา storage ของ A ให้ B (กัน ChainIdNotFoundException ตอน preload)
cp -r pc_test_A_storage pc_test_B_storage

# 5) เครื่อง B = peer (ไม่ขุด)
./Build/Windows/PlanetClicker.exe \
  --private-key $(cat /tmp/pc_keyB.hex) \
  --port 39711 \
  --no-miner \
  --peer "<pubkeyA>,127.0.0.1,39710" \
  --auto-click \
  --storage-path "C:/UnityProjects/planet-clicker/pc_test_B_storage" \
  -logFile "C:/UnityProjects/planet-clicker/pc_test_B.log"
```

เพิ่ม `-batchmode -nographics` ได้ถ้าต้องการ headless (TMP font NRE ตอน `-nographics` เป็นของ harmless)

**เกณฑ์ PASS**:
- log ของ B มี tx ถูกบรรจุใน block ฝั่ง A (`NextCount` รวมเพิ่มขึ้นเรื่อย ๆ)
- B ไล่ `Block #...` (จาก sync indicator / log) ตาม A ทัน
- log ไม่มี error แดงเกี่ยวกับ chain (เหลือแค่ข้อความ `Tip changed while mining. Retrying against the new tip.` ได้ = ปกติ, ไม่แดงแล้วหลังแก้)

**ผลล่าสุด**: PASS — A `0x3c6cCBE1…3Cda` (miner), B `0x1edD305d…7a243` (peer), tx ของ B execute บน A และ ranking สองฝั่งตรงกัน (63cD=40 / 2444=39)

---

## 6. ไฟล์ที่เกี่ยวข้อง (สรุป)

| ไฟล์ | สิ่งที่ทำ |
|---|---|
| `Assets/LibplanetUnity/Agent.cs` | identity เดียว, options ใหม่, genesis bootstrap, preload ไม่ fatal, CoMiner tip-race retry (และเงียบ exception ที่จัดการแล้ว), `TipIndex`/`PeerCount`/`IsMiner`/`IsSwarmRunning` |
| `Assets/LibplanetUnity/Helper/Options.cs` | DTO + hand-rolled command-line parser (`--private-key`, `--host`, `--port`, `--peer`, `--no-miner`, `--auto-click`, `--storage-path`) |
| `Assets/_Script/SignInController.cs` | หน้า sign-in (keystore + raw hex + create new) |
| `Assets/_Script/Game.cs` | init Agent หลัง sign-in, `--auto-click`, sync indicator polling |
| `Assets/LibplanetUnity/Editor/SignInSceneBuilder.cs` | สร้าง SignIn.unity อัตโนมัติ (เมนู Tools) |
| `Assets/Scenes/SignIn.unity`, `Assets/Scenes/Game.unity` | scene (Game มี `SyncStatusText` wired) |
| `SIGN-IN.md` | เอกสาร root cause + rationale ของ sign-in และปัญหา network |
| `run-two-nodes.ps1` | script ทดสอบสอง node แบบ param-driven |

> สำเนา `planet-clicker-nodeA/` และ `planet-clicker-nodeB/` ถูก sync ไฟล์ที่เกี่ยวข้องไว้เสมอ (Agent.cs, Options.cs, Game.cs, SignInController.cs, SignInSceneBuilder.cs, scenes)

---

## 7. ข้อจำกัด / ของที่รู้อยู่

- `--peer` ต้องใช้ pubkey hex ของ seed (ดูจาก log `The address of this node:` ของเครื่อง seed) — ยังไม่มี discovery ผ่าน seed file/ICE
- preload ของ node ใหม่ยังต้อง copy storage เพื่อเลี่ยง `ChainIdNotFoundException` ในบางกรณี (แก้ที่ root ด้วย genesis bootstrap แล้วแต่ copy storage ยังทำให้ sync เร็วขึ้นมาก)
- ถ้าเปิดสอง miner → fork (ตามกฎ topology ข้างบน) — ตัวเกมไม่ได้ block ไว้ ผู้เล่นต้องรันให้ถูก role
- Sync indicator อัปเดตทุก 1 วิ จึงอ่านค่าตอนหน้าจอเปิดทันทีอาจ delay สูงสุด 1 วิ

---

# 8. Stage 7 — Unity client: บันทึกการแก้บักครั้งใหญ่ (ProjectF, 2026-09-28)

Stage 7 ส่งโค้ดฝั่ง client ครบ (Infrastructure + Presentation, 37 สคริปต์,
manifest/asmdef/sync-dlls) — แต่การ compile ใน Unity จริงเป็นครั้งแรกของโค้ด
ชุดนี้ เลยไล่แก้บักยาวหลายรอบ Console สรุป root cause ได้เป็น 5 กลุ่มใหญ่
(หลาย error ที่เห็นหน้าจอเดียวกันมาจาก root เดียวกัน):

## 8.1 สาเหตุกลุ่มที่ 1 — package resolution (manifest)

- **`com.unity.modules.accessibility` ใส่ผิด** — เป็นโมดูลของ Unity 6 ไม่มีใน
  2022.3 → dependency ไม่ valid แล้ว **block การ resolve ทั้งโปรเจกต์**
  (ทุก error อื่นหลังจากนั้นเป็นเงาของอันนี้)
  - บทเรียน: manifest.json ต้องเช็คชื่อโมดูลกับ Unity เวอร์ชันนั้นจริงเสมอ
    อย่า copy ข้ามเวอร์ชัน
- เพิ่ม scoped registry **Unity NuGet** (`unitynuget-registry.openupm.com`,
  scope `org.nuget`) — ใช้ติดตั้ง `Grpc.Net.Client` เป็น UPM package
- สุดท้ายเลือกทาง NuGetForUnity แทน (ดู 8.2) และ registry นี้คงไว้เป็นทางเลือก

## 8.2 สาเหตุกลุ่มที่ 2 — MagicOnion 7 บน Unity: การแบ่งเจ้าของ DLL

`MagicOnion.Client.Unity` (UPM git, asmdef ชื่อ **`MagicOnion.Unity`**) ประกาศ
`Grpc.Core.Api.dll` + `Grpc.Net.Client.dll` เป็น `precompiledReferences` —
ถ้า DLL พวกนี้ไม่มีอยู่ จะพังทั้ง package (`GrpcChannelOptions` /
`IMagicOnionAwareGrpcChannel` / `Grpc.Net` หาไม่เจอ)

ทางแก้ = แบ่งเจ้าของชัดเจน **หนึ่ง assembly ต้องมีที่เดียวเท่านั้น**:

| Assembly | เจ้าของ |
|---|---|
| `MagicOnion.Client`, `MagicOnion.Abstractions`, `MagicOnion.Shared`, `MagicOnion.Serialization.MessagePack` (7.0.0) | **NuGetForUnity** (`Assets/packages.config`) |
| `Grpc.Net.Client`, `Grpc.Net.Common`, `Grpc.Core.Api` (2.66.0 ตรงกับ HubServer) | **NuGetForUnity** |
| `MessagePack` (3.1.10) | NuGetForUnity (มีอยู่แล้ว) |
| YetAnotherHttpHandler (1.11.5, asmdef `Cysharp.Net.Http.YetAnotherHttpHandler`) | **UPM git** |
| MagicOnion.Client.Unity | **UPM git** |
| Libplanet stack + ProjectF.* | `tools/sync-dlls.ps1` → `Assets/Plugins/ProjectF` |

- ⚠️ **กับดัก `slimRestore = true`** ใน `Assets/NuGet.config`:
  NuGetForUnity ติดตั้ง **เฉพาะ** package ที่ประกาศใน packages.config
  **ไม่ดึง transitive dependencies** — ต้อง enumerate ครบเอง
  (`MagicOnion.Client` ต้องมี `Serialization.MessagePack` + `Shared` ตาม,
  `Grpc.Net.Client` ต้องมี `Logging.Abstractions` + `DiagnosticSource`)
  ถ้า bump เวอร์ชันต้องไล่ nuspec ใหม่ทั้ง chain
- ⚠️ sync-dlls.ps1 เดิมเผลอ copy `MagicOnion.Abstractions` / `Grpc.Core.Api`
  ลง Plugins/ProjectF ด้วย (มากับ publish output ของ ProjectF.Shared) →
  duplicate assembly identity — ใส่ในรายการ skip (`nuget-provided`) แล้ว
  และห้ามเอากลับเข้า allowlist

## 8.3 สาเหตุกลุ่มที่ 3 — DLL เสียหายเงียบ ๆ (เจอบักที่อันตรายที่สุดของ Stage นี้)

Console รายงาน `ProjectF.Shared` ไม่มีอยู่ (`'Shared' does not exist in the
namespace 'ProjectF'`) ทั้งที่ DLL sync แล้ว ตรวจแล้วพบว่า
`ProjectF.Shared.dll` ใน `Assets/Plugins/ProjectF/` มีขนาด **4,096 bytes**
(ไฟล์จริง 12,800) — **ไฟล์ถูกตัดครึ่ง** จาก session ที่ถูกขัดจังหวะตอน
write ลงดิสก์ Unity **ไม่ error ว่า DLL เสีย** เพียงแค่ไม่โหลด แล้วรายงาน
เป็น "หา type ไม่เจอ" เป็นสิบ ๆ จุดแทน

- ตรวจว่า DLL sync แล้วด้วยขนาดไฟล์: `ls -la` เทียบ `bin/Release` กับ
  `Plugins/ProjectF` — ต่างกัน = sync ไม่สมบูรณ์
- วิธีแก้: `pwsh tools/sync-dlls.ps1` ใหม่ (script เช็ดโฟลเดอร์แล้ว copy
  ใหม่ทั้งชุด จึง fix ตัวเองได้)
- บทเรียน: **"type not found" เป็นได้ทั้ง missing using, missing reference
  และ corrupted DLL** — เช็คขนาดไฟล์/timestamp ก่อนไล่แก้โค้ด

## 8.4 สาเหตุกลุ่มที่ 4 — โค้ด Unity-side ที่ .NET build ไม่ได้ compile

โค้ด Stage 7 ไม่ได้อยู่ใน ProjectF.sln (Unity compile เอง) ทำให้ได้ compile
จริงครั้งแรกตอนเปิด Editor — เจอพวง:

- **using หลุด**: `System` (IDisposable/TimeSpan), `System.Threading`
  (CancellationToken), `Libplanet.Action` (IAction),
  `ProjectF.Infrastructure.Network` (NetworkSettings),
  `ProjectF.Shared.Presence` (PlayerMoveRequest),
  `ProjectF.Lib.Genesis` (GenesisBuilder)
- **Ambiguity**: `AnimationState` ชนกับ **`UnityEngine.AnimationState`**
  (legacy class) → ใช้ file-scoped alias
  `using AnimationState = ProjectF.Shared.Presence.AnimationState;`
  (`Direction` ไม่ชน จึงใช้ตรง ๆ ได้)
- **`Tables` ชนกับ namespace** (ProjectF.Tables.Tables vs property name) →
  alias `GeneratedTables`
- **`Convert.ToHexString/FromHexString` เป็น .NET 5+** — Mono 2022.3 ไม่มี
  → เขียน ParseHex/ToHex เอง (จุดเสี่ยง: เดาว่า BCL ใหม่มี = พัง)
- **C# 9 `init` ต้องมี polyfill `IsExternalInit`** → เพิ่ม
  `Infrastructure/SystemRuntimeCompatibility.cs`
- **`(Direction)(-1)` ห้ามใช้ cast ตรง ๆ** (CS0221) → `unchecked((Direction)(-1))`
- **`ImmutableArray<byte>.ToArray()` ไม่มี** (ตามเวอร์ชัน
  System.Collections.Immutable ที่ Unity resolve) → copy ผ่าน indexer
  `raw[i]` — ปลอดภัยทุกเวอร์ชัน

## 8.5 สาเหตุกลุ่มที่ 5 — Libplanet 5.5.3 API: ห้ามเดา ต้อง reflect

ประเด็นเดิมจาก stage-4 ("อย่า invent method names") กลับมาโจมตีอีกรอบ —
รอบนี้แก้โดย **reflect จาก assembly จริงผ่านโปรเจกต์ scratch dotnet**:

| เดามา | ความจริง 5.5.3 |
|---|---|
| `chain.World` (property) | **`chain.GetWorldState()`** (method, no-arg) |
| `chain.GetBlock(hash)` | **ไม่มี** — ใช้ list ของ tip hash ล่าสุด (64 รายการ) แล้วลอง `GetTxExecution(hash, txId)` ทีละอันแทน |
| `swarm.PreloadAsync(token)` | **`PreloadAsync(IProgress<BlockSyncState>, CancellationToken)`** — ส่ง `null` progress ได้ |
| `new ValidatorSet(ImmutableList<Validator>)` | **รับ `List<Validator>`** |
| `BlockHash` ใช้ `!=` | เป็น struct ไม่มี `operator!=` → **ใช้ `.Equals`** |
| `PrivateKey.ByteArray` = `byte[]` | เป็น **`ImmutableArray<byte>`** — copy ผ่าน indexer |

วิธี reflect: dotnet scratch project อ้าง `Libplanet 5.5.3` + `Libplanet.Net
5.5.3` แล้ว `GetMethods/GetProperties` dump — เร็วกว่าเดา-แล้ว-compile-ใน-Unity
มาก

## 8.6 ผลและสถานะ

- แก้ครบทั้ง 5 กลุ่ม — build ผ่าน, ทุก error ใน console ปิดเป็นรายการ
- Stage 7 code สมบูรณ์ (Infrastructure + Presentation + DI ผ่าน VContainer,
  SceneRouter คุมลำดับ load → unload → SetActive → presence ChangeScene)
- ขั้นถัดไป: ทำตาม `UnityProject/SETUP.md` ใน Editor (scenes/prefabs/
  NetworkSettings asset/build settings) แล้ว smoke test แบบ offline

---

# 9. Stage 11.5 + 16 — bounded shutdown & multi-instance (ProjectF, 2026-09-29→30)

รวมงาน 2 ชุด: **Stage 11.5** (teardown สถาปัตยกรรมใหม่) และ **Stage 16**
(CLI override + build script) จบด้วยการทดสอบ **2 game instance เห็นกันข้าม
instance สำเร็จ** ทั้งสองทิศทาง (commit `154e7d8`)

## 9.1 Stage 11.5 — สถาปัตยกรรม shutdown ใหม่

ปัญหาเดิม: ออก Play Mode แล้ว Editor ค้างที่ domain reload แบบเงียบ ๆ เพราะ
background task (swarm loop, pollers, presence watcher) ยังวิ่งค้างและแตะ
disposed resources

**โครงสร้างใหม่** (ทั้งหมด tracked):
- `BackgroundTaskRegistry` — registry เดียวของทุก background Task +
  `Loop(name, interval, ct, body)` เป็น loop primitive เดียว (cancellation
  check เป็นโครงสร้าง) + `WaitForExitAsync(task, timeout)` (Task.WhenAny +
  Task.Delay — **Unity Mono ไม่มี `Task.WaitAsync`**)
- บริการทุกตัว (LibplanetClient/StateWatcher/Monitor/ActionQueue/
  PlayerHubClient) มี ordered teardown: cancel → await task ตัวเอง (bounded
  3s) → dispose resource **ทีหลังสุด** + sync Dispose bridge ที่ `Task.Run`
  ก่อน `.Wait` เสมอ (sync-context deadlock ถ้า await บน main thread)
- `PlayModeShutdownGuard` (Editor, `[InitializeOnLoad]`) — hook ทั้ง
  `ExitingPlayMode` + `beforeAssemblyReload` + `EnteredEditMode`, force-dispose
  ผ่าน static `EditorShutdownHook` (bridge editor←runtime เพราะ asmdef ห้าม
  อ้างกันข้าม) แล้วรายงาน task ที่ยังไม่ตาย พร้อมชื่อ + อายุ
- ทดสอบ 31 EditMode tests เขียวหมด

## 9.2 Stage 16 — CLI override + build script

- `--instance-id <id>` / `--player-name <name>` parse ใน
  `RootLifetimeScope.Configure` **ก่อน** build container → `InstanceId`
  ไปขยาย `{instanceId}` ใน StorePath (`chain-player2`), `PlayerName` ใช้จริงใน
  AppBootstrapper (เดิม hardcode `"Player"` ทั้งที่ settings มี field —
  PlayerHubClient อ่านมาแต่ไม่เคยถูกใช้)
- `ProjectF.Editor.BuildPlayer` (win-dev/win-release → `build/win-dev`) รัน
  ผ่าน `-executeMethod` หรือ script-execute ใน Editor ที่เปิดอยู่ (batchmode
  ชน project lock กับ Editor ที่เปิดอยู่ไม่ได้)
- `tools/run-local.ps1`: pin validator key ที่ `{store}/privkey.txt` (key
  เปลี่ยน = genesis เปลี่ยน = `InvalidGenesisBlockException` ทุก store เก่า),
  รอ peer.txt จริง, auto-patch NetworkSettings.asset (UTF-8 **BOM** + CRLF,
  PS 5.1 อ่านไม่ถูกถ้าไม่มี BOM)

## 9.3 บัก presence ที่เจอตอนเทส 2 instance (แก้ 2 จุด)

รอบแรก: P2 (exe, join ก่อน) เห็น player1 แต่ **Editor (join ทีหลัง) ไม่เห็น
P2 ตลอดไป** — และทั้งสองฝั่ง log `remote joined` ของ **ตัวเอง**

1. **Late-joiner blind** (client): `PresenceConnection.ConnectAsync` ทิ้ง
   return ของ `JoinAsync` — ซึ่ง server ส่ง roster คนที่อยู่ก่อนหน้า
   (`PresenceRegistry.OthersIn`) กลับมา ส่วน broadcast `OnJoin` เดินทางเฉพาะ
   ไปยังคนอื่นใน group แล้ว → คน join ทีหลังไม่มีทางรู้จักคนเก่าเลย และ
   `RemotePlayerRegistry.OnMove` อัปเดตเฉพาะ session ที่รู้จักแล้ว = มองไม่เห็น
   ตลอดกาล แก้: ดึง roster จาก return แล้ว push เข้า receiver แบบ upsert
   (**ไม่ใช้ Clear** — กัน race กับ OnJoin ที่บินสวนมาช่วง roster ยังบินอยู่)
2. **Self-echo** (server): `Scene.All.OnJoin(_snapshot)` กระจายกลับถึงตัว
   joiner ด้วย → registry spawn remote clone ของตัวเอง แก้:
   `Scene.Except(new[]{ConnectionId}).OnJoin(...)` (⚠️ overload รับ
   `IEnumerable<Guid>` ไม่ใช่ Guid เดียว — CS1503 ตอน build hub)

หลังแก้: สองฝั่งเห็นกัน (`remote joined: P2` ใน Editor, `remote joined:
Player <session ต่างจากตัวเอง>` ใน player2) + peers 2 บน chain ยืนยันซ้ำ
หลัง commit ด้วย infra สด

## 9.4 ปัญหาใหญ่ของวัน — hang หลัง "EnteredEditMode: all tracked tasks
stopped cleanly"

**อาการ:** ออก Play Mode, guard รายงาน clean ครบ, แล้ว "Reloading Domain"
ค้าง 4+ นาที CPU idle — แปลว่ามีอะไร **untracked** ยังมีชีวิต

**สืบพยานบทเรียนสำคัญ: `Process.GetCurrentProcess().Threads.Count` คืน 0 บน
Unity Mono** — census แรกใช้มันแล้วเงียบทั้งไฟล์ ต้องเปลี่ยนไป walk Win32
`CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD)` + `Thread32First/Next` (DllImport
kernel32) ใน guard และ LibplanetClient ทั้งคู่

**พยานหลังแก้ (chain connected, tip #2354):**

```
[shutdown-guard] ExitingPlayMode: teardown begin — threads: 226
[chain] shutdown complete in 3073ms — threads 210→194.
[shutdown-guard] ExitingPlayMode: teardown end — threads: 226→194
[shutdown-guard] beforeAssemblyReload: teardown begin — threads: 165
[shutdown-guard] beforeAssemblyReload: teardown end — threads: 165→165
```

**แก้:** `NetMQ.NetMQConfig.Cleanup(false)` ท้าย `LibplanetClient.DisposeAsync`
(ห้าม block=true บน main thread; NetMQ.dll อยู่ใน Assets/Plugins/ProjectF
import ได้จาก runtime) — สาเหตุคือ NetMQ poller/agent threads ของ
NetMQTransport จอดใน native recv มองไม่เห็นจาก task registry และ
`Swarm.StopAsync` หยุดเฉพาะ loop ฝั่ง Libplanet เอง (stack จาก seed log:
`ReceiveMultipartMessageAsync` ค้างเป็นหลักฐาน)

**ยืนยัน 3 enter/exit cycles (chain connected):** teardown drop คงที่ −32
ทุกรอบ (228→196, 215→183, 212→180), reload-start flat (170/176/175),
idle steady-state กลับมา **173 = baseline เป๊ะ** — ⚠️ กับดักการวัด: ค่า probe
แรกหลัง reload ได้ 210 (wave ของ asset pipeline/Roslyn หลัง reload) probe
ซ้ำโดยไม่เข้า play mode = 173 นิ่ง ต้องแยก noise ของ editor เองออกก่อนตัดสิน

## 9.5 ปัญหาแวดล้อมที่เจอระหว่างทาง (บทเรียนสั้น)

- **Editor hang ซ้ำตอนแก้ไฟล์ขณะ exit Play Mode** — domain reload ชนกับ
  playmode transition 2 ครั้ง (force kill ครั้งเดียว, exit clean ได้ครั้ง
  หนึ่ง) — งาน editor หนัก ๆ ควรรอออก play mode ก่อน save ไฟล์
- **`dotnet run` หลายตัวชนกันเอง** — สตาร์ท seed ซ้ำระหว่าง build ค้าง →
  build lock กันตาย ทั้งหมดจอด (hub ไม่ขึ้น 2.5+ นาที = สงสัย code ผิดก่อนเสมอ
  แต่ครั้งนี้ lock ชนเอง) วิธีเดินทางที่ถูก: kill dotnet ทั้งหมด → `dotnet
  build` foreground ให้จบ → ค่อย start detached ทีละตัว
- **monitor loop ของ run-local.ps1 ตายพร้อม hub** — hub build fail หลังแก้
  `Except` ผิด → runner เอา seed ตายไปด้วย ต้อง start hub แยกได้ (dotnet run
  --project ตรง ๆ)
- **MCP session หลุดหลัง domain reload** (`Session not found` / 503 ชั่วครั้ง
  ครั้งเว้น) — CLI (`npx unity-mcp-cli run-tool ...`) ทนกว่า, และ config server
  **auto-launch Editor ให้เอง** ถ้า Editor ตายขณะ MCP ยังรับ connection อยู่
- bash บน Windows: `$var` ใน double-quoted PS command ถูก bash กลืนก่อน
  (ใช้ single-quote ครอบ), และ heredoc/Add-Type ผ่าน bash multi-line เพี้ยน
  ง่าย — สคริปต์ยาวเขียนลงไฟล์ดีกว่า inline
- `ls`/`cat` คนละ cwd ตอนสลับ dir เร็ว ๆ ทำให้สรุปผิด ("chain dir ว่าง!" ทั้ง
  ที่มี) — ใช้ path เต็มเมื่อตรวจของสำคัญ

## 9.6 สถานะปลายทาง

- 2-instance E2E ผ่านหลัง commit (fresh infra): Editor เห็น P2, P2 เห็น
  Player (session ต่างกัน), 0 exceptions ทั้งสองฝั่ง, teardown clean,
  IsPlaying false, ports ปลอด
- Known noise ที่ยังไม่แก้: `BlockHeaderMessage ... 0 replies` จาก seed
  (normal transport noise)
- MCP `timeoutMs` เพิ่มเป็น 600000 ใน UserSettings config (สำหรับ build/test
  ยาว)

## 9.7 ตามมาแก้ — `[state] poll failed: get_isActiveAndEnabled` (2026-09-30)

เดิมจดไว้ว่าเป็น known noise ไม่ fatal — แต่จริง ๆ คือบักจริงที่เกิบทุก poll
(2 Hz ตลอด session):

- **Root cause:** poll loop ของ StateWatcher วิ่งบน threadpool แล้ว raise
  `TipChanged`/`AvatarUpdated`/`OnReorg` ตรง ๆ — subscriber ฝั่ง UI ทุกตัว
  (HudView, ShopWindow, TaskBoardWindow, CraftWindow, InventoryWindow,
  FishingView) แตะ MonoBehaviour state ใน handler ของตัวเอง เลยโดน Unity
  ฉีด `get_isActiveAndEnabled can only be called from the main thread`
  กลับมาเป็น warning ที่ catch ของ poll loop แล้วหลอกว่า "poll failed"
  (ทั้งที่ state อ่านสำเร็จ — ตัว raise ต่างหากที่พัง)
- **แก้:** จับ `SynchronizationContext` ไว้ตอน `Start()` แล้ว raise ผ่าน
  `RaiseOnMain()` — ถ้าอยู่บน context นั้นอยู่แล้วหรือไม่มี context → invoke
  ตรง (EditMode tests เรียก `PollOnce` บน test thread ยัง sync ได้เหมือนเดิม),
  ถ้าบน threadpool → `context.Post()` กลับ main thread (Post เป็น FIFO จึง
  คงลำดับ TipChanged ก่อน AvatarUpdated ของ poll เดียวกัน) ส่วน chain read
  หนัก ๆ ยังอยู่บน threadpool เหมือนเดิม
- **พยาน:** play mode มี chain live, probe subscribe ทั้งสอง event แล้วเช็ค
  thread id ของ handler — `fired=14, offMainThread=0` (ทุกครั้งบน main thread
  tid=1), warnings 295→0 หลัง fix, EditMode 31/31 (commit `c3ba7c8`)
- **บทเรียน:** warning ที่โผล่ซ้ำถาวรแบบนี้ "ไม่ fatal" ก็ยังต้องไล่ — มัน
  บังคับว่า event contract ของ service ที่ poll บน background ต้องระบุชัดว่า
  handler วิ่งบน thread ไหน และครั้งแรกที่ subscriber แตะ Unity API มันจะระเบิด
  เป็น log spam แทน error ชัด ๆ

