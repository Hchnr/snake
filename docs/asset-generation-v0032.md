# v0.0.3.2 角色素材记录

生成日期：2026-11-16。使用内置 `image_gen` 工具，输出透明 PNG；未使用外部 API / CLI。素材是可运行的首版立绘，尚无完整分层骨骼、角色专属动作或目标用户识别率验收。

## 竞技场地面

位置：[`unity/Assets/Resources/ArenaStage.png`](../unity/Assets/Resources/ArenaStage.png)。不透明地面素材同样使用内置 `image_gen` 工具生成，提示词：

> Use case: game-asset. Asset type: seamless square repeating top-down floor texture for a premium mobile 2.5D character arena. Create a calm dark midnight-teal and deep indigo fantasy arena floor, softly lit matte smooth stone with subtle hand-painted color variation and very fine elegant engraved arcs and a few small aquamarine crystalline glints. Beautiful polished commercial casual game art, rich cinematic color but deliberately LOW CONTRAST and LOW VISUAL NOISE so bright flat outlined characters, colored paths and white text remain immediately readable on top. Large generous quiet open areas, no focal object. Orthographic directly overhead, seamless tiling at all four edges, uniform even lighting. NO characters, animals, fruit, large crystals, interface, text, numbers, symbols, logos, borders, drop shadows, vignette, grid, heavy pattern, flowers or perspective.

## 原创角色

### 森林守护者候选（2026-11-18）

[蜷曲姿态概念图](research/characters/forest-guardian-concept.png)由内置 `image_gen` 生成，未输入其他图像：小型森林穿山甲、叶片鳞甲、绿白配色、琥珀眼和卷曲蕨叶尾，透明背景。它是用于探索角色轮廓的候选，不在游戏资源目录，不占用角色 ID，也未接入商城或对局。

生成提示要求三分之四侧视、非对称蜷曲轮廓与清晰边缘；当前图的头部偏离画面中心、尾部占比大，与现有以精灵中心为头部碰撞锚点的表现不匹配，直接接入可能误导玩家。后续需设计正确的头部锚点和方向、分层或骨骼动作，验证小尺寸辨识度、透明边缘、包体和抖音真机表现，并记录素材使用权审查；不得将该概念图当作完整角色验收。

2026-11-18 固定场景诊断：在临时工程中替换玩家造型，尝试把图像归一化坐标 `(0.35, 0.67)` 设为精灵锚点（仅为目测候选，非已校准碰撞区域），并移除原有底部抬升。Unity 无图形构建、静音无头浏览器竖屏截图显示[角色下半部被地面截断](research/characters/forest-anchor-clipped.png)。第二个临时构建沿摄像机方向将该精灵前移 4 世界单位，[固定画面不再截断](research/characters/forest-anchor-depth.png)，但可能影响与其他角色、身体和场景的遮挡排序；名牌位置、动态转向、碰撞提示、不同相机和真机表现都未验证。这只是临时渲染实验，未修改正式资源或角色目录，不能据此声称碰撞已对齐或可上线。

位置：[`unity/Assets/Resources/Characters/`](../unity/Assets/Resources/Characters/)，文件分别为 `lanlight.png`、`ember.png`、`stardust.png`、`crystal.png`。通用提示词如下，`{subject}` 分别替换为下表内容：

> Use case: stylized-concept. Asset type: original full-character sprite for a polished 2.5D mobile arcade game. Create {subject}. A single full front-facing character, expressive and distinct silhouette, no limbs; occupies most of image with transparent margins. Professional crisp flat 2D puppet-inspired shapes with clean dark colored outlines, gentle luminous shading, limited highlights and harmonious eye design; vivid but sophisticated, read clearly at small size. Entire silhouette must be visible. Completely original design. No snake body, no floor, no text, no logo, no background or cast shadow.

| 文件 | subject |
| --- | --- |
| `lanlight.png` | a friendly mint-jade forest spirit with large rounded expressive eyes, two asymmetrical leaf-bud horns and an elegant tapered body, cream face highlights |
| `ember.png` | a cheerful warm amber fire spirit with round ears, a little flame-shaped forelock, large expressive eyes and a tapered copper-orange body |
| `stardust.png` | a dreamy lavender night-sky spirit with a crescent-moon crest, large expressive eyes, star-shaped cheek marks and a tapered purple body |
| `crystal.png` | a bright cyan crystal spirit with translucent faceted crystal horns, large expressive eyes, a small smile and a tapered ice-blue body |

### 沧澜（2026-11-18）

