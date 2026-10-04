/**
 * dev-mock-server.mjs —— 开发/验收用的最小状态服务（不依赖 DSH）
 *
 * 存在的理由：插件改了 JS 之后，同包替换要重启 DSH 才生效；这个小服务用**完全相同的协议**
 * 顶替插件，让桌宠一侧（长轮询、被打断演出）可以在不重启 DSH 的情况下验证。
 *
 * 用法：
 *   node dev-mock-server.mjs [port]                     # 默认 47832
 *   curl "http://127.0.0.1:47832/state?pid=1&wait=25"   # 长轮询状态（有事件立刻回）
 *   curl "http://127.0.0.1:47832/dev/event?kind=work-start"   # 模拟"用户开始干活"
 *   curl "http://127.0.0.1:47832/dev/event?kind=work-end"
 *   curl "http://127.0.0.1:47832/say?text=hello"        # 让桌宠说话
 *   curl "http://127.0.0.1:47832/dev/event?kind=quit"   # 让桌宠退出
 *
 * 桌宠侧启动：
 *   DeepSeekPet.exe --url http://127.0.0.1:47832/state --screen on
 */
import http from 'node:http'

const port = Number(process.argv[2]) || 47832
const state = {
  balance: { isAvailable: true, currency: 'CNY', total: '66.66', granted: '0.00', toppedUp: '66.66' },
  busy: false,
  message: '',
  quit: false,
  lastPollAt: 0,
  lastPid: null,
}
let seq = 0
let pending = null
const waiters = new Set()

function notify(kind) {
  pending = { seq: ++seq, kind, at: new Date().toISOString() }
  for (const w of Array.from(waiters)) { try { w.fire() } catch { /* 忽略 */ } }
}
function take() { const e = pending; pending = null; return e }
function payload() {
  return {
    ok: true,
    plugin: 'dev-mock',
    version: '0.0.2',
    updatedAt: new Date().toISOString(),
    stale: false,
    balance: state.balance,
    lowBalance: 10,
    message: state.message,
    command: state.quit ? 'quit' : '',
    event: take(),
    busy: state.busy,
    activityLabel: state.busy ? '干活中' : '',
    error: null,
  }
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url || '/', `http://127.0.0.1:${port}`)
  const send = (code, body) => {
    const text = JSON.stringify(body)
    res.writeHead(code, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store', 'Content-Length': Buffer.byteLength(text) })
    res.end(text)
  }
  if (url.pathname === '/health') { send(200, { ok: true, plugin: 'dev-mock' }); return }
  if (url.pathname === '/state') {
    state.lastPollAt = Date.now()
    const pid = Number(url.searchParams.get('pid'))
    if (Number.isFinite(pid) && pid > 0) state.lastPid = pid
    const wait = Math.min(60, Math.max(0, Number(url.searchParams.get('wait')) || 0))
    console.log(`[dev-mock] /state pid=${pid || 0} wait=${wait} hold=${wait > 0 && !pending}`)
    if (wait <= 0 || pending) { send(200, payload()); return }
    const w = { done: false, timer: null, fire: null }
    w.fire = () => {
      if (w.done) return
      w.done = true
      waiters.delete(w)
      if (w.timer) clearTimeout(w.timer)
      console.log(`[dev-mock] waiter fired (waiters=${waiters.size})`)
      send(200, payload())
    }
    w.timer = setTimeout(w.fire, wait * 1000)
    waiters.add(w)
    req.on('close', () => {
      if (!w.done) console.log('[dev-mock] waiter closed early (client 断开)')
      w.done = true
      waiters.delete(w)
      if (w.timer) clearTimeout(w.timer)
    })
    return
  }
  if (url.pathname === '/dev/event') {
    const kind = url.searchParams.get('kind') || 'work-start'
    if (kind === 'work-start') state.busy = true
    if (kind === 'work-end') state.busy = false
    if (kind === 'quit') state.quit = true
    notify(kind)
    console.log(`[dev-mock] event=${kind} (waiters=${waiters.size})`)
    send(200, { ok: true, kind, busy: state.busy })
    return
  }
  if (url.pathname === '/say') {
    state.message = url.searchParams.get('text') || ''
    notify('message')
    send(200, { ok: true, message: state.message })
    return
  }
  send(404, { ok: false, error: 'not found' })
})

server.listen(port, '127.0.0.1', () => console.log(`[dev-mock] listening http://127.0.0.1:${port}/state`))
