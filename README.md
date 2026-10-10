# Hsin

极致简单的妙妙直播web ui，基于WebRTC（由MediaMTX提供视频流转发），提供低延迟的单房间的直播服务/屏幕共享。

## 运行

直接下载编译好的二进制文件，解压到特定目录后，Windows 运行 `Hsin.exe`，Linux 运行 `./Hsin`。

> 由于自动拉取MediaMTX是从GitHub Releases拉取，如果网络无法访问github，可以使用--mediamtx-package <路径>命令来手动导入MediaMTX release中的包(zip/tar.gz)

首次启动会自动生成 `hsin.json` 配置文件，里面包含了自动生成的推流令牌，并且会自动拉取最新版的MediaMTX。

默认的webui位于: `http://localhost:24515`，Web UI Token 默认为空

远程使用前先按 [部署说明](docs/deployment.md) 配置两个 PublicOrigin、媒体地址和防火墙。OBS 编码与连接设置见 [推流说明](docs/obs.md)。所有配置修改均在重启 Hsin 后生效。

## 项目结构

后端项目位于 `server/Hsin.csproj`，前端项目位于 `web/`。

### 直接构建

依赖:
- .Net 10
- node.js 24.19.0
- pnpm 12.6.0

```powershell
pwsh -File scripts/publish.ps1 -Runtime win-x64
pwsh -File scripts/publish.ps1 -Runtime linux-x64
```

```sh
bash scripts/publish.sh linux-x64
```

## 许可证

Hsin 使用 [MIT License](LICENSE)。第三方组件说明见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

[MediaMTX](https://github.com/bluenviron/mediamtx)
