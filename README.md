# 灵蛇秘境 · Snake Club

v0.0.3.2 正在开发中，首发以抖音小游戏为目标，浏览器 WebGL 用于开发验证。玩家在大地图对抗 AI：可以吃果实，淘汰对手会直接成长；撞墙反弹，不会因此出局。首发设计采用本地 PvE 和真人异步每日挑战，不要求玩家同时在线。

## 当前内容

- **对局**：大厅主入口为「开始吞食」，使用不计账号奖励或排行的强弱吞食试玩，玩家每局有实验性攻防优势，出生附近有可正常拾取的果实；一名正常移动的 AI 从默认起步方向前方出生，初始尾部靠近玩家，仍遵守出生保护和正常战斗规则。开局显示保护剩余时间，保护中暂不能与对手互吃；保护结束后可留意身体上的金点。连续被同一对手弹开时，每局可自动触发一次短时脱困保护：期间可穿过对手，但双方不能互相攻击，结束后恢复原判定。根据接触部位的实际攻防吃身体，弱者碰到身体会弹开；头碰头另按强度判定。身体随当前长度和防御变大，尾部收窄、强化亮度和加速流光提示强弱，不在头顶显示攻防数字；视觉不改变碰撞范围。镜头随长度平滑拉远，大厅设置与暂停页可选择较近、标准或较远的相对视野。局内能量会提前两秒预告实际位置，玩家和 AI 均可争夺。旧版围猎和每日挑战保留在「更多玩法」，明确标注为旧版，并继续沿用身体碰撞淘汰及旧成绩规则。旧版每日挑战包含巨蛇悬赏、星核争夺、果潮混战三种轮换事件之一，以统一参数进行最长 60 秒的对局，使用固定 20 Hz、整数运算和可重放输入；联网后服务端重放成绩，再记录每日个人最高分与排行榜。当前规则为 `daily-4`：星核持有者会受到附近 AI 的追逐，AI 持有者也会回避近处威胁；与 `daily-1` / `daily-2` / `daily-3` 分榜并保留旧版回放。`daily-3` 和 `daily-4` 当日好友挑战各按原规则应战。今日有效成绩核验后可生成带签名的好友挑战，接收方核验同日同规则后可比较分数；抖音分享面板和真实设备仍需实测。离线对局明确标记为练习，不产生账号奖励或排名。 已签发凭证的待结算对局保存在本机，临时失败后会在前台按退避间隔自动重试，并仅提交给所属账号；未结算凭证过期或校验失败会显示原因；已成功结算但响应丢失时，即使凭证已过期仍可凭原账号和签名恢复结算回执，不重复发奖。
  发布顺序：先部署支持 `daily-4` 和账号快照版本的服务端并验证旧版接口，再发布客户端；回滚服务端前须确认没有待结算的 `daily-4` 对局。新版客户端会拒绝更旧的账号快照；恢复旧备份或回滚到不支持快照版本的服务后，需要让客户端重新登录并核对进度。
