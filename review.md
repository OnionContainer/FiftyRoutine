# 页 / 窗 / 控 结构审查

审查范围：`Personal_Management/Desktop/` 下的 `Page/`、`Window/`、`Control/`（旁路相关：`Services/Theme.cs` 内联弹窗、`App/App.xaml.cs` 启动窗）。

判定口径：


| 类型          | 定义                                                |
| ----------- | ------------------------------------------------- |
| **Page**    | 可嵌入主程序的页面模块（如日程记录页）                               |
| **Window**  | 弹窗；只存临时信息，不保留应持久化的信息；为 Page 提供展示与交互               |
| **Control** | 可在弹窗或页面中复用；一个或多个元素；只存临时信息；为 Page 或 Window 提供展示与交互 |


持久化一律由 `Services/` 存储层负责；Page / Window / Control 只做编辑态草稿与 UI 状态。

---

## 1. 现状清单

### 1.1 Page（6）

实现均为 `UserControl`，由 `MainWindow` 的 `TabControl` 嵌入。


| 名称   | 类 / 文件                                     | 规模（约）             | 职责摘要                                                   | 与定义是否吻合 |
| ---- | ------------------------------------------ | ----------------- | ------------------------------------------------------ | ------- |
| 日程记录 | `SchedulePage` · `Page/SchedulePage.*`     | cs ~1200 行（已大幅瘦身） | 编排：加载/结算、任务栏、开子窗；周板/任务卡/统计图/离线遮罩已下沉 Control            | **吻合**  |
| 收藏夹  | `FavoritesPage` · `Page/FavoritesPage.*`   | cs ~500 行         | 标签过滤、拖入/粘贴、私密 tag；卡片用 `ThumbCard`，遮罩用 `OfflineOverlay` | **吻合**  |
| 体重   | `WeightPage` · `Page/WeightPage.*`         | cs ~470 行         | 档案、记今天、批量粘贴、折线图；遮罩用 `OfflineOverlay`                   | **吻合**  |
| 小玩意  | `GadgetsPage` · `Page/GadgetsPage.*`       | cs ~110 行         | 当前仅「镜像文字」                                              | **吻合**  |
| 界面   | `AppearancePage` · `Page/AppearancePage.*` | cs ~250 行         | 主题副本；色/数字行用 `ThemeRows`                                | **吻合**  |
| 设置   | `SettingsPage` · `Page/SettingsPage.*`     | cs ~290 行         | 直接登录、Noco、LLM、快捷键提示                                    | **吻合**  |




### 1.2 Window（目录内）


| 名称       | 类 / 文件                   | 形态            | 职责摘要                                              | 与定义是否吻合             |
| -------- | ------------------------ | ------------- | ------------------------------------------------- | ------------------- |
| 主窗口      | `MainWindow`             | XAML `Window` | 壳：Tab + 状态栏 + 托盘；`IAppHost`                       | **壳窗**              |
| 登录窗      | `LoginWindow`            | XAML          | 选用户 / 新建 / 迁移                                     | **吻合**              |
| 新建/编辑任务  | `NewTaskWindow`          | XAML          | 任务草稿                                              | **吻合**              |
| 日程块样式编辑  | `BlockStyleEditorWindow` | XAML          | 层/预设/HLS；内嵌 `HlsColorPicker`                      | **吻合**（仍偏大）         |
| 记录时长     | `RecordDurationWindow`   | 纯代码           | 固定时长 / 至今                                         | **吻合**              |
| 笔记钉编辑    | `NotePinEditWindow`      | 纯代码           | 正文编辑                                              | **吻合**              |
| 任务执行     | `TaskRunWindow`          | XAML          | 计时 UI；状态在日程页 `TaskRunState`                       | **吻合**              |
| 奖励 / 愿望单 | `RewardWishWindow`       | XAML          | 奖励/愿望墙、抽奖、兑换；卡片用 `ThumbCard`，遮罩用 `OfflineOverlay` | **边界仍偏 Page**（见 §3） |
| 奖励编辑     | `RewardEditWindow`       | XAML          | 奖励草稿                                              | **吻合**              |
| 愿望编辑     | `WishEditWindow`         | XAML          | 愿望草稿                                              | **吻合**              |
| 收藏编辑     | `FavoriteAddWindow`      | XAML          | 收藏草稿                                              | **吻合**              |
| 缩略图选区    | `ThumbCropWindow`        | XAML          | 裁切视口                                              | **吻合**              |
| 二级密码     | `PinWindow`              | static 工厂     | 设/验密码                                             | **吻合**              |
| 任务提醒     | `ReminderWindow`         | static 工厂     | 置顶提醒                                              | **吻合**              |




### 1.3 Control（7，高优先级拆分已完成）


