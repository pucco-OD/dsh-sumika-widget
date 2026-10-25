window.__ModuleLoader__.load({
  id:'dsh-sumika-widget',
  factory(require) {
    const React=require('react'),h=React.createElement
    const NS='dsh-sumika-widget'
    const DICT={
      zh:{
        'status.working':'工作中','status.idle':'休息中',
        'action.show':'显示挂件','action.hide':'隐藏挂件','toggle.enabled':'启用挂件',
        'section.appearance':'外观与位置','section.sound':'提示音',
        'row.height':'挂件大小','row.margin':'屏幕边缘留距','row.volume':'提示音音量',
        'row.waiting':'等待用户回复','row.completed':'工作结束后提醒',
        'unit.px':'像素','unit.percent':'%',
        'action.choose':'选择音频','action.preview':'试听','action.reset':'恢复默认',
        'error.generic':'操作未成功','error.tooLarge':'音频文件不能超过 20 MB','error.readFailed':'音频读取失败'
      },
      en:{
        'status.working':'Working','status.idle':'Idle',
        'action.show':'Show widget','action.hide':'Hide widget','toggle.enabled':'Enable widget',
        'section.appearance':'Appearance and position','section.sound':'Sounds',
        'row.height':'Widget size','row.margin':'Screen edge margin','row.volume':'Sound volume',
        'row.waiting':'Waiting for your reply','row.completed':'Work finished',
        'unit.px':'px','unit.percent':'%',
        'action.choose':'Choose audio','action.preview':'Preview','action.reset':'Reset to default',
        'error.generic':'The action did not complete','error.tooLarge':'Audio files can be at most 20 MB','error.readFailed':'Could not read the audio file'
      }
    }
    // The active translator. Rebound by apply(); locale.bind reads the active locale
    // at call time, so only a re-render is needed when the language changes.
    let tr=key=>DICT.zh[key]??key
    const CSS=`
.sumika-settings{font:inherit;color:inherit;display:flex;flex-direction:column;gap:22px;max-width:940px;padding:4px 0 24px}
.sumika-settings *{box-sizing:border-box}.sumika-settings h2{font-size:22px;margin:0;font-weight:600}
.sumika-card{border:1px solid var(--dsw-alias-border-l2,rgba(128,128,128,.25));border-radius:18px;padding:22px 24px}.sumika-card h3{font-size:15px;font-weight:600;margin:0 0 17px}
.sumika-row{display:flex;align-items:center;justify-content:space-between;gap:22px;padding:17px 0;border-top:1px solid var(--dsw-alias-border-l2,rgba(128,128,128,.18))}.sumika-row:first-of-type{padding-top:0;border-top:0}.sumika-row:last-child{padding-bottom:0}
.sumika-label{font-size:14px;line-height:1.5}.sumika-controls{display:flex;gap:8px;align-items:center;flex-shrink:0}.sumika-actions{display:flex;gap:9px;flex-wrap:wrap}
.sumika-settings button{font:inherit;font-size:13px;color:inherit;border:1px solid var(--dsw-alias-border-l2,rgba(128,128,128,.3));border-radius:9px;background:transparent;padding:8px 13px;cursor:pointer;min-height:36px;transition:background .15s}
.sumika-settings button:hover{background:rgba(128,128,128,.13)}.sumika-settings button:disabled{opacity:.4;cursor:default}.sumika-settings .sumika-quiet{border:0;padding-left:6px;padding-right:6px;opacity:.7}
.sumika-status{display:flex;align-items:center;justify-content:space-between;gap:16px}.sumika-status strong{font-size:17px;font-weight:500}
.sumika-switch{appearance:none;width:38px;height:22px;border:0;border-radius:12px;background:#737373;position:relative;cursor:pointer;flex-shrink:0;transition:background .15s}.sumika-switch:checked{background:#dedede}.sumika-switch:after{content:'';position:absolute;top:3px;left:3px;width:16px;height:16px;border-radius:50%;background:#353535;transition:transform .15s}.sumika-switch:checked:after{transform:translateX(16px)}
.sumika-range-wrap{display:flex;align-items:center;gap:13px}.sumika-range{width:215px;accent-color:currentColor;cursor:pointer;touch-action:none}.sumika-number-wrap{display:flex;align-items:center;gap:4px}.sumika-number{width:56px;text-align:right;font:inherit;font-size:13px;font-variant-numeric:tabular-nums;color:inherit;background:transparent;border:1px solid var(--dsw-alias-border-l2,rgba(128,128,128,.3));border-radius:7px;padding:4px 7px;min-height:30px}.sumika-number:focus{outline:none;border-color:var(--dsw-alias-border-l2,rgba(128,128,128,.7))}.sumika-unit{font-size:13px;opacity:.75;min-width:16px}.sumika-error{color:#de8a7d;font-size:13px;line-height:1.6}
@media(max-width:800px){.sumika-card{padding:18px}.sumika-row{gap:14px;flex-wrap:wrap}.sumika-controls{flex-wrap:wrap}.sumika-range{width:160px}}
`
    function installTranslator(ctx) {
      const fallback=key=>DICT.zh[key]??key
      const locale=ctx?.locale??(typeof ctx?.get==='function'?ctx.get('locale'):null)
      if(!locale||typeof locale.register!=='function'||typeof locale.bind!=='function')return fallback
      const register=()=>{
        // The two-argument form (one call, dictionaries keyed by locale id) is what the
        // shipped client bundles use; the single-locale form is the documented route for
        // namespaces outside the merge table, so it backs this one up.
        try{return locale.register(NS,{zh:DICT.zh,en:DICT.en})}catch{}
        const disposers=[]
        for(const id of ['zh','en'])try{disposers.push(locale.register(NS,id,DICT[id]))}catch{}
        return()=>{for(const dispose of disposers)try{dispose()}catch{}}
      }
      try{
        if(typeof ctx.effect==='function')ctx.effect(register,'sumika: dictionaries')
        else register()
      }catch(error){console.error('[sumika] dictionary registration failed',error)}
      const bound=locale.bind(NS)
      return key=>{let value;try{value=bound(key)}catch{}return typeof value==='string'&&value!==key?value:fallback(key)}
    }
    async function api(path,body) {
      const response=await fetch('/sumika-widget/'+path,{method:body===undefined?'GET':'POST',credentials:'same-origin',headers:body===undefined?undefined:{'content-type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)})
      const value=await response.json()
      if(!response.ok||!value.ok)throw new Error(value.error||tr('error.generic'))
      return value
    }
    function previewQueue(send) {
      let pending={},timer=null,flight=null,stopped=false,failure=null
      const hasPending=()=>Object.keys(pending).length>0
      function pump(){
        if(timer){clearTimeout(timer);timer=null}
        if(stopped||flight||!hasPending())return flight||Promise.resolve()
        const patch=pending;pending={}
        flight=send(patch).then(()=>{failure=null},error=>{failure=error}).finally(()=>{flight=null;if(!stopped&&hasPending())timer=setTimeout(pump,24)})
        return flight
      }
      return {
        push(patch){Object.assign(pending,patch);if(!stopped&&!timer&&!flight)timer=setTimeout(pump,24)},
        async flush(){if(timer){clearTimeout(timer);timer=null}while(!stopped&&(flight||hasPending())){if(flight)await flight;else await pump()}if(failure)throw failure},
        dispose(){stopped=true;if(timer)clearTimeout(timer);pending={}}
      }
    }
    function LiveRange({label,value,min,max,step=1,unit,setting,onPreview,onCommit,onEditing}) {
      const percent=unit==='%',show=value=>percent?Math.round(value*100):Math.round(value),store=number=>percent?number/100:number
      const [draft,setDraft]=React.useState(value),current=React.useRef(value),editing=React.useRef(false),typing=React.useRef(false)
      const [text,setText]=React.useState(String(show(value)))
      // A poll must not overwrite the box while it holds a half-typed number.
      React.useEffect(()=>{if(!editing.current&&!typing.current){setDraft(value);current.current=value;setText(String(show(value)))}},[value])
      function begin(){editing.current=true;onEditing(true)}
      function change(event){begin();const next=Number(event.target.value);current.current=next;setDraft(next);onPreview({[setting]:next})}
      function commit(){if(!editing.current)return;editing.current=false;onEditing(false);void onCommit(setting,current.current)}
      // Nudging a slider onto an exact number is fiddly, so the box beside it takes a
      // typed one: whole numbers only, clamped to the same limits the slider enforces,
      // committed on Enter or blur.
      function editText(event){begin();typing.current=true;setText(event.target.value);const next=Number(event.target.value);if(!Number.isFinite(next))return;current.current=store(next);setDraft(current.current)}
      function commitText(){
        const raw=String(text).trim(),number=Math.round(Number(raw))
        typing.current=false
        if(!raw||!Number.isFinite(number)){setText(String(show(current.current)));commit();return}
        // Clamp in display units: a percentage setting stores 0..1 but the box
        // shows 0..100, so clamping after the conversion would turn 70 into 1.
        const next=store(Math.min(show(max),Math.max(show(min),number)))
        setText(String(show(next)));setDraft(next);current.current=next
        commit()
      }
      return h('div',{className:'sumika-range-wrap'},
        h('input',{type:'range',className:'sumika-range','aria-label':label,min,max,step,value:draft,
          onPointerDown:event=>{begin();event.currentTarget.setPointerCapture?.(event.pointerId)},onChange:change,onPointerUp:commit,onPointerCancel:commit,onBlur:commit,
          onKeyUp:event=>{if(['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End','PageUp','PageDown'].includes(event.key))commit()}}),
        h('span',{className:'sumika-number-wrap'},
          h('input',{type:'text',inputMode:'numeric',className:'sumika-number','aria-label':label+' value',value:text,
            onFocus:()=>{typing.current=true},onChange:editText,onBlur:commitText,
            onKeyDown:event=>{
              if(event.key==='Enter'){event.preventDefault();event.currentTarget.blur()}
              else if(event.key==='Escape'){event.preventDefault();typing.current=false;setText(String(show(current.current)));event.currentTarget.blur()}
            }}),
          h('span',{className:'sumika-unit'},unit)))
    }
    function Section() {
      const [snapshot,setSnapshot]=React.useState(null),[busy,setBusy]=React.useState(false),[error,setError]=React.useState('')
      const waitingInput=React.useRef(null),completedInput=React.useRef(null),inFlight=React.useRef(false),alive=React.useRef(true),pollError=React.useRef(false)
      const editing=React.useRef(false),syncing=React.useRef(0),pendingSave=React.useRef({}),saveFlight=React.useRef(null),previews=React.useRef(null),runningRef=React.useRef(false)
      runningRef.current=!!snapshot?.running
      if(!previews.current)previews.current=previewQueue(patch=>runningRef.current?api('preview',patch):Promise.resolve())
      React.useEffect(()=>{
        alive.current=true
        async function poll(){
          if(inFlight.current||syncing.current||editing.current||document.hidden)return
          try{const value=await api('state');if(alive.current&&!inFlight.current&&!syncing.current&&!editing.current){setSnapshot(value);if(pollError.current)setError('');pollError.current=false}}
          catch(e){if(alive.current){pollError.current=true;setError(e.message)}}
        }
        void poll();const timer=setInterval(poll,1000)
        return()=>{alive.current=false;clearInterval(timer);previews.current.dispose()}
      },[])
      function save(patch) {
        Object.assign(pendingSave.current,patch)
        if(saveFlight.current)return saveFlight.current
        inFlight.current=true;setBusy(true);setError('')
        saveFlight.current=(async()=>{
          try{while(Object.keys(pendingSave.current).length){const next=pendingSave.current;pendingSave.current={};const value=await api('settings',next);if(alive.current&&!Object.keys(pendingSave.current).length&&!editing.current)setSnapshot(value)}}
          catch(e){pendingSave.current={};if(alive.current)setError(e.message)}
          finally{saveFlight.current=null;inFlight.current=false;if(alive.current)setBusy(false)}
        })()
        return saveFlight.current
      }
      async function commitRange(key,value){
        syncing.current++
        try{await previews.current.flush();await save({[key]:value})}
        catch(e){if(alive.current)setError(e.message)}finally{syncing.current--}
      }
      async function perform(route,body) {
        if(inFlight.current||syncing.current)return
        inFlight.current=true;setBusy(true);setError('')
        try{const value=await api(route,body);if(alive.current)setSnapshot(value)}
        catch(e){if(alive.current)setError(e.message)}finally{inFlight.current=false;if(alive.current)setBusy(false)}
      }
      const action=name=>perform('action',{action:name})
      const button=(label,callback,props={})=>h('button',{type:'button',disabled:busy,onClick:callback,...props},label)
      // Rows deliberately have no description argument: labels and controls only.
      const row=(label,control)=>h('div',{className:'sumika-row'},h('div',{className:'sumika-label'},label),h('div',{className:'sumika-controls'},control))
      const toggle=(label,checked,onChange)=>h('input',{type:'checkbox',className:'sumika-switch','aria-label':label,checked:!!checked,disabled:busy,onChange:e=>onChange(e.target.checked)})
      const settings=snapshot?.settings,running=!!snapshot?.running,visible=running&&!snapshot?.hidden,diagnostics=snapshot?.diagnostics
      const statusKey=snapshot?.statusKey==='working'||snapshot?.statusKey==='idle'?snapshot.statusKey:null
      const status=statusKey?tr('status.'+statusKey):(snapshot?.status||diagnostics?.status||(['Working','Waiting','IdleExit','WorkingEnter'].includes(diagnostics?.current)?tr('status.working'):tr('status.idle')))
      const range=(label,setting,min,max,unit,step=1)=>h(LiveRange,{label,setting,value:settings[setting],min,max,unit,step,onEditing:value=>{editing.current=value},onPreview:patch=>previews.current.push(patch),onCommit:commitRange})
      async function upload(kind,file) {
        if(!file)return
        if(file.size>20*1024*1024){setError(tr('error.tooLarge'));return}
        try{
          const data=await new Promise((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(reader.result);reader.onerror=()=>reject(new Error(tr('error.readFailed')));reader.readAsDataURL(file)})
          await perform('sound',{kind,filename:file.name,base64:String(data).split(',')[1]})
        }catch(e){setError(e.message)}
      }
      const soundRow=(kind,label,ref)=>row(label,[
        h('input',{key:'input',ref,type:'file',accept:'.wav,.mp3,.wma,.m4a',style:{display:'none'},onChange:e=>{void upload(kind,e.target.files?.[0]);e.target.value=''}}),
        button(tr('action.choose'),()=>ref.current.click(),{key:'choose','data-testid':'choose-'+kind}),
        button(tr('action.preview'),()=>action(kind==='waiting'?'previewWaiting':'previewCompleted'),{key:'preview',disabled:busy||!running}),
        settings?.[kind==='waiting'?'waitingSound':'completedSound']&&button(tr('action.reset'),()=>save({[kind==='waiting'?'waitingSound':'completedSound']:null}),{key:'reset',className:'sumika-quiet'})
      ])
      return h('div',{className:'sumika-settings','data-sumika-settings':'true'},
        h('style',null,CSS),h('header',null,h('h2',null,'Sumika')),
        h('section',{className:'sumika-card'},h('div',{className:'sumika-status'},h('strong',null,status),
          button(visible?tr('action.hide'):tr('action.show'),()=>action(visible?'hide':'show'),{'data-testid':'toggle-visibility',disabled:busy||settings?.enabled===false})),
          settings&&h('div',{className:'sumika-row',style:{marginTop:18,paddingTop:17}},h('div',{className:'sumika-label'},tr('toggle.enabled')),toggle(tr('toggle.enabled'),settings.enabled,value=>save({enabled:value})))),
        settings&&h(React.Fragment,null,
          h('section',{className:'sumika-card'},h('h3',null,tr('section.appearance')),row(tr('row.height'),range(tr('row.height'),'height',180,600,tr('unit.px'))),row(tr('row.margin'),range(tr('row.margin'),'margin',8,100,tr('unit.px')))),
          h('section',{className:'sumika-card'},h('h3',null,tr('section.sound')),soundRow('waiting',tr('row.waiting'),waitingInput),soundRow('completed',tr('row.completed'),completedInput),row(tr('row.volume'),range(tr('row.volume'),'volume',0,1,'%',.01)))),
        error&&h('div',{className:'sumika-error',role:'alert'},error))
    }
    return {
      inject:['slots','locale'],
      apply(ctx){
        // This runs on the host's client-plugin bootstrap path, so nothing below may
        // escape: a broken panel must not take the shell down with it.
        try{tr=installTranslator(ctx)}catch(error){console.error('[sumika] dictionaries unavailable',error)}
        try{
          // Re-render on locale change: locale.bind() already reads the active locale
          // when called, so the panel only has to be asked to render again.
          function Localized(props) {
            const [,bump]=React.useReducer(count=>count+1,0)
            React.useEffect(()=>{
              const locale=ctx.locale??null
              if(!locale||typeof locale.subscribe!=='function')return undefined
              return locale.subscribe(()=>bump())
            },[])
            return h(Section,props)
          }
          ctx.slots.inject('settings.section',()=>ctx.slots.register({name:'settings.section',id:'sumika-widget',order:110,label:()=>'Sumika'},Localized))
        }catch(error){
          console.error('[sumika] settings panel could not be mounted; the rest of the app is unaffected',error)
        }
      }
    }
  }
})
