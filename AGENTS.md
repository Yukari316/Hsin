ref:~/.codex/AGENTS.md

# Hsin 工程协作规范

本文适用于整个仓库，是 Codex 与 Claude Code（经 `CLAUDE.md` 引用）共用的唯一仓库指引。项目需求已经实现，后续以维护当前功能和修复明确问题为主。项目代码风格与工程约束完整写在本文中，除顶部的全局规则外，不依赖其他外部规范文件。当前用户明确指令优先于本文。

## 1. 工程概览

- `server/`：ASP.NET Core/.NET 10 服务端，负责配置、Web API、房间状态、MediaMTX 安装和进程生命周期。
- `web/`：Vue 3 + Vite 前端，使用 TypeScript、pnpm、Tailwind CSS、daisyUI、Lucide 和原生 SVG。
- `tests/Hsin.Tests/`：xUnit 服务端测试，包含配置、认证、安装、进程和 MediaMTX 集成测试。
- `scripts/`：发布和许可证收集脚本。发布入口是 `scripts/publish.ps1` 与 `scripts/publish.sh`。
- `config/`：配置示例；`docs/`：部署、推流和运行说明。
- `agent_tmp/`：本地设计、计划、测试产物和运行证据，不属于产品源代码。

## 2. 目录与架构边界

### 服务端

- `server/Application/`：命令解析、命令分派、MediaMTX 启动协调和进程服务。
- `server/Configuration/`：配置加载、验证、资源读取、TLS 材料和配置模型。
- `server/Media/Installation/`：MediaMTX 下载、导入、版本校验、状态和回退。
- `server/Media/Configuration/`：生成 MediaMTX 运行配置；生成文件是运行产物，不手工维护。
- `server/Media/Control/`：Control API 客户端、房间状态轮询和房间快照。
- `server/Media/Process/`：平台进程生命周期。Windows 使用 `MediaMtxWindowsProcess` 和 Job Object；Linux 使用 `MediaMtxLinuxProcess`、专用 `LinuxMtxGuardian` 和原生辅助代码。
- `server/Web/`：控制器、Bearer 认证和静态页面回退。
- `server/Logging/`：Serilog 初始化、文件会话和日志保留。
- `server/Infrastructure/`：`AtomicFile` 原子写入，供配置回写、安装状态、MediaMTX 运行配置和证书快照共用。
- 生产类型以 `internal` 为主，`server/Properties/AssemblyInfo.cs` 通过 `InternalsVisibleTo` 向 `Hsin.Tests` 公开。

边界要求：

- `Program` 保持薄入口；参数由 `CommandLine` 集中解析，命令由 `CommandDispatcher` 集中分派。普通服务器参数启动 Web Host，更新/回退只启动所需的 Generic Host/DI，`--media-guardian` 在 Host 创建前进入 Linux Guardian；不要求 `serve` 命令。
- 使用 ASP.NET Core 自带 DI。安装、更新和回退由安装服务负责；启动协调由启动服务负责；进程启动、停止、等待和退出状态由进程服务负责。按服务、模型、配置生成和平台工具组织文件，不将职责重新塞入入口方法。
- Windows 和 Linux 生命周期保持独立；平台实现不得互相持有或嵌套实现同一个生命周期接口。
- `MediaMtxProcessService.StartAsync` 接收已选版本的实际二进制路径，并将其交给平台进程实现。平台在 DI 注册时选择，不通过 `useGuardian` 分支或传递启动委托形成多层调用链。
- Linux Guardian 只作为 Linux 专用内部启动入口，由主进程创建；Guardian 不启动 Web Host、不读取 Hsin 配置，只管理真实 MediaMTX 进程。
- 共享进程启动、输出泵、等待和进程树清理代码放入 `ProcessUtils`；平台相关的 `DllImport` 分别放在 `server/Media/Process/Native/WindowsNative.cs` 和 `LinuxNative.cs`。
- Windows 进程挂起创建，加入 Job 后恢复，确保主进程强制退出时清理所拥有的 MediaMTX。Linux Guardian 在控制管道 EOF 或停止请求时清理其媒体子进程；只管理自己创建的进程。
- MediaMTX 意外退出时记录原因并停止 Hsin，由管理员或外部服务管理器重启。进程和输出泵应在全局日志关闭前完成清理。
- 工程只运行一个固定 `live` 房间；OBS 使用 WHIP，浏览器使用 WHEP，新授权推流替换上一个推流。服务器仅转发，不做音视频二次编解码、录制或历史视频缓存。
- Windows/Linux 均支持直接运行，发布为自包含应用，不引入 Docker 部署。设计容量约 8 人共享 1080p、服务器总带宽 100 Mbps；延迟争取低于 1 秒，1～2 秒为可接受范围，性能结论必须有实测支持。
- 房间状态链路：`RoomMonitor` 每秒查询 Control API 的 `live` 路径，生成 `RoomSnapshot`（`idle`/`live`/`unavailable`），`Generation` 由本次启动 ID 与推流 `SourceId` 组成；前端轮询 `GET /api/room`，`generation` 变化即触发播放器重建 WHEP 连接，新推流替换旧推流依赖这一机制。
- `CommandDispatcher` 在 `MediaMtx.DataDirectory` 下独占打开 `hsin.lock`，服务器、`mediamtx update` 和 `rollback` 共用该锁，同一数据目录同时只能有一个实例；MediaMTX 运行配置每次启动生成到该目录的 `run/mediamtx.yml`。

