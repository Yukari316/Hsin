MediaMTX WebRTC reader from bluenviron/mediamtx v1.21.1:
https://github.com/bluenviron/mediamtx/blob/v1.21.1/internal/servers/webrtc/reader.js

Local changes expose getStats(), encode Basic credentials as UTF-8, and close
WHEP sessions (including sessions established while close is in progress).
OPTIONS and PATCH requests are canceled on close. POST is allowed to complete
so its returned session URL can be deleted; stale attempts cannot update the
current connection. The reader is exported as an ES module.
The upstream license is included in LICENSE.
