# 远眺（Far Gaze）

《空洞骑士：丝之歌》的观景模组。

## 主要功能

- 大黄蜂站定时，按下远眺键即可转身望向背景，并解除背景模糊。
- 远眺期间仍可向上或向下移动镜头，大黄蜂会保持背身姿势。
- 支持键盘和手柄，也可以在游戏内分别修改绑定按键。

## 下载与安装

### 方案一：通过模组管理器安装

打开 [远眺的 Thunderstore 页面](https://thunderstore.io/c/hollow-knight-silksong/p/FireFish/FarGaze/)，点击 **Install with Mod Manager**。如果尚未安装管理器，可先安装 [Thunderstore Mod Manager](https://get.thunderstore.io/)。在管理器中选择《空洞骑士：丝之歌》和要使用的配置档，安装远眺及其依赖，然后从该配置档启动游戏。管理器会处理模组本体和依赖，不需要再下载或手动放置 GitHub 上的 ZIP。

### 方案二：完全手动安装（以 Windows 为例）

打开 [GitHub v0.5.1 Release](https://github.com/FireFish0311/FarGaze/releases/tag/v0.5.1)，在 **Assets** 中下载 [`FarGaze-0.5.1.zip`](https://github.com/FireFish0311/FarGaze/releases/download/v0.5.1/FarGaze-0.5.1.zip) 作为模组本体。

模组本体包不包含依赖。尚未安装的依赖可从下表逐个下载，也可从同一 Release 下载 [`FarGaze-0.5.1-dependencies.zip`](https://github.com/FireFish0311/FarGaze/releases/download/v0.5.1/FarGaze-0.5.1-dependencies.zip)。依赖项包内是六个原始压缩包，并附有放置说明；已有对应版本的组件无需重复安装。

| 组件 | 用途 | 本版本使用的版本 |
|---|---|---|
| [BepInExPack Silksong](https://thunderstore.io/c/hollow-knight-silksong/p/silksong_modding/BepInExPack_Silksong/) | 模组加载器 | 1.0.3 |
| [ModMenu](https://thunderstore.io/c/hollow-knight-silksong/p/silksong_modding/ModMenu/) | 游戏内设置菜单 | 0.7.6 |
| [UnityHelper](https://thunderstore.io/c/hollow-knight-silksong/p/silksong_modding/UnityHelper/) | ModMenu 所需组件 | 1.2.0 |
| [WavLib](https://thunderstore.io/c/hollow-knight-silksong/p/SFGrenade/WavLib/v/1.1.1/) | UnityHelper 所需组件 | 1.1.1 |
| [MonoDetour BepInEx 5](https://thunderstore.io/c/hollow-knight-silksong/p/MonoDetour/MonoDetour_BepInEx_5/) | ModMenu 所需组件 | 0.7.15 |
| [MonoDetour](https://thunderstore.io/c/hollow-knight-silksong/p/MonoDetour/MonoDetour/) | 上一组件所需的库 | 0.7.15 |

可以从上表逐个打开页面，选择“手动下载”；也可以解压 `FarGaze-0.5.1-dependencies.zip`，从其中的 `packages/` 取出需要的组件压缩包。依赖项包的外层文件夹不能直接放进游戏目录，各组件还需分别解压。在 Steam 中打开《丝之歌》的本地文件目录，然后按以下位置放置：

1. 解压 BepInExPack，将压缩包内 **`BepInExPack` 文件夹中的内容** 放到游戏根目录，也就是与游戏可执行文件同一层。启动一次游戏后退出。
2. 解压其余依赖。将 ModMenu、UnityHelper、WavLib 的 DLL 放入 `BepInEx/plugins/`（可以各自建立子文件夹）；将 MonoDetour 两个压缩包中的 `core/` 和 `patchers/` 文件分别放入 `BepInEx/core/` 和 `BepInEx/patchers/`。
3. 解压 `FarGaze-0.5.1.zip`，将其中的整个 `FarGaze` 文件夹放入 `BepInEx/plugins/`。最终应有 `BepInEx/plugins/FarGaze/FarGaze.dll`。

### 已装其他模组：如何检查依赖

如果通过模组管理器启动游戏，请在**当前使用的配置档**中查看这些组件是否已安装并启用。管理器的配置档可能与 Steam 的游戏目录分开，检查文件时应打开该配置档的文件夹。

手动安装的玩家可在游戏目录中核对下列文件。`plugins/` 下的 DLL 可以位于任意子文件夹；已有对应文件的组件无需重复复制。

| 组件 | 查看位置或文件 |
|---|---|
| BepInExPack Silksong | 游戏根目录的 `winhttp.dll`，以及 `BepInEx/core/BepInEx.dll` |
| ModMenu | `BepInEx/plugins/` 下的 `Silksong.ModMenu.dll` |
| UnityHelper | `BepInEx/plugins/` 下的 `UnityHelper.dll` |
| WavLib | `BepInEx/plugins/` 下的 `WavLib.dll` |
| MonoDetour | `BepInEx/core/com.github.MonoDetour.dll` |
| MonoDetour BepInEx 5 | `BepInEx/patchers/0.com.github.MonoDetour.BepInEx.5.dll` |

请同时核对已装版本。上表文件只用于确认组件所在位置，不能单凭文件名判断版本；使用管理器时看配置档中的版本号，手动安装时看原压缩包的 `manifest.json`。缺少哪个组件，就从上面的下载链接或依赖项包中补装哪个。

## 操作与设置

默认键盘按键是 `B`。首次连接一种手柄时，默认按键是按下左摇杆（PlayStation 为 `L3`，Xbox 为 `LS`）。

在游戏中进入 **选项 → Mods → 远眺**，可以修改“键盘按键”“手柄按键”和“按键模式”。选择按键项后，直接按下想绑定的键。未连接手柄时，手柄一行显示“未连接手柄”；不同类型的手柄会分别记住上次设置的按键。

- **点按**：按一次进入远眺，再按一次退出。
- **长按**：按住时远眺，松开时退出。

如果新按键与当前游戏操作冲突，设置界面会提示，并保留原有绑定。

## 补充说明

- 行走、跳跃、攻击或受击会结束远眺；只有站定时才能再次进入。
- 正常进入、退出有转身动画；被其他动作打断时会直接恢复角色控制。
- 背景模糊会平滑消退和恢复；远眺时也会改善背景清晰度。
- 模组不修改游戏存档。卸载时删除 `BepInEx/plugins/FarGaze/` 即可；如需清除个人设置，再删除 `BepInEx/config/io.github.localdev.fargaze.cfg`。公共依赖也可能被其他模组使用。

## 其它

- 模组版本：**0.5.1**；使用 BepInEx 5。
- 已在 Steam 版《丝之歌》build 22479045 上验证，重点适配键盘与 PS4 手柄，同时支持常见的 PS5、Xbox、Nintendo 手柄布局。游戏更新或其他手柄型号的表现可能有所不同。
- 手柄按键提示图使用 xelu 的 [Free Keyboard and Controllers Prompts Pack](https://opengameart.org/content/free-keyboard-and-controllers-prompts-pack)，许可为 [CC0](https://creativecommons.org/publicdomain/zero/1.0/)。PlayStation 面键提示使用游戏自身的图标。