### 前端

- 页面组件放在 `web/src/components/`，统计和生命周期 composable 放在 `web/src/composables/`。
- `App.vue` 负责启动数据、鉴权页面切换、公告与展示图片加载和房间轮询（失败指数退避至 8 秒，401 回到密码页）；`RoomPage.vue` 负责便当网格布局，侧栏由 `RoomStatusCard`、`StreamInfoCard`、`ShowcaseCard` 组成；`LivePlayer.vue` 按房间状态、`generation`、`whepUrl` 和 Token 驱动播放，底层 reader 的启动、停止与过期回调隔离在 `useWebRtcPlayer`；`DebugPanel.vue` 负责调试数据展示。
- 采用 JavaScript + `<video>` 播放，复用 `web/src/vendor/reader.js`；保留第三方来源说明和许可证。
- 前端包管理器为 pnpm，保留 `web/pnpm-lock.yaml`；不生成 npm/yarn 锁文件。后端在 `server/` 下保持单项目，前端独立位于根目录 `web/`。
- UI 采用便当网格（Bento）风格，使用 daisyUI 组件。亮色主题使用 daisyUI 内置 `valentine`，暗色使用 `dracula`，组件中只引用 `--color-*` 等主题变量，不散落硬编码颜色。
- 首次跟随系统选择亮/暗主题，此后保存用户选择。标题栏右侧保留单图标圆形主题按钮；密码页按钮位于右上角。

## 3. C# 代码规范

### 格式、命名与类型

- 目标框架为 .NET 10，启用 nullable 和 implicit usings。使用 4 个空格、Allman 大括号、LF 换行、末尾换行，并清理行尾空格。
- 使用文件作用域命名空间。生产代码命名空间从 `Hsin` 开始，反映 `server/` 下的目录；测试使用 `Hsin.Tests`。移动文件时同步修改命名空间和引用。
- 类型、方法、属性和事件使用 PascalCase；局部变量、参数和私有字段使用 camelCase；接口以 `I` 开头，异步 Task/ValueTask 方法以 `Async` 结尾。
- 所有常量，包括局部常量，使用 `SCREAMING_SNAKE_CASE`，如 `DEFAULT_PORT`。有业务或协议意义的数字和固定数组位置用命名常量、属性或字段表达；原生数值和指针参数需有明确语义。
- 局部变量使用显式类型，不使用 `var`。声明类型清晰可见时可使用目标类型 `new()`；集合优先用 `[]`、`[..source]`。
- record 声明具名属性，通过对象初始化器构造；不使用位置参数 record 或主构造函数 record。多个属性赋值必须逐项换行，不能堆在同一行。
- 使用枚举或布尔值表达模式，另用字段保存自由文本，避免把模式和值编码在同一个字符串中；第三方协议字段遵循外部契约。
- 每个文件放一个主要公开类型，文件名与类型一致。复用 `GlobalUsings.cs` 已有引用；新增全局 using 必须供多个文件使用。

### 控制流、可空性与资源

