# 灵蛇秘境 · Snake Club

v0.0.3.2 正在开发中，首发以抖音小游戏为目标，浏览器 WebGL 用于开发验证。玩家在大地图对抗 AI：可以吃果实，淘汰对手会直接成长；撞墙反弹，不会因此出局。首发设计采用本地 PvE 和真人异步每日挑战，不要求玩家同时在线；不规划微信版本。

## 当前内容

- **对局**：大厅主入口为「开始围猎」，新玩家先进入教学练习，后续进入每日挑战。角色可以用轨迹闭环捕获圈内无保护的对手；身体碰撞淘汰、立即成长和撞墙反弹仍然有效。每日挑战包含巨蛇悬赏、星核争夺、果潮混战三种轮换事件之一，以统一参数进行最长 60 秒的对局，使用固定 20 Hz、整数运算和可重放输入；联网后服务端重放成绩，再记录每日个人最高分与排行榜。新规则为 `daily-3`，与 `daily-1` / `daily-2` 分榜并保留旧版回放。离线对局明确标记为练习，不产生账号奖励或排名。 已签发凭证的待结算对局保存在本机，临时失败后会在前台按退避间隔自动重试，并仅提交给所属账号；过期或校验失败会显示原因。
- **养成**：4 款皮肤、9 个天赋节点、推荐加点、免费重置及首局操作提示。累计完成 2 场有效账号对局可免费解锁「曜焰」；已拥有时不会重复发放。前 7 个标准活跃日的经验进度接近线性，后续采用对数增长；每日挑战统一属性。对局经验每天最多计入 3 局，对局基础金币每天最多计入 20 局；完成 2 局的日常目标和探洞经验另计。
- **梦游探洞**：账号联网派遣和领取，探索期间可离线；服务端按派遣时配置和服务器时间结算。三个洞穴各有碎片和固定图鉴里程碑，经验每天最多计入 20 点。
- **进阶玩法**：完成 3 场后可从「更多」进入三房间深潜原型；房间间可选择收圈护盾、反弹后免费加速或淘汰额外成长，第三房间有带护盾、蓄力方向提示和冲刺的首领原型。普通围猎也位于次级入口。完整探洞获得的碎片可逐步解锁梦游图鉴故事，查看不消耗碎片。
- **交互与美术**：四个原创完整角色、可分离的围猎轨迹、方向和碰撞提示、闭环反馈及动态结算。大厅、装扮和结算使用 Unity UI，并处理安全区；其他页面仍使用原有 UI。设置中可独立切换音乐、音效、精简特效和抖音短震动（默认关闭，设备支持情况需实机确认）。大厅和对局使用无额外音频文件的原创临时循环，首次交互后播放；指定热门歌曲的版本和授权尚待落实，移动端音质及生命周期仍需实测。手机在屏幕任意位置拖动转向，按住第二根手指加速；桌面支持鼠标拖动、方向键、WASD 和空格加速。
- **角色外形预览**：本机制作了 20 个「节奏盒子」角色的内部参考立绘，并支持分页试穿；预览只保存在本机，不进入服务器库存，角色本体不要求做成蛇。参考素材位于被 Git 忽略的 Unity `Editor` 目录，**不会推送到公开仓库**，默认构建也不打包；显式开启内部预览构建才会暂时加入。它们不是已获授权的正式联动，商用前须核实权利、取得所需许可并完成正式资产验收。
- **商业化预留**：对局、探洞结算与皮肤试穿预留自愿激励视频入口；真实奖励默认关闭，直到平台回调和服务端核验接入。基础奖励不依赖广告。

设计及数值依据见 [v0.0.3 计划](docs/plans/v0.0.3.md)、[v0.0.3.1 优化计划](docs/plans/v0.0.3.1.opt.md)、[v0.0.3.2 优化计划](docs/plans/v0.0.3.2.opt.md)和 [7 / 14 / 30 天经济测算](docs/plans/v0.0.3.1.balance.md)。v0.0.2 的真人房间代码仍用于开发研究，仅在 Unity 编辑器大厅按 F10 显示，不作为首发入口。

## 验证状态与未完成项

本机自动化覆盖竞技规则、成长、新旧每日挑战回放与分榜、探洞、旧数据迁移、SQLite 并发结算和重启恢复；Unity 编辑器检查资源导入，并在隔离场景运行角色动画及首页、装扮、结算按钮的指针命中和页面切换。已生成 Unity 版抖音离线预览包，需求方确认手机抖音扫码可以进入并看到内部参考皮肤；完整玩法、不同设备性能和平台接口仍待真机验证。

