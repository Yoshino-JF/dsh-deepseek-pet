/**
 * DeepSeek 桌宠 · DSH 宿主端插件
 * ---------------------------------------------------------------------------
 * 职责边界（重要）：
 *   · 余额与凭据只留在这里 —— 桌宠进程永远拿不到 API Key，只拿到余额数字。
 *   · 桌宠是一个独立的原生窗口程序（deepseek-pet/build/DeepSeekPet.exe），
 *     它每分钟来 http://127.0.0.1:<port>/state 取一次状态；插件本身
 *     只监听回环地址、不对外暴露，也不注册到 Web UI 的路由表。
 *   · 宿主卸载/重启不影响已经起来的桌宠（它被 spawn 成 detached 进程）。
 *
 * 工具：
 *   pet_balance —— 直接读余额（默认走 60 秒缓存，fresh=true 强制刷新）
 *   pet_control —— 起停桌宠 / 立即刷新余额 / 让桌宠说句话
 */
import http from 'node:http'
import { spawn } from 'node:child_process'
import { existsSync } from 'node:fs'
import { defineTool } from '@deepseek-ai/dsh-tools'

export const name = 'dsh-pet'
export const inject = ['tools', 'credentials']

const VERSION = '0.2.6'

const DEFAULTS = Object.freeze({
  port: 47831,
  petPath: process.env.DSH_PET_EXE || (new URL('./pet/DeepSeekPet.exe', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')),   // 默认用包内载荷；可用环境变量 DSH_PET_EXE 或插件配置覆盖
  credential: 'DEEPSEEK_API_KEY',
  apiBase: 'https://api.deepseek.com',
  refreshSeconds: 60,
  lowBalance: 10,
  autoStart: true,
  startDelayMs: 3000,
  /** 插件被卸载/宿主退出时是否顺手关掉桌宠（默认留着，桌宠是桌面常驻小玩意） */
  stopOnUnload: false,
})

const norm = (v) => String(v == null ? '' : v).trim().replace(/\/+$/, '')

export function apply(ctx, config) {
  const cfg = { ...DEFAULTS, ...(config || {}) }
  const port = Number(cfg.port) || DEFAULTS.port
  const ttlMs = Math.max(5, Number(cfg.refreshSeconds) || DEFAULTS.refreshSeconds) * 1000
  const lowBalance = Number(cfg.lowBalance) || 0

  const state = {
    balance: null,
    error: null,
    fetchedAt: 0,
    message: '',
    inflight: null,
    /** 桌宠每次来取状态都会带上自己的 pid —— 这是「桌宠还活着吗」的唯一可信来源 */
    lastPollAt: 0,
    lastPid: null,
    quitRequested: false,
    /** 宿主是否正在干活（用户主动发起的回合）*/
    busy: false,
    busySince: 0,
  }

  // ---- 长轮询：把桌宠的 /state 请求挂起，有事件立刻回（反应 <100ms）----
  const waiters = new Set()
  let eventSeq = 0
  let pendingEvent = null

  function notify(kind) {
    pendingEvent = { seq: ++eventSeq, kind, at: new Date().toISOString() }
    for (const w of Array.from(waiters)) { try { w.fire() } catch { /* 单个连接出错不影响其它 */ } }
  }

  function takeEvent() {
    if (!pendingEvent) return null
    const e = pendingEvent
    pendingEvent = null
    return e
  }

  const say = (msg) => { try { console.log(`[dsh-pet] ${msg}`) } catch { /* 忽略 */ } }

  // ---------------------------------------------------------------- 余额查询
  async function refresh(force) {
    if (!force && state.balance && Date.now() - state.fetchedAt < ttlMs) return state.balance
    if (state.inflight) return state.inflight
    state.inflight = (async () => {
      try {
        const cred = await ctx.credentials.resolve(cfg.credential)
        const key = cred && cred.value ? cred.value : ''
        if (!key) throw new Error(`凭据 ${cfg.credential} 未配置`)
        const res = await fetch(`${norm(cfg.apiBase)}/user/balance`, {
          headers: { Authorization: `Bearer ${key}`, Accept: 'application/json' },
          signal: AbortSignal.timeout(15000),
        })
        if (!res.ok) throw new Error(`HTTP ${res.status} ${res.statusText}`)
        const data = await res.json()
        const info = Array.isArray(data && data.balance_infos) ? data.balance_infos[0] : undefined
        const prevTotal = state.balance ? state.balance.total : null
        state.balance = {
          isAvailable: data && data.is_available === true,
          currency: (info && info.currency) || 'CNY',
          total: (info && info.total_balance) || '0.00',
          granted: (info && info.granted_balance) || '0.00',
          toppedUp: (info && info.topped_up_balance) || '0.00',
        }
        state.fetchedAt = Date.now()
        state.error = null
        if (prevTotal !== null && prevTotal !== state.balance.total) notify('balance')
      } catch (err) {
        state.error = (err && err.message) || String(err)
        say(`余额查询失败：${state.error}`)
      } finally {
        state.inflight = null
      }
      return state.balance
    })()
    return state.inflight
  }

  function payload(takeEv) {
    let stale = false
    if (state.balance && state.fetchedAt) stale = Date.now() - state.fetchedAt > ttlMs * 3
    const command = state.quitRequested ? 'quit' : ''
    state.quitRequested = false
    const event = (takeEv === false) ? pendingEvent : takeEvent()
    return {
      ok: !state.error && !!state.balance,
      plugin: 'dsh-pet',
      version: VERSION,
      updatedAt: state.balance ? new Date(state.fetchedAt).toISOString() : null,
      stale,
      balance: state.balance,
      lowBalance,
      message: state.message,
      command,
      event,
      busy: state.busy,
      activityLabel: state.busy ? '干活中' : '',
      error: state.error,
    }
  }

  const fmt = (b) => {
    if (!b) return '余额未知'
    const sym = b.currency === 'USD' ? '$' : '¥'
    const low = Number(b.total) < lowBalance
    return `${sym}${b.total}（赠金 ${sym}${b.granted} / 充值 ${sym}${b.toppedUp}）${b.isAvailable ? '' : ' · 账户不可用'}${low ? ' · 低于阈值' : ''}`
  }

  // ------------------------------------------------- 回环状态接口（只给桌宠用）
  const server = http.createServer((req, res) => {
    let url
    try {
      url = new URL(req.url || '/', `http://127.0.0.1:${port}`)
    } catch {
      res.writeHead(400).end('bad request')
      return
    }
    const send = (code, body) => {
      const text = JSON.stringify(body)
      res.writeHead(code, {
        'Content-Type': 'application/json; charset=utf-8',
        'Cache-Control': 'no-store',
        'Content-Length': Buffer.byteLength(text),
      })
      res.end(text)
    }
    if (url.pathname === '/health') {
      send(200, { ok: true, plugin: 'dsh-pet', version: VERSION })
      return
    }
    if (url.pathname === '/state') {
      // 桌宠自报 pid：据此判断它在不在，以及 stop 时该关谁
      state.lastPollAt = Date.now()
      const pid = Number(url.searchParams.get('pid'))
      if (Number.isFinite(pid) && pid > 0) state.lastPid = pid
      // 先回缓存、顺手在后台补一次，桌宠永远不等网络
      void refresh(false)
      const wait = Math.min(60, Math.max(0, Number(url.searchParams.get('wait')) || 0))
      if (wait <= 0 || pendingEvent) { send(200, payload()); return }
      // 长轮询：挂起这次请求，直到有事件或超时（桌宠据此做到「被叫去干活」秒级反应）
      const w = { done: false, timer: null, fire: null }
      w.fire = () => {
        if (w.done) return
        w.done = true
        waiters.delete(w)
        if (w.timer) clearTimeout(w.timer)
        send(200, payload())
      }
      w.timer = setTimeout(w.fire, wait * 1000)
      waiters.add(w)
      req.on('close', () => { w.done = true; waiters.delete(w); if (w.timer) clearTimeout(w.timer) })
      return
    }
    if (url.pathname === '/dev/event') {
      // 开发/验收用：手工触发一次宿主事件（桌宠据此演出「被吓到」）
      const kind = url.searchParams.get('kind') || 'work-start'
      if (kind === 'work-start') { state.busy = true; state.busySince = Date.now() }
      if (kind === 'work-end') state.busy = false
      notify(kind)
      send(200, { ok: true, kind, busy: state.busy })
      return
    }
    if (url.pathname === '/say') {
      const text = url.searchParams.get('text') || ''
      if (text) state.message = text
      notify('message')
      send(200, { ok: true, message: state.message })
      return
    }
    if (url.pathname === '/refresh') {
      void refresh(true).then(() => notify('balance'))
      send(200, { ok: true, refreshing: true })
      return
    }
    send(404, { ok: false, error: 'not found' })
  })
  server.on('error', (err) => say(`回环服务异常（端口 ${port}）：${err.message}`))
  try {
    server.listen(port, '127.0.0.1', () => say(`状态接口就绪：http://127.0.0.1:${port}/state`))
  } catch (err) {
    say(`回环服务启动失败：${err && err.message}`)
  }
  ctx.effect(() => () => { try { server.close() } catch { /* 忽略 */ } })

  // ------------------------------------------- 宿主「开始干活」事件（R2d 的触发器）
  // 只有「用户主动发起」才算：用户消息进入 inbox 后 10 秒内 agent 状态变 running。
  // 自动续跑 / 子代理 / 定时任务没有这条用户消息，所以不会把桌宠吓一跳。
  let userMsgAt = 0
  ctx.on('agent/inbox/inserted', (p) => {
    const role = p && p.message ? p.message.role : undefined
    if (!role || role === 'user') userMsgAt = Date.now()
  })
  ctx.on('agent/status', (p) => {
    const running = !!p && p.status === 'running'
    const byUser = running && Date.now() - userMsgAt < 10_000
    if (byUser && !state.busy) {
      state.busy = true
      state.busySince = Date.now()
      say('用户开始干活 → 已通知桌宠（正在玩游戏的话会被吓一跳）')
      notify('work-start')
    } else if (!running && state.busy) {
      state.busy = false
      notify('work-end')
    }
  })

  // ------------------------------------------------------------------ 桌宠进程
  const pet = { proc: null }
  /** 桌宠超过这么久没来取状态，就认为它已经关了 */
  const ALIVE_WINDOW_MS = 30_000

  function polledRecently() {
    return state.lastPollAt > 0 && Date.now() - state.lastPollAt < ALIVE_WINDOW_MS
  }

  function petRunning() {
    return (!!pet.proc && pet.proc.exitCode === null && !pet.proc.killed) || polledRecently()
  }

  function livePid() {
    if (pet.proc && pet.proc.exitCode === null && !pet.proc.killed) return pet.proc.pid
    return polledRecently() ? state.lastPid : null
  }

  function startPet() {
    if (petRunning()) return { ok: true, status: 'already-running', pid: pet.proc.pid }
    const exe = String(cfg.petPath || '')
    if (!exe || !existsSync(exe)) {
      return { ok: false, status: 'missing', error: `找不到桌宠程序：${exe || '(未配置 petPath)'}` }
    }
    try {
      const child = spawn(exe, ['--url', `http://127.0.0.1:${port}/state`], {
        detached: true,
        stdio: 'ignore',
        windowsHide: true,
      })
      child.unref()
      child.on('exit', () => { pet.proc = null })
      child.on('error', (err) => { say(`桌宠启动失败：${err.message}`); pet.proc = null })
      pet.proc = child
      say(`桌宠已启动 pid=${child.pid}`)
      return { ok: true, status: 'started', pid: child.pid }
    } catch (err) {
      return { ok: false, status: 'error', error: (err && err.message) || String(err) }
    }
  }

  function stopPet() {
    // 自己 spawn 的进程直接杀；不是自己起的（例如手动双击运行）就发一条软退出指令，
    // 桌宠下次取状态时会自己关掉。
    if (pet.proc && pet.proc.exitCode === null && !pet.proc.killed) {
      const pid = pet.proc.pid
      try {
        process.kill(pid)
        pet.proc = null
        say(`桌宠已关闭 pid=${pid}`)
        return { ok: true, status: 'stopped', pid }
      } catch (err) {
        return { ok: false, status: 'error', error: (err && err.message) || String(err) }
      }
    }
    if (polledRecently()) {
      const pid = state.lastPid
      state.quitRequested = true
      notify('quit')
      return { ok: true, status: 'quit-requested', pid, note: '桌宠不是本插件启动的，已发送软退出指令（最多 5 秒内自行关闭）' }
    }
    return { ok: true, status: 'not-running' }
  }

  // -------------------------------------------------------------------- 工具
  ctx.tools.register(defineTool({
    name: 'pet_balance',
    description: '查询当前 DeepSeek 开放平台账户余额（桌面桌宠显示的就是这个数）。默认走 60 秒缓存以避免频繁请求；fresh=true 时不看缓存立即重新查询。余额接口失败时返回缓存的最后一次结果与错误原因。',
    parameters: {
      fresh: { type: 'boolean', description: 'true=忽略缓存立即重新查询余额' },
    },
    output: {
      schema: { type: 'object', additionalProperties: true },
      render: (_args, value) => [{ type: 'text', text: String((value && value.summary) || '') }],
    },
    execute: async (args) => {
      await refresh(Boolean(args && args.fresh))
      const p = payload(false)
      const b = p.balance
      const lines = []
      if (b) {
        lines.push(`DeepSeek 账户余额：${fmt(b)}`)
        lines.push(`状态：${b.isAvailable ? '可用' : '不可用（余额已耗尽或账户异常）'}`)
        lines.push(`更新时间：${p.updatedAt ? new Date(p.updatedAt).toLocaleString('zh-CN') : '未知'}${p.stale ? '（数据可能已过期）' : ''}`)
      } else {
        lines.push('暂时拿不到余额。')
      }
      if (p.error) lines.push(`错误：${p.error}`)
      return { ok: p.ok, summary: lines.join('\n'), ...(b || {}), updatedAt: p.updatedAt, error: p.error }
    },
  }))

  ctx.tools.register(defineTool({
    name: 'pet_control',
    description: '控制桌面上的 DeepSeek 桌宠：start=叫起来（已运行则返回 already-running）、stop=关掉、status=看运行状态与余额接口信息、refresh=立即刷新余额、say=让桌宠说一句话（桌宠会弹出气泡）。桌宠是独立的原生窗口进程，宿主重启不会关掉它。',
    parameters: {
      action: { type: 'string', required: true, enum: ['start', 'stop', 'status', 'refresh', 'say', 'hide', 'show'], description: 'start/stop/status/refresh/say/hide/show' },
      text: { type: 'string', description: 'action=say 时桌宠要说的话（建议 40 字以内）' },
      seconds: { type: 'number', description: 'action=say 时气泡停留秒数（默认 8）' },
    },
    output: {
      schema: { type: 'object', additionalProperties: true },
      render: (_args, value) => [{ type: 'text', text: String((value && value.summary) || '') }],
    },
    execute: async (args) => {
      const action = (args && args.action) || 'status'
      if (action === 'start') {
        const r = startPet()
        return { ...r, summary: r.ok
          ? (r.status === 'already-running' ? `桌宠已经在跑了（pid ${r.pid}）` : `桌宠已启动（pid ${r.pid}）`)
          : `启动失败：${r.error}` }
      }
      if (action === 'stop') {
        const r = stopPet()
        return { ...r, summary: r.ok ? (r.status === 'stopped' ? `桌宠已关闭（pid ${r.pid}）` : '桌宠本来就没在跑') : `关闭失败：${r.error}` }
      }
      if (action === 'refresh') {
        await refresh(true)
        const p = payload(false)
        return { ok: p.ok, summary: p.balance ? `已刷新：${fmt(p.balance)}` : `刷新失败：${p.error || '未知错误'}`, ...p }
      }
      if (action === 'say') {
        const text = String((args && args.text) || '').trim()
        if (!text) return { ok: false, summary: 'action=say 需要同时给 text' }
        state.message = text
        return { ok: true, summary: `已让桌宠说：「${text}」（桌宠每 5 秒取一次状态，最迟 5 秒后弹气泡；没在跑就先 pet_control action=start）` }
      }
      if (action === 'hide' || action === 'show') {
        // 桌宠没有单独的隐藏指令：hide 直接关掉，show 再叫起来
        if (action === 'hide') {
          const r = stopPet()
          return { ok: r.ok, summary: r.ok ? '桌宠已收起（进程关闭）' : `操作失败：${r.error}` }
        }
        const r = startPet()
        return { ...r, summary: r.ok ? '桌宠已重新出现' : `操作失败：${r.error}` }
      }
      // status
      await refresh(false)
      const p = payload(false)
      const running = petRunning()
      const owned = !!pet.proc && pet.proc.exitCode === null && !pet.proc.killed
      return {
        ok: true,
        running,
        owned,
        pid: livePid(),
        lastPollAt: state.lastPollAt ? new Date(state.lastPollAt).toISOString() : null,
        port,
        endpoint: `http://127.0.0.1:${port}/state`,
        petPath: cfg.petPath,
        exeExists: existsSync(String(cfg.petPath || '')),
        balance: p.balance,
        error: p.error,
        summary: [
          `桌宠：${running ? `运行中（pid ${livePid()}${owned ? '，由插件启动' : '，非插件启动'}）` : '未运行'}`,
          `最近一次取状态：${state.lastPollAt ? new Date(state.lastPollAt).toLocaleTimeString('zh-CN') : '从未'}`,
          `程序：${cfg.petPath}${existsSync(String(cfg.petPath || '')) ? '' : '（不存在！请先运行 build.cmd）'}`,
          `状态接口：http://127.0.0.1:${port}/state`,
          `余额：${fmt(p.balance)}${p.error ? ` · 错误：${p.error}` : ''}`,
        ].join('\n'),
      }
    },
  }))

  // -------------------------------------------------------- 定时刷新 + 自启动
  const timer = setInterval(() => { void refresh(false) }, ttlMs)
  ctx.effect(() => () => clearInterval(timer))

  const boot = setTimeout(() => {
    void refresh(true)
    if (cfg.autoStart) {
      const r = startPet()
      if (!r.ok && r.status !== 'already-running') say(`自动启动桌宠未成功：${r.error}`)
    }
  }, Math.max(0, Number(cfg.startDelayMs) || DEFAULTS.startDelayMs))
  ctx.effect(() => () => clearTimeout(boot))

  ctx.effect(() => () => {
    if (cfg.stopOnUnload) stopPet()
  })

  say(`插件已装载，回环端口 ${port}，桌宠程序 ${existsSync(String(cfg.petPath || '')) ? '就绪' : '缺失（先跑 build.cmd）'}`)
}
