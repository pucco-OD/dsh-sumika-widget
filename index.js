import { promises as fs } from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { createController } from './controller.js'
import { createLifecycle } from './lifecycle.js'
import { createAgentState } from './agent-state.js'
import { createQuestions } from './questions.js'
import { existsSync } from 'node:fs'
import { randomBytes } from 'node:crypto'

const pluginRoot=path.dirname(fileURLToPath(import.meta.url))
const packaged=existsSync(path.join(pluginRoot,'runtime','Sumika.exe'))
const root=packaged?path.join(process.env.LOCALAPPDATA||path.join(os.homedir(),'AppData','Local'),'Sumika'):path.resolve(pluginRoot,'..')
const prefix='/sumika-widget'
export function apply(ctx) {
  const controller=createController(root,{executable:packaged?path.join(pluginRoot,'runtime','Sumika.exe'):path.join(root,'CodexPet.exe'),allowManual:!packaged})
  const lifecycle=createLifecycle(root,controller)
  const questions=createQuestions(ctx)
  const agent=createAgentState(ctx,questions)
  const token=randomBytes(24).toString('hex')
  ctx.effect(()=>{
    void lifecycle.start().catch(error=>ctx.logger.warn('Sumika start: %s',error.message))
    return ()=>lifecycle.dispose().catch(error=>ctx.logger.warn('Sumika stop: %s',error.message))
  })
  const send=(res,status,body)=>{res.writeHead(status,{'content-type':'application/json; charset=utf-8','cache-control':'no-store'});res.end(JSON.stringify(body))}
  const trusted=async req=>{
    const local=['127.0.0.1','::1','::ffff:127.0.0.1'].includes(req.socket?.remoteAddress)
    if(local&&req.headers.authorization==='Bearer '+token)return true
    if(!packaged&&local && req.headers.authorization) {
      try{const bridge=JSON.parse(await fs.readFile(path.join(os.homedir(),'.dsh/desktop-pet/bridge.json'),'utf8'));if(req.headers.authorization==='Bearer '+bridge.token)return true}catch{}
    }
    const connection=ctx.get('connection')
    if(connection?.requestRejection)return !connection.requestRejection({headers:req.headers})
    // Browser writes require a matching origin, including in a minimal host.
    return local && (!req.headers.origin || new URL(req.headers.origin).host===req.headers.host)
  }
  const body=req=>new Promise((resolve,reject)=>{
    let size=0;const chunks=[]
    req.on('data',part=>{size+=part.length;if(size>29*1024*1024){reject(new Error('文件超过大小限制'));req.destroy()}else chunks.push(part)})
    req.on('end',()=>{try{resolve(JSON.parse(Buffer.concat(chunks).toString('utf8')||'{}'))}catch{reject(new Error('请求格式不正确'))}})
    req.on('error',reject)
  })
  const handler=async(req,res)=>{
    try {
      if(!await trusted(req)){send(res,403,{ok:false,error:'请求未通过 DSH 连接校验'});return}
      const url=new URL(req.url,'http://localhost')
      if(req.method==='GET'&&url.pathname===prefix+'/agent-state'){send(res,200,agent.snapshot(Number(url.searchParams.get('after')??-1)));return}
      if(req.method==='GET'&&url.pathname===prefix+'/state'){send(res,200,{ok:true,...await controller.state()});return}
      if(req.method==='POST'&&url.pathname===prefix+'/reply'){send(res,200,{ok:true,...await questions.reply(await body(req))});return}
      if(req.method==='POST'&&url.pathname===prefix+'/settings'){send(res,200,{ok:true,...await controller.save(await body(req))});return}
      if(req.method==='POST'&&url.pathname===prefix+'/preview'){send(res,200,{ok:true,...await controller.preview(await body(req))});return}
      if(req.method==='POST'&&url.pathname===prefix+'/action'){const input=await body(req);send(res,200,{ok:true,...await controller.action(input.action)});return}
      if(req.method==='POST'&&url.pathname===prefix+'/sound'){const input=await body(req);send(res,200,{ok:true,...await controller.upload(input.kind,input.filename,input.base64)});return}
      send(res,404,{ok:false,error:'未找到请求的接口'})
    }catch(error){send(res,400,{ok:false,error:error.message||'操作失败'})}
  }
  ctx.inject(['webServer'],web=>{
    const origin=web.webServer.getOrigin()
    void fs.mkdir(root,{recursive:true}).then(()=>fs.writeFile(path.join(root,'.sumika-bridge.json'),JSON.stringify({origin,token,pid:process.pid}),{encoding:'utf8',mode:0o600})).catch(error=>ctx.logger.warn('Sumika connection: %s',error.message))
    const dispose=web.webServer.register({kind:'prefix',path:prefix,handler})
    web.effect(()=>dispose)
  })
}
