import { promises as fs } from 'node:fs'
import path from 'node:path'
import { randomBytes } from 'node:crypto'
import { spawn } from 'node:child_process'

export const DEFAULTS = {enabled:true,height:320,margin:24,volume:.55,muted:false,waitingSound:null,completedSound:null}
const soundKinds = new Set(['waiting','completed'])
const formats = new Set(['.wav','.mp3','.wma','.m4a'])
const commands = new Set(['idle','working','waiting','completed','connect','demo','quit','questionPreview','previewWaiting','previewCompleted','resetPosition'])
const delay = ms => new Promise(resolve=>setTimeout(resolve,ms))
export async function readJson(file, fallback=null) { try{return JSON.parse(await fs.readFile(file,'utf8'))}catch(error){if(error.code==='ENOENT'||error instanceof SyntaxError)return fallback;throw error} }
export async function atomic(file,value) {
  await fs.mkdir(path.dirname(file),{recursive:true})
  const temporary=file+'.'+randomBytes(6).toString('hex')+'.tmp'
  await fs.writeFile(temporary,typeof value==='string'?value:JSON.stringify(value),{encoding:'utf8',mode:0o600})
  await fs.rename(temporary,file)
}
export function validatePatch(input) {
  if(!input || typeof input!=='object' || Array.isArray(input)) throw new Error('设置格式不正确')
  const patch={}
  for(const [key,value] of Object.entries(input)) {
    if(key==='height' || key==='margin') {
      const [minimum,maximum]=key==='height'?[180,600]:[8,100]
      if(!Number.isInteger(value)||value<minimum||value>maximum) throw new Error(key==='height'?'高度应为 180–600':'边距应为 8–100')
    } else if(key==='volume') {
      if(typeof value!=='number'||!Number.isFinite(value)||value<0||value>1) throw new Error('音量应在 0–100% 之间')
    } else if(key==='muted' || key==='hidden' || key==='enabled') {
      if(typeof value!=='boolean') throw new Error('开关值不正确')
    } else if(key==='waitingSound'||key==='completedSound') {
      if(value!==null && (typeof value!=='string'||!/^assets\/sounds\/custom\/[a-z0-9-]+\.(wav|mp3|wma|m4a)$/.test(value))) throw new Error('音效路径不正确')
    } else throw new Error('不支持的设置项：'+key)
    patch[key]=value
  }
  return patch
}
export function createController(root, options={}) {
  const now=options.now||Date.now
  let writes=Promise.resolve()
  let previewWrites=Promise.resolve()
  const serial=operation=>{const task=writes.then(operation,operation);writes=task.catch(()=>{});return task}
  const state=async()=>{
    const [saved,diagnostics]=await Promise.all([readJson(path.join(root,'settings.json'),{}),readJson(path.join(root,'diagnostics.json'))])
    let running=!!diagnostics && now()-Date.parse(diagnostics.updatedAt)<5500
    if(running) { try{(options.isAlive||((pid)=>{process.kill(pid,0);return true}))(diagnostics.pid)|| (running=false)}catch{running=false} }
    const {hidden:discardedHidden,autoConnect:discardedFollowing,...persistent}=saved
    const settings={...DEFAULTS,...persistent}
    if(settings.muted){settings.volume=0;settings.muted=false}
    const hidden=running&&!!diagnostics.hidden
    const working=running&&(diagnostics.status==='工作中'||!diagnostics.status&&['Working','Waiting','IdleExit','WorkingEnter'].includes(diagnostics.current))
    const status=working?'工作中':'休息中'
    // `status` stays for older clients and for the native widget; `statusKey` is
    // the stable token the panel translates through the Client locale service.
    return {settings,running,hidden,visible:running&&!hidden,status,statusKey:working?'working':'idle',diagnostics:running?diagnostics:null}
  }
  async function queue(action, values={}) {
    const id=now().toString()+'-'+randomBytes(8).toString('hex')
    const body={action,...Object.fromEntries(Object.entries(values).map(([k,v])=>[k,v===null?'':String(v)]))}
    const request=path.join(root,'.sumika-commands',id+'.json'), reply=path.join(root,'.sumika-results',id+'.json')
    await atomic(request,body)
    const deadline=Date.now()+(options.timeoutMs||4500)
    while(Date.now()<deadline) {
      const result=await readJson(reply)
      if(result) {await fs.unlink(reply).catch(()=>{});if(!result.ok)throw new Error(result.error||'挂件未能应用设置');return result}
      await delay(8)
    }
    // Delete an unclaimed request so a timeout cannot unexpectedly apply later.
    await fs.unlink(request).catch(()=>{})
    throw new Error('挂件暂时未响应，请稍后重试')
  }
  async function saveUnlocked(input) {
    const patch=validatePatch(input), current=await state()
    if('volume' in patch)patch.muted=false
    else if(patch.muted){patch.volume=0;patch.muted=false}
    if(current.running) await queue('configure',patch)
    else {
      const {hidden:discardedHidden,...persistent}=patch
      await atomic(path.join(root,'settings.json'),{...current.settings,...persistent})
    }
    if(patch.enabled===false)await waitStopped()
    else if(patch.enabled===true)await startUnlocked()
    return state()
  }
  async function waitStopped() {
    const deadline=Date.now()+4500
    while((await state()).running) {
      if(Date.now()>=deadline)throw new Error('挂件尚未退出，请稍后重试')
      await delay(30)
    }
  }
  async function startUnlocked() {
    if(!(await state()).running) {
      const current=await state()
      if(!current.settings.enabled)throw new Error('请先启用挂件')
      const owner=await readJson(path.join(root,'.sumika-host.json'))
      if(!owner?.active || !Number.isInteger(owner.pid) || !Number.isInteger(owner.desktopPid))throw new Error('请从 DeepSeek Harness 启用挂件')
      for(const pid of [owner.pid,owner.desktopPid]){
        try{if(!(options.isAlive||((pid)=>{process.kill(pid,0);return true}))(pid))throw new Error()}
        catch{throw new Error('DeepSeek Harness 已退出')}
      }
      const exe=options.executable||path.join(root,'CodexPet.exe'); await fs.access(exe)
      const child=(options.spawn||spawn)(exe,['--host-session',owner.instance,'--data-dir',root],{cwd:path.dirname(exe),detached:false,stdio:'ignore',windowsHide:false})
      await new Promise((resolve,reject)=>{child.once('spawn',resolve);child.once('error',reject)})
      child.unref()
      const deadline=Date.now()+10000
      while(Date.now()<deadline){const snapshot=await state();if(snapshot.running)return snapshot;await delay(80)}
      throw new Error('挂件启动中，请稍后刷新状态')
    }
    return state()
  }
  return {
    state,
    save:input=>serial(()=>saveUnlocked(input)),
    startForHost:()=>serial(async()=>{
      if(!(await state()).settings.enabled)return state()
      if((await state()).running){await queue('quit');await waitStopped()}
      return startUnlocked()
    }),
    stopForHost:instance=>serial(async()=>{
      const current=await state()
      if(current.running && current.diagnostics.hostSession===instance){await queue('quit');await waitStopped()}
    }),
    preview:input=>{
      const patch=validatePatch(input)
      if(Object.keys(patch).some(key=>!['height','margin','volume'].includes(key)))throw new Error('不支持的预览设置')
      const task=previewWrites.then(async()=>{
        if(!(await state()).running)return {applied:false}
        await atomic(path.join(root,'.sumika-commands','live-preview.json'),Object.fromEntries(Object.entries({...patch,sentAt:now()}).map(([key,value])=>[key,String(value)])))
        return {applied:true}
      })
      previewWrites=task.catch(()=>{})
      return task
    },
    action:action=>serial(async()=>{
      if(action==='start') {
        return startUnlocked()
      } else if(action==='show') {
        return saveUnlocked({enabled:true,hidden:false})
      } else if(action==='hide') {
        return saveUnlocked({hidden:true})
      } else {
        if(!commands.has(action)||(!options.allowManual&&!['quit','previewWaiting','previewCompleted'].includes(action)))throw new Error('不支持的操作')
        if(!(await state()).running)throw new Error('请先启动挂件')
        await queue(action)
      }
      return state()
    }),
    upload:(kind,filename,base64)=>serial(async()=>{
      if(!soundKinds.has(kind))throw new Error('音效类型不正确')
      const extension=path.extname(filename||'').toLowerCase()
      if(!formats.has(extension))throw new Error('请选择 WAV、MP3、WMA 或 M4A 音频')
      if(typeof base64!=='string'||base64.length>28*1024*1024||!/^[A-Za-z0-9+/]*={0,2}$/.test(base64))throw new Error('音频文件无效或超过 20 MB')
      const bytes=Buffer.from(base64,'base64')
      if(!bytes.length||bytes.length>20*1024*1024)throw new Error('音频文件无效或超过 20 MB')
      const relative='assets/sounds/custom/'+kind+'-'+randomBytes(8).toString('hex')+extension
      const file=path.join(root,relative);await fs.mkdir(path.dirname(file),{recursive:true});await fs.writeFile(file,bytes)
      try{return await saveUnlocked({[kind==='waiting'?'waitingSound':'completedSound']:relative})}
      catch(error){await fs.unlink(file).catch(()=>{});throw error}
    })
  }
}
