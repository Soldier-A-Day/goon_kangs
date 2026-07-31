# SOLDIER : A DAY — Meshy AI 프롬프트 시트 (한국군 고증판)

> 문서번호 SAD-ART-002 · **v0.2** · 2026-07-30
> 대응: `ASSETS.md` §4 상호작용 소품 45종 + §4.1 지급 장비 6종
> 도구: Meshy AI (Text to 3D) · Realistic · Triangle topology · GLB export

---

## 0. v0.1에서 무엇이 틀렸나

초안은 **일반적인 서구/NATO 군대 기준**으로 작성되어 있었다. `Metal barracks locker` 로 생성하면
미군 풋락커나 서양식 라커가 나오지 한국군 관물대는 나오지 않는다. 아래가 실제로 수정된 항목이다.

| 항목 | v0.1 (오류) | v0.2 (수정) |
|---|---|---|
| **B-01 관물대** | 서양식 2문 캐비닛 | **철제 0.8T, 침대 옆 바닥 설치, 폭 약 50cm 세로 장방형**, 상단 선반 + 내부 행거봉 + 하단 전투화 칸 |
| **B-11 침대** | 2층 침대(bunk bed) | **단층 철제 1인 침대**. 침대형 생활관 8인 1실 표준이며 관물대가 침대 사이에 놓인다 |
| **D-01 소총** | 일반 assault rifle (→ M4/AR-15가 나옴) | **K2 소총** — 측면 접이식 플라스틱 개머리판, 조가비형 폴리머 총열덮개, 운반손잡이 없는 상부 총몸 일체형 가늠자 |
| **D-02 무전기** | 일반 backpack radio | **PRC-999K** — 배낭형 본체 + 원격제어기 |
| **C-01/02 트럭** | 일반 6륜 군용 트럭 | **K-511 두돈반** — 기아 제작 2.5톤 6×6, 각진 캡, 캔버스 호로 |
| **A-11 식판** | 서양식 compartment tray | **6칸 스테인리스 식판** (한국 급식 표준) |
| **A-23 장갑** | 일반 work gloves | **목장갑** — 흰 면장갑에 고무 도트 |
| **전 항목 색상** | `worn olive drab` 일괄 | **재질군 4종 분리** — 병영 철제 가구는 올리브가 아니라 밝은 회색 파우더코팅 |
| **피복 패턴** | 언급 없음 | **화강암 디지털 5색** (베이지그레이·다크올리브·포레스트그린·초콜릿·목탄) |
| **방독면** | 없음 | **K-5** — 서구식 단일 정화통과 달리 **양쪽 뺨에 정화통 2개** (부록 E) |

> **중요**: Meshy가 `K2`, `K-511`, `PRC-999K` 같은 한국 제식명을 학습했다고 가정하지 않는다.
> 모든 프롬프트는 **제식명 + 형태 묘사**를 함께 넣어, 이름을 몰라도 형태 서술이 결과를 끌고 가도록 작성했다.

---

## 1. 공통 설정

### 1.1 꼬리말 (전 항목 공통, 고정)

```
South Korean military equipment, realistic proportions,
plain unmarked surfaces without text or markings,
single isolated object, plain empty background, studio lighting, game asset, PBR
```

`plain unmarked surfaces without text or markings` 는 네거티브 프롬프트의 **긍정문 치환**이다.
Meshy의 네거티브 칸을 못 찾거나 모델 버전이 지원하지 않아도 이 문구만으로 글자 생성이 억제된다.

### 1.2 재질군 (항목마다 하나 선택 — 프롬프트에 이미 반영됨)

한국군은 물건 종류마다 색이 다르다. 전부 올리브색으로 칠하면 안 된다.

