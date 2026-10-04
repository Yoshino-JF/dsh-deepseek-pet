# DeepSeek 桌宠 · DSH 插件（宿主端）

把 DeepSeek 账户余额喂给桌面上的蓝鲸女仆桌宠，并提供两个模型工具。

- **桌宠本体**：`D:\DeepSeek\deepseek-pet`（独立原生程序，WPF 透明置顶窗）
- **本插件**：只做三件事 —— 用 DSH 凭据查余额、在 `127.0.0.1` 上开一个只读状态接口、起停桌宠进程

```
DSH 宿主插件  ──HTTP(127.0.0.1:47831/state)──▶  桌宠窗口
   │ 持 DEEPSEEK_API_KEY                         │ 只拿到余额数字
   └─ ctx.credentials.resolve()                  └─ 每 5 秒取一次
```

## 安装

```
plugin_manager install_bundle  D:\DeepSeek\deepseek-pet-plugin
```

首次安装即为新 bundle，**热生效**；此后修改 `index.js` 再装会返回
`restart-required`（同包替换需要重启 DSH 才会加载新的 JS 世代）。

## 配置（profile 的 `cordis.patch.yml` → `dsh-pet` 行）

| 字段 | 默认 | 说明 |
|---|---|---|
| `port` | `47831` | 状态接口端口，只监听 `127.0.0.1` |
| `petPath` | `D:\DeepSeek\deepseek-pet\build\DeepSeekPet.exe` | 桌宠程序路径 |
| `credential` | `DEEPSEEK_API_KEY` | 用哪条 DSH 凭据查余额（不写死密钥） |
| `apiBase` | `https://api.deepseek.com` | 余额接口前缀 |
| `refreshSeconds` | `60` | 缓存/刷新周期（秒） |
| `lowBalance` | `10` | 低于该值桌宠切「担心」表情、徽章变红 |
| `autoStart` | `true` | 宿主启动 3 秒后自动叫起桌宠 |
| `startDelayMs` | `3000` | 自动启动延迟，避开宿主 boot 期 |
| `stopOnUnload` | `false` | 卸载插件时是否顺手关掉桌宠（默认留着） |

## 长轮询与「干活事件」（v0.0.2）

- 桌宠用 `GET /state?wait=25` 长轮询：插件把请求挂起最多 25 秒，**有事件立刻返回**（实测反应 < 100ms），没有事件则超时返回一次心跳。
- 插件监听两个宿主事件来判断"用户主动开始干活"：
  `agent/inbox/inserted`（用户消息进入 inbox）→ 10 秒内 `agent/status` 变为 `running`。
  自动续跑 / 子代理 / 定时任务没有这条用户消息，因此**不会**触发桌宠的"被吓一跳"演出。
- 事件类型：`work-start`、`work-end`、`balance`（余额变化）、`message`（留言）、`quit`（软退出）。

## 开发与验收：不重启 DSH 也能测

插件改了 JS 之后，同包替换要重启 DSH 才生效。`dev-mock-server.mjs` 用**完全相同的协议**顶替插件：

```bat
node dev-mock-server.mjs 47835
DeepSeekPet.exe --url http://127.0.0.1:47835/state --screen on

curl "http://127.0.0.1:47835/dev/event?kind=work-start"   :: 模拟"用户开始干活"→ 桌宠被吓一跳
curl "http://127.0.0.1:47835/dev/event?kind=work-end"
curl "http://127.0.0.1:47835/say?text=hello"
```

## 工具

| 工具 | 参数 | 作用 |
|---|---|---|
| `pet_balance` | `fresh?` | 读余额；`fresh=true` 跳过 60 秒缓存 |
| `pet_control` | `action`（start/stop/status/refresh/say/hide/show）、`text?` | 起停桌宠、立即刷新、让桌宠弹一句话 |

## 接口

| 路径 | 说明 |
|---|---|
| `GET /health` | `{ok:true}` |
| `GET /state?pid=<桌宠pid>` | 桌宠拉取的状态（余额+留言），**先回缓存再后台补数据** |
| `GET /say?text=...` | 让桌宠说一句话（也可以由别的脚本调用） |
| `GET /refresh` | 触发一次强制刷新 |

## 安全

- API Key 只在宿主进程内使用，**不进日志、不下发**；桌宠只收到余额数字与货币。
- 状态接口只监听回环地址，且是只读的（`/say`、`/refresh` 也只影响桌宠显示）。
