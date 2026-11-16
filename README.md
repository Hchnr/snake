# 灵蛇秘境 · Snake Club

v0.0.3 正在开发中，首发以抖音小游戏为目标，浏览器 WebGL 用于开发验证。玩家在大地图对抗 AI：可以吃果实，淘汰对手会直接成长；撞墙反弹，不会因此出局。首发设计采用本地 PvE 和真人异步每日挑战，不要求玩家同时在线；不规划微信版本。

## 当前内容

- **对局**：普通 PvE 加入回弹冲刺与星核争夺。每日挑战以统一参数进行最长 60 秒的对局，使用固定 20 Hz、整数运算和可重放输入；联网后服务端重放成绩，再记录每日个人最高分与排行榜。离线对局明确标记为练习，不产生账号奖励或排名。
- **养成**：4 款皮肤、9 个天赋节点、免费重置及首局操作提示。前 7 个标准活跃日的经验进度接近线性，后续采用对数增长；每日挑战统一属性。对局经验每天最多计入 3 局，对局基础金币每天最多计入 20 局；完成 2 局的日常目标和探洞经验另计。
- **梦游探洞**：账号联网派遣和领取，探索期间可离线；服务端按派遣时配置和服务器时间结算。三个洞穴各有碎片和固定图鉴里程碑，经验每天最多计入 20 点。
- **交互与美术**：秘境大厅背景、3D 灵蛇展示、皮肤预览、竞技场视觉效果及可关闭的基础提示音。手机使用虚拟摇杆与加速按钮，桌面支持方向键、WASD 和空格加速。
- **商业化预留**：对局、探洞结算与皮肤试穿预留自愿激励视频入口；真实奖励默认关闭，直到平台回调和服务端核验接入。基础奖励不依赖广告。

设计及数值依据见 [v0.0.3 计划](docs/plans/v0.0.3.md)。v0.0.2 的真人房间代码仍用于开发研究，仅在 Unity 编辑器大厅按 F10 显示，不作为 v0.0.3 首发入口。

## 验证状态与未完成项

已在本机 Unity 编辑器运行游戏，检查大厅、普通对局操作、结算与进度同步，并完成一局每日挑战的上传、服务端回放与本地排名；自动化测试覆盖竞技规则、成长、每日挑战回放与排行、探洞和重复结算，并通过 Unity 编辑器检查账号响应与背景图片导入。尚未部署服务或发布小游戏，也未进行抖音模拟器、手机真机和平台接口测试。现有设计参数和交互效果仍需后续试玩、设备性能和平台兼容性验证。

本地服务为单进程、JSON 文件持久化的开发原型。正式公开榜和账号系统还需要事务型存储、HTTPS、域名配置、限流、监控、备份、异常治理以及多平台确定性验证；回放能核对规则和分数，不能单独证明由真人操作。匿名账号与正式账号同时已有不同存档时，自动合并冲突尚未实现。

抖音账号登录、分享和激励视频已有按官方接口编写的条件编译适配代码，但 TTSDK 尚未安装，当前 Unity 版本兼容性、分享链路、广告位及服务端可验证回调均须在实际环境确认；广告默认不会发奖。完整的音效与美术动效仍待制作。

## 本地开发环境

需要 Node.js 22+、.NET 10 SDK，以及 Unity `6000.0.85f2` 和 WebGL Build Support。Apple Silicon Mac 的 Unity Editor 还需要 Rosetta 2；首次运行由使用者处理账号、许可证及相关条款。Unity 与抖音转换器的兼容性尚未实测。

```sh
# 竞技与成长规则、房间通信、服务端账号/回放/探洞/排行
npm test

# 使用临时 Unity 工程检查客户端 JSON 响应和资源导入
npm run test:unity
```

每日挑战接口测试按实际局时等待，可能需要约 1 分钟；测试数据放在临时目录，不使用本机游戏存档。Unity 回归检查需要已安装并启用许可证的 Editor；可用 `UNITY_EDITOR` 指定可执行文件。

本地开发服务默认监听 `127.0.0.1:2567`。当前客户端的 API 配置使用 `2568`，以便和旧版房间服务并行运行。单机练习不需要启动它；正式账号进度、排行和探洞需要服务：

```sh
# 仅供本机开发，启用回环地址上的设备登录
SNAKE_PORT=2568 SNAKE_DEV_AUTH=1 npm run server:v3
```

开发服务将状态写入忽略版本管理的 `server/data/v0.0.3.json`。可用 `SNAKE_HOST`、`SNAKE_PORT` 和 `SNAKE_DATA_FILE` 修改监听地址、端口和数据文件。`SNAKE_SESSION_KEY` 应为仅在服务端配置的稳定随机密钥；不设置时每次重启随机生成，旧会话和未提交的对局凭证会失效。`SNAKE_ALLOWED_ORIGINS` 是逗号分隔的浏览器允许来源，默认只包含本机 8082 端口。开发登录仅允许回环地址，不能用于手机或公开环境。

客户端 API 地址与开发登录开关位于 `unity/Assets/Resources/GameConfig.json`；默认地址只适用于同一台电脑。公开服务需要配置手机可访问的 HTTPS 地址、抖音域名白名单和平台身份鉴权。抖音 `AppID`、`AppSecret` 只通过服务端环境变量 `SNAKE_DOUYIN_APPID` 与 `SNAKE_DOUYIN_SECRET` 提供，**不要提交密钥或写进客户端**。`SNAKE_DEV_ADS=1` 仅供显式本地开发模拟，不能用于生产发奖。

## Unity 工程与抖音适配

在 Unity Hub 中以指定 Editor 打开 `unity/`。首次打开时编辑器脚本会创建 `Assets/Scenes/Arena.unity` 和必需材质。需要构建时，可运行：

```sh
npm run build:unity:mac
npm run build:unity:webgl
npm run preview:unity
```

输出分别位于 `dist/unity-mac/Snake Club.app` 与 `dist/unity-webgl/`；浏览器预览默认使用 `http://127.0.0.1:8082`。其他 Unity 安装位置可设置 `UNITY_EDITOR`，构建输出可设置 `SNAKE_BUILD_OUTPUT`。这些命令不会自动生成抖音小游戏包。

本机这次测试另生成了 `dist/unity-v0.0.3-mac/Snake Club.app`，不覆盖旧版构建。桌面大厅可按回车开始普通对局，按 D 开始每日挑战；对局中使用 WASD 或方向键转向、空格加速。测试产物不纳入版本管理。

BGDT 已导入本机工程；使用[抖音官方指南](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/unity-game-access/install-connect/install-sdk)安装 TTSDK 后，才可在 WebGL 构建的 Scripting Define Symbols 中启用 `SNAKE_DOUYIN_TTSDK`。需要平台登录/分享/广告配置时应以当前官方接口和实际安装的 SDK 为准，再通过官方工具转换，并在抖音开发者工具与手机上验证。普通 WebGL 构建或旧版 `dist/douyin` 都不是本 Unity 版本的抖音发布包。

## 资源许可

大厅秘境背景为本项目生成的原创图片。界面字体是 Noto Sans SC 的 400 字重字符子集，原始字体来自 [Google Fonts](https://github.com/google/fonts/tree/main/ofl/notosanssc)，以 SIL Open Font License 1.1 发布；许可文本位于 `unity/Assets/Resources/NotoSansSC-OFL.txt`。子集不包含所有生僻字。