- **养成**：4 款皮肤、9 个天赋节点、推荐加点、免费重置及首局操作提示。累计完成 2 场有效账号对局可免费解锁「曜焰」；已拥有时不会重复发放。前 7 个标准活跃日的经验进度接近线性，后续采用对数增长；每日挑战统一属性。对局经验每天最多计入 3 局，对局基础金币每天最多计入 20 局；完成 2 局的日常目标和探洞经验另计。
- **梦游探洞**：账号联网派遣和领取，探索期间可离线；服务端按派遣时配置和服务器时间结算。三个洞穴各有碎片和固定图鉴里程碑，经验每天最多计入 20 点；进入页面或从后台恢复后重新同步服务端时间，断网时保留已派遣提示并等待联网领取。
- **进阶玩法**：完成 3 场后可从「更多」进入三房间深潜原型；房间间可选择收圈护盾、反弹后免费加速或淘汰额外成长，第三房间有带护盾、蓄力方向提示和冲刺的首领原型。旧版普通围猎也位于次级入口。完整探洞获得的碎片可逐步解锁梦游图鉴故事，查看不消耗碎片。
- **交互与美术**：五个原创角色立绘及各自轻量待机动作、可分离的围猎轨迹、方向和碰撞提示、闭环反馈及动态结算；小地图对手颜色跟随轨迹，画面外的事件目标有边缘提示。大厅、装扮和结算使用 Unity UI，并处理安全区；其他页面仍使用原有 UI。设置中可独立切换并调节音乐和音效音量，以及切换精简特效和抖音短震动（默认关闭，设备支持情况需实机确认）。强弱试玩的真实吞食和受伤使用不同短音效；后台恢复不重放旧事件。大厅和对局使用预渲染的原创临时循环，资源缺失时回退到程序合成；首次交互后播放，切换场景时淡出淡入；指定热门歌曲的版本和授权尚待落实，移动端音质及生命周期仍需实测。角色素材不可用时保留所有权并使用默认造型或备用显示，装扮页会提示。手机在屏幕任意位置拖动转向，按住第二根手指加速；桌面支持鼠标拖动、方向键、WASD 和空格加速。
- **角色外形预览**：本机制作了 20 个「节奏盒子」角色的内部参考立绘，并支持分页试穿；预览只保存在本机，不进入服务器库存，角色本体不要求做成蛇。参考素材位于被 Git 忽略的 Unity `Editor` 目录，**不会推送到公开仓库**，默认构建也不打包；显式开启内部预览构建才会暂时加入。它们不是已获授权的正式联动，商用前须核实权利、取得所需许可并完成正式资产验收。
- **商业化预留**：对局、探洞结算与皮肤试穿预留自愿激励视频入口；真实奖励默认关闭，直到平台回调和服务端核验接入。基础奖励不依赖广告。

强弱离线试玩中，玩家被吃后若身体仍较短，可在有限次数和冷却内获得带亮圈的回追果实；放置时会尽量避开对手当前蛇头，拥挤时可能减少或不生成。果实需实际拾取，对手之后也能争夺。具体规则和当前测试局限见 [高优先级体验优化](docs/plans/v0.0.3.4.opt.md)。

设计及数值依据见 [v0.0.3 计划](docs/plans/v0.0.3.md)、[v0.0.3.1 优化计划](docs/plans/v0.0.3.1.opt.md)、[v0.0.3.2 优化计划](docs/plans/v0.0.3.2.opt.md)和 [7 / 14 / 30 天经济测算](docs/plans/v0.0.3.1.balance.md)。v0.0.2 的真人房间代码仍用于开发研究，仅在 Unity 编辑器大厅按 F10 显示，不作为首发入口。

## 验证状态与未完成项

本机自动化覆盖竞技规则、成长、新旧每日挑战回放与分榜、探洞、旧数据迁移、SQLite 并发结算和重启恢复；Unity 编辑器检查资源导入，并在隔离场景运行角色动画及首页、装扮、结算按钮的指针命中和页面切换。已生成 Unity 版抖音离线预览包，需求方确认手机抖音扫码可以进入并看到内部参考皮肤；完整玩法、不同设备性能和平台接口仍待真机验证。

服务默认使用单进程 JSON 开发存储，也可显式启用 SQLite 事务存储，适合本机和单节点试用。普通 PvE 已使用服务端签发并绑定凭证的随机种子，普通对局共享初始化代码，并将新手状态、皮肤和天赋保存为开局快照；同一 .NET 运行时下的相同输入测试能复现轨迹。这只是回放基础：普通 PvE 已记录逐步输入并可在同一运行时试验回放，但服务端发奖仍只检查客户端上报的时长和操作次数，尚未验证输入。2026-11-17 在本机 arm64 .NET 10.0.404 与 Unity 6000.0.85f2 无图形编辑器的对照中，随机数抽样、果实和初始角度的指纹相同，但初始蛇头坐标存在浮点末位差异；其中一个测试种子的淘汰、事件与长度等离散状态逐帧指纹也不同，因此不能用于正式的可信发奖。复现命令为 `python3 scripts/test-pve-runtime.py`；其非零退出表示检测到差异，目标 WebGL 与真机仍未比较。需要版本化、跨平台验证和服务端重放后才能上线相关奖励。正式公开榜和账号系统还需要生产数据库与运维方案、HTTPS、域名配置、监控、备份恢复、异常治理、负载及多平台确定性验证；回放能核对规则和分数，不能单独证明由真人操作。匿名账号与正式账号同时已有不同存档时，自动合并冲突尚未实现。