- 小函数和直接控制流优先。模式匹配、LINQ 方法语法和 switch 表达式以提升可读性为前提；一个 switch 分支只处理一种输入类型。
- 表达式主体成员只用于单个简短表达式。不为了简写创建无行为价值的转发层、接口或委托。
- 用 `required`、nullable 标注、类型收窄和具名模型表达契约。避免空值宽恕运算符；确实必要时在就近位置写简短中文 `// SAFE:` 解释不变量。
- 用户界面可恢复故障要记录并呈现可用状态；无效配置、非法安装包、版本不匹配和启动失败必须明确报错，不能假装启动成功或静默降级。
- 异步 I/O 使用 `async`/`await` 并传递 `CancellationToken`；区分调用取消、超时和真正失败。不使用同步等待阻塞异步链路。
- 长期服务任务支持取消并等待结束；仅对确实需要后台执行的阻塞工作使用后台线程，不机械包装异步 I/O 为 `Task.Run`。
- 新增锁使用 `System.Threading.Lock`，不锁普通 `object`。共享状态必须说明同步和所有权，不以简化为由破坏线程安全。
- 通过构造函数注入真实服务依赖，使用 ASP.NET Core 内置 DI；按实际生命周期注册，数据记录、窗口或临时协议负载不作为服务。
- `IDisposable`/`IAsyncDisposable` 所有权清晰；释放必须等相关任务和输出泵结束。文件路径用 `Path` API，状态文件写入采用现有原子写入方式。
- 验证集中在 HTTP、配置文件、归档和第三方 I/O 边界；内部已保证的不变量不重复兜底。不能泄露 Token、管理密码、私钥或敏感配置。

### 日志与注释

- 使用 Serilog 结构化日志和具名属性；消息简洁表达正在做什么、结果和可操作的失败原因。安装、校验、启动、停止和版本切换等关键节点要有日志。
- 标识符使用英文，XML 文档和解释性注释使用简体中文。修改相关逻辑时同步更新其注释，不进行无关的批量翻译或格式化。
- 代码说明做什么，注释说明理由、所有权、线程模型、原生契约、单位或非直观边界。不逐行复述代码，不记录尝试历史或保留注释掉的旧实现。
- 公共 API 文档描述调用者可依赖的副作用、错误、单位、并发和资源所有权。`TODO`/`FIXME` 必须明确可执行的完成条件，类型或 lint 抑制必须缩小范围并说明原因。

## 4. TypeScript/Vue 代码规范

- TypeScript/Vue 使用 2 个空格缩进、单引号和省略语句末尾分号，保留 UTF-8、LF、末尾换行。组件文件用 PascalCase，composable 用 `useXxx`，函数和变量用 camelCase。
- 使用 TypeScript strict 模式；定义明确的接口、联合类型和 Props/Emits，不使用无依据的 `any`、强制断言或空值抑制。
- Vue 组件使用 `<script setup lang="ts">`，顺序为 script、template、必要的 style；组件职责单一，可复用状态放到 composable，不在模板内堆积业务流程。
- API 请求集中在 `web/src/api.ts`；Bearer Token 通过 `Authorization` 头传递。不要在每个路由组件中重复认证逻辑。
- WebRTC 连接必须有启动、停止、重连、取消和过期会话清理；轮询变化不能导致重复连接。
- 所有异步请求处理取消、401、网络错误和组件卸载；不要在卸载后的组件上写入状态。
- 样式优先使用 Tailwind/daisyUI；项目已有 CSS 变量和选择器时优先复用，不创建重复主题变量。
- 按钮、工具栏和控制项使用 Lucide 图标；图标按钮提供 `aria-label` 和必要的 tooltip。
- 不把实现细节写进产品文案；用户可见文字保持简短、准确并适配移动端。

### UI 与主题约束

- 标题栏、主体、页脚使用同一 `page-width` 宽度并水平居中。页面底色为 `--color-base-200`，卡片（`.tile`）为 `--color-base-100` 加大圆角。
- 桌面布局为“播放器 | 300px 侧栏”，侧栏依次为状态卡、画面卡和可选展示图片卡，展示图片填满侧栏剩余高度；播放统计和公告位于下方并跨两列。窄屏单列，顺序为播放器、侧栏、统计、公告，不出现横向滚动。
- 实际视频、暂停、连接和故障状态保持黑底，黑色视频底是主题变量规则的必要例外；空闲状态使用主题底色和 idle 图标。
- 不使用整页或两侧背景图。展示图片只在认证后以 Blob 加载，完整显示（`contain`），空余处用同图模糊填充；密码页不显示展示图片。
- 画面卡显示分辨率、帧率、视频码率和延迟估算；直播播放期间统计每秒采集一次，延迟估算为该秒内逐帧接收至显示估算的平均值，无数据时显示占位符。
- 播放统计由播放器控制栏按钮展开，在播放器下方显示指标卡和连接详情；历史在播放期间持续记录，展开即可看到最近一分钟。指标卡曲线使用 `--color-info`，显示当前值和最低/最高值。
- 原生 SVG 曲线依次为帧率、接收至显示估算、视频码率、音频码率、抖动、RTT、接收速率、发送速率。每秒统计、保留最近 60 点；缺失值不伪造为 0，也不跨缺失点连线。
- 码率/速率当前值与上下限分别独立选单位：低于 1 Mbps 用 Kbps，否则用 Mbps；十进制 `1 Mbps = 1000 Kbps`，保留两位小数，图表与文字共用格式化函数。
- 接收至显示估算只表示浏览器帧接收至预计显示的时间；缺少浏览器数据时显示不可用，不能宣称是服务器入站至显示或端到端延迟。
- 公告使用 GitHub 风格 Markdown，允许净化后的 HTML，必须保留安全过滤和卡片内边距。页脚保留可点击的 `Powered by Hsin · MediaMTX` 工程链接。

