// 校验 cordis.patch.yml 能被真正解析，且 insert 行的 name 与 package.json 的 name 一致。
//
// 为什么单独做成文件：此前把这段逻辑内联在 workflow 的 `node -e "..."` 里，
// 依赖 createRequire 在仓库根解析 js-yaml，环境差异会导致无法定位的失败。
// 放成仓库内的普通模块后，import 解析由 Node 按标准规则处理，且能打印完整诊断。
//
// 退出码：0 = 通过；1 = 校验不通过
import fs from 'node:fs'
import { createRequire } from 'node:module'
import { join } from 'node:path'
import { pathToFileURL } from 'node:url'

function fail(msg) {
  console.error('::error::' + msg)
  process.exit(1)
}

console.log('node ' + process.version)

let yaml
const attempts = []
for (const base of [import.meta.url, pathToFileURL(join(process.cwd(), 'package.json')).href]) {
  try {
    yaml = createRequire(base)('js-yaml')
    attempts.push('ok via ' + base)
    break
  } catch (e) {
    attempts.push('fail via ' + base + ' → ' + e.code)
  }
}
if (!yaml) {
  console.error('诊断：' + attempts.join(' | '))
  fail('无法加载 js-yaml（该步骤前应已 npm install js-yaml）')
}
console.log('js-yaml ' + (yaml.VERSION || 'loaded'))

for (const f of ['cordis.patch.yml', 'package.json']) {
  if (!fs.existsSync(f)) fail('缺少文件：' + f + '（cwd=' + process.cwd() + '）')
}

let doc
try {
  doc = yaml.load(fs.readFileSync('cordis.patch.yml', 'utf8'))
} catch (e) {
  fail('cordis.patch.yml 不是合法 YAML：' + e.message)
}

console.log('顶层类型：' + (Array.isArray(doc) ? 'array(' + doc.length + ')' : typeof doc))
if (!Array.isArray(doc)) fail('cordis.patch.yml 顶层应为 YAML 数组（loader patch 列表）')

const rows = doc.flatMap((r) => (r && Array.isArray(r.insert) ? r.insert : []))
console.log('insert 行数：' + rows.length)
for (const r of rows) console.log('  - name=' + JSON.stringify(r && r.name))

const row = rows.find((r) => r && r.name)
if (!row) fail('cordis.patch.yml 里找不到带 name 的 insert 行')

const pkg = JSON.parse(fs.readFileSync('package.json', 'utf8')).name
console.log('package.json.name      = ' + pkg)
console.log('cordis.patch.yml.name  = ' + row.name)

if (pkg !== row.name) fail('二者不一致 —— 会导致插件 failed to import')

if (row.name.startsWith('@')) {
  console.log('::notice::带 @ 的包名在 YAML 里必须加引号，已能解析，OK')
}
console.log('OK: bundle patch 校验通过')
