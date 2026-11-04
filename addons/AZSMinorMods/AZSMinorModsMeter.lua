-- Slim Suite meter: authoritative totals/checkpoints plus explicitly observed hit samples.
-- MeterQuery v1 and combat-text stream v1 from Aethus's addon_docs(2).md.
local K = AZSKIT;
local M = { view = 'damage', sort = 'amount', offset = 0, checkpoints = {}, samples = {damage={},healing={}} };
AZSMinorModsMeter = M;
local function Count(t) return table.getn(t or {}); end
local function Num(v) return type(v)=='number' and v or 0; end
local function Text(v) return tostring(math.floor(Num(v)+0.5)); end
local function Name(v) return string.lower(string.gsub(string.gsub(v or '', '^%[', ''), '%]$', '')); end
local function FitString(fs,text,width,minSize)
 if not fs then return;end
 if not fs.baseFont then fs.baseFont,fs.baseSize,fs.baseFlags=fs:GetFont();end
 local size=fs.baseSize or 10;fs:SetWidth(math.max(1,width));fs:SetText(tostring(text or ''));
 if fs.GetStringWidth and fs.baseFont and fs.SetFont then
  fs:SetFont(fs.baseFont,size,fs.baseFlags);
  while size>(minSize or 7) and fs:GetStringWidth()>width do size=size-1;fs:SetFont(fs.baseFont,size,fs.baseFlags);end
 end
end
local function AddSample(metric, key, amount, crit, owner)
 if type(amount)~='number' or amount<0 or not key then return; end
 owner=owner or (M.context and M.context.localOwnerId);if not owner then return;end
 key=owner.."|"..key;local bucket=M.samples[metric];local s=bucket[key];
 if not s then s={count=0,crits=0,regular=0};bucket[key]=s;end
 s.count=s.count+1;s.low=s.low and math.min(s.low,amount) or amount;s.high=math.max(s.high or 0,amount);
 if crit then s.crits=s.crits+1;s.minCrit=s.minCrit and math.min(s.minCrit,amount) or amount;s.maxCrit=math.max(s.maxCrit or 0,amount);s.highCrit=math.max(s.highCrit or 0,amount);
 else s.regular=s.regular+1;s.highNormal=math.max(s.highNormal or 0,amount);end
end
local function Release(view)
 local id=M.checkpoints[view];
 if id and AZS_ReleaseMeterCheckpoint then pcall(AZS_ReleaseMeterCheckpoint,id);end
 M.checkpoints[view]=nil;
end
local function SyncContext(context)
 if not context then return;end
 if M.runId~=context.runId or M.sessionId~=context.uiSessionId or M.epoch~=context.counterEpoch or M.authority~=context.authorityId then
  for view in pairs(M.checkpoints) do Release(view);end
  M.samples={damage={},healing={}};M.knownActors={};M.cursor=nil;M.dropped=0;M.owner=nil;M.offset=0;
  M.runId,M.sessionId,M.epoch,M.authority=context.runId,context.uiSessionId,context.counterEpoch,context.authorityId;
 end
 M.context=context;
end
function M.Reset()
 if M.view~='damage' and M.view~='healing' then return;end
 if not AZS_GetMeterContext or not AZS_CreateMeterCheckpoint then M.message='Reset requires the current game meter API';return;end
 local context=AZS_GetMeterContext();SyncContext(context);
 if not context then M.message='No run to reset';return;end
 if M.Drain then M.Drain();end
 local point,err=AZS_CreateMeterCheckpoint(context.runId);
 if not point then M.message=err and err.code or 'Reset unavailable';return;end
 Release(M.view);M.checkpoints[M.view]=point.checkpointId;M.samples[M.view]={};M.offset=0;M.message=nil;