抖音账号登录、分享和激励视频已有按官方接口编写的条件编译适配代码，但当前扫码预览仅为离线练习；分享链路、广告位及服务端可验证回调均须在实际环境确认，广告默认不会发奖。现有角色、玩法和动效仍属可运行原型：角色专属完整动作、首领和探洞演出的品质评审、玩家对照试玩、抖音真机性能与正式授权等尚未完成，不能宣称 v0.0.3.2 计划已全部验收。

## 本地开发环境

需要 Node.js 22+、.NET 10 SDK，以及 Unity `6000.0.85f2` 和 WebGL Build Support。Apple Silicon Mac 的 Unity Editor 还需要 Rosetta 2；首次运行由使用者处理账号、许可证及相关条款。当前 Unity 与 TTSDK 的组合已生成一次可打开的抖音预览包，尚未完成广泛兼容性验证。

```sh
# 竞技与成长规则、房间通信、服务端账号/回放/探洞/排行
npm test

# 测量桌面 .NET 每日挑战模拟分配，不包含 Unity 渲染或手机性能
python3 scripts/benchmark-daily.py
python3 scripts/benchmark-arena.py

# 后台编译 Unity 运行时代码，不启动编辑器或游戏
npm run test:unity:compile
# 本机安装并导入 TTSDK 后，还可检查抖音条件编译：
python3 scripts/check-unity-compile.py --douyin

# 使用临时 Unity 工程检查客户端 JSON 响应和资源导入
npm run test:unity

# 本地整数 PvE 服务端实验验证（仅本机、无奖励）
# 启动开发服务器时设置 SNAKE_FIXED_PVE_EVAL=1 和 SNAKE_DEV_AUTH=1；生产环境不会注册实验接口。
# 服务监听 127.0.0.1:2568，预览地址使用现有 CORS 白名单的 127.0.0.1:8082。
python3 scripts/build-unity-isolated.py webgl --offline-practice --no-graphics --fixed-pve-local-api http://127.0.0.1:2568 --output /tmp/snake-fixed-local-preview
# 使用 SNAKE_PREVIEW_ROOT=/tmp/snake-fixed-local-preview npm run preview:unity 启动浏览器预览。
# 实验接口为 POST /api/v3/internal/fixed-pve/start 和 /verify，需登录及完整回放。

# 仅离线试玩 A/B/C 和可选的开局实验 D，输出路径必须不存在
python3 scripts/build-unity-isolated.py webgl --offline-practice --comparison-preview --no-graphics --output /tmp/snake-abc-preview
# 抽样检查三方案开局（桌面脚本，不代替真人试玩）
python3 scripts/evaluate-daily-comparison.py

# 对比整数 PvE 在桌面 .NET 和无头 Unity WebGL 的样本指纹
python3 scripts/test-fixed-pve-webgl.py

# 构建隔离客户端并在本机以真实时间模拟触摸，验证一局无奖励回放
python3 scripts/test-fixed-pve-client-e2e.py

# 后台验证角落、小地图及暂停键附近的拖拽、双指与暂停恢复
python3 scripts/test-fullscreen-touch-webgl.py

# 检查强弱试玩脱困保护的实际触发、攻防、计时和回放
python3 scripts/test-squeeze-core.py

# 后台用模拟触摸完成强弱试玩，并将实际输入分别在 WebGL 与 .NET 重放
python3 scripts/test-strength-touch-replay-webgl.py

# 后台用拖拽、双指加速及松开操作完成强弱试玩的本机服务端无奖励回放验证
python3 scripts/test-strength-pve-client-e2e.py

# 使用图形后端在临时 Unity 工程中验证运行时动画与 UI 命中
npm run test:unity:play
```

