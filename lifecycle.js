import path from 'node:path'
import { randomBytes } from 'node:crypto'
import { atomic,readJson } from './controller.js'

export function createLifecycle(root,controller,owner={pid:process.pid,desktopPid:process.ppid,execPath:process.execPath}) {
  const file=path.join(root,'.sumika-host.json')
  const lease={...owner,instance:randomBytes(16).toString('hex'),active:true}
  let disposed=false,startup=null
  return {
    start(){
      if(!startup)startup=(async()=>{
        if(disposed)return
        await atomic(file,lease)
        if(!disposed)await controller.startForHost()
      })()
      return startup
    },
    async dispose(){
      disposed=true
      await startup?.catch(()=>{})
      if((await readJson(file))?.instance!==lease.instance)return
      // Release ownership even if a native acknowledgement is unavailable.
      await atomic(file,{...lease,active:false})
      await controller.stopForHost(lease.instance)
    }
  }
}
