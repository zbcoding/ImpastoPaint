# Impasto

[🇬🇧 English](readme.md) · 机器翻译，欢迎通过 pull request 提交修正。

Impasto 是一款适用于 Linux、Windows 和 macOS 的绘画与图像编辑应用。

Impasto 非常适合快速绘画、图像编辑、裁剪、调整尺寸以及基于图层的编辑。
使用文本工具书写文字，使用形状工具绘制自由曲线，使用选区工具复制和粘贴图像图层的局部。
请见下方截图。

![形状和文本作为可编辑对象，并带有各对象独立的历史记录](docs/screenshots/impasto-object-layers.png)

![文本工具，界面首选项对话框处于打开状态](docs/screenshots/impasto-text-tool.png)

![形状控制点吸附在画布中心线上，拖动时显示参考线](docs/screenshots/impasto-snap-to-grid.png)
特色功能图：移动和绘制的内容会吸附到画布网格、标尺单位，或画布边缘与中心线。

## 下载

Linux、Windows 和 macOS 的构建版本可在
[发布页面](https://github.com/zbcoding/ImpastoPaint/releases/latest) 获取。Windows 和
macOS 的构建版本未经签名，因此 SmartScreen 和 Gatekeeper 会发出警告。

Impasto 需要 GTK 4.18 或更高版本，这决定了它在 Linux 上的可用范围：

- **Flatpak**，来自 [FlatPark](https://flatpark.org/apps/com.github.zbcoding.Impasto/) 或
  发布页面。它自带 GTK，可在任何支持 flatpak 的系统上运行，包括 Ubuntu 22.04
  和 24.04。如果不确定选哪个，就用这个。
- **AppImage**（`Impasto-x86_64.AppImage`）。已捆绑 GTK，因此无需系统安装 GTK，但该
  捆绑包基于 **glibc 2.43** 构建，在更旧的系统上无法启动。也就是说
  Ubuntu 26.04、Fedora 44 和 Arch 可以运行；Ubuntu 24.04、Ubuntu 22.04、Fedora 43 和 Debian 13
  则不行 —— 请改用 Flatpak。
- **Zip**（`Impasto-linux-dotnet-*.zip`）。使用发行版提供的 GTK，因此需要系统中
  已安装 GTK 4.18+。

Impasto 基于 MIT 许可证授权（参见 `license-mit.txt`）。第三方
署名、声明和许可证文本位于 `THIRD-PARTY-NOTICES.md`。

## 与 Pinta 的关系

Impasto 是一个全新的独立项目。它起始于以 MIT 许可证发布的
[Pinta](https://github.com/PintaProject/Pinta) 源代码 —— 而 Pinta 本身是一款受
[Paint.NET™](https://www.getpaint.net/) 启发的 GTK 应用。
Pinta 的贡献者已在 Impasto 应用中列出。
Impasto 并非由 Pinta 项目或同一批贡献者维护。

<details>
<summary><h2>在 Windows 上构建</h2></summary>

首先，安装所需的 GTK 相关依赖：
- 安装 [MSYS2](https://www.msys2.org)
- 在 CLANG64 终端中运行 `pacman -S mingw-w64-clang-x86_64-libadwaita mingw-w64-clang-x86_64-webp-pixbuf-loader`。
  - 对于 ARM64 版 Windows，请使用 `CLANGARM64` 终端，并将 `clang-x86_64` 替换为 `clang-aarch64`。

随后可在 [Visual Studio](https://visualstudio.microsoft.com/) 中打开 `Pinta.sln` 来构建本应用。
请确保已通过 Visual Studio 安装程序安装 .NET 10。

在命令行中构建：
- [安装 .NET 10 SDK](https://dotnet.microsoft.com/)。
- 构建：
  - `dotnet build`
- 运行：
  - `dotnet run --project Impasto`

</details>

<details>
<summary><h2>在 macOS 上构建</h2></summary>

- 安装 .NET 10 和 GTK4
  - `brew install dotnet-sdk libadwaita adwaita-icon-theme gettext webp-pixbuf-loader`
  - 对于 Apple Silicon，请在环境中设置 `DYLD_LIBRARY_PATH=/opt/homebrew/lib`，以便应用能够加载 GTK 库
  - 对于 Intel，请在环境中设置 `DYLD_LIBRARY_PATH=/usr/local/lib`，以便应用能够加载 GTK 库
- 构建：
  - `dotnet build`
- 运行：
  - `dotnet run --project Impasto`

</details>

<details>
<summary><h2>在 Linux 上构建</h2></summary>

- 按照你所用 Linux 发行版的说明安装 [.NET 10](https://dotnet.microsoft.com/)。
- 安装其他依赖（以下说明针对 Ubuntu 22.10，其他发行版应类似）：
  - `sudo apt install autotools-dev autoconf-archive gettext intltool libadwaita-1-dev`
  - 最低库版本：`gtk` >= 4.18 且 `libadwaita` >= 1.8
  - 可选依赖：`webp-pixbuf-loader`
- 构建（方式一，用于开发和测试）：
  - `dotnet build`
  - `dotnet run --project Impasto`
- 构建（方式二，用于安装）：
  - `./autogen.sh`
    - 如果从 tarball 构建，请改为运行 `./configure`。
    - 添加 `--prefix=<install directory>` 参数可安装到 `/usr/local` 以外的目录。
  - `make install`

</details>

## 获取帮助 / 参与贡献：

欢迎贡献。简而言之：

- **代码** —— 最快捷的贡献方式是提交一个 issue 并描述你的建议
  或需求。请附上代码片段、文件名和相关背景。
  你也可以提交 PR。以 PR 形式贡献给免费版的代码
  也可能被用于 Impasto 的 Premium 版本，或用于任何采用 MIT 许可证的其他软件。
- **AI 编程工具** —— 欢迎使用。前提是你能解释自己提交的代码，并
  使其符合项目的架构；借助 AI 编写的代码在合并前可能会经过
  更严格的审查。
- **翻译** —— 以 pull request 形式贡献：先由 AI 起草新的 `.po`
  文件，再由该语言的使用者编辑和校对。

在贡献之前，请阅读 `CONTRIBUTING.md` 中的完整指南，其中包括 git/PR 工作流程。

- 你可以报告 [bug/问题](https://github.com/zbcoding/ImpastoPaint/issues)。
- 每个版本的重要变更记录在 `CHANGELOG.md` 中。
