# Third-party notices

Hsin is licensed under the MIT License in `LICENSE`. Third-party components retain their respective licenses and copyright notices.

## MediaMTX

The browser reader is derived from MediaMTX v1.21.1, licensed under MIT.

- Project: https://github.com/bluenviron/mediamtx
- Reader source: https://github.com/bluenviron/mediamtx/blob/v1.21.1/internal/servers/webrtc/reader.js
- License in the source tree: `web/src/vendor/LICENSE`
- License in Hsin release packages: `licenses/MediaMTX-LICENSE`

Local reader changes and provenance are documented in `web/src/vendor/README.md`. The MediaMTX server binary is downloaded separately from the official release. Its license is retained alongside the installed version under the configured media data directory.

## .NET

Self-contained Hsin releases include Microsoft .NET runtime components. Their license and third-party notices are supplied with the runtime distribution. See https://github.com/dotnet/runtime/blob/main/LICENSE.TXT and https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT.

## Application dependencies

The exact dependency versions are recorded in the project files and pnpm lock file. Frontend packages include Vue, Tailwind CSS, daisyUI, @lucide/vue, markdown-it, DOMPurify and github-markdown-css; backend packages include Serilog and YamlDotNet.

The publish scripts collect installed pnpm package notices and restored NuGet package notices into `licenses/npm` and `licenses/nuget`, retaining package metadata alongside original license and notice files. The collection includes build dependencies so their notices remain available even when a build tool contributes code or styles to a bundle. Optional pnpm packages absent on the build platform are not included.

NuGet packages that declare MIT or Apache-2.0 by license expression instead of including a license file are accompanied by that license text in `licenses/MIT.txt` or `licenses/Apache-2.0.txt`; their original nuspec metadata retains authorship and copyright information. These texts originate from the SPDX license list: https://github.com/spdx/license-list-data/tree/main/text. Any package with a different expression and no bundled text requires an explicit notice before publication.

The saxes 6.0.0 npm archive omits its license text. Its ISC license is retained from the official versioned source at https://github.com/lddubeau/saxes/blob/v6.0.0/LICENSE. Platform-specific @rolldown binding packages declare MIT; the corresponding rolldown package's original MIT license and copyright are also included in the collected npm notices.
