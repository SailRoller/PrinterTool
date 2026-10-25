# 打印机管理工具（内部版）

面向内部 IT 的 Windows 桌面工具，覆盖三类高频故障的处置：

- **重点一：共享打印机修复** —— 共享主机一键共享、接收主机一键重连（按 11b/709/bc4/来宾错误自动套预案）
- **重点二：打印服务管理** —— 服务状态看板、Print Spooler 标准化重启、卡死任务（队列文件）扫描与清理
- **重点三：驱动管理** —— 驱动与占用清单、驱动彻底卸载、注册表残留清理、驱动存储包（pnputil）管理

> 重点一（共享打印机修复）自 v0.3 起已实现。

技术栈：C# / .NET 8 WinForms，**零 NuGet 依赖**（winspool/advapi32 P/Invoke + 系统自带 reg.exe/pnputil.exe/netsh.exe/net.exe）。

## 功能说明

### 共享修复页（重点一）

| 功能 | 说明 |
| --- | --- |
| 共享主机一键共享 | 共享所选打印机（`SetPrinter` level 2）→ 启动网络发现服务（FDResPub/fdPHost/SSDPSRV/upnphost）→ 放行“文件和打印机共享”防火墙组（中英文组名都尝试）→ 可选允许来宾（启用 Guest + LSA 移除“拒绝网络登录”）→ 输出共享路径 |
| 接收主机一键重连 | 诊断主机（Ping + TCP 445）→ 移除旧连接 → `AddPrinterConnection`；失败按错误码自动套预案重试：281/1801（11b/709）→ RPC 修复，3012（bc4）→ 网络发现修复 |
| 11b/709 预案 | `HKLM\...\Print\RpcAuthnLevelPrivacyEnabled = 1` + 重启 Spooler（PrintNightmare 补丁 KB5005565 系列已知问题；两端都修） |
| bc4 预案 | 启动网络发现四服务 + 放行防火墙组（中英文组名均尝试） |
| 来宾预案 | 客户端侧 `AllowInsecureGuestAuth = 1`；主机侧“允许来宾”由一键共享的勾选项完成；提供“恢复默认安全策略”回滚 |

### 服务管理页

| 功能 | 说明 |
| --- | --- |
| 服务状态看板 | Spooler / Fax / LPD 三项，每 3 秒自动刷新，支持启动/停止/重启 |
| Spooler 一键修复 | 停止服务 → 清空队列目录（可选）→ 启动服务 → 验证，全流程日志 |
| 卡死任务清理 | 扫描真实 Spool 目录（读注册表 `DefaultSpoolDirectory`，默认 `System32\spool\PRINTERS`）中的残留文件，一键清理（自动停/启服务） |

### 驱动管理页

| 功能 | 说明 |
| --- | --- |
| 驱动与占用清单 | 双环境枚举（`Windows x64` + `Windows NT x86`），显示每个驱动被哪些打印机使用，并标出**隐藏/残留注册表引用**（`Print\Printers` 中仍引用该驱动但枚举不到的打印机项） |
| 驱动彻底卸载 | **升级阶梯**（专治"指定的打印机驱动程序当前正在使用"）：① 备份注册表并删除可见打印机实例 → ② 重启打印服务（使待删除落地、卸载驱动 DLL）→ ③ `DeletePrinterDriverEx` → ④ 仍被占用则：停服务、备份并删除仍引用该驱动的隐藏/残留注册表打印机项（`Print\Printers` 的 `Printer Driver` 值，含 EnumPrinters 看不到的项）、清空队列文件后重试 → ⑤ 仍被占用则 findstr 在 `INF\oem*.inf` 中按驱动名定位驱动包，`pnputil /delete-driver /uninstall /force` 强制卸载后重试 → ⑥ 仍失败给出处置建议（重启后重试/注销其他用户/退出厂商常驻软件/用残留清理页兜底） |
| 驱动存储包管理 | 通过 pnputil 枚举打印机类（Class GUID `4d36e979-…`）INF 包，支持 `pnputil /delete-driver /uninstall /force` |
| 残留清理 | 扫描 `HKLM\SYSTEM\CurrentControlSet\Control\Print\Environments` 下的驱动注册表项：Version-3 孤儿（API 枚举不到）标记"建议清理"；Version-4 无打印机关联标记"疑似"，由用户判断 |

### 安全设计