| 코드 | 대상 | 문구 |
|---|---|---|
| **MAT-A** | 병영 철제 가구 (관물대·침대·선반·세면대) | `powder coated sheet steel, light warm grey paint, slight edge wear` |
| **MAT-B** | 야전 장비 (난로·발전기·무전기·드럼·텐트·차량) | `olive drab painted metal, matte finish, light scuffs and rust` |
| **MAT-C** | 피복·천 (군장·가방·방한류) | `Korean digital granite camouflage fabric in beige grey, dark olive green, forest green, chocolate brown and charcoal` |
| **MAT-D** | 취사·위생 (식판·배식대·세면대 상판) | `brushed stainless steel, matte, light scratches` |
| **MAT-E** | 잡재질 (종이·플라스틱·목재) | `matte plastic and paper, muted utilitarian colors` |

### 1.3 네거티브 프롬프트 (칸이 있으면 함께 사용)

```
text, logo, insignia, letters, numbers, flag pattern, multiple objects,
ground plane, scene, background, shiny plastic, cartoon, character, human
```

### 1.4 생성 설정

| 항목 | 값 |
|---|---|
| Art style | Realistic |
| Topology | Triangle |
| Symmetry | On (§D 지급 장비는 Off) |
| Seed | **고정** (재생성 시 스타일 유지) |
| Export | GLB |

### 1.5 폴리 규칙 — 2배로 뽑고 줄인다

| 등급 | Meshy target | 최종 (ASSETS.md) |
|---|---:|---:|
| A 소형 | 2,000 | 300 |
| B 중형 | 3,000 | 1,500 |
| C 대형 | 12,000 | 6,000 |
| D 지급 장비 | 5,000 | 250~3,000 |

---

## A. 소형 소품 25종

최종 300 tris · Meshy target 2,000 · 아틀라스 `ATLAS_A` (2048)

| # | 에셋 | 관련 일과 | 재질군 |
|---|---|---|---|
| A-01 | 서류 바인더 | 일과표 게시, 인원 보고 | E |
| A-02 | 걸레 | 생활관 바닥 청소 | E |
| A-03 | 싸리비 | 연병장·복도 청소 | E |
| A-04 | 대걸레 | 화장실 청소 | E |
| A-05 | 야전삽 (접이식) | 제설, 배수로 정비, 진지 작업 | B |
| A-06 | 수통 + 컵 | 급수, 수분 회복 | B |
| A-07 | 붕대 롤 | 응급처치 | E |
| A-08 | 무전기 배터리 | 배터리 교체 | B |
| A-09 | 몽키스패너 | 차량 정비, 공구 정리 | B |
| A-10 | 드라이버 | 난방기 점검 | B |
| A-11 | **6칸 스테인리스 식판** | 배식, 식기 세척 | D |
| A-12 | 잔반통 | 잔반 처리 | D |
| A-13 | 분리수거함 | 쓰레기 분리 배출 | E |
| A-14 | K2 탄창 | 탄약 수령 | B |
| A-15 | 총기 손질 세트 | 총기 수입 | B |
| A-16 | 약병 | 의약품 재고 정리 | E |
| A-17 | 체온계 | 위생 점검 | E |
| A-18 | 야전 손전등 (L자형) | 야간 경계, 보일러실 순찰 | B |
| A-19 | 열쇠 꾸러미 | 창고·탄약고 개방 | B |
| A-20 | 모래주머니 | 결빙 구간 모래 살포 | C |
| A-21 | 세탁 바구니 | 세탁물 수령·배분 | E |
| A-22 | 수건 | 세면 | E |
| A-23 | **목장갑** | 작업 일과 전반 | E |
| A-24 | 접힌 태극기 | 게양·강하 | E |
| A-25 | 휴대 급수통 20L | 급수통 보충 | B |

### 프롬프트