WebGL 指纹和客户端回放检查需要 Chrome，可用 `SNAKE_CHROME` 指定可执行文件；它们在临时工程中编译并用静音无头浏览器运行，可能耗时数分钟。强弱试玩可运行 `python3 scripts/test-strength-pve-webgl.py`，对 18 局服务端实验、16 局 v4、24 局 v5、40 局 v6、70 局 v7、70 局 v8、96 局 v9 及 112 局 v10 的逐步可观察状态及部分内部状态进行 .NET / WebGL 指纹对照，并检查同帧吞食及阻挡、补给优先与到期拾取、脱困保护三个受控场景；该脚本和 `TryReplayStrength` 仅用于研发，不能验证所有隐藏状态、任意输入或抖音设备，也不能用于正式奖励。客户端检查还会启动临时本机服务并按真实时间完成一局。指纹一致只覆盖脚本样本，不代表抖音手机、所有输入或可信发奖已通过。

强弱触摸回放测试等待终局的默认上限为 600 秒，可通过 `SNAKE_STRENGTH_MATCH_TIMEOUT_SECONDS` 设置 60～900 秒；上限仅控制等待，不改变游戏速度或成功条件。负载较高时，静音无头浏览器可能比实时模拟慢，超时不能作为回放通过。

该触摸诊断的 CDP 请求有独立超时，连接中断会结束等待；外层超时会终止其进程组。此进程组清理目前仅支持 macOS / Linux（POSIX）。可运行 `node --test test/v3/cdp-requests.test.cjs` 和 `python3 test/v3/headless_process_test.py` 检查断连、超时与清理，不代替真实触摸或抖音设备测试。

`python3 scripts/test-strength-touch-replay-webgl.py` 默认严格要求自然对局出现保护结束提示和回追；触摸时序会变化，回追未发生时测试会失败。若只验证左拖限角及完整跨端回放，可显式运行 `SNAKE_STRENGTH_OPENING_LEFT=1 SNAKE_STRENGTH_REQUIRE_PROTECTION_CUE=0 SNAKE_STRENGTH_REQUIRE_RECOVERY=0 python3 scripts/test-strength-touch-replay-webgl.py`；输出会标明跳过的两项，不能把它当作回追或提示验收。确定性回追覆盖由上面的逐步指纹测试另行检查。

运行 `python3 scripts/evaluate-aligned-start.py --seeds 100` 可比较开局方向和首次转向辅助的脚本样本；结果不能代替真人手感或平衡测试。

运行 `python3 scripts/evaluate-opening-turn-cap.py --seeds 100` 可比较首次急转限角前后的脚本样本；身体近邻不是伤害判定，结果不能代表真人体验。

运行 `python3 scripts/evaluate-strength-ai.py` 可统计两组各 50 局固定输入及一组 50 局由同一 AI 策略控制玩家的强弱试玩结果；它适合辅助比较规则改动，不等于真人玩法或难度评估。

运行 `python3 scripts/evaluate-strength-encounter.py --seeds 100` 可比较离线试玩开局遭遇候选与之前的补给方案；固定方向和简单追尾脚本仅用于研发筛选，不代表真人或抖音设备表现。

运行 `python3 scripts/evaluate-strength-recovery.py --seeds 100` 可比较短蛇受伤后的回追候选；同种子下两套规则在首次符合条件的受伤前一致，之后随机过程及追逐策略可能分叉。

运行 `python3 scripts/evaluate-safe-recovery.py --seeds 300` 可比较旧版与安全落点候选在首次符合条件时的生成和下一步拾取；这是脚本对照，不代表真人或手机表现。

运行 `python3 scripts/benchmark-fixed-pve.py --strength` 可测桌面 .NET 强弱模拟的托管分配；不带参数测旧整数 PvE。结果包含开局、不包含 Unity 渲染，不能替代抖音手机上的帧时间与内存测量。

全屏触摸脚本使用隔离构建和静音无头浏览器，临时延长角色保护以完成输入测试；它不是抖音真机或锁屏恢复验收。

每日挑战接口测试按实际局时等待，可能需要约 1 分钟；测试数据放在临时目录，不使用本机游戏存档。Unity 回归检查需要已安装并启用许可证的 Editor；可用 `UNITY_EDITOR` 指定可执行文件。

