# 原生部署

## 发布包与运行目录

使用 `Hsin-win-x64.zip` 或 `Hsin-linux-x64.tar.gz` self-contained 发布包。包内包含 Hsin、Vue 静态页面及 `hsin.example.json`；MediaMTX 在首次运行时由 Hsin 下载，不预装在包内。部署机无需 Node.js 或单独的 .NET 运行时。Linux 需要其发行版对应的 .NET 原生系统依赖，参见 [.NET Linux 安装文档](https://learn.microsoft.com/dotnet/core/install/linux)。交叉发布成功不等于已经在目标系统运行通过。

将程序解压到新的应用目录，不用源码发布脚本覆盖正在运行的安装。可先复制 `hsin.example.json` 为 `hsin.json`，再填写站点配置；没有配置时首次启动生成默认配置。无参数启动使用可执行文件旁的 `hsin.json`，也可显式指定：

```powershell
.\Hsin.exe [--config C:\HsinData\hsin.json]
```

```sh
chmod u+x ./Hsin
./Hsin [--config C:\HsinData\hsin.json]
```

配置参数本身的相对路径相对于当前工作目录；配置中的图片、公告、证书、日志和媒体数据相对路径均相对于配置文件目录。运行账户需要写配置文件所在目录和媒体数据目录，以便原子写回令牌、安装状态和生成配置。启用文件日志时还需写日志目录。使用普通账户，不依靠程序自动提权。

首次启动将空的 `Publish.BearerToken` 初始化为 `publisher:<随机秘密>` 并持久化。已有非空令牌不会在重启、媒体更新或回退时变化。`WebUi.Token` 默认留空，不会自动生成；设置后观众需输入房间 token，Hsin Web API 使用 `Authorization: Bearer <WebUi.Token>`，刷新页面需要重新输入。MediaMTX WHEP 使用独立的 viewer 认证，不复用 Hsin Web API 的 Bearer 头。只从本地配置复制推流令牌，不把配置文件作为公开静态资源上传。

服务器访问 GitHub 不稳定时，先上传当前操作系统和架构对应的 MediaMTX 官方包，再通过启动参数导入：

```powershell
.\Hsin.exe --mediamtx-package C:\HsinData\mediamtx_v1.21.1_windows_amd64.zip [--config C:\HsinData\hsin.json]
```

```sh
./Hsin --mediamtx-package /srv/hsin/mediamtx_v1.21.1_linux_amd64.tar.gz [--config C:\HsinData\hsin.json]
```

包内只允许一个共同顶层目录及三个文件：`mediamtx.exe`（Linux 为 `mediamtx`）、`mediamtx.yml`、`LICENSE`。Hsin 验证归档路径、格式、YAML、二进制版本并记录归档 SHA-256；运行配置仍由当前 `hsin.json` 生成。导入成功后成为当前媒体版本，原版本可通过 rollback 恢复；下次普通启动复用导入版本。导入失败时命令明确报错，不静默启动原版本。导入参数只用于普通启动，不用于 update 或 rollback。

## 监听、域名与防火墙

| 用途 | 默认监听 | 说明 |
| --- | --- | --- |
| Web UI、应用 API | 0.0.0.0:24515/TCP | `WebUi.ListenAddress`、`WebUi.Port` |
| WHIP/WHEP 信令 | 0.0.0.0:24516/TCP | `MediaMtx.ListenAddress`、`MediaMtx.Port` |
| WebRTC 媒体 | 0.0.0.0:24516/UDP | 与信令使用相同端口数字、不同传输协议 |
| Control API | 127.0.0.1:24517/TCP | 仅 Hsin 本机管理调用，可配置 |

默认 PublicOrigin 为 localhost，仅适合本机。以同一域名远程访问时，修改已有配置中的对应字段：

```json
{
  "WebUi": { "PublicOrigin": "http://share.example.com:24515" },
  "MediaMtx": {
    "PublicOrigin": "http://share.example.com:24516",
    "AdditionalHosts": ["share.example.com"]
  }
}
```

这些是配置片段，应合并到原配置，保留令牌和其余设置。PublicOrigin 是浏览器实际使用的协议、主机和端口，不包含 `/live/whep`；AdditionalHosts 只放可达域名或 IP，不含协议和路径。WebRTC CORS 仅允许配置的 Web UI Origin。局域网使用时填实际局域网 IP，不能让其他机器连接自己的 localhost。

放行并在 NAT 上映射 24515/TCP、24516/TCP、24516/UDP。修改端口后同步修改 PublicOrigin 与防火墙。TCP 信令能连接不代表 UDP 媒体可达；媒体不经过 .NET。需要穿越特殊网络时配置 `MediaMtx.IceServers` 对接外部 STUN/TURN，Hsin 不捆绑 TURN 服务。禁止用 HTTP 反向代理代替 UDP 媒体端口映射。

默认 Control API 不对公网开放。仅改端口的例子：

```json
{
  "MediaMtx": {
    "ControlApi": {
      "ListenAddress": "127.0.0.1",
      "Port": 24527,
      "User": "hsin-control",
      "Password": "",
      "AllowedIps": ["127.0.0.1/32", "::1/128"]
    }
  }
}
```

非 loopback 监听必须配置非空独立管理密码和允许访问的 IP/CIDR，并包含 Hsin 自身调用来源。不能使用观看或推流凭据管理媒体服务。监听端口变更后，Hsin 的状态查询和就绪探测使用同一配置。

## 可选 HTTPS

`Tls.CertificateFile` 和 `Tls.PrivateKeyFile` 均为空时，Web UI 和 WHIP/WHEP 使用 HTTP，不强制重定向。HTTP 下信令与凭据没有 HTTPS 传输保护；WebRTC 媒体仍使用协议本身的 DTLS-SRTP。

启用 HTTPS 时提供匹配的 PEM 证书链和 PEM 私钥，并同步修改两个 PublicOrigin：

```json
{
  "Tls": {
    "CertificateFile": "certs/fullchain.pem",
    "PrivateKeyFile": "certs/privkey.pem"
  },
  "WebUi": { "PublicOrigin": "https://share.example.com:24515" },
  "MediaMtx": { "PublicOrigin": "https://share.example.com:24516" }
}
```

端口数字不因启用 HTTPS 改变。仅配置一项、文件无效或不可读时启动失败，不降级到 HTTP。浏览器和 OBS 均需信任证书；Hsin 不负责签发或续期。证书更新也需重启 Hsin。

## 外观与配置生效

`Appearance` 可配置 Title、LogoFile、ShowcaseImageFile、IdleIconFile、IdleMessage、PasswordPrompt 和 AnnouncementFile。页面采用卡片网格布局：桌面端播放器右侧依次为直播状态卡、画面信息卡（分辨率、帧率、视频码率、延迟估算）和展示图片卡，播放统计与公告位于下方；移动端按同样顺序单列排列。ShowcaseImageFile 是展示图片卡使用的图片，可为空，为空时不显示该卡片；图片完整显示，空余区域用同图模糊填充，竖图、横图均可。不再支持整页或左右背景图，旧配置中的 BackgroundLeftFile、BackgroundRightFile、MobileBackgroundFile 会被忽略。界面首次根据系统选择亮色或暗色，并在浏览器本地记住选择。延迟估算仅表示浏览器从收到画面到显示的时间，不包含网络和服务器转发延迟。

公告使用 Markdown 文件，支持 GitHub 风格渲染和净化后的 HTML。Logo 接口可匿名读取，密码页面会显示 Logo 和标题；展示图片、idle 图标、公告及房间信息需要 Bearer token，密码页面不显示展示图片。图片、公告与配置都在启动时读取；运行期间修改文件不会生效。

每次启动读取 `hsin.json` 并重新生成 `MediaMtx.DataDirectory/run/mediamtx.yml`。不手改生成文件，也不通过 MediaMTX API 改写配置。修改完成后停止并重新启动 Hsin；同一次运行中的媒体子进程重启仍使用当次配置快照。

## 日志

日志使用 Serilog，Hsin 与 MediaMTX 输出统一进入控制台，并默认写入文件：

```json
{
  "Logging": {
    "FileEnabled": true,
    "Directory": "logs",
    "RetentionDays": 7,
    "MinimumLevel": "Information"
  }
}
```

Directory 可以是当前操作系统的绝对路径，例如 Windows 的 `D:\\HsinLogs`（JSON 字符串形式）或 Linux 的 `/var/log/hsin`；相对路径按配置文件目录解析。启用时目录不可写会使启动失败。MinimumLevel 使用 Serilog 级别：Verbose、Debug、Information、Warning、Error、Fatal。

每次启动按服务器本地时间命名，例如 `2026-09-28_16-30-05.log`。跨天继续写原文件；同秒启动追加到同名文件，不覆盖。日志行含时区偏移。默认按最后写入时间保留过去七天的历史文件，启动和运行期间每小时清理。只处理日志目录顶层符合命名格式的历史文件，保留活动文件和未知文件。因此长时间运行的活动文件可能含超过七天的内容，RetentionDays 不是日志文件数量上限。

仅输出控制台：

```json
{ "Logging": { "FileEnabled": false } }
```

关闭后不创建或探测日志目录，也不执行历史清理，保留已有日志。Directory 和 RetentionDays 在关闭时不生效。重新启用恢复文件规则；更换目录后不清理旧目录。所有日志设置修改都在重启后生效。不要在分享日志时附上包含 Token 的配置文件。

## MediaMTX 更新与回退

普通启动复用已安装版本，不查询 latest；首次缺失时下载 Hsin 指定的版本。更新命令查询官方稳定发布，校验 SHA-256 后准备候选版本。先正常停止 Hsin，再执行命令：

```powershell
.\Hsin.exe mediamtx update --config C:\HsinData\hsin.json
.\Hsin.exe --config C:\HsinData\hsin.json
```

```sh
./Hsin mediamtx update --config /srv/hsin/hsin.json
./Hsin --config /srv/hsin/hsin.json
```

精确版本选择使用 `mediamtx update --version v1.21.1`，同时附上实际使用的 `--config`。运行中的实例持有目录锁，更新/回退应明确失败，不杀死已有服务。

候选在下次启动时先检查记录的 MediaMTX 二进制 SHA-256，再由二进制 `--version` 核对版本；这两项通过后才允许启动媒体进程。启动后通过 Control API 确认固定 `live` 路径就绪，失败则尝试此前活动版本。Windows 由 Hsin 直接启动媒体进程并在 Job 中托管，Linux 由 Hsin 启动专用 guardian 进程，guardian 再管理真实媒体进程。`--media-guardian` 是内部启动参数，应由 Hsin 普通服务器流程创建；它不读取 Hsin 配置或启动 Web Host。旧 `state.json` 缺少二进制 SHA-256 时启动会明确失败；请备份并移走旧 `data/mediamtx` 后重新下载或导入 MediaMTX 包。就绪成功不代表 OBS 和浏览器兼容性已经验证；更新后重新检查推流、观看、替换及延迟。

回退同样先停服务：

```powershell
.\Hsin.exe mediamtx rollback --config C:\HsinData\hsin.json
.\Hsin.exe --config C:\HsinData\hsin.json
```

```sh
./Hsin mediamtx rollback --config /srv/hsin/hsin.json
./Hsin --config /srv/hsin/hsin.json
```

回退只选择上一个媒体程序版本，继续使用当前 hsin.json 生成配置，不恢复旧 Token、端口或证书。没有上一个版本或旧版本不接受当前配置时明确失败。应用自身回滚使用上一发布包，保留独立配置与数据目录。更新、回退和重启都会中断现有媒体连接。

## 运行检查

先验证 UI 可访问、空闲状态正确，再用真实 OBS 推流并在另一台机器观看。遇到连接问题依次检查监听/Origin、Token、日志、UDP 可达性与浏览器编码支持。媒体进程不可用与正常未开播是不同状态，不应只凭灰色空闲界面判断服务正常。

约八人共享的验收应使用八个真实接收连接持续至少 30 分钟，记录 CPU、内存、网卡上下行、卡顿及延迟分布。调试卡片的“接收至显示估算”仅在浏览器提供逐帧接收与预计显示时间时出现，不包含服务器入站处理时间；RTT、解码耗时和抖动缓冲驻留时间也不能代替完整的发送到显示延迟。Windows/Linux 启动、退出和媒体运行须分别取得证据，构建结果不能替代。