```
A-01  Two ring document binder, hard cover folder with a metal spring clip, matte plastic and paper, muted utilitarian colors, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-02  Folded floor cleaning rag, thick crumpled cotton cloth, matte plastic and paper, muted utilitarian colors, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-03  Korean bamboo twig broom, long wooden handle with a wide fan shaped bundle of thin bamboo branches tied with wire, matte plastic and paper, muted utilitarian colors, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-04  Korean flat mop, long metal pole with a T shaped head holding a thick cotton pad, matte plastic and paper, muted utilitarian colors, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-05  Folding entrenching tool, small steel spade blade with a hinged joint and a short wooden handle, folded into a compact L shape, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-06  One liter military canteen in a fitted canvas pouch cover with a nesting metal cup underneath, screw cap on a short chain, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-07  Rolled gauze bandage, cylindrical fabric roll with a loose end flap, matte plastic and paper, muted utilitarian colors, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-08  Rectangular military radio battery pack, ribbed casing with two recessed terminals and a side latch, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-09  Adjustable monkey wrench, steel worm gear jaw and a knurled handle, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-10  Flathead screwdriver, steel shaft with a ribbed rubber grip handle, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-11  Korean stainless steel meal tray with six recessed compartments, one large rice section, one large soup section and four small side dish sections, rounded rectangular outline, brushed stainless steel, matte, light scratches, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-12  Large food waste bucket, deep cylindrical container with a loose lid and a folding bail handle, brushed stainless steel, matte, light scratches, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-13  Waste separation bin, open top rectangular plastic container with a wire frame bag holder rim, matte plastic and paper, muted utilitarian colors, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-14  Curved thirty round rifle magazine for a 5.56mm assault rifle, ribbed steel body with a floor plate, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-15  Rifle cleaning kit laid out, a segmented steel cleaning rod, a small oil bottle and a bore brush, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-16  Small medicine bottle, short cylindrical container with a ribbed screw cap, matte plastic and paper, muted utilitarian colors, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-17  Digital thermometer, slim probe stick with a small flat display window at the wide end, matte plastic and paper, muted utilitarian colors, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-18  Angle head military flashlight, L shaped body with a lens hood at the top and a belt clip on the back, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-19  Key ring bundle, a thick steel split ring holding several flat keys and a small tag, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-20  Sandbag, filled woven burlap sack tied at the neck and slumped flat at the base, Korean digital granite camouflage fabric in beige grey, dark olive green, forest green, chocolate brown and charcoal, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-21  Laundry basket, rectangular perforated plastic tub with two cut out side handles, matte plastic and paper, muted utilitarian colors, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-22  Neatly folded towel, thin rectangular cotton cloth folded into a flat stack, matte plastic and paper, muted utilitarian colors, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-23  Pair of Korean white cotton work gloves with rows of small rubber grip dots on the palm, laid flat, matte plastic and paper, muted utilitarian colors, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-24  Flag folded into a tight triangle bundle, thick plain fabric with visible fold creases, matte plastic and paper, muted utilitarian colors, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

A-25  Portable twenty liter water jug, rectangular container with a recessed spigot at the bottom front and a moulded top carry handle, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR
```

> **A-24 태극기**: 태극 문양은 Meshy가 제대로 만들지 못한다. **접힌 상태**로 생성해 문양이 거의 보이지 않게 하고,
> 게양된 상태가 필요하면 평면 메시에 **텍스처/데칼**로 붙인다.

---

## B. 중형 소품 15종

최종 1,500 tris · Meshy target 3,000 · 아틀라스 `ATLAS_B1` / `ATLAS_B2`