强弱服务端验证只在非生产环境显式设置 `SNAKE_STRENGTH_EVAL=1` 时启用，并检查回环地址；不要将实验端口经反向代理暴露。独立客户端实验开关也仅允许本机 HTTP 且不在抖音包启用。服务器签发种子、保存首局状态并拒绝无效或过快的回放，结果不发奖励、不进排行；客户端与服务器额外比较终局可观察状态的版本化非加密指纹，旧回执缺少指纹时会明确提示。指纹不覆盖全部隐藏状态，也不是防篡改证明；当前规则标识仍是可变的开发标记，不是正式版本或防作弊保证。

本地开发服务默认监听 `127.0.0.1:2567`。当前客户端的 API 配置使用 `2568`，以便和旧版房间服务并行运行。单机练习不需要启动它；正式账号进度、排行和探洞需要服务：

```sh
# 仅供本机开发，启用回环地址上的设备登录
SNAKE_PORT=2568 SNAKE_DEV_AUTH=1 npm run server:v3
```

开发服务将状态写入忽略版本管理的 `server/data/v0.0.3.json`。可用 `SNAKE_HOST`、`SNAKE_PORT` 和 `SNAKE_DATA_FILE` 修改监听地址、端口和数据文件。`SNAKE_SESSION_KEY` 应为仅在服务端配置的稳定随机密钥；不设置时每次重启随机生成，旧会话和未提交的对局凭证会失效。`SNAKE_ALLOWED_ORIGINS` 是逗号分隔的浏览器允许来源，默认只包含本机 8082 端口。开发登录仅允许回环地址，不能用于手机或公开环境。

需要在本机验证 SQLite 时，可指定一个新的数据库文件；首次创建时会在事务中一次性导入 `SNAKE_DATA_FILE` 指向的现有 JSON（若存在），原文件不被修改。应先备份并在副本上验证迁移，创建后再改 JSON 不会重新导入：

```sh
SNAKE_PORT=2568 SNAKE_DEV_AUTH=1 SNAKE_SQLITE_FILE=server/data/snake.sqlite npm run server:v3
```

### 背景音乐素材

大厅与对局默认使用程序合成的临时循环，设置中可分别调整音乐、音效音量。确认游戏内使用、剪辑、发行及商业化权利后，可分别将短音频放在 `unity/Assets/Resources/Music/Home` 和 `unity/Assets/Resources/Music/Battle`（使用 Unity 支持的音频文件扩展名并提交其 `.meta`）；客户端优先加载对应素材，缺失时使用临时循环。不同宿主的压缩格式、音质、音量、循环接缝和内存应在真实设备检查。当前未加入用户指定的热门歌曲，也未确认其游戏内使用授权。

### SQLite 备份与恢复演练

在线备份使用 SQLite Backup API 读取一致性快照，包含尚未 checkpoint 的 WAL 数据；工具检查数据库完整性和游戏表结构，并以仅当前用户可读写的权限创建新文件，拒绝覆盖已有文件。目标目录须预先创建并限制访问，正式备份还需要异地加密保存、保留策略和监控。

```bash
python3 scripts/backup-sqlite.py /path/to/game.sqlite /secure/backups/game-2026-11-17.sqlite
```

恢复时先停止所有指向目标数据库的服务，保留原数据库及其 `-wal`、`-shm` 文件；使用该工具将备份写入一个**新的路径**，将测试服务的 `SNAKE_SQLITE_FILE` 指向该路径，用原有 `SNAKE_SESSION_KEY` 校验账号、资产和排行榜，再根据运维切换流程上线。不要把快照直接覆盖正在使用的数据库，或把旧 WAL 文件混入恢复路径。测试套件会在源服务运行时备份并启动隔离副本，但这不能代替生产环境定期恢复演练。

```bash
python3 scripts/backup-sqlite.py /secure/backups/game-2026-11-17.sqlite /safe/restore/game.sqlite
```

