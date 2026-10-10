import { readFileSync } from 'node:fs'
import { expect, test, type Page } from '@playwright/test'

const configPath = process.env.HSIN_MEDIA_CONFIG
test.skip(!configPath, 'Set HSIN_MEDIA_CONFIG to a running Hsin instance configuration.')

interface MediaConfig {
  Publish: { BearerToken: string }
  WebUi: { PublicOrigin: string }
  MediaMtx: { PublicOrigin: string }
}

const config = configPath ? JSON.parse(readFileSync(configPath, 'utf8')) as MediaConfig : null

async function publish(page: Page, marker: string): Promise<void> {
  if (!config) throw new Error('Media integration configuration is absent')
  const result = await page.evaluate(async ({ url, token, marker }) => {
    const canvas = document.createElement('canvas')
    canvas.width = 1280
    canvas.height = 720
    const context = canvas.getContext('2d')
    if (!context) throw new Error('Canvas 2D is unavailable')
    let frame = 0
    const timer = window.setInterval(() => {
      context.fillStyle = frame++ % 2 ? '#df583e' : '#2b8c86'
      context.fillRect(0, 0, canvas.width, canvas.height)
      context.fillStyle = 'white'
      context.font = 'bold 90px sans-serif'
      context.fillText(`${marker} ${frame}`, 80, 380)
    }, 33)

    const audio = new AudioContext()
    const oscillator = audio.createOscillator()
    const destination = audio.createMediaStreamDestination()
    oscillator.connect(destination)
    oscillator.start()
    const stream = canvas.captureStream(30)
    stream.addTrack(destination.stream.getAudioTracks()[0])
    const connection = new RTCPeerConnection()
    for (const track of stream.getTracks()) connection.addTrack(track, stream)
    await connection.setLocalDescription(await connection.createOffer())
    if (connection.iceGatheringState !== 'complete') {
      await new Promise<void>((resolve, reject) => {
        const timeout = window.setTimeout(() => reject(new Error('ICE gathering timed out')), 10000)
        connection.addEventListener('icegatheringstatechange', () => {
          if (connection.iceGatheringState === 'complete') {
            window.clearTimeout(timeout)
            resolve()
          }
        })
      })
    }

    const response = await fetch(url, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/sdp' },
      body: connection.localDescription?.sdp,
    })
    if (!response.ok) throw new Error(`WHIP rejected publisher: ${response.status}`)
    await connection.setRemoteDescription({ type: 'answer', sdp: await response.text() })
    Reflect.set(globalThis, '__hsinMediaPublisherClose', () => {
      window.clearInterval(timer)
      connection.close()
      oscillator.stop()
      stream.getTracks().forEach(track => track.stop())
      void audio.close()
    })
    return response.status
  }, { url: `${config.MediaMtx.PublicOrigin}/live/whip`, token: config.Publish.BearerToken, marker })
  expect(result).toBe(201)
}

test('WHIP stream reaches the Hsin player and a new publisher replaces it', async ({ browser }) => {
  if (!config) throw new Error('Media integration configuration is absent')
  const first = await browser.newPage()
  const second = await browser.newPage()
  const viewer = await browser.newPage()
  try {
    await first.goto(config.WebUi.PublicOrigin)
    await second.goto(config.WebUi.PublicOrigin)
    await viewer.goto(config.WebUi.PublicOrigin)
    await expect(viewer.getByText('直播尚未开始')).toBeVisible()
    await publish(first, 'SOURCE A')
    await expect.poll(async () => viewer.locator('video').evaluate(video => {
      const media = video as HTMLVideoElement
      return media.readyState >= 2 && (media.srcObject as MediaStream)?.getAudioTracks().length > 0
    }), { timeout: 30000 }).toBe(true)
    await publish(second, 'SOURCE B')
    await expect.poll(async () => viewer.locator('video').evaluate(video => {
      const media = video as HTMLVideoElement
      return media.readyState >= 2 && (media.srcObject as MediaStream)?.getVideoTracks().length > 0
    }), { timeout: 30000 }).toBe(true)
    await second.evaluate(() => Reflect.get(globalThis, '__hsinMediaPublisherClose')?.())
    await expect(viewer.getByText('直播尚未开始')).toBeVisible({ timeout: 30000 })
  } finally {
    await first.close()
    await second.close()
    await viewer.close()
  }
})