服务默认使用单进程 JSON 开发存储，也可显式启用 SQLite 事务存储，适合本机和单节点试用。正式公开榜和账号系统还需要生产数据库与运维方案、HTTPS、域名配置、监控、备份恢复、异常治理、负载及多平台确定性验证；回放能核对规则和分数，不能单独证明由真人操作。匿名账号与正式账号同时已有不同存档时，自动合并冲突尚未实现。

抖音账号登录、分享和激励视频已有按官方接口编写的条件编译适配代码，但当前扫码预览仅为离线练习；分享链路、广告位及服务端可验证回调均须在实际环境确认，广告默认不会发奖。现有角色、玩法和动效仍属可运行原型：角色专属完整动作、首领和探洞演出的品质评审、玩家对照试玩、抖音真机性能与正式授权等尚未完成，不能宣称 v0.0.3.2 计划已全部验收。

## 本地开发环境

需要 Node.js 22+、.NET 10 SDK，以及 Unity `6000.0.85f2` 和 WebGL Build Support。Apple Silicon Mac 的 Unity Editor 还需要 Rosetta 2；首次运行由使用者处理账号、许可证及相关条款。当前 Unity 与 TTSDK 的组合已生成一次可打开的抖音预览包，尚未完成广泛兼容性验证。

```sh
# 竞技与成长规则、房间通信、服务端账号/回放/探洞/排行
npm test

# 后台编译 Unity 运行时代码，不启动编辑器或游戏
npm run test:unity:compile
# 本机安装并导入 TTSDK 后，还可检查抖音条件编译：
python3 scripts/check-unity-compile.py --douyin

# 使用临时 Unity 工程检查客户端 JSON 响应和资源导入
npm run test:unity

# 使用图形后端在临时 Unity 工程中验证运行时动画与 UI 命中
npm run test:unity:play
```

每日挑战接口测试按实际局时等待，可能需要约 1 分钟；测试数据放在临时目录，不使用本机游戏存档。Unity 回归检查需要已安装并启用许可证的 Editor；可用 `UNITY_EDITOR` 指定可执行文件。

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

只需手机浏览器临时试玩时，可为隔离构建添加 `--offline-practice`：构建副本会关闭开发登录、服务端地址和参考角色，不改变工作区配置；成绩和进度不会同步账号。手机浏览器版是兼容性预览，不是抖音小游戏包。可用 `SNAKE_PREVIEW_ROOT` 指定构建目录，并用 `SNAKE_PREVIEW_PREFIX=/随机字符串/` 为预览增加难以猜测的访问路径。预览服务只监听本机；若通过隧道分享，拿到链接的人均可访问，链接和构建应仅用于授权范围内的测试。

本机这次测试另生成了 `dist/unity-v0.0.3-mac/Snake Club.app`，不覆盖旧版构建。桌面大厅可按回车开始普通对局，按 D 开始每日挑战；对局中使用 WASD 或方向键转向、空格加速。测试产物不纳入版本管理。

使用[抖音官方指南](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/unity-game-access/install-connect/install-sdk)在本机安装 BGDT 和 TTSDK；这些工具不提交到仓库。TTSDK 6.9.7 已用于生成本机抖音预览包。以下命令会在隔离副本中构建**离线练习**，不上传或提交审核，输出目录必须尚不存在：

```sh
python3 scripts/build-douyin-preview.py --appid '你的小游戏AppID' --output 'dist/my-douyin-preview'
```

仅为经授权的小范围内部审阅，且本机已准备参考素材时，可显式加上 `--include-reference-skins`；该选项会将参考图放入测试包，不代表正式联动或已获商业使用权。构建完成后可将输出的 `tt-minigame` 目录导入抖音开发者工具生成预览码：预览时产物会交给抖音处理，并不等同于上传测试通道或更新已发布的版本。本离线练习包不使用本机游戏服务器，手机需要能够访问抖音获取预览内容。抖音账号登录、分享和广告需要另行配置与真机验证；普通 WebGL 构建或旧版 `dist/douyin` 都不是本 Unity 版本的抖音发布包。

## 资源许可

大厅秘境背景、战场地表与四个原创角色为本项目生成的图片；角色素材的生成方式和参考范围见[素材记录](docs/asset-generation-v0032.md)。节奏盒子参考角色仅用于本机造型试作，正式使用需确认相应权利。界面字体是 Noto Sans SC 的 400 字重字符子集，原始字体来自 [Google Fonts](https://github.com/google/fonts/tree/main/ofl/notosanssc)，以 SIL Open Font License 1.1 发布；许可文本位于 `unity/Assets/Resources/NotoSansSC-OFL.txt`。子集不包含所有生僻字。