end
local function Query()
 if not AZS_GetMeterContext or not AZS_GetMeterSnapshot then return nil,'Update the game for full meter details';end
 local context,err=AZS_GetMeterContext();
 if not context then return nil,err and err.code or 'Waiting for a run';end
 SyncContext(context);
 local q={apiVersion=1,runId=context.runId,metric=M.view=='healing' and 'healing' or 'damage',scope=M.view=='group' and (M.owner and 'owner' or 'party') or 'self',period=M.view=='boss' and 'encounter' or 'run',groupBy=M.view=='group' and not M.owner and 'owner' or 'source',windowSeconds=0};
 if M.view=='healing' then q.healingPerspective='performed';end
 if M.view=='group' and M.owner then q.ownerId=M.owner;end
 if M.checkpoints[M.view] then q.checkpointId=M.checkpoints[M.view];end
 local result;result,err=AZS_GetMeterSnapshot(q);
 if not result and err and (err.code=='CHECKPOINT_EXPIRED' or err.code=='CHECKPOINT_INVALIDATED' or err.code=='INVALID_CHECKPOINT') then
  Release(M.view);q.checkpointId=nil;result,err=AZS_GetMeterSnapshot(q);
 end
 if not result then return nil,err or 'Waiting for meter data';end
 M.context=result.context;
 M.absorbed=0;
 if M.view=='healing' then
  for _,credit in ipairs(result.contributions or {}) do
   if credit.creditKind=='absorb_consumed' then M.absorbed=M.absorbed+Num(credit.displayAmount);end
  end
 end
 M.knownActors=M.knownActors or {};
 for id,actor in pairs(result.actors or {}) do
  local owner=actor.kind=='hero' and id or actor.currentOwnerId;
  if owner and actor.name then
   local old=M.knownActors[actor.name];if old~=nil and old~=owner then M.knownActors[actor.name]=false;else M.knownActors[actor.name]=owner;end
  end
 end
 return result;
end
local function Samples(source,owner)
 owner=owner or (M.context and M.context.localOwnerId);if not owner then return nil;end
 if type(source)~='table' then return nil;end
 local bucket=M.samples[M.view=='healing' and 'healing' or 'damage'];
 if source.sourceKey then
  local exact=bucket[owner.."|"..source.sourceKey];if exact then return exact;end
 end
 local keys,seen={},{};
 for _,id in ipairs(source.spellIds or {}) do local key='spell:'..tostring(id);if not seen[key] then table.insert(keys,key);seen[key]=true;end end
 table.insert(keys,'name:'..Name(source.name));
 local result={count=0,crits=0,regular=0};
 for _,key in ipairs(keys) do
  local s=bucket[owner.."|"..key];if s then
   result.count=result.count+s.count;result.crits=result.crits+s.crits;result.regular=result.regular+s.regular;
   result.low=result.low and math.min(result.low,s.low) or s.low;
   if s.high then result.high=math.max(result.high or 0,s.high);end
   if s.minCrit then result.minCrit=result.minCrit and math.min(result.minCrit,s.minCrit) or s.minCrit;end
   if s.maxCrit then result.maxCrit=math.max(result.maxCrit or 0,s.maxCrit);end
   if s.highCrit then result.highCrit=math.max(result.highCrit or 0,s.highCrit);end
   if s.highNormal then result.highNormal=math.max(result.highNormal or 0,s.highNormal);end
  end
 end
 return result.count>0 and result or nil;
end
local function Tooltip(button)
 if not GameTooltip or not button.entry then return;end
 local row,source=button.entry,button.source;
 GameTooltip:SetOwner(button,'ANCHOR_RIGHT');GameTooltip:AddLine(button.label or 'Ability',1,0.82,0);
 if M.view=='group' and not M.owner then GameTooltip:AddLine('Click for this player\'s abilities',1,1,1);GameTooltip:Show();return;end
 local sample=Samples(source,row.ownerId);
 GameTooltip:AddLine(M.view=='boss' and 'Observed hits: all targets' or M.view=='healing' and 'Observed combat-log samples' or 'Observed samples since load / reset',0.7,0.7,0.7);
 local function Value(n) return n and Text(n) or 'Not recorded';end
 GameTooltip:AddLine('Min Hit: '..Value(sample and sample.low),1,1,1);
 GameTooltip:AddLine('Max Hit: '..Value(sample and sample.high),1,1,1);
 GameTooltip:AddLine('Min Crit: '..Value(sample and sample.minCrit),1,1,1);
 GameTooltip:AddLine('Max Crit: '..Value(sample and sample.maxCrit),1,1,1);
 if sample then
  GameTooltip:AddLine(string.format('Crit %%: %.1f%% (%d / %d)',sample.crits/sample.count*100,sample.crits,sample.count),1,1,1);
 else GameTooltip:AddLine('Crit %: Not recorded',1,1,1);end
 GameTooltip:AddLine(string.format('Share of total: %.1f%%',Num(row.share)*100),1,0.82,0);
 if (M.dropped or 0)>0 then GameTooltip:AddLine('Some damage samples were missed.',1,0.5,0);end
 GameTooltip:Show();
