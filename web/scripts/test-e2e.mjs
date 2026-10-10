import { spawn } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const server = await createServer({ server: { host: '127.0.0.1', port: 4173, strictPort: true } })
await server.listen()
try {
  const cli = fileURLToPath(new URL('../node_modules/@playwright/test/cli.js', import.meta.url))
  const child = spawn(process.execPath, [cli, 'test', ...process.argv.slice(2)], { stdio: 'inherit' })
  process.exitCode = await new Promise((resolve, reject) => {
    child.once('error', reject)
    child.once('exit', code => resolve(code ?? 1))
  })
} finally {
  await server.close()
}