## 5. 配置与安全约束

- 默认 Web UI 端口为 `24515`，MediaMTX WebRTC/WHIP/WHEP 端口为 `24516`，Control API 默认 `127.0.0.1:24517`。
- HTTP 端点使用控制器特性路由与认证中间件，通过 `[Authorize]`/`[AllowAnonymous]` 声明边界，不能在路由里重复手写授权判断。`POST /api/access` 有效认证返回 204，无效返回 401，并受每分钟 10 次的固定窗口限流（超出返回 429）。未匹配的 `/api/*` 由 `ApiFallbackController` 返回 404，不落入 SPA 回退。
- 默认不要求 Web UI Token；设置 `WebUi.Token` 后通过 Bearer 认证访问受保护 API，Token 只在前端内存中保存，刷新重新输入。MediaMTX WHEP 的 viewer 认证独立处理。
- 密码页保持提示、密码输入框和单图标确认按钮；普通 Enter 或点击确认进入，忽略组合 Enter 和输入法组合阶段，错误时显示提示及红色输入框描边。
- 推流使用独立的 `Publish.BearerToken`；不要复用观众 Token、Control API 凭据或把 Token 放入日志和静态文件。
- `WebUi.PublicOrigin` 和 `MediaMtx.PublicOrigin` 必须是完整 origin，不包含路径、查询、片段或用户凭据；TLS 开启时必须使用 HTTPS。
- WebRTC CORS、`AdditionalHosts`、监听地址和防火墙规则必须保持一致；远程部署必须使用客户端可达的域名/IP，不能把 `localhost` 当远程地址。
- TLS 证书和私钥必须成对配置，启动时读取并生成 MediaMTX 证书快照；不自动降级到 HTTP。
- 配置默认路径为程序目录下的 `hsin.json`，可用 `--config` 指定；文件缺失时按默认值生成。`Publish.BearerToken` 为空时生成 `publisher:` 前缀的随机令牌并原子写回配置文件，其他凭据不自动生成。
- 自定义内容通过配置文件指定；路径按配置文件目录解析。配置、图片、公告和证书在启动时读取，不做文件热监听；每次启动基于本次快照重新生成媒体运行配置。
- 首次缺失时自动下载 MediaMTX，正常启动复用已安装版本；管理员使用 `mediamtx update`/`rollback` 更新或回退。支持 `--mediamtx-package` 导入 ZIP/TAR.GZ 三文件包，校验二进制、配置和 LICENSE。
- 安装状态记录二进制 SHA-256；启动前先校验哈希和二进制 `--version`，再启动平台媒体进程/Guardian，启动后检查 Control API 就绪。版本检查不能以 Control API 替代。
- 候选失败时，只有旧活动版本独立通过预检才能接管；无可用版本则报错退出。显式导入失败报错并清理失败候选，不静默使用旧版本。更新和回退不能干预仍持有安装目录锁的运行实例。
- 日志使用 Serilog，默认保留 7 天；路径、保留天数、最低级别和文件启用开关可配置。文件名为本地启动时间 `yyyy-MM-dd_HH-mm-ss.log`，同秒追加、跨天仍写原文件。关闭文件日志不创建目录、不清理历史；活动日志和未知命名文件不删除。
- `Appearance.ShowcaseImageFile` 是房间侧栏展示图片，可为空；接口 `/assets/appearance/showcase` 需要认证。

## 6. 测试与验证

按风险选择最小充分验证。只运行与修改有关的构建、测试或页面检查；不机械叠加全量测试，不在已有验证通过后无理由重复执行。文档和忽略规则修改通常只做静态核对。

在仓库根目录运行。SDK 使用 `global.json` 的版本；已有还原结果时构建可加 `--no-restore`。按改动选择下面的命令，而非全部照跑：