| 名称                   | 路径                               | 形态                 | 职责                           | 使用方                      |
| -------------------- | -------------------------------- | ------------------ | ---------------------------- | ------------------------ |
| `WeekBoard`          | `Control/WeekBoard/WeekBoard.cs` | 代码类 ~930 行         | 周板绘制与交互（时间线、框选、笔记钉 Popup、聚焦） | `SchedulePage`           |
| `TaskCard`           | `Control/TaskCard/TaskCard.cs`   | static 工厂          | 任务卡（纹样/溢出条/记录·开始）            | `SchedulePage`           |
| `ThumbCard`          | `Control/ThumbCard/ThumbCard.cs` | static 工厂          | 缩略图卡片壳 + Hint                | `TaskCard`、收藏、奖励/愿望      |
| `OfflineOverlay`     | `Control/OfflineOverlay/*`       | `UserControl`      | Noco 未连接遮罩 +「尝试连接」           | 日程 / 收藏 / 体重 / 奖励窗       |
| `ScheduleStatsChart` | `Control/ScheduleStatsChart/*`   | static             | 日程块统计折线                      | `SchedulePage`           |
| `ThemeRows`          | `Control/ThemeRows/ThemeRows.cs` | static             | `ColorRow` / `NumberRow`     | `AppearancePage`         |
| `HlsColorPicker`     | `Control/HlsColorPicker/*`       | `FrameworkElement` | HLS 色环+三角                    | `BlockStyleEditorWindow` |


日程页当前结构：

```
SchedulePage（编排：加载、结算、打开子窗）
├── TaskCard（左栏，基于 ThumbCard）
├── WeekBoard（周板 + 笔记钉 Popup）
├── ScheduleStatsChart（底栏统计）
├── OfflineOverlay
└──（Page 协调）TaskRunWindow / NewTaskWindow / RecordDurationWindow / …
```



### 1.4 未放进 Page/Window/Control 目录、但实际是窗的东西


| 名称      | 位置                                   | 建议归类             |
| ------- | ------------------------------------ | ---------------- |
| 启动准备窗   | `App/App.xaml.cs`                    | Window（可保留在 App） |
| 文本提示    | `Services/Theme.cs` → `TextPrompt`   | Window           |
| 调整钱包    | `Services/Theme.cs` → `WalletPrompt` | Window           |
| 未显示小时提示 | `SchedulePage` 内联                    | Window           |
| 体重批量粘贴  | `WeightPage` 内联                      | Window           |


---



## 2. Control 分类核对

**全部正式 Control 均符合定义，不是 Window。**

- 无独立 HWND；嵌在 Page/Window 可视树或由工厂产出 `UIElement`。
- 只持临时 UI 状态；持久化仍由 Page/Services 编排。
- `BlockStyleEditorWindow.ParamControl` 仍是编辑器内部私有类型，不是仓库级 Control。

---



## 3. Window 边界


| 对象                 | 判断                                                                         |
| ------------------ | -------------------------------------------------------------------------- |
| `MainWindow`       | 应用壳，保持 Window                                                              |
| `RewardWishWindow` | 功能仍像可嵌入页；内容已用 `ThumbCard` / `OfflineOverlay`，下一步可抽 `RewardWishPanel`（中优先级） |
| 其余编辑/提醒/选区窗        | 符合 Window                                                                  |


---



## 4. 后续拆分（原中 / 低优先级，仍待做）



### 4.1 中优先级


| 候选                 | 从哪拆                            |
| ------------------ | ------------------------------ |
| `WeightChart`      | `WeightPage.RenderWeightChart` |
| `MirrorTextGadget` | `GadgetsPage`                  |
| 样式层参数面板            | `BlockStyleEditorWindow`       |
| 块样式预览条             | `NewTaskWindow`                |
| `RewardWishPanel`  | `RewardWishWindow` 内容区         |




### 4.2 低优先级 / 暂不拆

`SettingsPage`、薄窗（登录/密码/时长/笔记钉/提醒）、`ThumbCropWindow`、已拆好的 Control、`TextPrompt`/`WalletPrompt`（宜迁 `Window/` 目录而非拆 Control）。

### 4.3 高优先级 — **已完成**


| Control                   | 状态  |
| ------------------------- | --- |
| `TaskCard`                | 已完成 |
| `WeekBoard`               | 已完成 |
| `ScheduleStatsChart`      | 已完成 |
| `ThumbCard`               | 已完成 |
| `OfflineOverlay`          | 已完成 |
| `ThemeRows`（Color/Number） | 已完成 |


---



## 5. 归位建议（尚未改目录的项）


| 现状                                         | 建议                           |
| ------------------------------------------ | ---------------------------- |
| `TextPrompt` / `WalletPrompt` 在 `Theme.cs` | 挪到 `Window/`                 |
| 内联「未显示小时」「批量粘贴」                            | 独立小窗类                        |
| `RewardWishWindow`                         | 可继续独立 HWND；内容抽 Panel 便于将来嵌主窗 |


---



## 6. 一句话结论

- **高优先级 Control 拆分已落地**；`SchedulePage` 从约 2300 行降到约 1200 行，周板/任务卡/统计图/遮罩/主题行均在 `Control/`。  
- **Page / Window 清单未变**；`RewardWishWindow` 边界问题与散落小窗仍在，属后续工作。  
- **Control 无一误标为 Window**；分类与定义一致。