end
function M.Attach(frame)
 M.frame=frame;frame:SetWidth(340);frame:SetHeight(220);frame:EnableMouse(true);frame:EnableMouseWheel(true);
 frame.rows={};frame.tabs={};
 local views={{'damage','Damage'},{'healing','Healing'},{'boss','Boss'},{'group','Group'}};
 for i=1,4 do
  local view,label=views[i][1],views[i][2];
  local tab=K.Button(frame,label,78,function() M.view=view;M.owner=nil;M.offset=0;M.message=nil;M.nextRead=0;end);
  tab:SetPoint('TOPLEFT',frame,'TOPLEFT',8+(i-1)*81,-6);frame.tabs[view]=tab;
 end
 frame.amount=K.Button(frame,'Damage Done',118,function() M.sort='amount';M.offset=0;end);
 frame.amount:SetPoint('TOPLEFT',frame,'TOPLEFT',8,-34);
 frame.rate=K.Button(frame,'DPS',64,function() M.sort='rate';M.offset=0;end);frame.rate:SetPoint('LEFT',frame.amount,'RIGHT',4,0);
 frame.reset=K.Button(frame,'Reset',58,function() M.Reset();M.nextRead=0;end);frame.reset:SetPoint('TOPRIGHT',frame,'TOPRIGHT',-8,-34);
 frame.back=K.Button(frame,'Back',58,function() M.owner=nil;M.offset=0;M.nextRead=0;end);frame.back:SetPoint('TOPRIGHT',frame,'TOPRIGHT',-8,-34);frame.back:Hide();
 frame.total=frame:CreateFontString(nil,'OVERLAY','GameFontHighlightSmall');frame.total:SetPoint('TOPLEFT',frame,'TOPLEFT',8,-64);
 frame.status=frame:CreateFontString(nil,'OVERLAY','GameFontHighlightSmall');frame.status:SetPoint('BOTTOMLEFT',frame,'BOTTOMLEFT',8,5);
 frame:SetScript('OnMouseWheel',function() M.offset=math.max(0,math.min(math.max(0,Count(M.rows)-M.visible),M.offset-(arg1 or 0)*3));end);
 for i=1,64 do
  local row=CreateFrame('Button',nil,frame);row:SetPoint('TOPLEFT',frame,'TOPLEFT',8,-84-(i-1)*20);row:SetHeight(19);
  row:SetHighlightTexture('Interface\\Buttons\\ButtonHilight-Square');
  row.icon=row:CreateTexture(nil,'ARTWORK');row.icon:SetPoint('LEFT',row,'LEFT',0,0);row.icon:SetWidth(16);row.icon:SetHeight(16);
  row.name=row:CreateFontString(nil,'OVERLAY','GameFontHighlightSmall');row.name:SetPoint('LEFT',row,'LEFT',20,0);row.name:SetJustifyH('LEFT');
  row.value=row:CreateFontString(nil,'OVERLAY','GameFontHighlightSmall');row.value:SetPoint('RIGHT',row,'RIGHT',-2,0);row.value:SetJustifyH('RIGHT');
  row:SetScript('OnEnter',function() Tooltip(this);end);row:SetScript('OnLeave',function() if GameTooltip then GameTooltip:Hide();end end);
  row:SetScript('OnClick',function() if M.view=='group' and not M.owner and this.entry then M.owner=this.entry.ownerId;M.offset=0;M.nextRead=0;if GameTooltip then GameTooltip:Hide();end end end);
  row:EnableMouseWheel(true);row:SetScript('OnMouseWheel',frame:GetScript('OnMouseWheel'));frame.rows[i]=row;
 end
