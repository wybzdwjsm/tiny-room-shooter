# 发布前检查清单

## 已完成（2026-11-16）

- [x] **WebGL 打包成功**：Editor.log 记录 `[Build] WebGL 打包成功：Builds\WebGL，大小 3 MB，用时 162 秒`
- [x] 构建内容验证：解压 `WebGL.data.br` 后能搜到「守灯人」「灯油只剩一夜」「潮眼」，确认打进去的是 v1.0 剧情而非旧版
- [x] 上传包：`守灯人-v1.0-WebGL.zip`（3.47 MB），`index.html` 在压缩包根目录，可直接拖进 itch.io
- [x] Git：两个提交（`d593594` 功能 + `110702a` 推送脚本），分支 `main`，工作区干净

**仍待完成**：GitHub 上创建空仓库 `wybzdwjsm/tiny-room-shooter`（服务器返回 `Repository not found`），然后执行 `.\push-to-github.ps1`。

---

内容已冻结（v1.0）。下面每一项都能独立验证，打勾前不要跳。

## 一、打包（Unity 侧）

- [ ] 打开 `W4Scene`，停掉 Play；
- [ ] 等右下角编译结束，Console **0 errors**（警告可以留，但不能有红字）；
- [ ] 菜单 `Tools → Build WebGL (for itch.io)`；
- [ ] 产物在 `Builds/WebGL`，应该有 `index.html` + `Build/WebGL.*`；
- [ ] **确认 `decompressionFallback` 已开**（BuildHelper 会自动设），否则 itch.io 上是黑屏；
- [ ] 把 `Builds/WebGL` 整个目录打成 zip（`index.html` 要在 zip 根目录，不能多一层文件夹）。

## 二、本地验收（打完包后，别只在编辑器里测）

- [ ] 在项目根目录跑 `node serve-webgl.js "Builds/WebGL" 8788`；
- [ ] 打开 `http://127.0.0.1:8788/`，确认能进标题页；
- [ ] 完整打一局**标准模式**：3 波 + Boss，中间至少选 2 张词条；
- [ ] 打一局**无尽模式**，至少撑到第 6 波（验证 Boss 之后能继续）；
- [ ] 检查这 8 件事：
  - [ ] 人物能动、能跳、能射，滞空明显长于"砖头手感"；
  - [ ] 左右两侧能看到墙，走过去停在墙面处；
  - [ ] 每波平台布局不同、**彼此不重叠**，出生点不同；
  - [ ] 平台能挡子弹（站在平台下往上打，子弹会炸开）；
  - [ ] 敌人从墙后飞出来，落地才开始动；开火前会亮一下；
  - [ ] 三选一在屏幕下方，不挡视野，点得动也按得了 1/2/3；
  - [ ] Boss 半血变红，血条上出现「狂暴」；
  - [ ] 无尽模式超过历史最远波数时，弹一次「★ 超过最远记录」。
- [ ] 按 `Esc` 暂停再恢复，画面继续（**不应该**让编辑器/浏览器"停住"）；
- [ ] 暂停面板能看到当前模式与波数；
- [ ] `M` 静音，再按一次恢复，BGM 要回来。

## 三、性能与兼容

- [ ] Chrome / Edge 各跑一次（itch 上大多数人用这两个）；
- [ ] 窗口拖到很窄（4:3）和很宽，墙要跟着贴边，不露缝；
- [ ] 按 `F1` 关掉调参面板后再打一局（确认不依赖调试面板）；
- [ ] Profiler（或浏览器 Performance）看一局里没有明显掉帧。

## 四、itch.io 页面

- [ ] 按 `ITCH-PAGE.md` 填标题、简介、标签、分类；
- [ ] 上传 zip，勾 "This file will be played in the browser"；
- [ ] Embed 尺寸 1280×720，开全屏按钮；
- [ ] 封面图（630×500）+ 至少 3 张截图 +（可选）30 秒 GIF；
- [ ] 已知问题写清楚（子弹穿平台 / 无寻路 / 单段音乐）；
- [ ] 先存 Draft，自己用无痕窗口完整玩一局，再转 Public。

## 五、仓库收尾

- [ ] `git tag v1.0.0` 并推送；
- [ ] README 的「构建与运行」确认是最新的（`node serve-webgl.js` 那条路径要对）；
- [ ] `Docs/GAME-COMPLETION-ROADMAP.md` 的版本表把 v1.0 标成 ✅；
- [ ] 把 playtest 记录（谁、卡在哪、改了什么）追加到 `unity-starter/README.md`。

## 卡住了怎么办

| 现象 | 先查这个 |
| --- | --- |
| itch 上黑屏 | 是否重新打过包（`decompressionFallback` 要重打才生效）；zip 里 `index.html` 是否在根目录 |
| 本地能跑，itch 上声音不响 | 浏览器要求先点一下画面；itch 的 iframe 需要在 embed 里允许音频 |
| 打包报 `scripts are compiling` | 等编译结束再点，BuildHelper 现在会直接提示，不用猜 |
| 进 Play 后画面不动 | 看右上角角标 `ts` 是不是 1.00，再看 Console 的「阶段 →」日志还有没有在新增 |
