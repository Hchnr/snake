# 贪吃蛇 · Snake Club

一个无需后端的竖屏贪吃蛇小游戏。抖音小游戏为优先适配平台，微信小游戏复用同一套玩法和画面；本地浏览器预览用于开发调试。

## 玩法

吃果实得分并增长，撞墙或撞到身体结束，填满棋盘获胜。滑动屏幕或点击屏幕方向键控制；可暂停、继续、重开，并通过平台分享按钮邀请朋友。最高分保存在当前平台的本地缓存，不跨设备或平台同步。浏览器预览额外支持方向键 / WASD，空格或 Enter 开始、暂停、继续和重开。

## 快速开始

需要 Node.js 18 或更高版本；没有第三方依赖，不需要 `npm install`。

```sh
npm test
npm run build
```

构建结果：

| 目录 | 用途 |
| --- | --- |
| `dist/douyin` | 抖音小游戏开发者工具工程，优先验证 |
| `dist/wechat` | 微信开发者工具小游戏工程 |
| `dist/browser` | 本地浏览器预览 |

### 抖音小游戏

1. 在抖音开放平台准备小游戏账号和 AppID，安装并登录抖音开发者工具。仅调试时可按开发者工具的提示使用测试号。
2. 在开发者工具中选择导入**小游戏**，项目目录选择 `dist/douyin`；按提示填写真实或测试 AppID。
3. 编译后检查画面、触摸、切后台恢复、本地最高分和分享，并使用开发者工具的预览功能进行真机测试。
4. 在支持的宿主中，点击游戏内“侧边栏福利”，跳转首页侧边栏，再从侧边栏返回并领取薄荷配色。上线前按[官方技术指南](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/open-ability/Introduction-for-tech)在模拟器和真机验证完整复访链路；该配色奖励保存在本机。

构建时也可通过环境变量指定 AppID：

```sh
SNAKE_DOUYIN_APPID=你的抖音小游戏AppID npm run build
```

### 微信小游戏

在微信开发者工具中选择导入**小游戏**，目录选择 `dist/wechat`，按提示填写微信小游戏 AppID，再分别进行模拟器和真机验证。默认使用 `touristappid` 作为开发工具占位配置；需要账号相关能力时应使用有权限的真实 AppID。

```sh
SNAKE_WECHAT_APPID=你的微信小游戏AppID npm run build
```

重新构建会保留已存在的 `project.config.json` 中的 AppID 和其他本地项目设置，环境变量优先；源码和入口等生成文件会被覆盖，不应直接编辑。`dist/` 不纳入 Git。

### 浏览器预览

```sh
npm run build
npm run preview
```

访问 `http://127.0.0.1:8080`。服务仅监听本机；在 `cuihu-pwm-dev` 远程运行时，可在自己的电脑另开终端转发端口：

```sh
ssh -L 8080:127.0.0.1:8080 cuihu-pwm-dev
```

然后在本机浏览器打开上述地址。若开发者工具运行在本机而项目在远程，可将 `dist/douyin` 或 `dist/wechat` 拷贝到本机后再导入；修改代码后需重新构建并拷贝。

## 目录

```text
src/core.js          纯游戏规则
src/app.js           Canvas 画面、输入、游戏循环
src/platform.js      抖音 / 微信平台适配
src/main.js          小游戏入口
src/browser.js       开发预览适配
scripts/             构建和本地预览
browser/             浏览器预览页面
test/                自动化测试
docs/plans/          版本计划
```

## 测试与接入边界

`npm test` 覆盖游戏规则、边界条件、存储失败、生命周期、触摸、分享和抖音侧边栏复访，并通过模拟 `tt` / `wx` 运行构建后的平台入口。这些测试不能代替平台开发者工具、真实账号和手机上的验证；分享展示、侧边栏链路及上线审核以各平台实际结果为准。项目未包含登录、服务端、云存档、排行榜、广告、支付或审核发布流程。

参考：[抖音小游戏开发指南](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/dev-guide/bytedance-mini-game)、[抖音小游戏分享逻辑](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/open-ability/forward/logic)、[微信小游戏开发文档](https://developers.weixin.qq.com/minigame/dev/guide/)。