| # | 에셋 | 관련 일과 | 재질군 | 아틀라스 |
|---|---|---|---|---|
| B-01 | **관물대** | 관물대 정돈 점검 | A | B1 |
| B-02 | 야전 난로 (연통형) | 난방기 점검, 난로 유지 | B | B1 |
| B-03 | 발전기 | 발전기 급유 | B | B1 |
| B-04 | 무전기 본체 + 원격제어기 | 정시 교신, 암호 코드 갱신 | B | B1 |
| B-05 | 의약품장 | 의약품 재고 정리 | A | B1 |
| B-06 | 공구 캐비닛 | 공구 정리 | A | B1 |
| B-07 | 배식대 | 배식 준비 | D | B1 |
| B-08 | 안테나 마스트 | 안테나 설치 | B | B1 |
| B-09 | 야전 전화기 | 초소 교대 인수인계 | B | B2 |
| B-10 | 유류 드럼통 200L | 유류통 운반, 급유 | B | B2 |
| B-11 | **단층 철제 침대** | 생활관, 모포 정리 | A | B2 |
| B-12 | 창고 선반 유닛 | 물자 운반, 재물조사 | A | B2 |
| B-13 | 공용 세면대 | 세면, 위생 점검 | A+D | B2 |
| B-14 | 사격 표적틀 | 사격훈련 (D-03) | E | B2 |
| B-15 | 제독 샤워 스탠드 | 화생방 제독 (D-05) | B | B2 |

### 프롬프트

```
B-01  Korean army barracks personal locker, tall narrow vertical steel cabinet about fifty centimeters wide, two hinged front doors with a simple latch, an open shelf across the top for a folded blanket stack, an interior hanging rail and a low compartment at the bottom for boots, thin sheet steel construction, powder coated sheet steel, light warm grey paint, slight edge wear, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

B-02  Barracks coal stove, upright cylindrical steel drum body on short legs, a hinged front loading hatch, an ash pan below and a narrow flue pipe rising from the top and bending sideways, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

B-03  Portable military generator, boxy tubular steel roll frame around an exposed engine block, a round fuel cap on top, a pull start handle and rubber feet, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

B-04  Military field radio set on a desk, a rectangular ribbed metal transceiver case with a recessed front control panel of rotary knobs, a separate small remote control unit beside it and a coiled cable handset resting on top, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

B-05  Infirmary medicine cabinet, wall mounted steel box with a hinged glass panel door and two interior shelves, a small latch handle, powder coated sheet steel, light warm grey paint, slight edge wear, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

B-06  Workshop tool cabinet, upright steel unit with four stacked drawers with recessed pull handles and a small worktop lip, powder coated sheet steel, light warm grey paint, slight edge wear, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

B-07  Mess hall serving counter, long stainless steel unit with a flat top, a raised back splash, a tubular tray slide rail along the front and an open lower shelf, brushed stainless steel, matte, light scratches, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

B-08  Portable field antenna mast, folding tripod base supporting a segmented telescopic pole with three guy wires and small ground stakes, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

B-09  Military field telephone, a rugged rectangular case with a fold down lid, a hand crank on the side and a heavy receiver resting in a cradle hook, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

B-10  Two hundred liter fuel drum, upright cylindrical steel barrel with two rolling ribs around the body and two bungs on the top lid, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

B-11  Single tier barracks bed, welded tubular steel frame with a low headboard and footboard, a flat mesh base and a thin mattress with a folded blanket at one end, powder coated sheet steel, light warm grey paint, slight edge wear, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

B-12  Warehouse storage rack, open steel shelving unit with four flat levels, angled corner posts with punched holes and diagonal back bracing, powder coated sheet steel, light warm grey paint, slight edge wear, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

B-13  Communal barracks washstand, a long shallow stainless steel trough basin on a steel frame with a row of four simple faucets rising from a horizontal supply pipe and a plain mirror panel above, brushed stainless steel, matte, light scratches, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

B-14  Shooting range target frame, two wooden posts driven into a small base holding a flat rectangular board with a rounded upper body silhouette outline, matte plastic and paper, muted utilitarian colors, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

B-15  Decontamination shower stand, an upright pipe frame on a flat base with an overhead spray nozzle head, a side hand valve and a hose fitting at the bottom, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR
```

> **B-01 관물대 검증 포인트**: 결과물이 서양식 라커(가로로 넓고 통짜 문)로 나오면 실패다.
> **세로로 길고 폭이 좁으며, 상단에 개방 선반이 있어야** 한국군 관물대다.
> 침대형 생활관에서는 침대 사이 바닥에 놓이므로 벽걸이형이 아니라 **바닥 설치형**이다.