```powershell
dotnet build Hsin.slnx -c Release
dotnet test tests/Hsin.Tests/Hsin.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ConfigurationTests"
pnpm --dir web install --frozen-lockfile
pnpm --dir web run typecheck
pnpm --dir web run test src/components/MetricSparkline.test.ts
pnpm --dir web run build
```

涉及相关页面布局时，使用已有浏览器资产运行聚焦页面测试：

```powershell
$env:PLAYWRIGHT_BROWSERS_PATH=(Resolve-Path agent_tmp).Path
pnpm --dir web run test:e2e --grep "pages.spec.ts"
```

前端工具链版本见 `README.md`（Node.js 24.19.0、pnpm 12.6.0）。本地联调后端可用 `dotnet run --project server/Hsin.csproj -- --config <路径>`；不传 `--config` 时配置位于构建输出目录。该命令会按配置下载并启动真实 MediaMTX、占用 24515～24517 端口，不要与用户正在运行的实例共用配置或端口。

单文件 Vitest 过滤不要在脚本名后加 `--`，否则 `--` 会原样传给 Vitest 并导致全部测试运行。`test:e2e` 自动在 `127.0.0.1:4173` 启动 Vite 并以 Chromium 运行，结果写入 `agent_tmp/playwright-results/`；页面测试通过 `page.route` 模拟 API，不需要运行后端。本地 `pnpm --dir web run dev` 将 `/api` 和 `/assets` 代理到 `http://localhost:24515`。

测试范围：

- 配置和服务端单元测试覆盖配置持久化、认证、日志、安装校验和进程边界。
- 真媒体集成测试由显式环境条件启用：xUnit 使用 `HSIN_TEST_MEDIAMTX`（`[MediaIntegrationFact]`），Playwright `e2e/media.spec.ts` 使用指向运行中实例配置文件的 `HSIN_MEDIA_CONFIG`；默认不启动或模拟真实推流，不占用用户现有实例的端口、不停止正在运行的服务。
- Debian 运行测试由用户执行，需要时给出步骤；不要在本机强行模拟 Debian 或把交叉编译当运行证据。未执行的真实 OBS、多观众及其他浏览器验收必须说明。
- 前端单元测试覆盖 API、主题、Markdown、播放器状态、统计和调试历史；页面测试覆盖密码页、便当布局、展示图片认证加载、主题、响应式统计面板和 WebRTC 页面流程。
- 改动测试本身时必须重新运行对应测试；失败时报告实际失败原因，不用旧结果替代当前证据。

## 7. 发布与变更纪律

- 正确性与真实性、安全可恢复性、可靠性、可维护性依次优先于速度和改动规模。诊断区分事实、假设和未验证边界，追查根因而不是掩盖现象。
- 设计和实现计划、代理工作文档、临时验证证据放在 `agent_tmp/`。用户要求先计划再实现时，先保存可执行计划；已授权的工作不重复索取确认。
- 禁止过度工程化、过度防御性分支、仅服务测试桩的生产代码和无价值测试。不为假设中的未来需求或旧字段添加兼容层、映射、双读双写。
- 不反复审查已检查的稳定模块；修复时只追查当前缺陷及相邻同类问题。保留用户已有修改，不做无关重构、格式化、依赖升级或配置变更。
- 发布前端静态文件到 `server/wwwroot/`，发布产物写入 `artifacts/`；Windows 为 ZIP，Linux 为 TAR.GZ。发布命令为 `pwsh -File scripts/publish.ps1 -Runtime win-x64|linux-x64` 或 `bash scripts/publish.sh linux-x64|win-x64`；同名产物已存在时脚本拒绝覆盖，在 Windows 打 Linux 包需要 Git for Windows 自带的 GNU tar/gzip。
- 运行配置、日志、媒体下载、安装状态、发布产物、编译输出、Node 模块、测试报告和构建缓存不得提交。
- 用户当前约束为不执行 Git 动作；未经明确授权不运行 Git 命令、不修改索引、不提交或推送。`.gitignore` 不会自动取消已有跟踪，需单独说明，不擅自清理索引。
- 修改目录、项目路径或公开配置字段时，必须同步解决方案、测试项目、脚本、示例和文档，并运行至少一次构建验证。
- 变更报告必须说明实际修改、验证结果、未验证边界、已知风险、回滚方式和是否达到需求。
- 发现范围外问题只报告，不顺手修复；修复必须保持最小改动并与当前需求直接对应。
