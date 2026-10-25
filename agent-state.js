// Observes the host's agent events; no extra bridge plugin is required.
export function createAgentState(ctx,questions) {
  let sequence=Date.now(),counter=0
  const events=[],prompts=new Map(),calls=new Map(),running=new Set()
  const sessionOf=agent=>agent?.id??agent?.sessionId??agent?.session?.id
  // A turn already in flight when this plugin loads emits no status event of its
  // own (the append feed is forward-only), so read each live session's log once
  // and carry over the turns that have a start without a matching end.
  let seeded=false
  function seedRunning() {
    if(seeded)return
    try{
      const sessions=typeof ctx.get==='function'?ctx.get('sessions'):null
      if(!sessions||typeof sessions.list!=='function')return
      seeded=true
      for(const session of sessions.list()){
        const header=session?.header
        if(!session||header?.origin==='subagent'||header?.parentSession)continue
        const last=Number(session.seq??0)
        let open=false
        // Walk back to the newest turn boundary instead of copying the whole log;
        // a boundary is normally a few hundred events away.
        for(let step=1;step<=20000&&step<=last;step++){
          const item=session.eventAt?.(last-step)
          if(!item)continue
          if(item.type==='turn/end')break
          if(item.type==='turn/start'){open=true;break}
        }
        if(open)running.add(String(session.id))
      }
    }catch{}
  }
  seedRunning()
  function event(value){events.push({seq:++sequence,...value});if(events.length>64)events.shift()}
  function remove(id){if(prompts.delete(id))event({type:'prompt-done',promptId:id})}
  ctx.on('tools/pre-execute',async(exec,next)=>{
    questions?.noteTool(exec)
    const decision=await next()
    if(!exec?.callId)return decision
    const sessionId=String(sessionOf(exec.agent)??'')
    let prompt
    if(exec.name==='ask_user_question'){
      const questions=exec.arguments?.questions,question=Array.isArray(questions)?questions[0]:null
      if(question?.question)prompt={kind:'question',question:String(question.question),header:String(question.header??''),options:question.options??[],multiSelect:!!question.multiSelect}
    }else if(decision?.kind==='ask')prompt={kind:'approval',question:exec.name?'是否允许 '+exec.name+'？':'需要你的授权',header:'',options:[],detail:String(decision.displayReason?.zh??decision.displayReason?.en??decision.reason??'')}
    if(prompt){const id='sumika-'+ ++counter;prompts.set(id,{id,sessionId,...prompt});calls.set(String(exec.callId),id);event({type:'prompt',promptId:id,sessionId})}
    return decision
  },{global:true})
  ctx.on('tools/result',exec=>{questions?.finishTool(exec);const call=String(exec?.callId??''),id=calls.get(call);if(id){calls.delete(call);remove(id)}},{global:true})
  ctx.on('agent/status',payload=>{
    const id=String(sessionOf(payload?.agent)??payload?.sessionId??'');if(!id)return
    if(payload.status==='running')running.add(id)
    else if(payload.status==='idle')running.delete(id)
  },{global:true})
  ctx.on('session/event',(session,value)=>{
    const id=String(session?.id??'')
    if(!id||session.header?.origin==='subagent'||session.header?.parentSession)return
    // Seeding goes through turn/start as well as agent/status: the host replays
    // a session's earlier events to a newly attached listener (that is what
    // firstLiveSeq marks), so a turn already in flight at startup is recovered.
    if(value?.type==='turn/start'){running.add(id);return}
    if(value?.type!=='turn/end')return
    // Any finished turn clears the flag, replayed history included: a turn that
    // ended in error or was aborted would otherwise leave the widget on 工作中.
    running.delete(id)
    if(typeof session.firstLiveSeq==='number'&&value.seq<session.firstLiveSeq)return
    const reason=value.data?.reason?.kind;if(!['completed','max-tokens','blocked'].includes(reason))return
    for(const [promptId,prompt]of prompts)if(prompt.sessionId===id)remove(promptId)
    event({type:'turn-end',sessionId:id,reason})
  },{global:true})
  return {snapshot:after=>{
    seedRunning()
    const real=questions?.prompts()??[]
    return {seq:sequence,events:events.filter(item=>item.seq>after),prompts:[...real,...[...prompts.values()].filter(p=>!real.some(q=>q.sessionId===p.sessionId&&p.kind===q.kind))],running:[...running]}
  }}
}