---

## C. 대형 소품 5종

최종 6,000 tris · Meshy target 12,000 · **개별 1024 텍스처**

| # | 에셋 | 관련 일과 | 재질군 | LOD |
|---|---|---|---|---|
| C-01 | **K-511 두돈반 (카고)** | 차량 정비, 시동 점검 | B | 3단 |
| C-02 | **K-511 (호로 씌운 보급형)** | 보급 트럭 하역 (D-14) | B | 3단 |
| C-03 | 야전 텐트 | 텐트 설치 (D-09, 협동 2인) | B | 2단 |
| C-04 | 들것 | 환자 후송 (**협동 2인**) | B | 2단 |
| C-05 | 급수 탱크 트레일러 | 급수 라인 구축 (D-15 고온) | B | 2단 |

### 프롬프트

```
C-01  South Korean K-511 two and a half ton military cargo truck, six wheel drive with three axles, a tall boxy flat fronted cab with a flat windshield and round headlights on the fender tops, an open rear cargo bed with wooden slat side rails and fold down bench seats, high ground clearance and knobby tires, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

C-02  South Korean K-511 military supply truck with the rear cargo bed covered by a canvas tarpaulin stretched over five arched metal bows, a laced rear flap, six wheel drive with three axles and a boxy flat fronted cab, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

C-03  Military canvas squad tent, pitched ridge roof over vertical side walls, a centre ridge pole and end poles, a rolled up entrance flap at one end, taut guy ropes running to ground stakes, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

C-04  Military folding stretcher, two long carrying poles with wooden grip ends, a taut canvas bed slung between them and two hinged spreader bars underneath, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

C-05  Military water tank trailer, a horizontal cylindrical tank mounted on a two wheel towable chassis with a drawbar and jack stand, a top filler hatch and a rear manifold with two taps, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR
```

> **C-01/C-02 검증 포인트**: 미군 M35(둥근 후드·돌출 라디에이터 그릴)가 나오면 실패다.
> K-511은 **캡 전면이 평평하고 각졌으며, 헤드라이트가 펜더 상단에 노출**되어 있다.

---

## D. 지급 장비 6종 (캐릭터 부착)

아틀라스 `ATLAS_C` · **Symmetry Off** · 부착점 정리 필요

| # | 에셋 | 최종 | target | 부착 | 보직 |
|---|---|---:|---:|---|---|
| D-01 | **K2 소총** | 3,000 | 6,000 | 손 / 등 | 전원 지급 |
| D-02 | **PRC-999K 배낭형 무전기** | 1,200 | 3,000 | 등 | 통신병 |
| D-03 | 의무낭 | 1,000 | 2,500 | 허리 | 의무병 |
| D-04 | 휴대 공구 가방 | 1,400 | 3,000 | 손 | 행정병 |
| D-05 | 야간투시경 | 900 | 2,500 | 두부 | 야간 경계 |
| D-06 | 클립보드 | 250 | 1,500 | 손 | 행정병 |

### 프롬프트

```
D-01  South Korean K2 assault rifle, a 5.56mm gas piston rifle with a side folding solid plastic buttstock hinged at the right rear of the receiver, a ribbed clam shell polymer handguard covering a long gas tube, a flat topped upper receiver with an integral aperture rear sight and no carry handle, a front sight post on a gas block, a pistol grip and a curved thirty round magazine, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

D-02  Backpack military field radio, a tall rectangular ribbed transceiver case carried on a padded frame with two shoulder straps, a thick flexible whip antenna rising from one top corner, a recessed front control panel and a handset clipped to the side, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

D-03  Medic field bag, a squat canvas satchel with a fold over top flap, two buckle straps, a wide shoulder sling and two flat side pouches, Korean digital granite camouflage fabric in beige grey, dark olive green, forest green, chocolate brown and charcoal, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

D-04  Portable tool bag, an open top canvas carrier with a rigid oval mouth frame, a single arched carry handle and a row of outer tool loops, Korean digital granite camouflage fabric in beige grey, dark olive green, forest green, chocolate brown and charcoal, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

D-05  Helmet mounted night vision goggle, twin short cylindrical lens tubes joined side by side on a hinged flip up arm with a helmet mount bracket and a rear counterweight battery pod, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

D-06  Clipboard holding a stack of blank paper, a flat rigid board with a hinged metal spring clip at the top edge, matte plastic and paper, muted utilitarian colors, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR
```

