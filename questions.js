import {randomUUID} from 'node:crypto'

// Wrap the real Host waterfall. Both answerers stay available; the first
// settlement wins and cancels only the other UI, never the calling Turn.
export function createQuestions(ctx) {
  const live=new Map(),calls=new Map(),continued=new Map(),executions=new Map()
  const sessionOf=agent=>String(agent?.id??agent?.session?.id??'')
  const service=name=>{try{return ctx.get(name)}catch{return undefined}}
  function prompt(request,id,callId) {
    return {id,callId,sessionId:sessionOf(request.agent),kind:'question',answerable:true,
      question:request.questions[0]?.question??'',questions:request.questions.map(q=>({
        id:q.id,question:q.question,header:q.header??'',detail:q.detail??'',multiSelect:!!q.multiSelect,allowCustom:q.allowCustom!==false,
        options:(q.options??[]).map(o=>({label:o.label,description:o.description??''}))
      }))}
  }
  async function answerRequest(request,next,approval=false){
    const list=approval?[{id:'approval',question:'是否允许 '+request.toolName+'？',allowCustom:false,
      detail:[request.displayReason?.zh??request.displayReason?.en??request.reason,JSON.stringify(executions.get(String(request.callId))??{},null,2)].filter(Boolean).join('\n'),
      options:[{label:'允许本次'},{label:'拒绝'}]}]:request.questions
    if(!list?.length)return next()
    const id=randomUUID(),original=request.signal,otherUI=new AbortController()
    request.signal=original?AbortSignal.any([original,otherUI.signal]):otherUI.signal
    let settle,abort,timer,iterator
    const local=new Promise((resolve,reject)=>{
      settle=resolve
      abort=()=>reject(original?.reason??new Error('问题已取消'))
      if(original?.aborted)abort();else original?.addEventListener('abort',abort,{once:true})
    })
    const callId=request.callId??request.wait?.callId??calls.get(sessionOf(request.agent))
    const entry={prompt:prompt({...request,questions:list},id,callId),accept:value=>settle({kind:'answer',value:approval?(value.answers[0].selected[0]==='允许本次'?'allowed-once':'rejected'):value}),claimed:false,
      release:()=>settle({kind:'delegate'})}
    if(approval)entry.prompt.kind='approval'
    live.set(id,entry)
    // Start the regular DSH UI as well. A missing UI provider must not discard
    // the desktop answerer, while cancellation and real errors still propagate.
    const downstream=Promise.resolve().then(next)
    const remote=downstream.then(value=>({kind:'remote',value}),error=>{
      if(error?.code==='NO_PROVIDER')return new Promise(()=>{})
      throw error
    })
    try {
      if(request.wait?.timed&&request.agent&&service('userQuestions')?.attachWait){
        iterator=service('userQuestions').attachWait(request.agent,callId,request.signal)
        const opening=await iterator.next()
        if(!opening.done)timer=setTimeout(()=>{
          const error=new Error('ask_user_question timed out before the user answered');error.name='UserQuestionError';error.code='ASK_TIMED_OUT'
          otherUI.abort(error);settle({kind:'timeout',error})
        },opening.value.remainingMs)
      }
      const result=await Promise.race([local,remote])
      if(result.kind==='delegate'){clearTimeout(timer);return await downstream}
      if(result.kind==='timeout')throw result.error
      if(result.kind==='answer')otherUI.abort(new Error('已通过 Sumika 回复'))
      return result.value
    } finally {
      clearTimeout(timer);live.delete(id);original?.removeEventListener('abort',abort)
      // On normal settlement close the losing UI. During unload delegation,
      // leave DSH's own answerer and its original signal intact.
      if(!entry.delegated)otherUI.abort(new Error('问题已结束'))
      if(iterator){void iterator.return().catch(()=>{})}
    }
  }
  ctx.on('user-questions/request',(request,next)=>answerRequest(request,next),{global:true,prepend:true})
  ctx.on('approval/request',(request,next)=>answerRequest(request,next,true),{global:true,prepend:true})
  ctx.effect?.(()=>()=>{for(const entry of live.values()){entry.delegated=true;entry.release()}live.clear()})
  function reconcile() {
    const found=new Set(),agents=service('agents'),projections=service('sessionProjections')
    for(const agent of agents?.roots?.()??[]){
      const active=projections?.stateOf(agent.session,'userQuestions')?.questions.active??[]
      const queued=new Set([...(agent.inbox?.nextStep??[]),...(agent.inbox?.nextTurn??[])].filter(m=>m.source?.kind==='user-question-reply').map(m=>m.source.callId))
      for(const pending of active.filter(q=>q.state==='continued')){
        const key=sessionOf(agent)+'|'+pending.callId
        if(queued.has(pending.callId))continue
        found.add(key)
        if([...live.values()].some(e=>e.prompt.sessionId===sessionOf(agent)&&e.prompt.callId===pending.callId))continue
        if(!continued.has(key))continued.set(key,{prompt:prompt({agent,questions:pending.questions},randomUUID(),pending.callId),agent,key})
      }
    }
    for(const key of continued.keys())if(!found.has(key))continued.delete(key)
  }
  function validate(questions,answers){
    if(!Array.isArray(answers)||answers.length!==questions.length)throw new Error('请回答所有问题后再发送')
    const seen=new Set()
    return {answers:answers.map(answer=>{
      const question=questions.find(q=>q.id===answer.id)
      if(!question||seen.has(answer.id))throw new Error('问题编号不正确');seen.add(answer.id)
      if(!Array.isArray(answer.selected)||answer.selected.some(s=>typeof s!=='string'||!question.options.some(o=>o.label===s))||new Set(answer.selected).size!==answer.selected.length)throw new Error('选项不正确')
      if(!question.multiSelect&&answer.selected.length>1)throw new Error('此问题只能选择一个选项')
      const custom=typeof answer.custom==='string'?answer.custom.trim():''
      if(custom&&question.allowCustom===false)throw new Error('授权请求请明确选择允许或拒绝')
      if(custom.length>65536)throw new Error('回复过长')
      if((custom&&answer.selected.length)||(!custom&&!answer.selected.length))throw new Error('请选择选项或输入回复')
      return {id:answer.id,selected:[...answer.selected],...(custom?{custom}:{})}
    })}
  }
  return {
    noteTool(exec){if(exec.name==='ask_user_question')calls.set(sessionOf(exec.agent),String(exec.callId));executions.set(String(exec.callId),exec.arguments)},
    finishTool(exec){executions.delete(String(exec.callId));if(calls.get(sessionOf(exec.agent))===String(exec.callId))calls.delete(sessionOf(exec.agent))},
    prompts(){reconcile();return [...live.values(),...continued.values()].map(e=>e.prompt)},
    async reply(input){
      reconcile()
      const entry=live.get(input.promptId)??[...continued.values()].find(e=>e.prompt.id===input.promptId)
      if(!entry||entry.claimed)throw new Error('此问题已回答或已结束')
      const answer=validate(entry.prompt.questions,input.answers);entry.claimed=true
      if(entry.accept){entry.accept(answer);return {accepted:true}}
      try{
        if(!await service('userQuestions')?.answer(entry.agent,entry.prompt.callId,answer))throw new Error('此问题已结束')
        continued.delete(entry.key);return {accepted:true}
      }catch(error){entry.claimed=false;throw error}
    }
  }
}
