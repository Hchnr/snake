# 蛇蛇竞技场 · Snake Club

v0.0.2 正在迁移到 Unity 3D。目标平台为抖音小游戏，浏览器 WebGL 用于开发验证。玩法以蛇与蛇对抗为核心：地图中有果实，击败对手可直接成长；撞墙会反弹，不会因此淘汰。手机使用虚拟摇杆与加速按钮，桌面支持方向键、WASD 和空格加速。

- **PvE**：本机单人对抗 AI，无需网络或服务器。
- **PvP**：通过房间号邀请真人，房间不添加 AI。
- **混合**：真人与 AI 在同一房间对战。

详细规则和开发顺序见 [v0.0.2 计划](docs/plans/v0.0.2.md)。v0.0.1 的 JavaScript 源码和构建脚本保留在原位置，不能把旧构建产物当作 Unity 版本。旧版也支持电脑蛇，但仍使用撞墙出局等旧规则，详见 [旧版电脑蛇计划](docs/plans/snake-arena.md)。

## 当前验证状态

竞技规则和本地 WebSocket 房间服务已有自动化测试。本机已安装并激活 Unity Editor，安装 WebGL 组件和 Rosetta 2；macOS 与浏览器 WebGL 版本已成功构建，桌面版首屏已实际显示。抖音官方 BGDT 工具已导入，TTSDK 安装、官方转换和抖音真机验证仍待完成，因此当前仓库**尚未生成可发布的抖音 Unity 包**。游戏完整交互、真机表现、联机稳定性及平台接口仍需实测。

## 本地工具与测试

需要 Node.js 22+、.NET 10 SDK，以及 Unity `6000.0.85f2` 和 WebGL Build Support。Apple Silicon Mac 上的 Unity Editor 还需要 Rosetta 2；首次运行需由使用者处理相应条款、账号和许可证。版本选择与抖音转换器的兼容性仍需实测，详见计划。

```sh
# v0.0.2：纯 C# 竞技规则和本地房间集成测试
npm run test:v2

# v0.0.1：保留的旧版测试和构建，不是 Unity 产物
npm test
npm run build
```

## Unity 运行和浏览器构建

在 Unity Hub 中以指定 Editor 打开 `unity/`。首次打开时编辑器脚本会创建 `Assets/Scenes/Arena.unity` 和必需材质；打开该场景后进入 Play。也可在 macOS 通过以下命令构建：

```sh
npm run build:unity:mac
npm run build:unity:webgl
npm run preview:unity
```

构建目录分别为 `dist/unity-mac/Snake Club.app` 和 `dist/unity-webgl/`；浏览器预览默认监听 `http://127.0.0.1:8082`。其他系统或安装位置可设置 `UNITY_EDITOR` 为 Unity 编辑器可执行文件的绝对路径；`SNAKE_BUILD_OUTPUT` 可更改构建输出路径。构建命令不会自动生成抖音小游戏包。

## 本地真人房间

```sh
npm run server:v2
```

服务默认监听 `127.0.0.1:2567`，健康检查为 `http://127.0.0.1:2567/health`，客户端默认连接 `ws://127.0.0.1:2567/ws`。打开两个本机客户端，一个选择创建房间，另一个输入相同模式和房间号加入。只需要单机 PvE 时不必启动服务。

可通过 `SNAKE_HOST` 和 `SNAKE_PORT` 配置监听地址及端口。客户端地址在 `unity/Assets/Resources/GameConfig.json` 中设置并随游戏构建，玩家只需输入房间号。其他设备访问时需配置其可访问的服务地址；公网发布还需要实际部署、TLS/WSS、抖音域名配置和必要的鉴权、运维。当前服务是本地开发原型，不包含生产级身份鉴权、持久化、扩容或反作弊，不应直接暴露到公网。

抖音官方支持自建后端或使用抖音云托管，但云托管的 WebSocket 网关文档明确标明不适用于小游戏；本项目没有假设该网关可用，具体云入口及连通性需要在实际环境中验证。

## 抖音小游戏接入

Unity WebGL 构建完成后，仍需使用[抖音官方 Unity WebGL 转换工具](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/open-capacity/sc_webgl_overall)生成小游戏工程，再用抖音开发者工具和手机验证。抖音的[安装指南](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/unity-game-access/install-connect/install-sdk)要求先安装 BGDT 并登录，再通过 BGDT 安装 TTSDK；本机已导入官方 BGDT 3.0.271，尚未安装 TTSDK。官方 SDK 工具及其本地状态不纳入版本管理，可按官方指南重新安装。建议采用[Unity 新包体格式](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/unity-game-access/install-connect/unity-new)；已有小游戏是否能切换引擎方案、实际权限、AppID、资源托管及网络配置以平台当前要求为准。不得将旧版 `dist/douyin` 或普通 WebGL 构建误标为新的 Unity 小游戏包。

## 字体许可

界面字体是 Noto Sans SC 的 400 字重和常用字符子集，原始字体来自 [Google Fonts](https://github.com/google/fonts/tree/main/ofl/notosanssc)，以 SIL Open Font License 1.1 发布；许可文本位于 `unity/Assets/Resources/NotoSansSC-OFL.txt`。子集不包含所有生僻字。