> **D-01 검증 포인트**: M4 카빈(신축식 튜브 스톡, 상단 피카티니 레일, 삼각 프론트사이트)이 나오면 실패다.
> K2는 **옆으로 접히는 통짜 플라스틱 개머리판**과 **조가비형 폴리머 총열덮개**가 핵심 실루엣이다.
> K2C1은 총열덮개가 4면 레일로 바뀐 개량형이므로, 구형 K2를 원하면 레일을 언급하지 말 것.

---

## E. 부록 — 피복 중 강체 파츠

`ASSETS.md` §1.2의 두부 슬롯 4종은 원칙적으로 Meshy 대상이 아니지만(§H),
**방독면과 방탄모는 강체**라 생성 결과가 쓸 만하다. 51종 집계에는 포함하지 않는다.

```
E-01  South Korean K-5 gas mask, a full face respirator with a wide panoramic single piece visor, a rubber face seal with head harness straps, and two short cylindrical filter canisters mounted one on each cheek, olive drab painted metal, matte finish, light scuffs and rust, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR

E-02  Military combat helmet, a rounded shell with a slight brim, an interior suspension pad system, a four point chin strap and a fabric cover, Korean digital granite camouflage fabric in beige grey, dark olive green, forest green, chocolate brown and charcoal, South Korean military equipment, realistic proportions, plain unmarked surfaces without text or markings, single isolated object, plain empty background, studio lighting, game asset, PBR
```

> **E-01 핵심**: K-5는 서구식 단일 정화통 마스크와 달리 **양쪽 뺨에 정화통이 2개** 붙는다.
> 정화통이 하나만 나오면 실패다.

---

## F. 아틀라스 그룹 · 텍스처 예산

Meshy는 항목마다 2K~4K PBR 텍스처를 뱉는다. **그대로 쓰면 51개 × 3맵 ≈ 495MB**로
`ASSETS.md`의 텍스처 예산 350MB를 넘기고, 드로우콜도 51개가 그대로 늘어난다.

| 아틀라스 | 포함 | 해상도 | 항목당 영역 | 용량 |
|---|---|---:|---:|---:|
| `ATLAS_A` | A-01 ~ A-25 (25종) | 2048 | ~409px | 11.2MB |
| `ATLAS_B1` | B-01 ~ B-08 (8종) | 2048 | ~724px | 11.2MB |
| `ATLAS_B2` | B-09 ~ B-15 (7종) | 2048 | ~774px | 11.2MB |
| `ATLAS_C` | D-01 ~ D-06 (6종) | 2048 | ~836px | 11.2MB |
| 개별 | C-01 ~ C-05 (5종) | 1024 × 5 | — | 14.0MB |
| **합계** | **51종** | | | **58.8MB** |

맵 구성: Albedo(DXT1) + Normal(DXT5) + **ORM 패킹**(Occlusion=R, Roughness=G, Metallic=B, DXT1)

---

## G. 후처리 파이프라인

1. **GLB 임포트** → Blender
2. **Decimate** — 목표 폴리로 감축 (Planar 먼저, 부족하면 Collapse)
3. **원점·스케일 정규화** — 원점을 바닥 중심으로, 1 unit = 1m
4. **UV 재배치** → 아틀라스 슬롯에 팩
5. **텍스처 베이크** + ORM 패킹
6. **LOD 생성** — LOD1 50% / LOD2 20% / LOD3 5%
7. **FBX 익스포트** → Unity Addressables 그룹 배정

