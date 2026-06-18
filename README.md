# 📋 Clipboard Manager (剪贴板管理器)

一个基于 **Tauri v2 + React + Vite + Tailwind CSS** 开发的现代、轻量级、高性能剪贴板管理器。

---

## 🌟 特性

* **多类型捕获**：支持捕获并展示纯文本、富文本 (HTML/RTF)、图片以及复制的文件路径。
* **全局快捷键**：默认使用 `Alt + C` 快速唤出或隐藏主界面。
* **无缝隐藏**：
  * 按下 `ESC` 键可瞬间隐藏界面。
  * 当焦点切换至其他应用时，窗口将自动隐藏，静默无打扰。
* **系统托盘集成**：
  * 常驻系统右下角托盘，左键点击快速唤出。
  * 右键菜单支持：打开主界面、进入设置、一键清理历史记录、退出程序。
* **智能检索与快捷指令**：
  * 实时模糊搜索历史剪贴板内容。
  * 搜索框支持**快捷清理指令**，输入并回车直接清理，无需确认弹窗：
    * `clear`：清理所有历史记录，并清空输入框。
    * `clear 5`：清理最新的 5 条历史记录。
    * `clear 1d` / `clear 12h`：清理指定时间段之前的文件/历史。
* **个性化设置**：
  * **开机自启**：一键开启或关闭开机自启（正式 Release 版本支持完美后台静默自启，不弹控制台窗口）。
  * **捕获偏好**：自定义是否捕获图片、是否捕获复制的文件。
  * **存储优化**：支持自定义历史记录保留天数、最大记录数上限，程序会自动定期清理过期及冗余的图片缓存。
  * **自定义快捷键**：支持自定义全局唤出的快捷键手势。

---

## 🛠️ 开发与构建

### 前置要求
* [Node.js](https://nodejs.org/) (建议 LTS)
* [Rust 编译环境](https://www.rust-lang.org/) (确保 `cargo` / `rustc` 安装配置正确)

### 1. 安装依赖
```bash
npm install
```

### 2. 开发环境 (Dev)
启动前端 Vite 服务和 Rust 后端（开发模式下由于包含调试支持，会自动弹出命令行黑窗口以显示日志）：
```bash
npm run dev
# 或直接启动 Tauri 开发环境
npx tauri dev
```

### 3. 正式打包 (Build)
编译并构建正式发布版本（隐藏黑窗口命令行，自启时完全静默运行）：
```bash
npx tauri build
```

打包完成后，生成的资源位于：
* **绿色单文件版 `.exe`**：[src-tauri/target/release/clipboard-manager.exe](file:///d:/workspace/clipboard-manager/src-tauri/target/release/clipboard-manager.exe)
* **安装包 (如 `.msi`) 目录**：`src-tauri/target/release/bundle/`

---

## 📂 项目结构
* `src/`：React 前端代码（页面布局、主题、交互逻辑及设置面板）。
* `src-tauri/`：Tauri 后端 Rust 代码（快捷键绑定、剪贴板监听、自启配置及本地 SQLite 数据库读写）。