`SNAKE_RATE_LIMIT_PER_MIN` 可设置单进程、按直接连接 IP 的每分钟请求上限，`/health` 除外；反向代理环境需另外设计可信来源与全局限流。`SNAKE_ENV=production` 要求指定 SQLite 和至少 32 字符的稳定 `SNAKE_SESSION_KEY`，并禁止开发登录和开发广告模拟；这些启动检查不代表服务已具备生产部署条件。

普通 PvE 当前仅凭客户端上报的时长和操作次数结算，不能证明对局真实。因此生产模式暂不签发新的普通 PvE 对局，也不结算未完成的旧普通 PvE 对局；已完成的对局仍可凭原凭证查询回执。客户端会退回无账号奖励的练习，已操作并完成练习的账号可在当前客户端会话中进入每日挑战；练习不会改变服务端进度，切换账号不继承该入口状态。每日挑战通过服务端回放验证后才记录正式进度。开发环境保留原有联调用途。只有完成规则版本化、可信回放和目标平台验证后才可重新开放正式奖励；这一保护不代表其他功能已满足发布要求。

新客户端使用 `X-Snake-Catalog: 2` 声明可读取新版角色目录。未声明的旧客户端仍可使用旧存档；账号拥有或装备新版角色时，服务端返回 426 而不返回旧客户端无法识别的角色编号。旧客户端可能只显示通用离线提示，正式部署仍需配套更新引导；该标记是兼容性声明，不是身份或权限验证。

客户端 API 地址与开发登录开关位于 `unity/Assets/Resources/GameConfig.json`；默认地址只适用于同一台电脑。公开服务需要配置手机可访问的 HTTPS 地址、抖音域名白名单和平台身份鉴权。抖音 `AppID`、`AppSecret` 只通过服务端环境变量 `SNAKE_DOUYIN_APPID` 与 `SNAKE_DOUYIN_SECRET` 提供，**不要提交密钥或写进客户端**。`SNAKE_DEV_ADS=1` 仅供显式本地开发模拟，不能用于生产发奖。

## Unity 工程与抖音适配

在 Unity Hub 中以指定 Editor 打开 `unity/`。首次打开时编辑器脚本会创建 `Assets/Scenes/Arena.unity` 和必需材质。需要构建时，可运行：

```sh
npm run build:unity:mac
npm run build:unity:webgl
npm run preview:unity
```

需要在**本机内部预览**包含参考角色的 macOS 版本时，可显式执行：

```sh
SNAKE_INCLUDE_REFERENCE_ASSETS=1 npm run build:unity:mac
```

该开关仅供内部审阅，并要求本机已提供 `unity/Assets/Editor/ReferenceSkins/` 下的参考图片；公开仓库不含这些图片。默认构建不包含参考角色素材。构建脚本在完成或失败时清理临时拷贝，并在普通构建中检查打包内容。

输出分别位于 `dist/unity-mac/Snake Club.app` 与 `dist/unity-webgl/`；浏览器预览默认使用 `http://127.0.0.1:8082`。其他 Unity 安装位置可设置 `UNITY_EDITOR`，构建输出可设置 `SNAKE_BUILD_OUTPUT`。这些命令不会自动生成抖音小游戏包。

若当前 Unity 工程已在编辑器中打开，可以使用隔离构建脚本指定**尚不存在的**输出路径，不关闭正在使用的工程，也不覆盖旧构建：

```sh
python3 scripts/build-unity-isolated.py mac --output 'dist/my-build/Snake Club.app'
python3 scripts/build-unity-isolated.py webgl --output 'dist/my-webgl'
SNAKE_PREVIEW_ROOT=dist/my-webgl npm run preview:unity
```

只需手机浏览器临时试玩时，可为隔离构建添加 `--offline-practice`：构建副本会关闭开发登录、服务端地址和参考角色，不改变工作区配置；成绩和进度不会同步账号。需要试玩实验性整数 PvE 时，可在该离线构建额外添加 `--fixed-pve-preview`；本机 WebGL 构建可再添加 `--no-graphics`，通过 Unity 无图形批处理构建，目标平台兼容性以实际结果为准。也可给下方抖音离线预览构建添加相同参数。该开关只有在两个服务地址为空且开发登录关闭时生效，首页和局内会标明试玩，结果不提交、不发账号奖励。它尚未完成画面、手感和性能验收，不是发布版本。手机浏览器版是兼容性预览，不是抖音小游戏包。可用 `SNAKE_PREVIEW_ROOT` 指定构建目录，并用 `SNAKE_PREVIEW_PREFIX=/随机字符串/` 为预览增加难以猜测的访问路径。预览服务只监听本机；若通过隧道分享，拿到链接的人均可访问，链接和构建应仅用于授权范围内的测试。