end
function M.Draw(active,editing)
 local f=M.frame;if not f then return;end
 local healing=M.view=='healing';local unit=healing and 'HPS' or 'DPS';
 local width=f:GetWidth();local tabW=math.max(24,(width-16-9)/4);
 for i,view in ipairs({'damage','healing','boss','group'}) do
  local tab=f.tabs[view];tab:SetWidth(tabW);tab:ClearAllPoints();tab:SetPoint('TOPLEFT',f,'TOPLEFT',8+(i-1)*(tabW+3),-6);
  if view==M.view then tab:LockHighlight();else tab:UnlockHighlight();end
 end
 f.amount:SetText(healing and 'Healing Done' or 'Damage Done');f.rate:SetText(unit);
 f.amount:SetWidth(math.max(50,math.min(118,(width-88)*0.62)));f.rate:SetWidth(math.max(30,math.min(64,(width-88)*0.32)));
 if M.sort=='amount' then f.amount:LockHighlight();f.rate:UnlockHighlight();else f.rate:LockHighlight();f.amount:UnlockHighlight();end
 f.reset:Hide();f.back:Hide();
 if M.view=='damage' or M.view=='healing' then f.reset:Show();elseif M.view=='group' and M.owner then f.back:Show();end
 local now=GetTime();
 if now>=(M.nextRead or 0) then
  M.nextRead=now+0.35;
  local snapshot,err=Query();M.snapshot=snapshot;M.error=err;M.nextRead=now+math.max(0.35,type(err)=='table' and Num(err.retryAfterSeconds) or 0);
 end
 local snapshot=M.snapshot;local rows={};
 if snapshot then for _,row in ipairs(snapshot.rows or {}) do table.insert(rows,row);end end
 table.sort(rows,function(a,b)
  local av=M.sort=='rate' and Num(a.displayRate and a.displayRate.value) or Num(a.displayAmount);
  local bv=M.sort=='rate' and Num(b.displayRate and b.displayRate.value) or Num(b.displayAmount);
  if av~=bv then return av>bv;end
  return tostring(a.rowId)<tostring(b.rowId);
 end);
 M.rows=rows;M.visible=math.max(1,math.min(64,math.floor((f:GetHeight()-108)/20)));
 M.offset=math.max(0,math.min(M.offset,math.max(0,Count(rows)-M.visible)));
 local totals=snapshot and snapshot.totals;
 local totalText=totals and (Text(totals.displayAmount)..' | '..(totals.displayRate and totals.displayRate.status=='ready' and Text(totals.displayRate.value) or '--')..' '..unit)
  or (type(M.error)=='table' and (M.error.code=='NO_PARTY' and 'Join a party to see group damage' or M.error.code=='NO_ENCOUNTER' and 'No boss fight yet' or 'Waiting: '..tostring(M.error.code)) or M.error or 'Waiting for meter data');
 if healing then
  if M.absorbed and M.absorbed>0 then totalText=totalText..' | Absorbed: '..Text(M.absorbed);end
  if type(AZS_RUN)=='table' and type(AZS_RUN.sh)=='number' then totalText=totalText..' | Current shield: '..Text(AZS_RUN.sh);end
 end
 FitString(f.total,totalText,math.max(30,width-16),7);
 local statusText=M.message or (Count(rows)>0 and (tostring(M.offset+1)..'-'..tostring(math.min(Count(rows),M.offset+M.visible))..' / '..Count(rows)..' | Scroll for more') or (editing and 'Meter preview' or 'No data yet'));
 FitString(f.status,statusText,math.max(30,width-16),7);
 for i,button in ipairs(f.rows) do
  local row=i<=M.visible and rows[M.offset+i] or nil;
  if row then
   local source=snapshot.sources and snapshot.sources[row.sourceKey];local actor=snapshot.actors and snapshot.actors[row.ownerId];
   button.entry=row;button.source=source;button.label=source and source.name or actor and actor.name or 'Unknown source';
   local rowWidth=math.max(20,width-16);button:SetWidth(rowWidth);
   local rateText=row.displayRate and row.displayRate.status=='ready' and Text(row.displayRate.value) or '--';
   local amountText=Text(row.displayAmount)..' dmg';
   local primary=M.sort=='rate' and (rateText..' '..unit) or amountText;
   local valueWidth=math.max(72,math.floor(rowWidth*0.47));
   local nameWidth=math.max(24,rowWidth-20-valueWidth-4);
   FitString(button.name,button.label,nameWidth,7);
   FitString(button.value,primary..' | '..string.format('%.1f%%',Num(row.share)*100),valueWidth,7);
   if source and source.icon and source.icon.status=='ready' then button.icon:SetTexture(source.icon.path);button.icon:Show();else button.icon:Hide();end
   button:Show();
  else button.entry=nil;button:Hide();end
 end
end
-- The stream belongs to this addon alone; polling never consumes another addon's events.
local stream=CreateFrame('Frame');
function M.Drain()
 if not AZS_GetCombatTextEvents or not AZS_GetMeterContext then return;end
 local context=AZS_GetMeterContext();if not context then return;end;SyncContext(context);
 for read=1,16 do
  local batch,err=AZS_GetCombatTextEvents(M.cursor,256);
  if not batch then if err and err.code=='STREAM_CHANGED' then M.cursor=nil;end;return;end
  M.cursor=batch.cursor;M.dropped=(M.dropped or 0)+Num(batch.droppedCount);
  for _,event in ipairs(batch.events or {}) do
   if event.kind=='damage' then AddSample('damage',event.meterSourceKey or (event.spellId and ('spell:'..tostring(event.spellId)) or ('name:'..Name(event.abilityName or event.damageType))),event.amount,event.critical==true);end
  end
  if not batch.hasMore then break;end
 end