### 한국군 고증 마감 (Unity 단계)

Meshy는 글자·문양을 만들지 못하므로 아래는 전부 **데칼/텍스처**로 처리한다.

- 부대 마크, 계급장, 이름표
- **태극기 문양** (A-24는 접힌 상태로만 생성)
- 차량 번호판, 등록번호 (C-01/C-02)
- 관물대 명찰

### 부착물(§D) 추가 처리

- 손 부착물(D-01, D-04, D-06)은 **그립 위치에 빈 오브젝트** 배치 후 리그 본에 constraint
- D-05 야간투시경은 E-02 방탄모와 **간섭 검사** 필요

---

## H. Meshy로 만들지 말 것

| 대상 | 이유 | 대안 |
|---|---|---|
| 부대 맵 모듈 95종 | 씬·건축 생성 불가. 모듈 간 격자·스냅이 안 맞는다 | 박스 모델링 후 **AI Texturing**만 사용 |
| 훈련 맵 9종 | 위와 동일 | 지형 툴 + 모듈 조립 |
| 캐릭터 베이스 + 피복 22파츠 | 리그 1종에 56클립 리타게팅이 전제인데 생성 메시는 토폴로지가 안 맞는다 | 베이스 메시 구매/제작 후 피복만 모델링 (방독면·방탄모는 §E 예외) |
| 간부 NPC 3종 | 위와 동일 | 플레이어 베이스 리타게팅 |
| A 등급 천 뭉치 (A-02, A-22, A-23) | 형태 정의가 약해 결과가 불안정 | 직접 만드는 편이 빠름. 결과 보고 판단 |

---

## I. 진행 순서

1. **파일럿 3종** — `B-01 관물대` · `D-01 K2 소총` · `C-01 K-511`
   v0.1에서 가장 크게 틀렸던 세 항목이다. **한국군 고증이 실제로 먹히는지**를 여기서 판별한다
2. 통과하면 **B 등급 15종 일괄** → 아틀라스 B1/B2
3. **C 등급 5종** (개별 텍스처, 품질 기준 최상)
4. **D 등급 6종 + E 부록 2종** — 부착점 정리 포함
5. **A 등급 25종 마지막**

### 체크리스트 (항목마다)

- [ ] 재질군을 올바로 골랐는가 (병영 가구에 올리브색을 칠하지 않았는가)
- [ ] 글자·문양이 생성되지 않았는가
- [ ] **서구식 대응물이 나오지 않았는가** (관물대→라커, K2→M4, K-511→M35)
- [ ] Decimate 후 실루엣이 유지되는가
- [ ] 원점이 바닥 중심인가, 스케일이 1m 기준인가
- [ ] 아틀라스 슬롯에 UV가 들어갔는가 / ORM 패킹했는가 / LOD 생성했는가

---

## J. 미확정

1. **관물대 정확 치수 미확보** — 강판 0.8T와 폭 약 50cm는 확인했으나 공식 규격서의 높이·깊이는 찾지 못했다.
   실물 사진을 Meshy **Image to 3D**에 넣는 쪽이 텍스트 프롬프트보다 정확할 수 있다.
2. **훈련소 vs 일반 부대 침대** — 일반 생활관은 단층 침대(B-11)로 잡았으나, 육군훈련소 등 일부는 2층 침대를 쓴다.
   D-01~02 입소 구간을 훈련소로 설정한다면 2층 침대 변형이 추가로 필요하다.
3. **Meshy의 한국 제식명 인지 여부 미검증** — `K2`, `K-511`, `K-5` 를 학습했는지 알 수 없어 형태 묘사를 병기했다.
   파일럿에서 제식명만 넣은 버전과 비교해 보면 이후 프롬프트를 줄일 수 있다.
4. **소품 수량 ±20%** — `duties.json` 확정 시 변동.
5. **A 등급 25종의 Meshy 실효성** — 300 tris 목표는 Meshy 최저 출력보다 훨씬 낮다.
