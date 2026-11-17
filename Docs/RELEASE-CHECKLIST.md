# 发布状态与剩余项（v2.9，2026-11-17）

## 已完成 ✅

| 项 | 状态 |
| --- | --- |
| 源码推送 GitHub | `main` 分支，远程 HEAD 与本地一致 |
| WebGL 打包 | `Tools → Build WebGL (for itch.io)` 已出包，`webGLCompressionFormat: 0`，itch.io 不会黑屏 |
| 打包完整性 | 17 个文件，`index.html` 在 zip 根目录，资源引用完整，zip 校验无损坏 |
| GitHub Release | tag `v2.9`，资产 `tiny-room-shooter-v2.9-webgl.zip`（4.5 MB）已上传 |
| README | 已重写为面向读者的项目说明 |
| itch.io 文案 | `ITCH-PAGE.md` 已更新到 v2.9 描述（4 夜 / 22 招 / 程序化美术） |

---

## 还差什么

### 一、itch.io 上传（主要缺口，需要你本人登录）

itch.io 需要账号登录，无法由 Agent 代操作。步骤：

1. https://itch.io → Log in → 头像 → **Create new project**
2. 文案从 `ITCH-PAGE.md` 复制：标题「守灯人 / The Lamplighter」、短描述、分类 Game / Action+Shooter、标签那串直接贴；
3. Project URL 填 `tiny-room-shooter`（二级域名，定下后别改）；
4. 上传 `tiny-room-shooter-v2.9-webgl.zip`；
5. **上传后点该文件编辑，勾选 "This file will be played in the browser"**（不勾玩家只能下载）；
6. Embed 尺寸 1280×720，勾 Fullscreen button；
7. Visibility 先 Draft，自查后转 Public。

### 二、视觉素材（itch.io 页面用，目前一个都没有）

- [ ] 封面图 **630×500**（itch.io 标准尺寸）
- [ ] 至少 **3 张截图**（1280×720：标题页 / 战斗 / Boss 战）
- [ ] （可选）30 秒实机 GIF 或短视频，放在页面顶部

截图可从本地 `node serve-webgl.js "Builds/WebGL" 8788` 打开后用系统截图工具截；封面可以用其中一张裁切。

### 三、实机验收（发布前最后一遍）

建议在**浏览器**（不只是编辑器）跑一次，确认：

- [ ] 标准模式能完整通关（4 夜 + Boss），中间选过词条；
- [ ] 无尽模式撑过第 6 波（验证 Boss 后能继续）；
- [ ] Boss 4 种招式能正常打出，酸雨安全点有明显绿光标记；
- [ ] 按 `Esc` 暂停再恢复，画面继续（不会卡住）；
- [ ] `M` 静音再恢复，BGM 回来；
- [ ] Chrome / Edge 各跑一次，窗口拖窄（4:3）墙仍贴边；
- [ ] 无痕窗口打开一次（确认公开链接正常）。

### 四、可选（不影响发布）

- [ ] 填 `Docs/PLAYTEST-NOTES.md` 的实测数据（谁玩了、卡在哪、改了什么）；
- [ ] `Docs/POSTMORTEM.md` 的面试四问补上你自己的回答；
- [ ] 发布后在 README 顶部补 itch.io 链接。

---

## 卡住了查这里

| 现象 | 先查 |
| --- | --- |
| itch 上黑屏 | zip 的 `index.html` 是否在根目录；是否勾了 "played in the browser" |
| 本地能跑、itch 上没声音 | 浏览器要求先点一下画面；embed 需允许音频 |
| 打包报 `scripts are compiling` | 等编译结束再点，BuildHelper 会提示 |
| 进 Play 画面不动 | 看右上角 `ts` 是否 1.00，Console 是否还有「阶段 →」日志 |