- 发布版带 `requireAdministrator` 清单，启动自动请求提权
- **任何注册表修改（删打印机/删驱动/清残留）前都自动 `reg export` 备份**，按操作时间归档于 `backups\yyyyMMdd-HHmmss\`
- 全量操作日志写入 `logs\yyyy-MM-dd.log`，界面底部实时显示

## 构建与分发

```bat
:: 日常构建
dotnet build -c Release

:: 完整版：单文件（自包含 .NET 运行时 + 压缩，约 63MB，目标机器免安装任何依赖）
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish

:: 轻量版：单文件（约 300KB，目标机器需已装 .NET 8 桌面运行时；注意 SDK 不支持依赖框架单文件压缩）
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=false -o publish-lite
```

说明：
- exe 图标由 `tools\make-icon.ps1` 生成的 `app.ico` 提供（PowerShell 绘制的多尺寸打印机图标），改图标后重新生成并发布即可；
- 体积优化采用**单文件压缩**（`EnableCompressionInSingleFile`，138MB→约 63MB）。WinForms 官方不支持裁剪（PublishTrimmed），故不使用 trim 以保证运行稳定；
- 本地开发/测试构建可加 `-p:NoAdminManifest=true` 跳过管理员清单，便于在普通权限下运行只读命令。

## 命令行模式（供脚本/批量处理）

```
PrinterTool.exe --list-services                    列出打印相关服务状态
PrinterTool.exe --list-printers                    列出打印机及驱动占用
PrinterTool.exe --list-drivers                     列出已安装驱动
PrinterTool.exe --list-packages                    列出驱动存储中的打印机类驱动包
PrinterTool.exe --scan-residuals                   扫描驱动注册表残留
PrinterTool.exe --restart-spooler [--no-clear]     重启打印服务（默认清空队列）
PrinterTool.exe --delete-driver --name <驱动名> [--env <环境>] [--keep-printers] [--no-restart]
PrinterTool.exe --share-printer --name <打印机名> [--allow-guest]
PrinterTool.exe --unshare-printer --name <打印机名>
PrinterTool.exe --reconnect \\主机\打印机 [--no-autofix]
PrinterTool.exe --fix rpc|discovery|guest          应用 11b/709、bc4、来宾预案
```

示例（批量修复卡死的打印队列，可配合开机脚本/GPO 使用）：

```bat
PrinterTool.exe --restart-spooler
```

## 目录结构

```
PrinterTool/
├── Program.cs               入口（GUI/CLI 分流）
├── Cli.cs                   命令行模式
├── app.manifest             管理员提权清单
├── Native/Winspool.cs       winspool.drv P/Invoke（枚举/删除/共享打印机与驱动）
├── Core/
│   ├── ServiceHelper.cs     服务控制（启停/启动类型，advapi32 P/Invoke）
│   ├── SpoolerController.cs 重点二：Spooler 重启 + 队列清理
│   ├── PrinterDriverManager.cs  重点三：驱动枚举/占用/彻底卸载
│   ├── DriverPackageManager.cs  pnputil 驱动存储包（多语言输出兼容解析）
│   ├── ResidualScanner.cs   注册表残留扫描/清理
│   ├── ShareHost.cs         重点一：一键共享（共享+服务+防火墙+可选来宾）
│   ├── ShareClient.cs       重点一：一键诊断重连（错误码→预案自动重试）
│   ├── ShareFixes.cs        重点一：11b/709、bc4、来宾三大预案 + 恢复默认
│   ├── LsaRights.cs         LSA 来宾网络登录权限（Guest 共享开关）
│   ├── FirewallHelper.cs    netsh 防火墙规则组（中英文组名兼容）
│   ├── ProcessRunner.cs     命令执行与本地化输出捕获
│   ├── BackupManager.cs     注册表修改前自动备份
│   └── OperationLog.cs      操作日志
└── UI/                      WinForms 界面（服务管理/共享修复/驱动管理/残留清理/打印常识/日志与备份）
```

其他目录：`tools/make-icon.ps1`（图标生成脚本）、`app.ico`（应用程序图标）、`publish/`（完整版）、`publish-lite/`（轻量版）。

## 路线图

1. 共享凭据管理（保存/刷新访问打印主机所需的账号密码）
2. 故障预案库扩展 / 终端用户自助模式 / 定时健康巡检
3. 驱动删除后自动匹配驱动存储包的关联提示