抖音离线预览包可在衣橱选择未解锁的原创造型进行本次试玩；选择只影响画面，不增加属性、不写入账号或持久保存。配置了账号服务的环境仍按账号拥有和解锁规则处理。

试玩新的强弱吃身体原型，可使用 `python3 scripts/build-unity-isolated.py webgl --offline-practice --strength-pve-preview --no-graphics --output /tmp/snake-strength-preview`；抖音隔离预览构建也支持 `--strength-pve-preview`。该模式仅供离线试玩：攻击力足够时吃接触到的身体段，弱者碰身体不会直接出局；对手身体上的金点提示按当前攻防比较较弱的部位，不保证下一步命中。头碰头、AI、数值和视觉仍是待验证原型。它不登录、不入榜、不发奖，也不会修改旧版日榜。

本机这次测试另生成了 `dist/unity-v0.0.3-mac/Snake Club.app`，不覆盖旧版构建。桌面大厅可按回车开始普通对局，按 D 开始每日挑战；对局中使用 WASD 或方向键转向、空格加速。测试产物不纳入版本管理。

使用[抖音官方指南](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/unity-game-access/install-connect/install-sdk)在本机安装 BGDT 和 TTSDK；这些工具不提交到仓库。TTSDK 6.9.7 已用于生成本机抖音预览包。以下命令会在隔离副本中构建**离线练习**，不上传或提交审核，输出目录必须尚不存在：

```sh
python3 scripts/build-douyin-preview.py --appid '你的小游戏AppID' --output 'dist/my-douyin-preview'
```

该脚本默认使用 Unity `-batchmode -nographics`，避免构建时显示游戏或抢占桌面焦点；本机 TTSDK 6.9.7 已完成无图形离线转换。失败或超时时会把可用的 Unity 日志保存在本机临时目录并打印路径；重试应使用新的输出目录。脚本还会检查产物位于输出目录内、入口文件非空、JSON 与竖屏和 AppID 配置，以及声明的分包目录存在且位于项目内；这些结构检查不能保证资源引用完整或真机运行正常。其他 SDK 版本的兼容性以实际构建为准；只有明确需要图形环境时才使用 `--graphics`。

仅为经授权的小范围内部审阅，且本机已准备参考素材时，可显式加上 `--include-reference-skins`；该选项会将参考图放入测试包，不代表正式联动或已获商业使用权。构建完成后可将输出的 `tt-minigame` 目录导入抖音开发者工具生成预览码：预览时产物会交给抖音处理，并不等同于上传测试通道或更新已发布的版本。构建副本启用 `SNAKE_DOUYIN_TTSDK` 编译开关；离线预览会初始化 SDK，但不会请求抖音登录或连接账号服务。此前生成的预览包没有自动启用该开关，需重新构建才能验证这些平台适配代码。本离线练习包不使用本机游戏服务器，手机需要能够访问抖音获取预览内容。抖音账号登录、分享和广告需要另行配置与真机验证；普通 WebGL 构建或旧版 `dist/douyin` 都不是本 Unity 版本的抖音发布包。

## 资源许可

大厅秘境背景、战场地表与五个原创角色为本项目生成的图片；角色素材的生成方式和参考范围见[素材记录](docs/asset-generation-v0032.md)。节奏盒子参考角色仅用于本机造型试作，正式使用需确认相应权利。界面字体是 Noto Sans SC 的 400 字重字符子集，原始字体来自 [Google Fonts](https://github.com/google/fonts/tree/main/ofl/notosanssc)，以 SIL Open Font License 1.1 发布；许可文本位于 `unity/Assets/Resources/NotoSansSC-OFL.txt`。子集不包含所有生僻字。