end
stream:SetScript('OnUpdate',function()
 this.elapsed=(this.elapsed or 0)+(arg1 or 0);if this.elapsed<0.1 then return;end;this.elapsed=0;M.Drain();
end);
-- Match only localized heal formats the client actually supplies. Do not parse player chat.
local healPatterns;
local function BuildHealPatterns()
 local formats={
 {'HEALEDCRITSELFSELF',1,2,true,'healing'},{'HEALEDCRITSELFOTHER',1,3,true,'healing'},
 {'HEALEDCRITOTHERSELF',2,3,true,'healing',1},{'HEALEDCRITOTHEROTHER',2,4,true,'healing',1},
 {'HEALEDSELFSELF',1,2,false,'healing'},{'HEALEDSELFOTHER',1,3,false,'healing'},
 {'HEALEDOTHERSELF',2,3,false,'healing',1},{'HEALEDOTHEROTHER',2,4,false,'healing',1},
 {'SPELLLOGCRITSELFOTHER',1,3,true,'damage'},{'SPELLLOGCRITOTHEROTHER',2,4,true,'damage',1},
 {'SPELLLOGSELFOTHER',1,3,false,'damage'},{'SPELLLOGOTHEROTHER',2,4,false,'damage',1}
 };
 healPatterns={};
 for _,format in ipairs(formats) do
  local text=getglobal(format[1]);if type(text)=='string' then
   local pattern='';local i=1;local captures={};local nextField=0;
   while i<=string.len(text) do
    local tail=string.sub(text,i);
    local a,b,pos,typ=string.find(tail,'^%%(%d+)%$([sd])');
    if not a then a,b,typ=string.find(tail,'^%%([sd])');pos=nil;end
    if a then nextField=nextField+1;table.insert(captures,tonumber(pos) or nextField);pattern=pattern..(typ=='d' and '(%d+)' or '(.-)');i=i+b;
    else local ch=string.sub(text,i,i);pattern=pattern..string.gsub(ch,'([%^%$%(%)%%%.%[%]%*%+%-%?])','%%%1');i=i+1;end
   end
   table.insert(healPatterns,{pattern='^'..pattern..'$',fields=captures,spell=format[2],amount=format[3],crit=format[4],metric=format[5],actor=format[6]});
  end
 end
end
local log=CreateFrame('Frame');
for _,name in ipairs({'CHAT_MSG_SPELL_SELF_BUFF','CHAT_MSG_SPELL_PERIODIC_SELF_BUFFS','CHAT_MSG_SPELL_PARTY_BUFF','CHAT_MSG_SPELL_FRIENDLYPLAYER_BUFF','CHAT_MSG_SPELL_PET_BUFF','CHAT_MSG_SPELL_SELF_DAMAGE','CHAT_MSG_SPELL_PARTY_DAMAGE','CHAT_MSG_SPELL_FRIENDLYPLAYER_DAMAGE','CHAT_MSG_SPELL_PET_DAMAGE','VARIABLES_LOADED'}) do pcall(log.RegisterEvent,log,name);end
log:SetScript('OnEvent',function()
 if event=='VARIABLES_LOADED' then BuildHealPatterns();return;end
 if type(arg1)~='string' then return;end
 if AZS_GetMeterContext then local context=AZS_GetMeterContext();if not context then return;end;SyncContext(context);end
 if not healPatterns then BuildHealPatterns();end
 for _,format in ipairs(healPatterns) do
  local matches={string.find(arg1,format.pattern)};
  if matches[1] then
   local fields={};for i,index in ipairs(format.fields) do fields[index]=matches[i+2];end
   local owner=format.actor and M.knownActors and M.knownActors[fields[format.actor]] or (not format.actor and M.context and M.context.localOwnerId);
   local isLocal=owner and M.context and owner==M.context.localOwnerId;
   -- The native outgoing stream already covers our own pets and spells. Never count its
   -- matching damage log a second time. Other owners require a known, unambiguous actor.
   if owner and fields[format.spell] and not (format.metric=='damage' and isLocal and AZS_GetCombatTextEvents) then AddSample(format.metric,'name:'..Name(fields[format.spell]),tonumber(fields[format.amount]),format.crit,owner);end
   return;
  end
 end
end);