位置：`unity/Assets/Resources/Characters/tidal.png`。使用内置 `image_gen` 生成透明 PNG，独立原创，不以内部参考角色为输入。Unity 导入尺寸上限 1024；当前为整张立绘，尚无分层、专属动作或目标用户验收。提示词：

> Create one original 2D game character sprite for a polished cheerful casual mobile snake-arena game: a small cute blue aquatic otter spirit with flowing wave-shaped fins, luminous turquoise details, lively friendly eyes, a distinctive silhouette, rounded stylized vector-like painted shading. Full body centered, front-facing, upright, clear silhouette, occupies most of canvas with safe margin; no typography, no border, no shadow on ground, no other objects. Style consistent with colorful premium Chinese mobile game mascots, crisp linework, smooth shading. Transparent background.

## 内部参考角色

本机位置：`unity/Assets/Editor/ReferenceSkins/`，该目录已被 Git 忽略，公开仓库不含这些图片。这些是基于 [Sprunki 非官方角色资料](https://sprunki.com/sprunki-characters)中角色普通形态图片生成的内部视觉参考；20 个 PNG 的文件名与 `SkinCatalog` 中 `reference-` 后的 ID 相同。它们**不是官方联动资产或授权证明**，不随默认播放器构建发布。

2026-11-18：以本机 `wenda.png` 为输入，使用内置 imagegen 分别编辑出同画布的头部和身体两张透明 PNG，位于本机忽略目录 `ReferenceSkins/wenda_layers/`；原完整立绘保留。头部要求保留五官、耳朵和侧边轮廓并移除身体；身体要求保留原位置与比例、移除头部并向颈部稍作延伸。两层为内部动作测试样板，不是原始分层文件，也不是授权证明；需要人工核对参考及视觉品质。仅显式选择包含参考素材的内部构建时暂存到 Resources，默认构建不包含；缺失任一层时渲染回退到完整参考图，构建若发现不完整的本机层素材则失败。

同日以本机 `raddy.png` 为输入，使用内置 imagegen 分别编辑出同画布的头部和身体透明 PNG，位于本机忽略目录 `ReferenceSkins/raddy_layers/`；头部保留五个尖角、半垂眼皮与蓝色眼睛，身体保持原位置和轮廓并向上延伸用于衔接。本机重组图与输入图做了视觉对照，分层仍是内部参考样板，不是授权资产或原作还原验收。仅显式内部参考构建暂存；Wenda、Raddy 分别独立加载和回退。Raddy 身体层的导入上限为 512，其头部及 Wenda 层仍为 1024；不同导入尺寸按完整画布比例对齐。

生成时先查看各角色参考图，再以其为输入，要求保留整体轮廓、眼睛、比例、配色、身体和标志性部件，并明确禁止蛇身、3D 玩具、额外肢体、文字和背景。Wenda 与 Raddy 分别使用以下提示词：

> Use case: identity-preserve. Asset type: internal game character sprite, transparent PNG. Reference image is Wenda from Sprunki; faithfully retain the exact full-character front-facing silhouette and identity: tall pointed white cat ears, circular white head, white eyelashes/side spikes, eyebrows and large outlined eyes, mouth, and the same long simple white trapezoidal torso. Keep the relative head/body size, the eye and ear spacing, and clean flat 2D outlined puppet style extremely close to the provided reference. Output a single complete standing character centered with comfortable transparent margin, crisp high-resolution antialiased edges. Slightly refined production-quality line work is permitted but do not redesign, do not make it a snake, sphere, toy, 3D, or add arms, feet, accessories, text, logos, backgrounds, or drop shadows. The sprite is for an internal reference-only proof of concept.

> Use case: identity-preserve. Asset type: internal game character sprite, transparent PNG. Reference image is Raddy from Sprunki; faithfully retain the exact full-character front-facing silhouette and identity: bright red circular head, five dark outlined triangular spikes arranged as shown, raised curved eyebrows, large white eyes with half-lowered red eyelids and blue irises, restrained straight mouth, and the same long simple red trapezoidal torso. Keep relative head/body size, positions, dark red outlines and clean flat 2D outlined puppet style extremely close to the provided reference. Output a single complete standing character centered with comfortable transparent margin, crisp high-resolution antialiased edges. Slightly refined production-quality line work is permitted but do not redesign, do not make it a snake, sphere, toy, 3D, or add arms, feet, accessories, text, logos, backgrounds, or drop shadows. The sprite is for an internal reference-only proof of concept.

Oren、Clukr、Fun Bot、Vineria、Gray、Pinki 使用以下模板，`{features}` 对应下表：

> Use case: identity-preserve. Asset type: internal reference game character sprite, transparent PNG. Recreate the full front-facing Sprunki character in the provided reference. Faithfully preserve its exact silhouette, proportions, face layout, colors and characteristic features: {features}. Retain its long simple torso and original clean flat outlined 2D puppet style. Output one complete centered character with transparent margins and crisp edges. No snake body, sphere, 3D toy, arms, feet, new accessories, text, logo, background or shadow. Do not redesign the character; this is an internal reference-only prototype.

| 角色 | features |
| --- | --- |
| Oren | orange head and torso, orange headphones, twin round antennae, orange fringe and large wide eyes |
| Clukr | silver head and torso, a broad flat silver cymbal hat, central upright antenna, orange irises and toothy smile |
| Fun Bot | silver head with yellow antennae and a large black rectangular visor containing cyan eyes, layered yellow torso |
| Vineria | green head and body, long mint-green vine hair with small pink flowers, curved dark-green leaf, half-lidded eyes |
| Gray | gray head and torso, dark pointed cat ears, wide eyes, three small dark freckles on each cheek, slightly anxious expression |
| Pinki | pink head and torso, two tall rounded rabbit ears, purple bow, half-lidded eyes with green irises, a smiling mouth |

其余 12 个角色使用以下模板和下表特征：

> Use case: identity-preserve. Asset type: internal reference game character sprite, transparent PNG. Recreate the full front-facing Sprunki character in the provided reference. Faithfully preserve its exact silhouette, proportions, face layout, colors and characteristic features: {features}. Keep the original flat outlined 2D puppet style and whole original body, no redesign. Output one complete centered character with transparent margins and crisp edges. No snake body, 3D toy, extra limbs, new accessories, text, logo, background or shadow. This is an internal reference-only prototype.

| 角色 | features |
| --- | --- |
| Brud | brown puppet with a tilted dark gray bucket and strap, wide eyes and a simple triangular smile |
| Garnold | gold puppet with a broad rectangular black and blue visor and black oval mouth |
| OWAKCX | lime green angular spiky hair, uneven wide eyes and wavy anxious mouth |
| Sky | light cyan puppet with round bear ears, a single upward tuft and half-lidded green eyes |
| Mr. Sun | a round smiling yellow sun with pointed orange rays, two black oval eyes and a black smiling mouth; preserve the entire sun without adding a humanoid torso |
| Durple | purple puppet with two tall horns, pointed ear fins, a green and a red iris, and an asymmetric smile |
| Mr. Tree | a smiling brown tree with a broad rounded bright green canopy; preserve the complete tree shape without adding a humanoid body |
| Simon | bright yellow puppet with two round antennae, pointed ears, spiky fringe, purple eyes and a toothy smile |
| Tunner | beige puppet with a wide brown hat, long pointed hair, green eyes and a small smile |
| Mr. Fun Computer | a desktop computer face on a wooden table with a rainbow propeller cap, visible cable and power strip; preserve this entire non-humanoid silhouette |
| Jevin | a blue figure with a dark navy hood, half-lidded yellow eyes and a blue tapered torso |
| Black | a black figure in black top hat and black suit with a white tie and small half-closed white eyes; the black silhouette must remain visible against transparency |

Mr. Tree 首次生成出现树冠缺口，后续以原始参考图重新生成，并对结果使用以下编辑提示：

> Edit this exact Mr. Tree sprite. Remove the dark blurred spot at the center of the green tree canopy and make the canopy uniformly bright flat green, with solid opaque alpha across the entire canopy. Preserve the silhouette, green outline, brown trunk, eyes, mouth, all proportions, and transparent background exactly. No hole, gradient, shadow, new detail, text, or other changes.

Black 重新生成使用以下提示：

> Use case: identity-preserve. Internal game sprite: recreate Sprunki Black, the reference character, as a full front-facing black 2D puppet figure with a large round head, tall black top hat and tapered black torso, two small half-closed white semicircle eyes and a white necktie. The character itself must be solid nearly black opaque pixels, with a thin charcoal-gray outline around the entire hat, head, and torso so the silhouette remains visible against a dark arena. Outside the figure only, use true transparent alpha; do not make the character disappear into the transparent background. Faithful simple flat outline, no snake, limbs, text, logo or other accessories.

参考素材仍需人工逐个审阅，不能把生成文件数当作正式质量或授权验收通过。
