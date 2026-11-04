-- Slim Suite: chat filters, HUD controls, and other optional player tools.
--   * Optional chat filters independently hide level-up stat-bonus lines, newly learned spell lines, racial-buff
--     notices, selected opening/lair lines, and game-authored
--     NPC/boss dialogue and run-roleplay lines. /azsmods hide <words> also hides matching non-player text. Player chat is never hidden.
--   * The camera goes as far out as the game allows, 50 yards (the Max Camera Distance setting at its top), and starts there. Turn it off in
--     /azsmods to keep the game's own setting.
--   * Boss markers follow the live API spawn order: oldest active boss gets Skull, then Cross, Circle and Star.
--     Awake Guardians join after the real bosses; ordinary enemies are never marked by name guesses.
--   * In co-op, a friend who falls gets a marker over their head, blinking on and off 16 times. Each friend has their
--     own (Square, Moon, Circle), so several can be down at once. Those three are kept for this alone.
--   * Play the included sound once when you or a teammate dies (off until enabled).
--   * Move, resize and scale the UI: drag a green handle to move it; right-click for width, height and scale.
--     The action bar, XP bar, buffs, cast bar, party frames and kills/gold/cache tally can switch between horizontal and vertical layouts.
--     Bar 2 and Right bar are intentionally not included; Action Bar 1 can optionally show empty slots.
--     Optional anchoring latches nearby movable frames together. Positions and custom sizes are saved continuously across /reload.
--     The resize menu and tooltip can also be moved while settings are active. Native frames stay in their game layout until a UI replacement or edit is chosen.
--     The primary chat window, tab and input box move together from one Chat handle.
--   * The kills, gold and next-cache cost use the documented AZS_RUN values in a movable replacement for the tally.
--     Right-click its green handle to switch between a compact horizontal row and a vertical three-line layout.
--   * The run clock and next-boss countdown stay together in one movable replacement for the game's clock.
--   * Event announcements use the game's localized text, remain visible for 10 seconds, fade at both ends, and move
--     with their own handle. They are separate from the "Move to Begin" prompt.
--   * The stored-levels button is a movable replacement that opens the game's own choices through its documented API.
--   * Active boss, guardian and elite bars sit in the upper-left encounter area, separate from the centered run
--     clock. Horizontal cards grow from the left; right-click switches to narrow vertical bars. HP uses the
--     documented current/maximum values, refreshed on AZSUNITS updates.
--   * Hide the target frame: this game picks its own targets, so the frame can go.
--   * Plain action bar: the gryphons and the bar's art go, and only the six spell squares are left, with the bar
--     no wider than they are (and the two page arrows), so it leaves no empty space.
--   * Cooldown numbers: each spell square shows the seconds until its spell is ready again.
-- /azsmods (or the gear by the minimap) opens the settings window: every one of these on or off.
-- Lua 5.0 / 1.12 interface: scripts read `this`, `event`, `arg1`, `arg2`; no # operator; only the 1.12 API.

local K = AZSKIT;
local NAME = "Slim Suite";

-- the stat bonus a level-up gives, in each of the game's languages (a piece of the line that has no number in it), as
-- the game itself writes "You have gained {damage}% damage, {health} maximum health and {regen} health per second."
local function DB()
	if ( type(AZSMinorModsDB) ~= "table" ) then AZSMinorModsDB = {}; end
	local d = AZSMinorModsDB;
	if ( d.hideLevel == nil ) then d.hideLevel = true; end
 if ( d.hideNewSpells == nil ) then d.hideNewSpells = d.hideLevel; end
	if ( d.hideRP == nil ) then d.hideRP = false; end
	if ( d.helloOnStart == nil ) then d.helloOnStart = true; end
	if ( d.hideRacialInfo == nil ) then d.hideRacialInfo = false; end
	if ( d.hideOpeningLine == nil ) then d.hideOpeningLine = false; end
	if ( d.hideLairLine == nil ) then d.hideLairLine = false; end
	if ( d.showGuardianFlag == nil ) then
		d.showGuardianFlag = true;
		if ( type(AZS_IsHudShown) == "function" ) then
			local ok, shown = pcall(AZS_IsHudShown, "flag");
			if ( ok and shown ~= nil ) then d.showGuardianFlag = shown and true or false; end
		end
	end
	for _,key in ipairs({'replaceClock','replaceEncounters','replaceTally','replaceBanners'}) do if d[key]==nil then d[key]=false;end end
	if ( d.camera == nil ) then d.camera = true; end
	if ( d.plainBar == nil ) then d.plainBar = false; end
	if ( d.cooldowns == nil ) then d.cooldowns = false; end
	d.box, d.bloat, d.x, d.y = nil, nil, nil, nil;          -- the retired Say & Party box and "Remove bloat"
	if ( d.bossMarks == nil ) then d.bossMarks = true; end
	if ( d.deathMarks == nil ) then d.deathMarks = true; end
	if ( d.deathSound == nil ) then d.deathSound = false; end
	if ( d.hideTarget == nil ) then d.hideTarget = false; end
	if ( d.moveLevelButton == nil ) then d.moveLevelButton = false; end
	if ( d.moveMeter == nil ) then d.moveMeter = false; end
	if ( d.showRacialAbility == nil ) then d.showRacialAbility = false; end
	if ( type(d.places) ~= "table" ) then d.places = {}; end
	if ( type(d.sizes) ~= "table" ) then d.sizes = {}; end
	if ( type(d.scales) ~= "table" ) then d.scales = {}; end
	if ( type(d.muted) ~= "table" ) then d.muted = {}; end
	if ( type(d.padding) ~= "table" ) then d.padding = {}; end
	if ( type(d.orientations) ~= "table" ) then d.orientations = {}; end
	if ( d.buffGrowthDirection ~= "up" and d.buffGrowthDirection ~= "down" ) then d.buffGrowthDirection = "down"; end
	if ( d.buffCustomLayout == nil ) then d.buffCustomLayout = d.orientations.BuffFrame == "vertical"; end
	if ( type(d.sizeMenuPosition) ~= "table" ) then d.sizeMenuPosition = {}; end
	if ( type(d.lockPanelPosition) ~= "table" ) then d.lockPanelPosition = {}; end
	if ( type(d.gridDivisions) ~= "number" ) then d.gridDivisions = tonumber(d.gridSpacing) or 128; end
	if ( d.gridDivisions ~= 32 and d.gridDivisions ~= 64 and d.gridDivisions ~= 128 and d.gridDivisions ~= 256 ) then d.gridDivisions = 128; end
	d.gridSpacing = nil;
	if ( d.showEditGrid == nil ) then d.showEditGrid = true; end
	if ( d.gridCenterVertical == nil ) then d.gridCenterVertical = true; end
	if ( d.gridCenterHorizontal == nil ) then d.gridCenterHorizontal = true; end
	if ( d.startUnlocked == nil ) then d.startUnlocked = false; end
	if ( d.tallyVertical == nil ) then d.tallyVertical = false; end
	if ( d.showEmptyActionSlots == nil ) then d.showEmptyActionSlots = false; end
	if ( d.buildRowsAttached == nil ) then d.buildRowsAttached = true; end
	if ( d.replaceBuildRows == nil ) then d.replaceBuildRows = false; end
	if ( type(d.itemRowEquipped) ~= "table" ) then d.itemRowEquipped = {}; end
	if ( type(d.itemRowSeen) ~= "table" ) then d.itemRowSeen = {}; end
	if ( type(d.itemRowCounter) ~= "number" ) then d.itemRowCounter = 0; end
	if ( type(d.chatOffsets) ~= "table" ) then d.chatOffsets = {}; end
	if ( d.anchorSnap == nil ) then d.anchorSnap = false; end
	if ( type(d.words) ~= "table" ) then d.words = {}; end
	-- Older versions enabled several replacement controls as defaults. This one-time migration
	-- returns those defaults to the native game UI while keeping saved positions and sizes intact.
	if ( (tonumber(d.uiDefaultsVersion) or 0) < 1 ) then
		d.replaceClock, d.replaceEncounters, d.replaceTally, d.replaceBanners = false, false, false, false;
		d.moveLevelButton, d.moveMeter, d.replaceBuildRows = false, false, false;
		d.showRacialAbility, d.cooldowns = false, false;
		d.uiDefaultsVersion = 1;
	end
	return d;
end

-- Keep chat-only helpers in their own lexical scope. The WoW 1.12 Lua runtime allows at
-- most 200 active locals in the addon chunk; the UI and mover code below needs that budget.
local HookChat;
do
local STAT_WORDS = { "maximum health and", "maximale gesundheit und", "de salud máxima y", "de vida máxima e",
                     "points de vie maximum et", "maksymalnego zdrowia i", "maximálního zdraví a", "до макс. здоров'я і",
                     "к макс. здоровью и", "、最大体力+", "最大生命值和每秒", ", 최대 생명력 ", " azami can ve saniyede ",
                     " viață maximă și " };
-- the game's "New spell" in its languages
local NEW_SPELL_WORDS = { "new spell", "neuer zauber", "hechizo nuevo", "novo feitiço", "nouveau sort", "nowe zaklęcie",
                          "nové kouzlo", "vrajă nouă", "yeni büyü", "нове закляття", "новое заклинание", "新しい呪文",
                          "新法术", "새 주문" };

-- the "you have learned a new spell" lines: the game's own texts in its language, the part
-- before the spell's name, and the English ways of saying it
local LEARN_WORDS = { "you have learned a new spell", "you have learned a new ability", "you have learned", "you learned",
                      "you learn " };

local function Prefix(text)
	if ( type(text) ~= "string" ) then return nil; end
	local s = string.find(text, "%", 1, true);
	local head = s and string.sub(text, 1, s - 1) or text;
	head = string.lower(string.gsub(head, "^%s+", ""));
	if ( string.len(head) < 6 ) then return nil; end
	return head;
end

local function LearnLine(low)
	for _, g in ipairs({ "ERR_LEARN_SPELL_S", "ERR_LEARN_ABILITY_S", "ERR_LEARN_RECIPE_S" }) do
		local own = Prefix(getglobal(g));
		if ( own and string.find(low, own, 1, true) ) then return true; end
	end
	for i = 1, table.getn(LEARN_WORDS) do
		if ( string.find(low, LEARN_WORDS[i], 1, true) ) then return true; end
	end
	return false;
end

-- ---------------------------------------------------------------- the level-up text out of the chat
local function Any(low, list)
	for i = 1, table.getn(list) do
		if ( string.find(low, list[i], 1, true) ) then return true; end
	end
	return false;
end

local CHAT_FILTER = { activeEvent = nil, allowPlayers = false };
local RACIAL_INFO_WORDS = { "racial buff", "racial bonus", "racial ability", "racial effect" };
local cachedRacialId, cachedRacialName;

local function RacialAbilityName()
	local spells = type(AZS_SPELLS) == "table" and AZS_SPELLS or {};
	local racial = spells[table.getn(spells)];
	local racialId = type(racial) == "table" and tonumber(racial.spell) or nil;
	if ( not racialId ) then cachedRacialId, cachedRacialName = nil, nil; return nil; end
	if ( cachedRacialId == racialId ) then return cachedRacialName; end
	cachedRacialId, cachedRacialName = racialId, nil;
	if ( not GetSpellBookItemInfo ) then return nil; end
	for slot = 1, 512 do
		local ok, kind, spellId = pcall(GetSpellBookItemInfo, slot, BOOKTYPE_SPELL or "spell");
		if ( not ok or not kind ) then break; end
		if ( kind == "SPELL" and tonumber(spellId) == racialId and type(GetSpellName) == "function" ) then
			local nameOk, value = pcall(GetSpellName, slot, BOOKTYPE_SPELL or "spell");
			if ( nameOk and type(value) == "string" ) then cachedRacialName = CHAT_FILTER.PlainText(value); end
			break;
		end
	end
	return cachedRacialName;
end

CHAT_FILTER.PlainText = function(msg)
	local text = string.lower(msg or "");
	text = string.gsub(text, "|c%x%x%x%x%x%x%x%x", "");
	text = string.gsub(text, "|r", "");
	text = string.gsub(text, "^%s+", "");
	text = string.gsub(text, "%s+$", "");
	return text;
end;

CHAT_FILTER.playerEvents = {
	CHAT_MSG_SAY=true, CHAT_MSG_YELL=true, CHAT_MSG_WHISPER=true, CHAT_MSG_PARTY=true,
	CHAT_MSG_PARTY_LEADER=true, CHAT_MSG_RAID=true, CHAT_MSG_RAID_LEADER=true,
	CHAT_MSG_RAID_WARNING=true, CHAT_MSG_GUILD=true, CHAT_MSG_OFFICER=true,
	CHAT_MSG_CHANNEL=true, CHAT_MSG_EMOTE=true, CHAT_MSG_TEXT_EMOTE=true,
	CHAT_MSG_BATTLEGROUND=true, CHAT_MSG_BATTLEGROUND_LEADER=true,
	CHAT_MSG_BN_WHISPER=true, CHAT_MSG_BN_CONVERSATION=true
};

CHAT_FILTER.IsRoleplay = function(msg, sourceEvent)
	if ( CHAT_FILTER.playerEvents[sourceEvent] ) then return false; end
	local text = CHAT_FILTER.PlainText(msg);
	if ( text == "" ) then return false; end
	-- The creator's banner API is the authoritative source for scripted run announcements.
	-- Match only actual banner text (or a banner phrase embedded in a chat line), never arbitrary NPC chatter.
	local banners = type(AZS_BANNERS) == "table" and AZS_BANNERS or {};
	for _, banner in ipairs(banners) do
		local roleplayKind = type(banner) == "table" and
			(banner.kind == "boss" or banner.kind == "finalboss" or banner.kind == "bossdown" or banner.kind == "guardian");
		if ( roleplayKind and type(banner.text) == "string" ) then
			local bannerText = CHAT_FILTER.PlainText(banner.text);
			if ( bannerText ~= "" and string.find(text, bannerText, 1, true) ) then return true; end
		end
	end
	-- This common opening announcement can precede its first AZS_BANNERS snapshot.
	if ( string.find(text, "the burning legion", 1, true) and string.find(text, "survive the onslaught", 1, true) ) then return true; end
	-- These events are game-authored dialogue or narration, not player messages.
	return sourceEvent == "CHAT_MSG_MONSTER_SAY" or sourceEvent == "CHAT_MSG_MONSTER_YELL" or
	       sourceEvent == "CHAT_MSG_MONSTER_EMOTE" or sourceEvent == "CHAT_MSG_RAID_BOSS_EMOTE" or
	       sourceEvent == "CHAT_MSG_RAID_BOSS_WHISPER";
end;

local function Hidden(msg, sourceEvent)
	if ( type(msg) ~= "string" ) then return false; end
	if ( CHAT_FILTER.allowPlayers or CHAT_FILTER.playerEvents[sourceEvent or CHAT_FILTER.activeEvent] ) then return false; end
	if ( string.find(msg, "|Hplayer:", 1, true) ) then return false; end   -- a player's own words
	local d = DB();
	local low = CHAT_FILTER.PlainText(msg);
	if ( d.hideRacialInfo ) then
		local racialInfo = Any(low, RACIAL_INFO_WORDS);
		-- The game's racial announcement usually uses the localized ability name, not the
		-- English words "racial buff". AZS_SPELLS documents the racial as its last entry.
		local racialName = not racialInfo and RacialAbilityName() or nil;
		if ( racialName and racialName ~= "" and string.find(low, racialName, 1, true) ) then racialInfo = true; end
		if ( racialInfo ) then return true; end
	end
	if ( d.hideOpeningLine and string.find(low, "oogax q the troll shaman enters warsong gulch", 1, true) ) then return true; end
	if ( d.hideLairLine and string.find(low, "who dares to enter my lair", 1, true) ) then return true; end
	if ( d.hideRP and CHAT_FILTER.IsRoleplay(msg, sourceEvent or CHAT_FILTER.activeEvent) ) then return true; end
	if ( d.hideLevel ) then
		-- Hide only the stat-bonus line. Level notices and spell-learning lines have separate controls.
		if ( Any(low, STAT_WORDS) or
		     (string.find(low, "you have gained", 1, true) and string.find(low, "% damage", 1, true) and
		      string.find(low, "health", 1, true)) ) then return true; end
	end
	if ( d.hideNewSpells and (Any(low, NEW_SPELL_WORDS) or LearnLine(low)) ) then return true; end
	for i = 1, table.getn(d.words) do
		if ( string.find(low, d.words[i], 1, true) ) then return true; end
	end
	return false;
end

-- every chat window's AddMessage (what the game and the addons print), and the chat's own event handler
local hooked = {};
local function HookFrame(cf)
	if ( not cf or hooked[cf] or not cf.AddMessage ) then return; end
	hooked[cf] = true;
	local orig = cf.AddMessage;
	cf.AddMessage = function(self, msg, r, g, b, id, hold)
		if ( Hidden(msg) ) then return; end
		return orig(self, msg, r, g, b, id, hold);
	end;
end

HookChat = function()
	for i = 1, 7 do pcall(HookFrame, getglobal("ChatFrame" .. i)); end
	pcall(HookFrame, DEFAULT_CHAT_FRAME);
	if ( type(ChatFrame_OnEvent) == "function" and not hooked.event ) then
		hooked.event = true;
		local orig = ChatFrame_OnEvent;
		ChatFrame_OnEvent = function(ev)
			local e = ev or event;
			if ( type(arg1) == "string" and e and string.find(e, "^CHAT_MSG_") and Hidden(arg1, e) ) then return; end
			local previousEvent, previousPlayerGuard = CHAT_FILTER.activeEvent, CHAT_FILTER.allowPlayers;
			CHAT_FILTER.activeEvent = e;
			CHAT_FILTER.allowPlayers = CHAT_FILTER.playerEvents[e] and true or false;
			local ok, result = pcall(orig, ev);
			CHAT_FILTER.activeEvent, CHAT_FILTER.allowPlayers = previousEvent, previousPlayerGuard;
			if ( not ok ) then error(result); end
			return result;
		end;
	end
end;
end -- scoped chat filter helpers

-- ---------------------------------------------------------------- the camera at its farthest: 50
-- The game's Max Camera Distance slider is cameraDistanceMaxFactor, a factor of cameraDistanceMax (15 yards): its
-- top, 3.33, is 50 yards. Minor Mods sets it to 50 yards when the game starts (and zooms out to it once); turned off
-- in /azsmods, the game's own setting stays.
local MAX_CAMERA = 50;

local function Camera(zoomOut)
	if ( not DB().camera or not SetCVar ) then return; end
	local base = 15;
	if ( GetCVar ) then
		local ok, v = pcall(GetCVar, "cameraDistanceMax");
		if ( ok and tonumber(v) and tonumber(v) > 0 ) then base = tonumber(v); end
	end
	pcall(SetCVar, "cameraDistanceMaxFactor", string.format("%.4f", MAX_CAMERA / base));
	if ( zoomOut and CameraZoomOut ) then pcall(CameraZoomOut, MAX_CAMERA); end
end

local function ApplyGuardianFlag()
	if ( type(AZS_SetHudShown) == "function" ) then
		pcall(AZS_SetHudShown, "flag", DB().showGuardianFlag and true or false);
	end
end

-- ---------------------------------------------------------------- raid markers on bosses and guardians, and on friends who fall
-- The API returns real bosses in spawn order. Awake Guardians follow that list, as requested by the shared
-- boss/Guardian marker rule. Rebuild the four kill markers from the current ordered API data on each update.
-- Boss markers: Skull (8), Cross (7), Circle (2), Star (1). Friendly death markers: Diamond (3),
-- Triangle (4), Moon (5), Square (6).
local BOSS_ICONS = { 8, 7, 2, 1 };
local DEATH_ICONS = { 3, 4, 5, 6 };
local DEATH_BLINKS = 16;                                 -- on and off this many times
local DEATH_SOUND = "Interface\\AddOns\\AZSMinorMods\\Baby Crying.mp3";
local function PlayDeathSound()
	if ( not DB().deathSound or not PlaySoundFile ) then return; end
	pcall(PlaySoundFile, DEATH_SOUND);
end

local deathSoundFrame = CreateFrame("Frame");
deathSoundFrame:RegisterEvent("PLAYER_DEAD");
deathSoundFrame:SetScript("OnEvent", function() PlayDeathSound(); end);

local marked = {};                                        -- boss/guardian id -> { icon, name }
local tries = {};                                         -- boss id -> target-and-back tries (at most 3)
local fallen = {};                                        -- party unit -> was down at the last look
local partyNames = {};                                    -- party unit -> identity, so a replacement in the same slot is new
local blinks = {};                                        -- party unit -> { left = times, at = next change, on = shown }

local function Units()
	local u = { "target", "mouseover", "targettarget" };
	for i = 1, 4 do table.insert(u, "party" .. i .. "target"); end
	return u;
end

local function Mark(unit, icon)
	if ( not SetRaidTarget or not UnitExists(unit) ) then return false; end
	if ( GetRaidTargetIndex and GetRaidTargetIndex(unit) == icon ) then return true; end
	return pcall(SetRaidTarget, unit, icon);
end

local function MarkBoss(name, icon, id, duplicateName)
	local list = Units();
	for i = 1, table.getn(list) do
		local u = list[i];
		if ( UnitExists(u) and UnitName(u) == name and not UnitIsPlayer(u) and not UnitIsDead(u) ) then
			-- A name alone cannot distinguish simultaneous same-name spawns. The API exposes
			-- the current target's spawn id, so use it for that token and never guess between twins.
			local targetMatches = not (u == "target" and type(AZS_TARGET) == "table" and
				AZS_TARGET.id ~= nil and AZS_TARGET.id ~= id);
			if ( targetMatches and (not duplicateName or (u == "target" and type(AZS_TARGET) == "table" and AZS_TARGET.id == id)) ) then
				return Mark(u, icon);
			end
		end
	end
	if ( duplicateName ) then return false; end
	-- not one of them: target it by its name, mark it, and give back the target there was
	if ( not TargetByName or (tries[id] or 0) >= 3 ) then return false; end
	tries[id] = (tries[id] or 0) + 1;
	local had = UnitExists("target") and UnitName("target") or nil;
	if ( not pcall(TargetByName, name, true) ) then return false; end
	local ok = false;
	if ( UnitExists("target") and UnitName("target") == name and
		(type(AZS_TARGET) ~= "table" or AZS_TARGET.id == nil or AZS_TARGET.id == id) ) then ok = Mark("target", icon); end
	if ( had and had ~= name ) then
		if ( TargetLastTarget ) then pcall(TargetLastTarget); end
	elseif ( not had and ClearTarget ) then
		pcall(ClearTarget);
	end
	return ok;
end

local function ClearBossMark(id, assignment, duplicateName)
	if ( type(assignment) ~= "table" or not assignment.icon or not SetRaidTarget ) then return; end
	local list = Units();
	for i = 1, table.getn(list) do
		local u = list[i];
		if ( UnitExists(u) and UnitName(u) == assignment.name and not UnitIsPlayer(u) and
			not UnitIsDead(u) and GetRaidTargetIndex and GetRaidTargetIndex(u) == assignment.icon ) then
			local targetMatches = not (u == "target" and type(AZS_TARGET) == "table" and
				AZS_TARGET.id ~= nil and AZS_TARGET.id ~= id);
			if ( targetMatches and (not duplicateName or (u == "target" and type(AZS_TARGET) == "table" and AZS_TARGET.id == id)) ) then
				pcall(SetRaidTarget, u, 0);
				return;
			end
		end
	end
	-- If it is uniquely named and not in the common unit tokens, target it briefly to clear only our old
	-- marker. Requiring the API spawn ID prevents clearing a same-name replacement by mistake.
	if ( duplicateName or not TargetByName or not UnitExists("target") and not ClearTarget ) then return; end
	local had = UnitExists("target") and UnitName("target") or nil;
	if ( not pcall(TargetByName, assignment.name, true) ) then return; end
	if ( UnitExists("target") and UnitName("target") == assignment.name and type(AZS_TARGET) == "table" and
		AZS_TARGET.id == id and GetRaidTargetIndex and GetRaidTargetIndex("target") == assignment.icon ) then
		pcall(SetRaidTarget, "target", 0);
	end
	if ( had and TargetLastTarget ) then pcall(TargetLastTarget);
	elseif ( not had and ClearTarget ) then pcall(ClearTarget); end
end

local function BossMarks()
	local d = DB();
	if ( not d.bossMarks ) then
		for id, assignment in pairs(marked) do ClearBossMark(id, assignment, assignment.duplicate); marked[id] = nil; end
		return;
	end
	-- only while a run is being played: out of it the list can still hold the last run's bosses
	if ( type(AZS_GetPhase) == "function" and AZS_GetPhase() ~= "playing" ) then return; end
	local active, alive, ordered = {}, {}, {};
	-- The creator guarantees AZS_GetBosses() is in spawn order. Keep all current real bosses
	-- ahead of guardians so the first living boss always owns Skull and the next owns Cross.
	-- Do not keep stale IDs from a prior list order: this is recomputed on each AZSUNITS update.
	local function AddInOrder(list, defaultKind, includeSleepingGuardians)
		if type(list) ~= "table" then return; end
		for i = 1, table.getn(list) do
			local unit = list[i];
			local kind = type(unit) == "table" and (unit.kind or (unit.final and "final") or defaultKind) or nil;
			if type(unit) == "table" and unit.id and unit.name and
				(kind ~= "guardian" or includeSleepingGuardians or unit.awake == true or unit.state == 1) then
					if not active[unit.id] then
						local item = { id = unit.id, name = unit.name, kind = kind or "boss" };
						active[unit.id] = item;
						table.insert(ordered, item);
					end
			end
		end
	end
	local ok, bosses = false, nil;
	if type(AZS_GetBosses) == "function" then ok, bosses = pcall(AZS_GetBosses); end
	if ok then AddInOrder(bosses, "boss", false); end
	local okGuardians, guardians = false, nil;
	if type(AZS_GetGuardians) == "function" then okGuardians, guardians = pcall(AZS_GetGuardians); end
	if okGuardians then AddInOrder(guardians, "guardian", false); end
	-- Keep compatibility with the raw stream when a client build has not exposed the getters yet.
	if table.getn(ordered) == 0 then
		local units = type(AZS_UNITS) == "table" and AZS_UNITS or {};
		for i = 1, table.getn(units) do
			local u = units[i];
			if type(u) == "table" and (u.kind == "final" or u.kind == "boss" or u.kind == "guardian") and
				u.id and u.name and (u.kind ~= "guardian" or u.state == 1) then
				AddInOrder({u}, u.kind, false);
			end
		end
	end
	for i = 1, table.getn(ordered) do alive[ordered[i].id] = true; end
	local nameCounts = {};
	for i = 1, table.getn(ordered) do nameCounts[ordered[i].name] = (nameCounts[ordered[i].name] or 0) + 1; end
	for id, assignment in pairs(marked) do
		local index;
		for i = 1, table.getn(ordered) do if ( ordered[i].id == id ) then index = i; break; end end
		local nextIcon = index and BOSS_ICONS[index] or nil;
		if ( not alive[id] or assignment.icon ~= nextIcon ) then
			local sameNameReplacement = false;
			for i = 1, table.getn(ordered) do
				if ( ordered[i].name == assignment.name and ordered[i].id ~= id ) then sameNameReplacement = true; break; end
			end
			ClearBossMark(id, assignment, assignment.duplicate or sameNameReplacement);
			marked[id] = nil;
		end
	end
	for i = 1, table.getn(ordered) do
		local b, icon = ordered[i], BOSS_ICONS[i];
		if ( icon and (not marked[b.id] or marked[b.id].icon ~= icon) ) then
			local duplicateName = nameCounts[b.name] > 1;
			if ( MarkBoss(b.name, icon, b.id, duplicateName) ) then marked[b.id] = { icon = icon, name = b.name, duplicate = duplicateName }; end
		end
	end
	for id in pairs(tries) do if ( not alive[id] ) then tries[id] = nil; end end
end

-- a friend down in co-op: their own marker over their head, on and off 16 times. The first three friends have their
-- own (Square, Moon, Circle); a fourth (a party of 5) takes whichever of the three is free, or waits for one.
local function DeathIcon(u, i)
	local used = {};
	for v, b in pairs(blinks) do if ( v ~= u and b.icon ) then used[b.icon] = true; end end
	if ( DEATH_ICONS[i] and not used[DEATH_ICONS[i]] ) then return DEATH_ICONS[i]; end
	for k = 1, table.getn(DEATH_ICONS) do if ( not used[DEATH_ICONS[k]] ) then return DEATH_ICONS[k]; end end
	return nil;
end

local function DeathMarks(now)
	local d = DB();
	for i = 1, 4 do
		local u = "party" .. i;
		if ( not UnitExists(u) ) then
			fallen[u] = nil; partyNames[u] = nil;
		else
			local name = UnitName(u);
			-- Wait for the unit's identity before counting a transition; this avoids double-alerts while the party frame loads.
			if ( name ) then
				if ( partyNames[u] ~= name ) then partyNames[u] = name; fallen[u] = false; end
				local connected = not UnitIsConnected or UnitIsConnected(u);
				local down = connected and (UnitIsDeadOrGhost and UnitIsDeadOrGhost(u) or UnitHealth(u) == 0) and true or false;
				if ( down and not fallen[u] ) then
					if ( d.deathSound ) then PlayDeathSound(); end
					if ( d.deathMarks ) then blinks[u] = { left = DEATH_BLINKS * 2, at = now, on = false, slot = i }; end
				end
				fallen[u] = down;
			end
		end
	end
	if ( not d.deathMarks ) then
		for u in pairs(blinks) do if ( UnitExists(u) ) then pcall(SetRaidTarget, u, 0); end; blinks[u] = nil; end
		return;
	end
	for u, b in pairs(blinks) do
		if ( not b.icon ) then b.icon = DeathIcon(u, b.slot); end
		if ( b.icon and now >= b.at ) then
			b.on = not b.on;
			if ( UnitExists(u) ) then pcall(SetRaidTarget, u, b.on and b.icon or 0); end
			b.left = b.left - 1;
			b.at = now + 0.4;
			if ( b.left <= 0 ) then
				if ( UnitExists(u) ) then pcall(SetRaidTarget, u, 0); end
				blinks[u] = nil;
			end
		end
	end
end

local markFrame = CreateFrame("Frame");
local nextBoss = 0;
markFrame:RegisterEvent("CHAT_MSG_ADDON");
markFrame:SetScript("OnEvent", function()
	if ( arg1 == "AZSUNITS" ) then
		nextBoss = GetTime() + 0.75;
		pcall(BossMarks);
	end
end);
markFrame:SetScript("OnUpdate", function()
	local now = GetTime();
	pcall(DeathMarks, now);
	if ( now >= nextBoss ) then
		nextBoss = now + 0.75;
		pcall(BossMarks);
	end
end);


-- ---------------------------------------------------------------- moving the UI, and the target frame
-- Unlocked, each of these frames gets a handle over it (its name on a green panel): drag the handle and the frame
-- follows. The current lock state and every moved position are retained in SavedVariables across /reload.
local MOVABLE = { { "PlayerFrame", "Player" }, { "TargetFrame", "Target" }, { "PartyMemberFrame1", "Party" },
                  { "MinimapCluster", "Minimap" }, { "BuffFrame", "Buffs" },
                  { "MainMenuBar", "Action bars" }, { "MainMenuExpBar", "XP bar" }, { "ChatFrame1", "Chat" },
                  { "AZSMinorModsMeterFrame", "Damage meter" },
                  { "AZSMinorModsStoredLevelsButton", "Level Up" },
                  { "AZSMinorModsClockFrame", "Run clock / Next boss" },
                  { "AZSMinorModsBossFrames", "Encounter bars" },
                  { "AZSMinorModsAnnouncementFrame", "Event announcements" },
                  { "AZSMinorModsSpellRowFrame", "Build spell row" },
                  { "AZSMinorModsItemRowFrame", "Worn rings / trinkets" },
                  { "AZSMinorModsTallyFrame", "Kills / Gold / Cache" }, { "GameTooltip", "Tooltip" } };
local handles = {};
local editOnly;
local function IsEditing(name)
 return DB().unlocked and (not editOnly or editOnly == name);
end
local baseSizes = {};
local basePoints = {};
local mutedBaseAlphas = {};
local mutedBaseMouse = {};
-- Forward declarations are required because placement and the update ticker are
-- defined before the implementations later in this file.
local VisualInsets;
local RootBounds, SetRootPosition, RootFactor;
local ApplyFeatureMute;
local ApplyMovableOrientation;
local CaptureActionBarLayout;
local sizeMenu, sizeMenuText, sizeMenuWidth, sizeMenuHeight, sizeMenuScale, sizeMenuScaleInput, sizeMenuPadding;
local editGrid, editGridLines = nil, {};
local sizeTarget;
local lockPanel;
local ShowEditGrid;
local SetAnchorSnap;
local xpBarDetached = false;
local buffButtonOriginalPoints = {};
local buffLayoutApplying = false;
local actionSlotsGridApplied = false;
local tallyFrame;
local tallyNativeWasShown;
local tallyNativeOverridden = false;
local meterFrame;
local meterNativeWasShown;
local meterNativeOverridden = false;
local UpdateMeter;
local clockFrame;
local clockNativeWasShown;
local clockNativeOverridden = false;
local levelButton;
local levelNativeWasShown;
local levelNativeOverridden = false;
local bossFrame;
local bossNativeWasShown;
local bossNativeOverridden = false;
local bannerNativeWasShown;
local bannerNativeOverridden = false;
local announcementFrame;
local announcementStarted;
local announcementDuration = 10;
local announcementFade = 0.6;
local lastBannerId;
local encounterRows = {};
local actionBarOriginal;
local itemRow;
local itemRowButtons = {};
local spellRow;
local spellRowButtons = {};
local buildNativeWasShown;
local buildNativeOverridden = false;

-- The item deck reads the five real 1.12 jewelry equipment slots. The inventory API supplies
-- the currently worn icon and tooltip. API slot IDs are 1-based; ammo is slot 0.
local ITEM_EQUIP_SLOTS = { 2, 11, 12, 13, 14 };
local function ItemTextureKey(texture)
	if ( type(texture) ~= "string" ) then return nil; end
	return string.gsub(texture, "^Interface.Icons.", "");
end

local function LayoutItemRow()
	if ( not itemRow ) then return; end
	local gap = 4;
	if ( not DB().sizes.AZSMinorModsItemRowFrame ) then
		-- Keep the four equipment slots visible when empty so the row can always be placed and sized.
		if ( DB().orientations.AZSMinorModsItemRowFrame == "vertical" ) then
			itemRow:SetWidth(36); itemRow:SetHeight(4 * 32 + 3 * gap);
		else itemRow:SetWidth(4 * 32 + 3 * gap); itemRow:SetHeight(36); end
	end
	local vertical = DB().orientations.AZSMinorModsItemRowFrame == "vertical";
	local width, height = itemRow:GetWidth() or 36, itemRow:GetHeight() or 36;
	local iconSize = vertical and math.max(12, math.min(width - 4, (height - gap * 3) / 4))
		or math.max(12, math.min(height - 4, (width - gap * 3) / 4));
	for i = 1, table.getn(itemRowButtons) do
		local button = itemRowButtons[i];
		button:ClearAllPoints();
		button:SetWidth(iconSize); button:SetHeight(iconSize);
		if ( vertical ) then button:SetPoint("TOP", itemRow, "TOP", 0, -((i - 1) * (iconSize + gap)));
		else button:SetPoint("LEFT", itemRow, "LEFT", (i - 1) * (iconSize + gap), 0); end
	end
end

local function LayoutSpellRow()
	if ( not spellRow ) then return; end
	local gap = 4;
	if ( not DB().sizes.AZSMinorModsSpellRowFrame ) then
		if ( DB().orientations.AZSMinorModsSpellRowFrame == "vertical" ) then
			spellRow:SetWidth(36); spellRow:SetHeight(6 * 32 + 5 * gap);
		else spellRow:SetWidth(6 * 32 + 5 * gap); spellRow:SetHeight(36); end
	end
	local vertical = DB().orientations.AZSMinorModsSpellRowFrame == "vertical";
	local width, height = spellRow:GetWidth() or 36, spellRow:GetHeight() or 36;
	local iconSize = vertical and math.max(12, math.min(width - 4, (height - gap * 5) / 6))
		or math.max(12, math.min(height - 4, (width - gap * 5) / 6));
	for i = 1, 6 do
		local button = spellRowButtons[i];
		if ( button ) then
			button:ClearAllPoints(); button:SetWidth(iconSize); button:SetHeight(iconSize);
			if ( vertical ) then button:SetPoint("TOP", spellRow, "TOP", 0, -((i - 1) * (iconSize + gap)));
			else button:SetPoint("LEFT", spellRow, "LEFT", (i - 1) * (iconSize + gap), 0); end
		end
	end
end

-- Default replacement rows mirror the native build cluster: six spells in the upper row,
-- worn jewelry directly below. The spell row uses the screenshot-based lower-left-center anchor.
function AZSMinorMods_BuildRowDefaultPlace(name)
	local x, y = UIParent:GetWidth() * 0.275, UIParent:GetHeight() * 0.35;
	if ( name == "AZSMinorModsItemRowFrame" ) then
		if ( DB().buildRowsAttached ) then
			return { relative = "AZSMinorModsSpellRowFrame", point = "TOPLEFT", relativePoint = "BOTTOMLEFT", dx = 0, dy = -4 };
		end
		return { x = x, y = y - 40, coordinateSpace = "uiParent" };
	end
	return { x = x, y = y, coordinateSpace = "uiParent" };
end

local function RefreshItemRow()
	if ( not itemRow ) then return; end
	local d = DB();
	local equipped, visible = {}, {};
	for i = 1, table.getn(ITEM_EQUIP_SLOTS) do
		local slot = ITEM_EQUIP_SLOTS[i];
		local texture = GetInventoryItemTexture and GetInventoryItemTexture("player", slot) or nil;
		local link = GetInventoryItemLink and GetInventoryItemLink("player", slot) or nil;
		if ( texture ) then
			local identity = link or tostring(texture);
			local previous = d.itemRowEquipped[slot];
			if ( type(previous) ~= "table" or previous.identity ~= identity ) then
				local order = d.itemRowSeen[identity];
				if ( type(order) ~= "number" ) then
					d.itemRowCounter = d.itemRowCounter + 1;
					order = d.itemRowCounter;
					d.itemRowSeen[identity] = order;
				end
				previous = { identity = identity, order = order };
				d.itemRowEquipped[slot] = previous;
			end
			equipped[slot] = { texture = texture, link = link, order = previous.order,
			                   knownName = AZSMinorModsItemCatalog and AZSMinorModsItemCatalog[ItemTextureKey(texture)] or nil };
			table.insert(visible, { slot = slot, item = equipped[slot] });
		else
			d.itemRowEquipped[slot] = nil;
		end
	end
	table.sort(visible, function(a, b) return a.item.order < b.item.order; end);
	for i = 1, table.getn(itemRowButtons) do
		local button = itemRowButtons[i];
		local entry = visible[i];
		if ( button ) then
			if ( entry ) then
				button.slot = entry.slot;
				button.itemTexture = entry.item.texture;
				button.itemName = entry.item.knownName;
				button.icon:SetTexture(entry.item.texture);
				button.icon:Show();
				button:Show();
			else
				button.slot, button.itemTexture, button.itemName = nil, nil, nil;
				button.icon:SetTexture(nil);
				-- The empty quickslot frame is intentional: it keeps the custom second row visible
				-- and movable before the first item is equipped.
				button:Show();
			end
		end
	end
	LayoutItemRow();
	if ( d.replaceBuildRows ) then itemRow:Show(); else itemRow:Hide(); end
	if ( IsEditing("AZSMinorModsItemRowFrame") and d.replaceBuildRows and handles.AZSMinorModsItemRowFrame ) then handles.AZSMinorModsItemRowFrame:Show(); end
end

local function CreateItemRow()
	if ( itemRow or not UIParent ) then return; end
	itemRow = CreateFrame("Frame", "AZSMinorModsItemRowFrame", UIParent);
	itemRow:SetWidth(140); itemRow:SetHeight(36);
	local d = DB();
	local oldItemPlace = d.places.AZSMinorModsItemRowFrame;
	if ( d.buildRowsAttached and type(oldItemPlace) == "table" and oldItemPlace.relative == "MainMenuBar"
		and K.Num(oldItemPlace.dx) == 0 and K.Num(oldItemPlace.dy) == -4 ) then
		oldItemPlace.relative = "AZSMinorModsSpellRowFrame";
	end
	if ( not d.places.AZSMinorModsItemRowFrame ) then
		d.places.AZSMinorModsItemRowFrame = AZSMinorMods_BuildRowDefaultPlace("AZSMinorModsItemRowFrame");
	end
	itemRow:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 8, -130);
	itemRow:SetFrameStrata("MEDIUM");
	for i = 1, 4 do
		local button = CreateFrame("Button", "AZSMinorModsItemButton" .. i, itemRow);
		button:SetWidth(32); button:SetHeight(32);
		button:SetNormalTexture("Interface\\Buttons\\UI-Quickslot2");
		button:SetHighlightTexture("Interface\\Buttons\\ButtonHilight-Square");
		button.icon = button:CreateTexture(nil, "ARTWORK");
		button.icon:SetPoint("TOPLEFT", button, "TOPLEFT", 3, -3);
		button.icon:SetPoint("BOTTOMRIGHT", button, "BOTTOMRIGHT", -3, 3);
		button.icon:SetTexCoord(0.07, 0.93, 0.07, 0.93);
		button:EnableMouse(true);
		button:SetScript("OnEnter", function()
			if ( not this.slot or not GameTooltip ) then return; end
			GameTooltip:SetOwner(this, "ANCHOR_RIGHT");
			if ( GameTooltip.SetInventoryItem ) then GameTooltip:SetInventoryItem("player", this.slot);
			elseif ( this.itemName ) then GameTooltip:AddLine(this.itemName); GameTooltip:Show(); end
		end);
		button:SetScript("OnLeave", function() if ( GameTooltip ) then GameTooltip:Hide(); end end);
		itemRowButtons[i] = button;
	end
	RefreshItemRow();
end
CreateItemRow();

local function SpellTextureById(spellId)
	if ( type(spellId) ~= "number" or not GetSpellBookItemInfo or not GetSpellTexture ) then return nil; end
	-- AZS_SPELLS supplies canonical spell IDs. Resolve those against the real spellbook; never
	-- substitute an icon based only on a localized name or a guessed spell key.
	for i = 1, 512 do
		local kind, id = GetSpellBookItemInfo(i, BOOKTYPE_SPELL or "spell");
		if ( not kind ) then break; end
		if ( kind == "SPELL" and tonumber(id) == spellId ) then
			local ok, texture = pcall(GetSpellTexture, i, BOOKTYPE_SPELL or "spell");
			if ( ok and texture ) then return texture, i; end
		end
	end
	return nil;
end

local function SourceHasSpellId(source, spellId)
	if ( type(source) ~= "table" or type(source.spellIds) ~= "table" or not spellId ) then return false; end
	for _, id in ipairs(source.spellIds) do if ( tonumber(id) == tonumber(spellId) ) then return true; end end
	return false;
end

local function RefreshSpellRow()
	if ( not spellRow ) then return; end
	local spells = type(AZS_SPELLS) == "table" and AZS_SPELLS or {};
	local learnedCount = math.max(0, table.getn(spells) - 1); -- racial spell is last
	-- The live loadout API carries exact selected ability icons, including talent-granted spells
	-- whose icons are not resolvable from the legacy spellbook scan.
	local loadout, loadoutContext;
	if ( type(AZS_GetMeterContext) == "function" and type(AZS_GetMeterLoadout) == "function" ) then
		local okContext; okContext, loadoutContext = pcall(AZS_GetMeterContext);
		if ( okContext and type(loadoutContext) == "table" and loadoutContext.runId ) then
			local okLoadout, result = pcall(AZS_GetMeterLoadout, { apiVersion = 1, runId = loadoutContext.runId });
			if ( okLoadout and type(result) == "table" ) then loadout = result; end
		end
	end
	local loadoutIcons = {};
	if ( loadout and type(loadout.abilities) == "table" ) then
		for _, ability in ipairs(loadout.abilities) do
			if ( type(ability) == "table" and ability.learned ~= false and ability.selected ~= false and
				type(ability.sourceKey) == "string" ) then
				local source = type(loadout.sources) == "table" and loadout.sources[ability.sourceKey] or nil;
				local icon = type(source) == "table" and source.icon or nil;
				local iconPath = type(icon) == "table" and icon.status == "ready" and icon.path or nil;
				if ( iconPath ) then
					-- Match the API descriptor to AZS_SPELLS by its stable ability key or exact
					-- spell ID. Do not infer slot numbering from undocumented slotId conventions.
					for i = 1, learnedCount do
						local spell = spells[i];
						if ( type(spell) == "table" and
							((source.abilityKey and spell.key == source.abilityKey) or SourceHasSpellId(source, spell.spell)) ) then
							if ( not loadoutIcons[i] or (source.abilityKey and spell.key == source.abilityKey) ) then
								loadoutIcons[i] = { path = iconPath, name = source.name };
							end
						end
					end
				end
			end
		end
	end
	for i = 1, 6 do
		local button = spellRowButtons[i];
		local spell = spells[i];
		local loadoutIcon = loadoutIcons[i];
		local iconLoaded = false;
		button.sourceName = loadoutIcon and loadoutIcon.name or nil;
		local texture, spellBookSlot;
		if ( i <= learnedCount and type(spell) == "table" ) then
			texture, spellBookSlot = SpellTextureById(tonumber(spell.spell));
		end
		button.spellBookSlot = spellBookSlot;
		if ( loadoutIcon ) then
			button.spell = spell;
			button.icon:SetTexture(loadoutIcon.path); button.icon:Show(); iconLoaded = true;
		end
		if ( not iconLoaded and i <= learnedCount and type(spell) == "table" ) then
			button.spell, button.spellBookSlot = spell, spellBookSlot;
			if ( texture ) then button.icon:SetTexture(texture); button.icon:Show(); iconLoaded = true; else button.icon:SetTexture(nil); button.icon:Hide(); end
		else
			if ( not iconLoaded ) then
				button.spell, button.spellBookSlot = nil, nil;
				button.icon:SetTexture(nil); button.icon:Hide();
			end
		end
		button:Show();
	end
	LayoutSpellRow();
	if ( DB().replaceBuildRows ) then spellRow:Show(); else spellRow:Hide(); end
	if ( IsEditing("AZSMinorModsSpellRowFrame") and DB().replaceBuildRows and handles.AZSMinorModsSpellRowFrame ) then handles.AZSMinorModsSpellRowFrame:Show(); end
end

local function CreateSpellRow()
	if ( spellRow or not UIParent ) then return; end
	spellRow = CreateFrame("Frame", "AZSMinorModsSpellRowFrame", UIParent);
	spellRow:SetWidth(212); spellRow:SetHeight(36); spellRow:SetFrameStrata("MEDIUM");
	local d = DB();
	local oldSpellPlace = d.places.AZSMinorModsSpellRowFrame;
	if ( type(oldSpellPlace) == "table" and oldSpellPlace.relative == "MainMenuBar"
		and oldSpellPlace.point == "TOPLEFT" and oldSpellPlace.relativePoint == "BOTTOMLEFT"
		and K.Num(oldSpellPlace.dx) == 0 and K.Num(oldSpellPlace.dy) == -4 ) then
		oldSpellPlace.relative, oldSpellPlace.point, oldSpellPlace.relativePoint = nil, nil, nil;
		oldSpellPlace.dx, oldSpellPlace.dy = nil, nil;
		oldSpellPlace.x, oldSpellPlace.y, oldSpellPlace.coordinateSpace = UIParent:GetWidth() * 0.275, UIParent:GetHeight() * 0.35, "uiParent";
	end
	if ( not d.places.AZSMinorModsSpellRowFrame ) then
		d.places.AZSMinorModsSpellRowFrame = AZSMinorMods_BuildRowDefaultPlace("AZSMinorModsSpellRowFrame");
	end
	spellRow:SetPoint("BOTTOMLEFT", UIParent, "BOTTOMLEFT", 8, 170);
	for i = 1, 6 do
		local button = CreateFrame("Button", "AZSMinorModsSpellButton" .. i, spellRow);
		button:SetID(i); button:SetWidth(32); button:SetHeight(32);
		button:SetNormalTexture("Interface\\Buttons\\UI-Quickslot2");
		button:SetHighlightTexture("Interface\\Buttons\\ButtonHilight-Square");
		button.icon = button:CreateTexture(nil, "ARTWORK");
		button.icon:SetPoint("TOPLEFT", button, "TOPLEFT", 3, -3);
		button.icon:SetPoint("BOTTOMRIGHT", button, "BOTTOMRIGHT", -3, 3);
		button.icon:SetTexCoord(0.07, 0.93, 0.07, 0.93);
		button:EnableMouse(true);
		button:SetScript("OnEnter", function()
			if ( not this.spell or not GameTooltip ) then return; end
			GameTooltip:SetOwner(this, "ANCHOR_RIGHT");
			if ( this.spellBookSlot and GameTooltip.SetSpellBookItem ) then GameTooltip:SetSpellBookItem(this.spellBookSlot, BOOKTYPE_SPELL or "spell");
			elseif ( this.spell.key or this.sourceName ) then
				GameTooltip:AddLine(tostring(this.sourceName or this.spell.key));
				if ( this.spell.rank ) then GameTooltip:AddLine("Rank " .. tostring(this.spell.rank), 1, 0.82, 0); end
				if ( this.spell.spell ) then GameTooltip:AddLine("Spell ID: " .. tostring(this.spell.spell), 0.7, 0.7, 0.7); end
				if ( type(this.spell.cdmax) == "number" and this.spell.cdmax > 0 ) then GameTooltip:AddLine("Cooldown: " .. tostring(this.spell.cdmax) .. " sec", 0.7, 0.7, 0.7); end
				GameTooltip:Show();
			end
		end);
		button:SetScript("OnLeave", function() if ( GameTooltip ) then GameTooltip:Hide(); end end);
		spellRowButtons[i] = button;
	end
	RefreshSpellRow();
end
CreateSpellRow();

local function SetNativeBuildHidden(on)
	if ( type(AZS_SetHudShown) ~= "function" ) then return; end
	if ( on ) then
		local shown = true;
		if ( type(AZS_IsHudShown) == "function" ) then
			local ok, value = pcall(AZS_IsHudShown, "build");
			if ( ok and value ~= nil ) then
				if ( type(value) == "number" ) then shown = value ~= 0;
				elseif ( type(value) == "string" ) then shown = value ~= "0" and string.lower(value) ~= "false";
				else shown = value and true or false; end
			end
		end
		buildNativeWasShown = shown;
		if ( shown ) then
			DB().buildNativeOriginal = shown;
			local ok = pcall(AZS_SetHudShown, "build", false);
			buildNativeOverridden = ok and true or false;
		end
	elseif ( buildNativeOverridden or type(DB().buildNativeOriginal) == "boolean" ) then
		local restore = DB().buildNativeOriginal;
		if ( type(restore) ~= "boolean" ) then restore = buildNativeWasShown; end
		local ok = pcall(AZS_SetHudShown, "build", restore);
		if ( ok ) then buildNativeOverridden = false; DB().buildNativeOriginal = nil; end
	end
end

local function ApplyBuildRowReplacement()
	local enabled = DB().replaceBuildRows;
	SetNativeBuildHidden(enabled);
	if ( spellRow ) then if ( enabled ) then spellRow:Show(); else spellRow:Hide(); end end
	if ( itemRow ) then if ( enabled ) then itemRow:Show(); else itemRow:Hide(); end end
end
ApplyBuildRowReplacement();

local itemRowEvents = CreateFrame("Frame");
itemRowEvents:RegisterEvent("UNIT_INVENTORY_CHANGED");
itemRowEvents:RegisterEvent("PLAYER_ENTERING_WORLD");
itemRowEvents:SetScript("OnEvent", function()
	if ( event == "UNIT_INVENTORY_CHANGED" and arg1 ~= "player" ) then return; end
	pcall(RefreshItemRow);
end);
-- Some game gear changes are applied by the run rather than a bag click. Poll the four exposed
-- equipment slots as a fallback so the item deck updates even if that transition omits the event.
local itemRowPoll = 0;
itemRowEvents:SetScript("OnUpdate", function()
	itemRowPoll = itemRowPoll + (arg1 or 0);
	if ( itemRowPoll >= 0.5 ) then itemRowPoll = 0; pcall(RefreshItemRow); pcall(RefreshSpellRow); pcall(ApplyBuildRowReplacement); end
end);

local spellRowEvents = CreateFrame("Frame");
spellRowEvents:RegisterEvent("PLAYER_ENTERING_WORLD");
spellRowEvents:RegisterEvent("ACTIONBAR_SLOT_CHANGED");
spellRowEvents:RegisterEvent("ACTIONBAR_PAGE_CHANGED");
spellRowEvents:RegisterEvent("CHAT_MSG_ADDON");
spellRowEvents:SetScript("OnEvent", function()
	if ( event == "CHAT_MSG_ADDON" and arg1 ~= "AZSSPELLS" ) then return; end
	pcall(RefreshSpellRow);
	pcall(ApplyBuildRowReplacement);
end);

local function SetNativeTallyHidden(on)
	if ( type(AZS_SetHudShown) ~= "function" or tallyNativeWasShown ~= true ) then return; end
	if ( on ) then
		local shown = true;
		if ( type(AZS_IsHudShown) == "function" ) then
			local ok, value = pcall(AZS_IsHudShown, "tally");
			if ( ok and value ~= nil ) then shown = value and true or false; end
		end
		if ( shown ) then
			DB().tallyNativeOriginal = tallyNativeWasShown;
			pcall(AZS_SetHudShown, "tally", false);
			tallyNativeOverridden = true;
		end
	elseif ( tallyNativeOverridden ) then
		pcall(AZS_SetHudShown, "tally", tallyNativeWasShown);
		tallyNativeOverridden = false;
		DB().tallyNativeOriginal = nil;
	end
end

local function TallyValue(value)
	if ( type(value) ~= "number" ) then return "-"; end
	return tostring(math.floor(value));
end

local function UpdateTally()
 if not DB().replaceTally then if tallyFrame then tallyFrame:Hide();end;SetNativeTallyHidden(false);return;end
	if ( not tallyFrame ) then return; end
	local phase = type(AZS_GetPhase) == "function" and AZS_GetPhase() or nil;
	local active = phase and phase ~= "ready";
	local run = type(AZS_RUN) == "table" and AZS_RUN or nil;
	if ( active and run ) then
		local kills, gold, cache = TallyValue(run.kills), TallyValue(run.gold), TallyValue(run.chest);
		if ( DB().tallyVertical ) then
			tallyFrame.text:SetText("Kills  " .. kills .. "\nGold  " .. gold .. "\nCache  " .. cache);
		else
			tallyFrame.text:SetText("Kills " .. kills .. "    Gold " .. gold .. "    Cache " .. cache);
		end
		tallyFrame:Show();
		SetNativeTallyHidden(true);
	elseif ( IsEditing("AZSMinorModsTallyFrame") ) then
		if ( DB().tallyVertical ) then tallyFrame.text:SetText("Kills  -\nGold  -\nCache  -");
		else tallyFrame.text:SetText("Kills -    Gold -    Cache -"); end
		tallyFrame:Show();
		SetNativeTallyHidden(false);
	else
		tallyFrame:Hide();
		SetNativeTallyHidden(false);
	end
end

local function CreateTallyFrame()
	tallyFrame = CreateFrame("Frame", "AZSMinorModsTallyFrame", UIParent);
	tallyFrame:SetWidth(252);
	tallyFrame:SetHeight(28);
	tallyFrame:SetPoint("BOTTOMLEFT", UIParent, "BOTTOMLEFT", 12, 24);
	tallyFrame:SetFrameStrata("HIGH");
	tallyFrame:EnableMouse(false);
	tallyFrame.text = tallyFrame:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall");
	tallyFrame.text:SetPoint("LEFT", tallyFrame, "LEFT", 4, 0);
	tallyFrame.text:SetPoint("RIGHT", tallyFrame, "RIGHT", -4, 0);
	tallyFrame.text:SetJustifyH("LEFT");
	local savedOriginal = DB().tallyNativeOriginal;
	if ( savedOriginal ~= nil ) then
		tallyNativeWasShown = savedOriginal and true or false;
		if ( type(AZS_SetHudShown) == "function" ) then pcall(AZS_SetHudShown, "tally", tallyNativeWasShown); end
		DB().tallyNativeOriginal = nil;
	else
		local shown;
		if ( type(AZS_IsHudShown) == "function" ) then
			local ok, value = pcall(AZS_IsHudShown, "tally");
			if ( ok and value ~= nil ) then shown = value and true or false; end
		end
		if ( shown == nil and GetCVar ) then shown = GetCVar("hudTally") ~= "0"; end
		if ( shown == nil ) then shown = true; end
		tallyNativeWasShown = shown;
	end
	tallyFrame:Hide();
end

CreateTallyFrame();

local function SetNativeMeterHidden(on)
	if ( type(AZS_SetHudShown) ~= "function" ) then return; end
	if ( on ) then
		local shown = true;
		if ( type(AZS_IsHudShown) == "function" ) then
			local ok, value = pcall(AZS_IsHudShown, "meter");
			if ( ok and value ~= nil ) then shown = value and true or false; end
		end
		if ( shown ) then
			DB().meterNativeOriginal = shown;
			local ok = pcall(AZS_SetHudShown, "meter", false);
			meterNativeOverridden = ok and true or false;
		end
	elseif ( meterNativeOverridden ) then
		local restore = DB().meterNativeOriginal;
		if ( type(restore) ~= "boolean" ) then restore = meterNativeWasShown; end
		local ok = pcall(AZS_SetHudShown, "meter", restore);
		if ( ok ) then meterNativeOverridden = false; DB().meterNativeOriginal = nil; end
	end
end

local function MeterNumber(value)
	if ( type(value) ~= "number" ) then return "0"; end
	if ( value >= 1000000 ) then return string.format("%.1fm", value / 1000000); end
	if ( value >= 10000 ) then return string.format("%.1fk", value / 1000); end
	return tostring(math.floor(value + 0.5));
end

local function CreateMeterFrame()
	meterFrame = CreateFrame("Frame", "AZSMinorModsMeterFrame", UIParent);
	meterFrame:SetWidth(250); meterFrame:SetHeight(180);
	meterFrame:SetPoint("BOTTOMRIGHT", UIParent, "BOTTOMRIGHT", -8, 44);
	meterFrame:SetFrameStrata("HIGH");
	meterFrame:SetBackdrop({ bgFile = "Interface\\Tooltips\\UI-Tooltip-Background", edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
	                         tile = true, tileSize = 16, edgeSize = 12, insets = { left = 3, right = 3, top = 3, bottom = 3 } });
	meterFrame:SetBackdropColor(0.025, 0.02, 0.035, 0.88);
	meterFrame:SetBackdropBorderColor(0.7, 0.52, 0.2, 0.9);
	meterFrame:EnableMouse(false);
	AZSMinorModsMeter.Attach(meterFrame);
	local savedOriginal = DB().meterNativeOriginal;
	if ( savedOriginal ~= nil ) then
		meterNativeWasShown = savedOriginal and true or false;
		if ( type(AZS_SetHudShown) == "function" ) then pcall(AZS_SetHudShown, "meter", meterNativeWasShown); end
		DB().meterNativeOriginal = nil;
	else
		local shown;
		if ( type(AZS_IsHudShown) == "function" ) then
			local ok, value = pcall(AZS_IsHudShown, "meter");
			if ( ok and value ~= nil ) then shown = value and true or false; end
		end
		if ( shown == nil and GetCVar ) then shown = GetCVar("hudMeter") ~= "0"; end
		if ( shown == nil ) then shown = true; end
		meterNativeWasShown = shown;
	end
	meterFrame:Hide();
end

CreateMeterFrame();

UpdateMeter = function()
	if ( not meterFrame ) then return; end
	local phase = type(AZS_GetPhase) == "function" and AZS_GetPhase() or nil;
	local active = phase and phase ~= "ready";
	local editing = IsEditing("AZSMinorModsMeterFrame") and DB().moveMeter;
	if ( not DB().moveMeter ) then
		meterFrame:Hide(); SetNativeMeterHidden(false); return;
	end
	if ( not active and not editing ) then
		meterFrame:Hide(); SetNativeMeterHidden(false); return;
	end
	SetNativeMeterHidden(true);
	meterFrame:Show();
	AZSMinorModsMeter.Draw(active, editing);
end

local meterPoll = CreateFrame("Frame");
meterPoll:SetScript("OnUpdate", function()
	this.elapsed = (this.elapsed or 0) + (arg1 or 0);
	if ( this.elapsed >= 0.35 ) then this.elapsed = 0; pcall(UpdateMeter); end
end);

local function SetNativeClockHidden(on)
	if ( type(AZS_SetHudShown) ~= "function" or clockNativeWasShown ~= true ) then return; end
	if ( on ) then
		local shown = true;
		if ( type(AZS_IsHudShown) == "function" ) then
			local ok, value = pcall(AZS_IsHudShown, "clock");
			if ( ok and value ~= nil ) then shown = value and true or false; end
		end
		if ( shown ) then
			DB().clockNativeOriginal = clockNativeWasShown;
			pcall(AZS_SetHudShown, "clock", false);
			clockNativeOverridden = true;
		end
	elseif ( clockNativeOverridden ) then
		pcall(AZS_SetHudShown, "clock", clockNativeWasShown);
		clockNativeOverridden = false;
		DB().clockNativeOriginal = nil;
	end
end

local function SetNativeBossesHidden(on)
	if ( type(AZS_SetHudShown) ~= "function" or bossNativeWasShown ~= true ) then return; end
	if ( on ) then
		local shown = true;
		if ( type(AZS_IsHudShown) == "function" ) then
			local ok, value = pcall(AZS_IsHudShown, "bosses");
			if ( ok and value ~= nil ) then shown = value and true or false; end
		end
		if ( shown ) then
			DB().bossNativeOriginal = bossNativeWasShown;
			pcall(AZS_SetHudShown, "bosses", false);
			bossNativeOverridden = true;
		end
	elseif ( bossNativeOverridden ) then
		pcall(AZS_SetHudShown, "bosses", bossNativeWasShown);
		bossNativeOverridden = false;
		DB().bossNativeOriginal = nil;
	end
end

local function SetNativeBannersHidden(on)
	if ( type(AZS_SetHudShown) ~= "function" or bannerNativeWasShown ~= true ) then return; end
	if ( on ) then
		local shown = true;
		if ( type(AZS_IsHudShown) == "function" ) then
			local ok, value = pcall(AZS_IsHudShown, "banners");
			if ( ok and value ~= nil ) then shown = value and true or false; end
		end
		if ( shown ) then
			DB().bannerNativeOriginal = bannerNativeWasShown;
			pcall(AZS_SetHudShown, "banners", false);
			bannerNativeOverridden = true;
		end
	elseif ( bannerNativeOverridden ) then
		pcall(AZS_SetHudShown, "banners", bannerNativeWasShown);
		bannerNativeOverridden = false;
		DB().bannerNativeOriginal = nil;
	end
end

local function SetNativeLevelButtonHidden(on)
	if ( type(AZS_SetHudShown) ~= "function" or levelNativeWasShown ~= true ) then return; end
	if ( on ) then
		local shown = true;
		if ( type(AZS_IsHudShown) == "function" ) then
			local ok, value = pcall(AZS_IsHudShown, "levels");
			if ( ok and value ~= nil ) then shown = value and true or false; end
		end
		if ( shown ) then
			DB().levelNativeOriginal = levelNativeWasShown;
			pcall(AZS_SetHudShown, "levels", false);
			levelNativeOverridden = true;
		end
	elseif ( levelNativeOverridden ) then
		pcall(AZS_SetHudShown, "levels", levelNativeWasShown);
		levelNativeOverridden = false;
		DB().levelNativeOriginal = nil;
	end
end

local function UpdateLevelButton()
	if ( not levelButton ) then return; end
	local canReplace = type(AZS_OpenStoredLevels) == "function";
	local replacing = DB().moveLevelButton and canReplace;
	SetNativeLevelButtonHidden(replacing);
	if ( not replacing ) then levelButton:Hide(); return; end
	local count = 0;
	if ( type(AZS_GetStoredLevels) == "function" ) then
		local ok, value = pcall(AZS_GetStoredLevels);
		if ( ok and type(value) == "number" ) then count = math.max(0, math.floor(value)); end
	end
	local level = type(UnitLevel) == "function" and UnitLevel("player") or nil;
	levelButton.level:SetText("LEVEL " .. (level and tostring(level) or ""));
	levelButton.count:SetText(count > 0 and tostring(count) or "");
	-- The game's stored-level count is the availability signal. Do not preview this button before a point is spendable.
	if ( count > 0 ) then levelButton:Show(); else levelButton:Hide(); end
end

-- AZS_SetHudShown stores native HUD visibility in the game's saved interface settings. Restore
-- every value this addon temporarily changed before a clean logout/reload, so disabling Slim Suite
-- in the AddOns screen cannot leave the game's own controls hidden on the next launch. The saved
-- originals are also kept until this point so a reload can restore them before this addon hides
-- the native controls again.
local function RestoreNativeHudOnLogout()
	if ( type(AZS_SetHudShown) ~= "function" ) then return; end
	local d = DB();
	local restore = {
		{ "build", "buildNativeOriginal" },
		{ "tally", "tallyNativeOriginal" },
		{ "meter", "meterNativeOriginal" },
		{ "clock", "clockNativeOriginal" },
		{ "bosses", "bossNativeOriginal" },
		{ "banners", "bannerNativeOriginal" },
		{ "levels", "levelNativeOriginal" }
	};
	for i = 1, table.getn(restore) do
		local part, key = restore[i][1], restore[i][2];
		if ( type(d[key]) == "boolean" ) then
			local ok = pcall(AZS_SetHudShown, part, d[key]);
			if ( ok ) then d[key] = nil; end
		end
	end
end

local nativeHudShutdown = CreateFrame("Frame");
nativeHudShutdown:RegisterEvent("PLAYER_LOGOUT");
nativeHudShutdown:SetScript("OnEvent", RestoreNativeHudOnLogout);

-- The native Level Up control uses the class symbol from WoW's character-create atlas.
-- Keep this 1.12 mapping local because CLASS_ICON_TCOORDS is not consistently global in
-- Vanilla clients/addon environments.
local LEVEL_CLASS_ICON_COORDS = {
	WARRIOR = { 0, 0.25, 0, 0.25 },
	MAGE = { 0.25, 0.5, 0, 0.25 },
	ROGUE = { 0.5, 0.75, 0, 0.25 },
	DRUID = { 0.75, 1, 0, 0.25 },
	HUNTER = { 0, 0.25, 0.25, 0.5 },
	SHAMAN = { 0.25, 0.5, 0.25, 0.5 },
	PRIEST = { 0.5, 0.75, 0.25, 0.5 },
	WARLOCK = { 0.75, 1, 0.25, 0.5 },
	PALADIN = { 0, 0.25, 0.5, 0.75 }
};

local function PlayerClassIconCoords()
	if ( type(UnitClass) ~= "function" ) then return nil; end
	local localized, english = UnitClass("player");
	local key = string.upper(tostring(english or localized or ""));
	return LEVEL_CLASS_ICON_COORDS[key];
end

local function CreateLevelButton()
	local savedOriginal = DB().levelNativeOriginal;
	if ( savedOriginal ~= nil ) then
		levelNativeWasShown = savedOriginal and true or false;
		if ( type(AZS_SetHudShown) == "function" ) then pcall(AZS_SetHudShown, "levels", levelNativeWasShown); end
		DB().levelNativeOriginal = nil;
	else
		local shown;
		if ( type(AZS_IsHudShown) == "function" ) then
			local ok, value = pcall(AZS_IsHudShown, "levels");
			if ( ok and value ~= nil ) then shown = value and true or false; end
		end
		if ( shown == nil and GetCVar ) then shown = GetCVar("hudLevels") ~= "0"; end
		if ( shown == nil ) then shown = true; end
		levelNativeWasShown = shown;
	end
	levelButton = CreateFrame("Button", "AZSMinorModsStoredLevelsButton", UIParent);
	levelButton:SetWidth(184);
	levelButton:SetHeight(58);
	levelButton:SetFrameStrata("MEDIUM");
	levelButton:SetBackdrop({ bgFile = "Interface\\Tooltips\\UI-Tooltip-Background", edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
	                         tile = true, tileSize = 16, edgeSize = 12, insets = { left = 3, right = 3, top = 3, bottom = 3 } });
	levelButton:SetBackdropColor(0.13, 0.055, 0.025, 0.98);
	levelButton:SetBackdropBorderColor(0.82, 0.61, 0.24, 1);
	levelButton:EnableMouse(true);
	-- Keep a faint gold halo behind the button, like the game's own level-up control.
	-- This is a region of the button itself, so it follows saved position, scale, and hover growth.
	levelButton.glow = levelButton:CreateTexture(nil, "BACKGROUND");
	levelButton.glow:SetTexture("Interface\\Buttons\\UI-ActionButton-Border");
	if ( levelButton.glow.SetBlendMode ) then levelButton.glow:SetBlendMode("ADD"); end
	levelButton.glow:SetVertexColor(1, 0.72, 0.22, 0.16);
	levelButton.glow:SetPoint("CENTER", levelButton, "CENTER", 0, 0);
	levelButton.glow:SetWidth(228);
	levelButton.glow:SetHeight(82);
	levelButton.glow:SetAlpha(0.16);
	levelButton.classIcon = levelButton:CreateTexture(nil, "ARTWORK");
	levelButton.classIcon:SetTexture("Interface\\Glues\\CharacterCreate\\UI-CharacterCreate-Classes");
	levelButton.classIcon:SetPoint("LEFT", levelButton, "LEFT", 10, 0);
	levelButton.classIcon:SetWidth(40);
	levelButton.classIcon:SetHeight(40);
	local classCoords = PlayerClassIconCoords();
	if ( classCoords ) then
		levelButton.classIcon:SetTexCoord(classCoords[1], classCoords[2], classCoords[3], classCoords[4]);
	else
		levelButton.classIcon:Hide();
	end
	levelButton.level = levelButton:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall");
	levelButton.level:SetPoint("TOPLEFT", levelButton, "TOPLEFT", 58, -10);
	levelButton.level:SetTextColor(1, 0.78, 0.22);
	levelButton.text = levelButton:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge");
	levelButton.text:SetPoint("BOTTOMLEFT", levelButton, "BOTTOMLEFT", 58, 8);
	levelButton.text:SetText("LEVEL UP!");
	levelButton:SetScript("OnClick", function()
		if ( type(AZS_OpenStoredLevels) == "function" ) then pcall(AZS_OpenStoredLevels); end
	end);
	levelButton.count = levelButton:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall");
	levelButton.count:SetPoint("TOPRIGHT", levelButton.classIcon, "TOPRIGHT", 7, 7);
	levelButton.count:SetTextColor(1, 0.78, 0.18);
	local function LevelButtonBaseScale()
		return type(DB().scales.AZSMinorModsStoredLevelsButton) == "number" and DB().scales.AZSMinorModsStoredLevelsButton or 1;
	end
	local function CaptureLevelButtonPoints()
		local points = {};
		for i = 1, levelButton:GetNumPoints() do
			local point, relative, relativePoint, x, y = levelButton:GetPoint(i);
			points[i] = { point, relative, relativePoint, x, y };
		end
		return points;
	end
	local function RestoreLevelButtonPoints(points)
		levelButton:ClearAllPoints();
		for i = 1, table.getn(points or {}) do
			local p = points[i]; levelButton:SetPoint(p[1], p[2], p[3], p[4], p[5]);
		end
	end
	local function GrowLevelButtonInPlace()
		-- SetScale grows from the frame's anchor, which made the button appear to slide away
		-- from its resting location. Preserve its visual center while the hover scale changes.
		if ( not levelButton.hoverPoints ) then levelButton.hoverPoints = CaptureLevelButtonPoints(); end
		local left, right, bottom, top = RootBounds(levelButton);
		if ( not left or not right or not bottom or not top ) then return; end
		local centerX, centerY = (left + right) / 2, (bottom + top) / 2;
		levelButton:SetScale(LevelButtonBaseScale() * 1.06);
		local newLeft, newRight, newBottom, newTop = RootBounds(levelButton);
		if ( newLeft and newRight and newBottom and newTop ) then
			SetRootPosition(levelButton, centerX - (newRight - newLeft) / 2, centerY - (newTop - newBottom) / 2);
		end
	end
	local function RestoreLevelButtonAfterHover()
		levelButton:SetScale(LevelButtonBaseScale());
		if ( levelButton.hoverPoints ) then
			RestoreLevelButtonPoints(levelButton.hoverPoints);
			levelButton.hoverPoints = nil;
		end
	end
	levelButton:SetScript("OnEnter", function()
		GrowLevelButtonInPlace();
		levelButton.glow:SetAlpha(0.24);
	end);
	levelButton:SetScript("OnLeave", function()
		RestoreLevelButtonAfterHover();
		levelButton.glow:SetAlpha(0.16);
	end);
	levelButton:SetScript("OnHide", function()
		RestoreLevelButtonAfterHover();
		levelButton.glow:SetAlpha(0.16);
	end);
	if ( MainMenuBar ) then levelButton:SetPoint("BOTTOMLEFT", MainMenuBar, "TOPLEFT", 0, 6);
	else levelButton:SetPoint("BOTTOMLEFT", UIParent, "BOTTOMLEFT", 32, 100); end
	levelButton:Hide();
end

local function ClockText(seconds)
	if ( type(seconds) ~= "number" or seconds < 0 ) then return "--:--"; end
	seconds = math.floor(seconds);
	return string.format("%02d:%02d", math.floor(seconds / 60), math.mod(seconds, 60));
end

local function FramePaddingLimit(name)
	if ( name == "AZSMinorModsClockFrame" ) then return 6; end
	if ( name == "AZSMinorModsBossFrames" ) then return 8; end
	if ( name == "AZSMinorModsTallyFrame" ) then return 8; end
	if ( name == "AZSMinorModsAnnouncementFrame" ) then return 20; end
	if ( name == "BuffFrame" ) then return 16; end
	return 0;
end

local function FramePadding(name)
	local value = DB().padding[name];
	return type(value) == "number" and math.max(0, math.min(FramePaddingLimit(name), value)) or 0;
end

local function ApplyClockLayout()
	if ( not clockFrame or not clockFrame.time or not clockFrame.next ) then return; end
	local orientation = DB().orientations.AZSMinorModsClockFrame;
	if ( orientation ~= "horizontal" ) then orientation = "vertical"; end
	local padding = FramePadding("AZSMinorModsClockFrame");
	if ( orientation == "vertical" ) then
		clockFrame.time:ClearAllPoints();
		clockFrame.time:SetPoint("TOP", clockFrame, "TOP", 0, -padding);
		clockFrame.time:SetWidth(math.max(100, clockFrame:GetWidth() - (padding * 2)));
		clockFrame.time:SetJustifyH("CENTER");
		clockFrame.next:ClearAllPoints();
		clockFrame.next:SetPoint("TOP", clockFrame.time, "BOTTOM", 0, -(2 + padding));
		clockFrame.next:SetWidth(math.max(100, clockFrame:GetWidth() - (padding * 2)));
		clockFrame.next:SetJustifyH("CENTER");
	else
		clockFrame.time:ClearAllPoints();
		clockFrame.time:SetPoint("LEFT", clockFrame, "LEFT", 4 + padding, 0);
		clockFrame.time:SetWidth(math.max(60, (clockFrame:GetWidth() - (padding * 2)) * 0.34));
		clockFrame.time:SetJustifyH("RIGHT");
		clockFrame.next:ClearAllPoints();
		clockFrame.next:SetPoint("LEFT", clockFrame.time, "RIGHT", 8 + padding, 0);
		clockFrame.next:SetPoint("RIGHT", clockFrame, "RIGHT", -(4 + padding), 0);
		clockFrame.next:SetJustifyH("LEFT");
	end
end

local function CreateClockFrame()
	clockFrame = CreateFrame("Frame", "AZSMinorModsClockFrame", UIParent);
	clockFrame:SetWidth(220);
	clockFrame:SetHeight(54);
	clockFrame:SetPoint("TOP", UIParent, "TOP", 0, -6);
	clockFrame:SetFrameStrata("MEDIUM");
	clockFrame:EnableMouse(false);
	clockFrame.time = clockFrame:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge");
	clockFrame.time:SetPoint("TOP", clockFrame, "TOP", 0, 0);
	clockFrame.time:SetWidth(220);
	clockFrame.time:SetJustifyH("CENTER");
	clockFrame.time:SetTextColor(1, 0.82, 0.28);
	clockFrame.next = clockFrame:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
	clockFrame.next:SetPoint("TOP", clockFrame.time, "BOTTOM", 0, -2);
	clockFrame.next:SetWidth(220);
	clockFrame.next:SetJustifyH("CENTER");
	clockFrame.next:SetTextColor(0.74, 0.72, 0.66);
	-- Both lines share one parent and one movement handle; they cannot be separated while repositioning.
	ApplyClockLayout();
	local savedOriginal = DB().clockNativeOriginal;
	if ( savedOriginal ~= nil ) then
		clockNativeWasShown = savedOriginal and true or false;
		if ( type(AZS_SetHudShown) == "function" ) then pcall(AZS_SetHudShown, "clock", clockNativeWasShown); end
		DB().clockNativeOriginal = nil;
	else
		local shown;
		if ( type(AZS_IsHudShown) == "function" ) then
			local ok, value = pcall(AZS_IsHudShown, "clock");
			if ( ok and value ~= nil ) then shown = value and true or false; end
		end
		if ( shown == nil and GetCVar ) then shown = GetCVar("hudClock") ~= "0"; end
		if ( shown == nil ) then shown = true; end
		clockNativeWasShown = shown;
	end
	local savedBossOriginal = DB().bossNativeOriginal;
	if ( savedBossOriginal ~= nil ) then
		bossNativeWasShown = savedBossOriginal and true or false;
		if ( type(AZS_SetHudShown) == "function" ) then pcall(AZS_SetHudShown, "bosses", bossNativeWasShown); end
		DB().bossNativeOriginal = nil;
	else
		local shown;
		if ( type(AZS_IsHudShown) == "function" ) then
			local ok, value = pcall(AZS_IsHudShown, "bosses");
			if ( ok and value ~= nil ) then shown = value and true or false; end
		end
		if ( shown == nil and GetCVar ) then shown = GetCVar("hudBosses") ~= "0"; end
		if ( shown == nil ) then shown = true; end
		bossNativeWasShown = shown;
	end
	clockFrame:Hide();
end

local function BossPriority(unit)
	if ( unit.kind == "final" ) then return 1; end
	if ( unit.kind == "boss" ) then return 2; end
	if ( unit.kind == "guardian" ) then return 3; end
	if ( unit.kind == "elite" ) then return 4; end
	return nil;
end

local function EncounterVertical()
	return DB().orientations.AZSMinorModsBossFrames == "vertical";
end

local function SetGameDefaultFont(fontString, size)
	-- Slim Suite uses WoW's standard 1.12 UI typeface throughout, including custom HUD labels.
	local defaultFont = STANDARD_TEXT_FONT or "Fonts\\FRIZQT__.TTF";
	local ok, result = pcall(fontString.SetFont, fontString, defaultFont, size);
	if ( not ok or result == false ) then pcall(fontString.SetFont, fontString, "Fonts\\FRIZQT__.TTF", size); end
end

local function EncounterList()
	-- AZS_UNITS carries live hp/hpm for bosses, guardians and elites; the game updates it before the AZSUNITS event.
	local units = type(AZS_UNITS) == "table" and AZS_UNITS or {};
	local list = {};
	for i = 1, table.getn(units) do
		local unit = units[i];
		local priority = type(unit) == "table" and BossPriority(unit) or nil;
		local awake = type(unit) == "table" and (unit.kind ~= "guardian" or unit.state == 1);
		if ( priority and awake and type(unit.id) == "number" and type(unit.name) == "string"
			and type(unit.hp) == "number" and type(unit.hpm) == "number" and unit.hp > 0 and unit.hpm > 0 ) then
			table.insert(list, { id = unit.id, name = unit.name, kind = unit.kind, hp = unit.hp, hpm = unit.hpm, priority = priority, order = i });
		end
	end
	table.sort(list, function(a, b)
		if ( a.priority ~= b.priority ) then return a.priority < b.priority; end
		return a.order < b.order;
	end);
	return list;
end

local function ClampAbsoluteFrame(name, f)
	if ( not f or DB().unlocked ) then return; end
	local saved = DB().places[name];
	if ( type(saved) == "table" and saved.relative ) then return; end
	local minX, minY, maxOffsetX, maxOffsetY = VisualInsets(f);
	local maxX = UIParent:GetWidth() - maxOffsetX;
	local maxY = UIParent:GetHeight() - maxOffsetY;
	local x, right, y = RootBounds(f);
	if ( x == nil and type(saved) == "table" ) then x = saved.x; end
	if ( y == nil and type(saved) == "table" ) then y = saved.y; end
	if ( type(x) ~= "number" or type(y) ~= "number" ) then return; end
	local nx = math.max(-minX, math.min(maxX, x));
	local ny = math.max(-minY, math.min(maxY, y));
	if ( nx ~= x or ny ~= y ) then
		SetRootPosition(f, nx, ny);
	end
end

local function EncounterRow(index)
	if ( encounterRows[index] ) then return encounterRows[index]; end
	local row = CreateFrame("Button", nil, bossFrame);
	row:SetWidth(520);
	row:SetHeight(48);
	row.name = row:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall");
	row.name:SetPoint("TOPLEFT", row, "TOPLEFT", 4, -2);
	row.name:SetWidth(176);
	row.name:SetHeight(22);
	row.name:SetJustifyH("LEFT");
	row.name:SetJustifyV("TOP");
	row.name:SetNonSpaceWrap(true);
	SetGameDefaultFont(row.name, 10);
	row.name:SetTextColor(1, 0.22, 0.16);
	row.bar = CreateFrame("StatusBar", nil, row);
	row.bar:SetWidth(176);
	row.bar:SetHeight(8);
	row.bar:SetPoint("TOPLEFT", row, "TOPLEFT", 4, -28);
	row.bar:SetStatusBarTexture("Interface\\TargetingFrame\\UI-StatusBar");
	row.bar:SetStatusBarColor(0.9, 0.06, 0.025, 1);
	row.bar:SetMinMaxValues(0, 1);
	row.bar:SetValue(1);
	row.bar.background = row.bar:CreateTexture(nil, "BACKGROUND");
	row.bar.background:SetTexture("Interface\\TargetingFrame\\UI-StatusBar");
	row.bar.background:SetAllPoints(row.bar);
	row.bar.background:SetVertexColor(0.04, 0.015, 0.01, 0.9);
	row:EnableMouse(true);
	row:SetHighlightTexture("Interface\\Buttons\\ButtonHilight-Square");
	row:SetScript("OnClick", function()
		local unit = this.unit;
		if ( not unit or not unit.name or not TargetByName ) then return; end
		-- The encounter API exposes stable IDs for health tracking, but the client target API
		-- selects visible units by name. Ask it for this exact name and leave range/visibility
		-- rejection to the game; never retarget a different nearby enemy as a fallback.
		pcall(TargetByName, unit.name, true);
	end);
	row:SetScript("OnEnter", function()
		if ( not this.unit or not GameTooltip ) then return; end
		if ( GameTooltip.SetClampedToScreen ) then GameTooltip:SetClampedToScreen(true); end
		local center = this:GetCenter();
		local onRight = center and center * RootFactor(this) > (UIParent:GetWidth() / 2);
		GameTooltip:SetOwner(this, onRight and "ANCHOR_LEFT" or "ANCHOR_RIGHT");
		GameTooltip:AddLine(this.unit.name .. "  [" .. (this.unit.kind == "guardian" and "Guardian" or (this.unit.kind == "elite" and "Elite" or "Boss")) .. "]");
		GameTooltip:AddLine("Health: " .. tostring(math.floor(this.unit.hp)) .. " / " .. tostring(math.floor(this.unit.hpm)), 1, 0.82, 0);
		GameTooltip:AddLine("Click to target", 0.75, 0.75, 0.75);
		GameTooltip:Show();
	end);
	row:SetScript("OnLeave", function() if ( GameTooltip ) then GameTooltip:Hide(); end end);
	encounterRows[index] = row;
	return row;
end

local function UpdateEncounterRows(list)
	local count = table.getn(list);
	if ( count == 0 ) then return 0; end
	local vertical = EncounterVertical();
	local padding = FramePadding("AZSMinorModsBossFrames");
	local gap = (vertical and 10 or 8) + padding;
	local configured = DB().sizes.AZSMinorModsBossFrames;
	-- Match the default boss target-frame scale shown in the reference: a readable, substantial
	-- nameplate and health bar rather than a tiny floating strip. Cards still distribute evenly
	-- when several enemies are active.
	local defaultCardWidth = vertical and 48 or 520;
	local minCardWidth = vertical and 34 or 520;
	local minimumFrameWidth = count * (vertical and 18 or minCardWidth) + ((count - 1) * gap) + (padding * 2);
	local frameWidth = type(configured) == "table" and configured.w or (count * defaultCardWidth + ((count - 1) * gap) + (padding * 2));
	frameWidth = math.max(minimumFrameWidth, frameWidth);
	local cardWidth = math.max(vertical and 18 or minCardWidth, (frameWidth - (padding * 2) - ((count - 1) * gap)) / count);
	frameWidth = count * cardWidth + ((count - 1) * gap) + (padding * 2);
	local frameHeight = type(configured) == "table" and configured.h or (vertical and 252 or (count * 48));
	local minimumHeight = vertical and math.max(60, 52 + (padding * 3)) or math.max(count * 42, 42 + (padding * 2));
	frameHeight = math.max(minimumHeight, frameHeight);
	bossFrame:SetWidth(frameWidth);
	bossFrame:SetHeight(frameHeight);
	local rowHeight = vertical and math.max(58, frameHeight - (padding * 2)) or math.max(42, (frameHeight - (padding * 2)) / count);
	for i = 1, table.getn(list) do
		local unit = list[i];
		local row = EncounterRow(i);
		row.unit = unit;
		row:SetWidth(cardWidth);
		row:SetHeight(rowHeight);
		row.name:ClearAllPoints();
		row.bar:ClearAllPoints();
		row:ClearAllPoints();
		row:SetPoint("TOPLEFT", bossFrame, "TOPLEFT", vertical and (padding + ((i - 1) * (cardWidth + gap))) or padding, -(padding + (vertical and 0 or ((i - 1) * rowHeight))));
		row.name:SetText(unit.name);
		if ( vertical ) then
			SetGameDefaultFont(row.name, 8.5);
			local barTopOffset = 34 + padding;
			-- Match the narrow, evenly spaced columns in the reference. Each boss name stays above its own bar.
			row.name:SetWidth(cardWidth);
			row.name:SetHeight(30);
			row.name:SetPoint("TOP", row, "TOP", 0, -(2 + padding));
			if ( row.bar.SetOrientation ) then pcall(row.bar.SetOrientation, row.bar, "VERTICAL"); end
			row.bar:SetWidth(math.max(10, cardWidth - (padding * 2)));
			row.bar:SetHeight(math.max(18, rowHeight - barTopOffset - padding));
			row.bar:SetPoint("TOP", row, "TOP", 0, -barTopOffset);
		else
			SetGameDefaultFont(row.name, 14);
			row.name:SetWidth(cardWidth - 8 - (padding * 2));
			row.name:SetHeight(22);
			row.name:SetJustifyH("CENTER");
			row.name:SetPoint("TOPLEFT", row, "TOPLEFT", 4 + padding, -(2 + padding));
			if ( row.bar.SetOrientation ) then pcall(row.bar.SetOrientation, row.bar, "HORIZONTAL"); end
			local width = math.max(20, cardWidth - 8 - (padding * 2));
			row.bar:SetWidth(unit.kind == "guardian" and math.max(20, width * 0.78) or width);
			row.bar:SetHeight(12);
			row.bar:SetPoint("TOPLEFT", row, "TOPLEFT", 4 + padding, -(24 + padding));
		end
		-- Keep the StatusBar range small and stable. Raw boss HP can be very large, and using it as
		-- the range can hide small health changes at the UI's numeric precision; show the live fraction.
		local health = math.max(0, math.min(1, unit.hp / unit.hpm));
		row.bar:SetMinMaxValues(0, 1);
		row.bar:SetValue(health);
		row:Show();
	end
	for i = table.getn(list) + 1, table.getn(encounterRows) do encounterRows[i]:Hide(); end
	return frameHeight;
end

local function UpdateClock()
 if not DB().replaceClock then if clockFrame then clockFrame:Hide();end;SetNativeClockHidden(false);return;end
	if ( not clockFrame ) then return; end
	local phase = type(AZS_GetPhase) == "function" and AZS_GetPhase() or nil;
	local live = phase == "ready" or phase == "playing" or phase == "choosing" or phase == "paused";
	-- This movable replacement must not inherit the native clock's independent HUD visibility flag.
	local showClock = live;
	local preview = IsEditing("AZSMinorModsClockFrame") and not showClock;
	if ( showClock ) then
		local elapsed = type(AZS_GetRunTime) == "function" and AZS_GetRunTime() or nil;
		if ( phase == "ready" and type(elapsed) ~= "number" ) then elapsed = 0; end
		clockFrame.time:SetText(ClockText(elapsed));
		local upcoming = {};
		if ( type(AZS_GetUpcomingBosses) == "function" ) then
			local ok, value = pcall(AZS_GetUpcomingBosses);
			if ( ok and type(value) == "table" ) then upcoming = value; end
		end
		local left = type(upcoming[1]) == "table" and upcoming[1].left or nil;
		local run = type(AZS_RUN) == "table" and AZS_RUN or nil;
		if ( type(left) ~= "number" and run and type(run.nb) == "number" and run.nb >= 0 ) then left = run.nb; end
		if ( type(left) == "number" and left >= 0 ) then
			clockFrame.next:SetText("next boss in " .. ClockText(math.ceil(left)));
		else
			clockFrame.next:SetText("next boss in --:--");
		end
		SetNativeClockHidden(true);
		clockFrame.time:Show();
		clockFrame.next:Show();
	else
		SetNativeClockHidden(false);
		if ( preview ) then
			clockFrame.time:SetText("00:00");
			clockFrame.next:SetText("next boss in --:--");
			clockFrame.time:Show();
			clockFrame.next:Show();
		else
			clockFrame.time:Hide();
			clockFrame.next:Hide();
		end
	end
	if ( showClock or preview ) then
		clockFrame:Show();
		ClampAbsoluteFrame("AZSMinorModsClockFrame", clockFrame);
	else clockFrame:Hide(); end
end

local function UpdateBossFrames()
 if not DB().replaceEncounters then if bossFrame then bossFrame:Hide();end;SetNativeBossesHidden(false);return;end
	if ( not bossFrame ) then return; end
	local phase = type(AZS_GetPhase) == "function" and AZS_GetPhase() or nil;
	local live = phase == "playing" or phase == "choosing" or phase == "paused";
	-- The replacement is driven by the documented AZS_UNITS data, independently of whether the
	-- player had the native boss frames enabled. Visibility of the original HUD must not gate ours.
	if ( live ) then
		local list = EncounterList();
		local height = UpdateEncounterRows(list);
		if ( height > 0 ) then
			bossFrame:SetHeight(height);
			ClampAbsoluteFrame("AZSMinorModsBossFrames", bossFrame);
			SetNativeBossesHidden(true);
			bossFrame:Show();
		else
			SetNativeBossesHidden(false);
			bossFrame:Hide();
		end
	elseif ( IsEditing("AZSMinorModsBossFrames") ) then
		for i = 1, table.getn(encounterRows) do encounterRows[i]:Hide(); end
		local vertical = EncounterVertical();
		local configured = DB().sizes.AZSMinorModsBossFrames;
		bossFrame:SetWidth(type(configured) == "table" and configured.w or (vertical and 76 or 520));
		bossFrame:SetHeight(type(configured) == "table" and configured.h or (vertical and 252 or 48));
		ClampAbsoluteFrame("AZSMinorModsBossFrames", bossFrame);
		SetNativeBossesHidden(false);
		bossFrame:Show();
	else
		for i = 1, table.getn(encounterRows) do encounterRows[i]:Hide(); end
		SetNativeBossesHidden(false);
		bossFrame:Hide();
	end
end

local encounterUpdates = CreateFrame("Frame");
encounterUpdates:RegisterEvent("CHAT_MSG_ADDON");
encounterUpdates:SetScript("OnEvent", function()
	if ( arg1 == "AZSUNITS" ) then pcall(UpdateBossFrames); end
end);

CreateClockFrame();
bossFrame = CreateFrame("Frame", "AZSMinorModsBossFrames", UIParent);
bossFrame:SetWidth(520);
bossFrame:SetHeight(48);
-- Match the game's centered encounter nameplate, with the independent run clock above it.
bossFrame:SetPoint("TOP", UIParent, "TOP", 0, -76);
bossFrame:SetFrameStrata("MEDIUM");
bossFrame:EnableMouse(false);
bossFrame:Hide();

local function CreateAnnouncementFrame()
	announcementFrame = CreateFrame("Frame", "AZSMinorModsAnnouncementFrame", UIParent);
	local bannerWidth = math.max(280, math.min(920, UIParent:GetWidth() - 24));
	announcementFrame:SetWidth(bannerWidth);
	announcementFrame:SetHeight(60);
	announcementFrame:SetPoint("CENTER", UIParent, "CENTER", 0, 112);
	announcementFrame:SetFrameStrata("HIGH");
	announcementFrame:EnableMouse(false);
	announcementFrame.text = announcementFrame:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge");
	announcementFrame.text:SetPoint("CENTER", announcementFrame, "CENTER", 0, 0);
	announcementFrame.text:SetWidth(bannerWidth - 20);
	announcementFrame.text:SetHeight(60);
	announcementFrame.text:SetJustifyH("CENTER");
	announcementFrame.text:SetJustifyV("MIDDLE");
	announcementFrame.text:SetNonSpaceWrap(true);
	SetGameDefaultFont(announcementFrame.text, 22);
	announcementFrame.text:SetTextColor(0.96, 0.82, 0.52);
	local savedOriginal = DB().bannerNativeOriginal;
	if ( savedOriginal ~= nil ) then
		bannerNativeWasShown = savedOriginal and true or false;
		if ( type(AZS_SetHudShown) == "function" ) then pcall(AZS_SetHudShown, "banners", bannerNativeWasShown); end
		DB().bannerNativeOriginal = nil;
	else
		local shown;
		if ( type(AZS_IsHudShown) == "function" ) then
			local ok, value = pcall(AZS_IsHudShown, "banners");
			if ( ok and value ~= nil ) then shown = value and true or false; end
		end
		if ( shown == nil and GetCVar ) then shown = GetCVar("hudBanners") ~= "0"; end
		if ( shown == nil ) then shown = true; end
		bannerNativeWasShown = shown;
	end
	announcementFrame:Hide();
end

-- Posts "hello world" to /say when a run starts (its opening banner). Off when DB().helloOnStart is false.
local lastHelloAt;
local function MaybeHelloOnStart(banner)
	if ( not DB().helloOnStart or type(banner) ~= "table" or type(banner.text) ~= "string" ) then return; end
	if ( string.find(string.lower(banner.text), "survive the onslaught", 1, true) == nil ) then return; end
	if ( lastHelloAt and GetTime() - lastHelloAt < 60 ) then return; end
	lastHelloAt = GetTime();
	pcall(SendChatMessage, "hello world", "SAY");
end

local function ShowAnnouncement(banner)
	MaybeHelloOnStart(banner);
	if ( not DB().replaceBanners ) then
		if ( announcementFrame ) then announcementFrame:Hide(); end
		SetNativeBannersHidden(false);
		return;
	end
	if ( type(banner) ~= "table" ) then return; end
	if ( banner.id ~= nil and banner.id == lastBannerId ) then return; end
	local text = type(banner.text) == "string" and banner.text or "";
	if ( text == "" and type(AZS_BANNER) == "table" and type(AZS_BANNER.text) == "string" ) then
		text = AZS_BANNER.text;
	end
	if ( text == "" and type(banner.name) == "string" and banner.name ~= "" ) then text = banner.name; end
	if ( text == "" ) then return; end
	lastBannerId = banner.id;
	if ( bannerNativeWasShown ~= true and not DB().unlocked ) then return; end
	announcementFrame.text:SetText(text);
	if ( banner.boss ) then announcementFrame.text:SetTextColor(1, 0.24, 0.13);
	else announcementFrame.text:SetTextColor(0.96, 0.82, 0.52); end
	announcementStarted = GetTime();
	announcementFrame:SetAlpha(0);
	announcementFrame:Show();
	ClampAbsoluteFrame("AZSMinorModsAnnouncementFrame", announcementFrame);
	SetNativeBannersHidden(true);
end

local function UpdateAnnouncementFrame()
 if not DB().replaceBanners then if announcementFrame then announcementFrame:Hide();end;SetNativeBannersHidden(false);return;end
	if ( not announcementFrame ) then return; end
	local maxWidth = math.max(40, UIParent:GetWidth() - 24);
	if ( announcementFrame:GetWidth() > maxWidth ) then announcementFrame:SetWidth(maxWidth); end
	announcementFrame.text:SetWidth(math.max(40, announcementFrame:GetWidth() - 20));
	announcementFrame.text:SetHeight(announcementFrame:GetHeight());
	if ( announcementStarted ) then
		local elapsed = GetTime() - announcementStarted;
		if ( elapsed >= announcementDuration ) then
			announcementStarted = nil;
			announcementFrame:Hide();
			return;
		end
		local alpha = 1;
		if ( elapsed < announcementFade ) then alpha = elapsed / announcementFade;
		elseif ( elapsed > announcementDuration - announcementFade ) then
			alpha = (announcementDuration - elapsed) / announcementFade;
		end
		announcementFrame:SetAlpha(math.max(0, math.min(1, alpha)));
	elseif ( IsEditing("AZSMinorModsAnnouncementFrame") ) then
		announcementFrame.text:SetText("Hogger enters the battlefield!");
		announcementFrame.text:SetTextColor(1, 0.24, 0.13);
		announcementFrame:SetAlpha(1);
		announcementFrame:Show();
		ClampAbsoluteFrame("AZSMinorModsAnnouncementFrame", announcementFrame);
	else
		announcementFrame:Hide();
	end
end

CreateAnnouncementFrame();
announcementFrame:SetScript("OnUpdate", function()
	if ( announcementStarted ) then UpdateAnnouncementFrame(); end
end);
local bannerHandler = function(banner) pcall(ShowAnnouncement, banner); end;
local bannerHandlerRegistered = false;
local function RegisterAnnouncementHandler()
	if ( bannerHandlerRegistered ) then return true; end
	if ( type(AZS_RegisterBannerHandler) ~= "function" ) then return false; end
	local ok = pcall(AZS_RegisterBannerHandler, bannerHandler);
	if ( ok ) then
		bannerHandlerRegistered = true;
		if ( bannerNativeWasShown == true and DB().replaceBanners ) then SetNativeBannersHidden(true); end
	end
	return bannerHandlerRegistered;
end
pcall(RegisterAnnouncementHandler);
CreateLevelButton();
pcall(UpdateLevelButton);
local tallyTicker = CreateFrame("Frame");
tallyTicker.elapsed = 0;
tallyTicker:SetScript("OnUpdate", function()
	this.elapsed = this.elapsed + (arg1 or 0);
	if ( this.elapsed >= 0.25 ) then
		this.elapsed = 0;
		pcall(UpdateTally);
		pcall(UpdateClock);
		pcall(UpdateBossFrames);
		pcall(UpdateAnnouncementFrame);
		pcall(UpdateLevelButton);
		pcall(ApplyActionSlotGrid);
		pcall(RefreshItemRow);
		local tooltipHandle = handles.GameTooltip;
		if ( tooltipHandle ) then
			if ( IsEditing("GameTooltip") ) then tooltipHandle:Show();
			else tooltipHandle:Hide(); end
		end
		for i = 1, table.getn(MOVABLE) do
			local name = MOVABLE[i][1];
			if ( DB().muted[name] ) then pcall(ApplyFeatureMute, name); end
		end
	end
end);

-- GetLeft/GetRight are in the queried frame's effective-scale units. All movement state
-- and comparisons below use UIParent units; SetPoint offsets must be converted back.
RootFactor = function(frame)
	return (frame:GetEffectiveScale() or 1) / (UIParent:GetEffectiveScale() or 1);
end

RootBounds = function(frame)
	local l, r, b, t = frame:GetLeft(), frame:GetRight(), frame:GetBottom(), frame:GetTop();
	if ( not l or not r or not b or not t ) then return nil; end
	local factor = RootFactor(frame);
	return l * factor, r * factor, b * factor, t * factor;
end

SetRootPosition = function(frame, x, y)
	local factor = RootFactor(frame);
	frame:ClearAllPoints();
	frame:SetPoint("BOTTOMLEFT", UIParent, "BOTTOMLEFT", x / factor, y / factor);
end

local function SavePosition(name, f)
	local p = DB().places[name];
	if ( type(p) ~= "table" ) then p = {}; DB().places[name] = p; end
	local left, right, bottom = RootBounds(f);
	p.x, p.y, p.coordinateSpace = left, bottom, "uiParent";
end

-- Resize, scale and snapped anchors can all push visible child controls past a screen edge.
-- Clamp the drawn bounds (not just the parent frame) and convert a clipped relative anchor to
-- a stable absolute location. Save the corrected place immediately so it survives /reload.
local function ClampToClient(name, frame)
	if ( not frame or not UIParent ) then return; end
	local left, right, bottom = RootBounds(frame);
	if ( not left or not bottom ) then return; end
	local minX, minY, maxOffsetX, maxOffsetY = VisualInsets(frame);
	local maxX = UIParent:GetWidth() - maxOffsetX;
	local maxY = UIParent:GetHeight() - maxOffsetY;
	local x = math.max(-minX, math.min(maxX, left));
	local y = math.max(-minY, math.min(maxY, bottom));
	if ( math.abs(x - left) > 0.01 or math.abs(y - bottom) > 0.01 ) then
		SetRootPosition(frame, x, y);
		SavePosition(name, frame);
		local p = DB().places[name];
		p.relative, p.point, p.relativePoint, p.dx, p.dy = nil, nil, nil, nil, nil;
	end
end

local function ApplySize(name, f)
	local s = DB().sizes[name];
	if ( name == "GameTooltip" and type(s) ~= "table" ) then
		s = { w = 260, h = 140 };
		DB().sizes[name] = s;
	end
	if ( type(s) == "table" and s.w and s.h and s.w > 0 and s.h > 0 ) then
		f:SetWidth(s.w);
		f:SetHeight(s.h);
	end
	if ( name == "AZSMinorModsClockFrame" ) then ApplyClockLayout(); end
	if ( name == "MainMenuBar" and ApplyMovableOrientation ) then ApplyMovableOrientation(name); end
	if ( name == "AZSMinorModsItemRowFrame" ) then LayoutItemRow(); end
	if ( name == "AZSMinorModsSpellRowFrame" ) then LayoutSpellRow(); end
	local scale = DB().scales[name];
	if ( type(scale) == "number" ) then
		if ( name == "PartyMemberFrame1" ) then
			for i = 1, 4 do local party = getglobal("PartyMemberFrame" .. i); if ( party and party.SetScale ) then party:SetScale(scale); end end
		elseif ( f.SetScale ) then f:SetScale(scale); end
	end
end

-- Include visible child frames in edge clamping. Some Blizzard bars draw their buttons a few
-- logical pixels outside the parent frame, so clamping only the parent's rectangle leaves a gap.
VisualInsets = function(frame)
	local baseLeft, baseRight, baseBottom = RootBounds(frame);
	if ( not baseLeft or not baseBottom ) then return 0, 0, 0, 0; end
	local uiScale = UIParent:GetEffectiveScale() or 1;
	local frameScale = frame:GetEffectiveScale() or uiScale;
	local factor = frameScale / uiScale;
	-- MainMenuExpBar's child textures retain their old horizontal extents after the native
	-- status bar is rotated. Its own bounds are the visible/movable bounds in either layout.
	if ( frame == MainMenuExpBar ) then
		return 0, 0, (frame:GetWidth() or 0) * factor, (frame:GetHeight() or 0) * factor;
	end
	if ( frame == MainMenuBar or frame == PartyMemberFrame1 or frame == BuffFrame ) then
		-- These Blizzard groups have unused parent padding or detached siblings. Clamp their
		-- actual visible controls as a group so scale cannot leave a gap or clip a companion.
		local minX, minY, maxX, maxY;
		local function Include(child)
			if ( not child or (child.IsShown and not child:IsShown()) ) then return; end
			local left, right, bottom, top = RootBounds(child);
			if ( left and right and bottom and top ) then
				local l, r, b, t = left - baseLeft, right - baseLeft, bottom - baseBottom, top - baseBottom;
				minX = minX and math.min(minX, l) or l; maxX = maxX and math.max(maxX, r) or r;
				minY = minY and math.min(minY, b) or b; maxY = maxY and math.max(maxY, t) or t;
			end
		end
		if ( frame == MainMenuBar ) then
			for i = 1, 6 do Include(getglobal("ActionButton" .. i)); end
			Include(ActionButton12);
			if ( ActionButton12 ) then Include(ActionButton12.slimRacialBorder); end
			Include(ActionBarUpButton); Include(ActionBarDownButton);
		elseif ( frame == BuffFrame ) then
			for i = 1, (MAX_PLAYER_BUFFS or 32) do Include(getglobal("BuffButton" .. i)); end
		else
			for i = 1, 4 do Include(getglobal("PartyMemberFrame" .. i)); end
		end
		if ( minX and minY and maxX and maxY ) then return minX, minY, maxX, maxY; end
	end
	local minX, minY = 0, 0;
	local maxX = (frame:GetWidth() or 0) * factor;
	local maxY = (frame:GetHeight() or 0) * factor;
	local function IncludeDescendants(parent, seen)
		if ( not parent or seen[parent] or not parent.GetChildren ) then return; end
		seen[parent] = true;
		local ok, children = pcall(function() return { parent:GetChildren() }; end);
		if ( not ok or type(children) ~= "table" ) then return; end
		for i = 1, table.getn(children) do
			local child = children[i];
			if ( child and (not child.IsShown or child:IsShown()) ) then
				local got, left, right, bottom, top = pcall(function()
					return RootBounds(child);
				end);
				if ( got and left and right and bottom and top ) then
					minX = math.min(minX, left - baseLeft);
					maxX = math.max(maxX, right - baseLeft);
					minY = math.min(minY, bottom - baseBottom);
					maxY = math.max(maxY, top - baseBottom);
				end
				IncludeDescendants(child, seen);
			end
		end
	end
	IncludeDescendants(frame, {});
	return minX, minY, maxX, maxY;
end;

local function CollectFeatureFrames(frame, list, seen, depth)
	if ( not frame or seen[frame] or depth > 4 ) then return; end
	seen[frame] = true;
	table.insert(list, frame);
	if ( not frame.GetChildren ) then return; end
	local ok, children = pcall(function() return { frame:GetChildren() }; end);
	if ( not ok or type(children) ~= "table" ) then return; end
	for i = 1, table.getn(children) do CollectFeatureFrames(children[i], list, seen, depth + 1); end
end

ApplyFeatureMute = function(name)
	local frame = getglobal(name);
	if ( not frame ) then return; end
	local list, seen = {}, {};
	CollectFeatureFrames(frame, list, seen, 0);
	if ( name == "PartyMemberFrame1" ) then
		for i = 2, 4 do CollectFeatureFrames(getglobal("PartyMemberFrame" .. i), list, seen, 0); end
	elseif ( name == "ChatFrame1" ) then
		CollectFeatureFrames(getglobal("ChatFrame1Tab"), list, seen, 0);
		CollectFeatureFrames(getglobal("ChatFrame1EditBox"), list, seen, 0);
	end
	local muted = DB().muted[name] and true or false;
	for i = 1, table.getn(list) do
		local target = list[i];
		if ( target and target.GetAlpha and target.SetAlpha ) then
			if ( muted ) then
				if ( mutedBaseAlphas[target] == nil ) then
					local ok, alpha = pcall(target.GetAlpha, target);
					mutedBaseAlphas[target] = ok and alpha or 1;
				end
				if ( mutedBaseMouse[target] == nil and target.IsMouseEnabled ) then
					local ok, enabled = pcall(target.IsMouseEnabled, target);
					if ( ok ) then mutedBaseMouse[target] = enabled and true or false; end
				end
				pcall(target.SetAlpha, target, 0);
				if ( target.EnableMouse and mutedBaseMouse[target] ~= nil ) then pcall(target.EnableMouse, target, false); end
			else
				local alpha = mutedBaseAlphas[target];
				if ( alpha ~= nil ) then pcall(target.SetAlpha, target, alpha); mutedBaseAlphas[target] = nil; end
				local enabled = mutedBaseMouse[target];
				if ( enabled ~= nil and target.EnableMouse ) then pcall(target.EnableMouse, target, enabled); mutedBaseMouse[target] = nil; end
			end
		end
	end
	local handle = handles[name];
	if ( handle ) then
		if ( muted ) then
			handle:SetBackdropColor(0.22, 0.22, 0.24, 0.58);
			handle:SetBackdropBorderColor(0.6, 0.6, 0.62, 0.85);
			if ( handle.text and name ~= "GameTooltip" ) then handle.text:SetTextColor(0.72, 0.72, 0.74); end
		else
			handle:SetBackdropColor(0.1, 0.6, 0.1, 0.45);
			handle:SetBackdropBorderColor(0.3, 1, 0.3, 0.9);
			if ( handle.text and name ~= "GameTooltip" ) then handle.text:SetTextColor(1, 1, 1); end
		end
	end
end;

local function ToggleFeatureMute(name)
	DB().muted[name] = not DB().muted[name];
	ApplyFeatureMute(name);
	if ( sizeMenu and sizeMenu.muteButton ) then
		sizeMenu.muteButton:SetText(DB().muted[name] and "Show feature" or "Hide feature");
	end
end

local function CurrentOrientation(name)
	local orientation = DB().orientations[name];
	if ( orientation == "vertical" or orientation == "horizontal" ) then return orientation; end
	if ( name == "AZSMinorModsClockFrame" ) then return "vertical"; end
	if ( name == "PartyMemberFrame1" ) then return "vertical"; end
	return "horizontal";
end

local function BuffGrowthDirection()
	return DB().buffGrowthDirection == "up" and "up" or "down";
end

local function ApplyBuffOrientation()
	if ( not BuffFrame or buffLayoutApplying ) then return; end
	local orientation = CurrentOrientation("BuffFrame");
	local limit = MAX_PLAYER_BUFFS or 32;
	local visible, iconW, iconH, durationW, durationH = {}, nil, nil, 0, 0;
	for i = 1, limit do
		local button = getglobal("BuffButton" .. i);
		if ( button ) then
			if ( not buffButtonOriginalPoints[i] and button.GetNumPoints ) then
				local points = {};
				for p = 1, button:GetNumPoints() do
					local a, relative, relativePoint, x, y = button:GetPoint(p);
					points[p] = { a, relative, relativePoint, x, y };
				end
				buffButtonOriginalPoints[i] = points;
			end
			if ( button:IsShown() ) then
				table.insert(visible, button);
				iconW = iconW or button:GetWidth(); iconH = iconH or button:GetHeight();
				local duration = getglobal("BuffButton" .. i .. "Duration");
				if ( duration and duration:IsShown() ) then
					local w = duration.GetStringWidth and duration:GetStringWidth() or 0;
					local h = duration.GetHeight and duration:GetHeight() or 0;
					durationW = math.max(durationW, w or 0);
					durationH = math.max(durationH, h or 0);
				end
			end
		end
	end
	if ( not iconW or not iconH or table.getn(visible) == 0 ) then return; end
	buffLayoutApplying = true;
	if ( orientation == "horizontal" and not DB().buffCustomLayout ) then
		for i = 1, limit do
			local button = getglobal("BuffButton" .. i);
			local points = buffButtonOriginalPoints[i];
			if ( button and points and table.getn(points) > 0 ) then
				button:ClearAllPoints();
				for p = 1, table.getn(points) do
					local a = points[p];
					button:SetPoint(a[1], a[2], a[3], a[4], a[5]);
				end
			end
		end
		local base = baseSizes.BuffFrame;
		if ( base ) then BuffFrame:SetWidth(base.w); BuffFrame:SetHeight(base.h); end
		buffLayoutApplying = false;
		return;
	end
	-- The stock cooldown text sits below each icon. Reserve its own cell space so duration labels
	-- never collide with the next buff when the row is vertical or packed into multiple columns.
	-- Treat each icon and its cooldown text as one cell. The text's measured size is used
	-- instead of a fixed guessed gap, so long durations never collide with the next buff.
	local gap, count = 4 + FramePadding("BuffFrame"), table.getn(visible);
	local cellW = math.max(iconW, durationW) + 4;
	local cellH = iconH + durationH + 4;
	local customSize = DB().sizes.BuffFrame;
	local columns, rows;
	if ( orientation == "vertical" ) then
		local maxRows = count;
		if ( type(customSize) == "table" and customSize.h and customSize.h > 0 ) then
			maxRows = math.max(1, math.floor((customSize.h + gap) / (cellH + gap)));
		end
		rows = math.min(count, maxRows);
		columns = math.ceil(count / rows);
	else
		local base = baseSizes.BuffFrame;
		local maxWidth = type(customSize) == "table" and customSize.w or (base and base.w) or (iconW * 10);
		columns = math.max(1, math.min(count, math.floor((maxWidth + gap) / (cellW + gap))));
		rows = math.ceil(count / columns);
	end
	local direction = BuffGrowthDirection();
	local verticalOffset = direction == "up" and 1 or -1;
	for i = 1, count do
		local column, row;
		if ( orientation == "vertical" ) then
			column = math.floor((i - 1) / rows);
			row = math.mod(i - 1, rows);
		else
			column = math.mod(i - 1, columns);
			row = math.floor((i - 1) / columns);
		end
		local button = visible[i];
		button:ClearAllPoints();
		-- Keep the first icon at the anchor. New rows/vertical icons extend in the selected direction.
		button:SetPoint("BOTTOMLEFT", BuffFrame, "BOTTOMLEFT",
			column * (cellW + gap) + (cellW - iconW) / 2,
			verticalOffset * row * (cellH + gap));
	end
	local requiredWidth = columns * (cellW + gap) - gap;
	local requiredHeight = rows * (cellH + gap) - gap;
	BuffFrame:SetWidth(math.max(type(customSize) == "table" and customSize.w or 0, requiredWidth));
	BuffFrame:SetHeight(math.max(type(customSize) == "table" and customSize.h or 0, requiredHeight));
	buffLayoutApplying = false;
end

local function ApplyActionSlotGrid()
	local on = DB().showEmptyActionSlots and true or false;
	if ( on == actionSlotsGridApplied ) then return; end
	local action = on and ActionButton_ShowGrid or ActionButton_HideGrid;
	if ( type(action) ~= "function" ) then return; end -- retry later once the game action buttons are available
	local found = false;
	for i = 1, 6 do
		local button = getglobal("ActionButton" .. i);
		if ( button ) then pcall(action, button); found = true; end
	end
	if ( found ) then actionSlotsGridApplied = on; end
end

CaptureActionBarLayout = function()
	if ( actionBarOriginal or not MainMenuBar ) then return; end
	local saved = DB().actionBarHorizontalSize;
	local w, h = MainMenuBar:GetWidth(), MainMenuBar:GetHeight();
	if ( type(saved) == "table" and saved.w and saved.h ) then w, h = saved.w, saved.h;
	else DB().actionBarHorizontalSize = { w = w, h = h }; end
	actionBarOriginal = { w = w, h = h, buttonSize = nil, buttons = {}, racial = nil, arrows = {} };
	for i = 1, 6 do
		local button = getglobal("ActionButton" .. i);
		if ( button and button.GetNumPoints ) then
			actionBarOriginal.buttonSize = actionBarOriginal.buttonSize or button:GetWidth();
			local points = {};
			for p = 1, button:GetNumPoints() do
				local point, relative, relativePoint, x, y = button:GetPoint(p);
				points[p] = { point, relative, relativePoint, x, y };
			end
			actionBarOriginal.buttons[i] = points;
		end
	end
	if ( ActionButton12 and ActionButton12.GetNumPoints ) then
		local points = {};
		for p = 1, ActionButton12:GetNumPoints() do
			local point, relative, relativePoint, x, y = ActionButton12:GetPoint(p);
			points[p] = { point, relative, relativePoint, x, y };
		end
		actionBarOriginal.racial = { w = ActionButton12:GetWidth(), h = ActionButton12:GetHeight(), points = points,
			shown = ActionButton12.IsShown and ActionButton12:IsShown() or false };
	end
	for _, name in ipairs({ "ActionBarUpButton", "ActionBarDownButton" }) do
		local arrow = getglobal(name);
		if ( arrow and arrow.GetNumPoints ) then
			local points = {};
			for p = 1, arrow:GetNumPoints() do
				local point, relative, relativePoint, x, y = arrow:GetPoint(p);
				points[p] = { point, relative, relativePoint, x, y };
			end
			actionBarOriginal.arrows[name] = { w = arrow:GetWidth(), h = arrow:GetHeight(), points = points };
		end
	end
	if ( AZS_RacialRim and AZS_RacialRim.GetNumPoints ) then
		local points = {};
		for p = 1, AZS_RacialRim:GetNumPoints() do
			local point, relative, relativePoint, x, y = AZS_RacialRim:GetPoint(p);
			points[p] = { point, relative, relativePoint, x, y };
		end
		actionBarOriginal.rim = points;
	end
end

function AZSMinorMods_RestoreRacialActionButtonLayout()
	local original = actionBarOriginal;
	if ( not original or not original.racial or not ActionButton12 ) then return; end
	ActionButton12:SetWidth(original.racial.w); ActionButton12:SetHeight(original.racial.h);
	ActionButton12:ClearAllPoints();
	for p = 1, table.getn(original.racial.points) do
		local a = original.racial.points[p]; ActionButton12:SetPoint(a[1], a[2], a[3], a[4], a[5]);
	end
	if ( original.racial.shown ) then ActionButton12:Show(); else ActionButton12:Hide(); end
	if ( ActionButton12.slimRacialBorder ) then ActionButton12.slimRacialBorder:Hide(); end
	if ( AZS_RacialRim and original.rim ) then
		AZS_RacialRim:ClearAllPoints();
		for p = 1, table.getn(original.rim) do local a = original.rim[p]; AZS_RacialRim:SetPoint(a[1], a[2], a[3], a[4], a[5]); end
	end
end

ApplyMovableOrientation = function(name)
	local orientation = CurrentOrientation(name);
	if ( name == "MainMenuBar" ) then
		local d = DB();
		if ( not d.unlocked and not d.showRacialAbility and not d.plainBar and orientation ~= "vertical" and type(d.sizes.MainMenuBar) ~= "table" ) then return; end
        local first = getglobal("ActionButton1");
        if ( not first ) then return; end
        local vertical = orientation == "vertical";
        local saved = DB().sizes.MainMenuBar;
        local showRacial = d.showRacialAbility and true or false;
        local buttonCount = showRacial and 7 or 6;
        local gap, racialGap = 2, showRacial and 4 or 0;
        local side = first:GetWidth() or 36;
        if ( type(saved) == "table" ) then
            side = math.max(12, math.min(vertical and saved.w or saved.h,
                ((vertical and saved.h or saved.w) - gap * (buttonCount - 1) - racialGap) / buttonCount));
        end
        for i = 1, buttonCount do
            local button = getglobal("ActionButton" .. (i == 7 and 12 or i));
            if ( button ) then
                button:SetWidth(side); button:SetHeight(side); button:ClearAllPoints();
                local offset = (i - 1) * (side + gap) + (showRacial and i == 7 and racialGap or 0);
                button:SetPoint("TOPLEFT", MainMenuBar, "TOPLEFT", vertical and 0 or offset, vertical and -offset or 0);
                if ( showRacial and i == 7 and AZS_RacialRim and AZS_RacialRim.SetAllPoints ) then AZS_RacialRim:ClearAllPoints(); AZS_RacialRim:SetAllPoints(button); end
                if ( showRacial and i == 7 and not button.slimRacialBorder ) then
                    local rim = CreateFrame("Frame", nil, button);
                    rim:SetPoint("TOPLEFT", button, "TOPLEFT", -2, 2);
                    rim:SetPoint("BOTTOMRIGHT", button, "BOTTOMRIGHT", 2, -2);
                    rim:SetBackdrop({edgeFile="Interface\\Tooltips\\UI-Tooltip-Border", edgeSize=8});
                    rim:SetBackdropBorderColor(1, 0.78, 0.18, 1); rim:EnableMouse(false);
                    button.slimRacialBorder = rim;
                end
                if ( showRacial and i == 7 ) then button:Show(); end
            end
        end
		if ( not showRacial ) then AZSMinorMods_RestoreRacialActionButtonLayout(); end
        MainMenuBar:SetWidth(vertical and side or side * buttonCount + gap * (buttonCount - 1) + racialGap);
        MainMenuBar:SetHeight(vertical and side * buttonCount + gap * (buttonCount - 1) + racialGap or side);
        if ( ActionBarUpButton ) then
            ActionBarUpButton:ClearAllPoints(); ActionBarUpButton:SetWidth(12); ActionBarUpButton:SetHeight(12);
            ActionBarUpButton:SetPoint("TOPRIGHT", first, "TOPLEFT", -2, 0);
        end
        if ( ActionBarDownButton ) then
            ActionBarDownButton:ClearAllPoints(); ActionBarDownButton:SetWidth(12); ActionBarDownButton:SetHeight(12);
            ActionBarDownButton:SetPoint("TOPRIGHT", first, "TOPLEFT", -2, -14);
        end
	elseif ( name == "MainMenuExpBar" ) then
		local d = DB();
		if ( not d.unlocked and type(d.places.MainMenuExpBar) ~= "table" and type(d.sizes.MainMenuExpBar) ~= "table"
			and type(d.scales.MainMenuExpBar) ~= "number" and d.orientations.MainMenuExpBar == nil ) then return; end
		local xp = MainMenuExpBar;
		-- Some client builds keep rested-XP child textures anchored to the original horizontal
		-- extent after rotating the status bar. Clip children to the bar when this Frame API exists,
		-- so the movable handle and the rendered XP indicator share the same visible bounds.
		if ( xp and xp.SetClipsChildren ) then pcall(xp.SetClipsChildren, xp, true); end
		if ( xp and xp.SetOrientation ) then pcall(xp.SetOrientation, xp, string.upper(orientation)); end
		local base = baseSizes.MainMenuExpBar;
		local saved = DB().sizes.MainMenuExpBar;
		if ( xp and base and type(saved) ~= "table" ) then
			if ( orientation == "vertical" ) then xp:SetWidth(base.h); xp:SetHeight(base.w);
			else xp:SetWidth(base.w); xp:SetHeight(base.h); end
			DB().sizes.MainMenuExpBar = { w = xp:GetWidth(), h = xp:GetHeight() };
		end
	elseif ( name == "BuffFrame" ) then
		ApplyBuffOrientation();
	elseif ( name == "AZSMinorModsBossFrames" ) then
		UpdateBossFrames();
	elseif ( name == "PartyMemberFrame1" ) then
		local d = DB();
		if ( not d.unlocked and d.orientations.PartyMemberFrame1 == nil
			and type(d.places.PartyMemberFrame1) ~= "table" and type(d.sizes.PartyMemberFrame1) ~= "table"
			and type(d.scales.PartyMemberFrame1) ~= "number" ) then return; end
		local root = PartyMemberFrame1;
		if ( not root ) then return; end
		for i = 2, 4 do
			local frame = getglobal("PartyMemberFrame" .. i);
			if ( frame ) then
				if ( frame.SetUserPlaced ) then frame:SetUserPlaced(true); end
				frame:ClearAllPoints();
				if ( orientation == "horizontal" ) then
					frame:SetPoint("LEFT", getglobal("PartyMemberFrame" .. (i - 1)), "RIGHT", 8, 0);
				else
					frame:SetPoint("TOP", getglobal("PartyMemberFrame" .. (i - 1)), "BOTTOM", 0, -4);
				end
				if ( frame.SetUserPlaced ) then frame:SetUserPlaced(true); end
			end
		end
	end
end

function AZSMinorMods_RestoreNativeActionBarLayout()
	local original = actionBarOriginal;
	if ( not original or not MainMenuBar ) then return; end
	MainMenuBar:SetWidth(original.w); MainMenuBar:SetHeight(original.h);
	for i = 1, 6 do
		local button = getglobal("ActionButton" .. i);
		local points = original.buttons[i];
		if ( button ) then
			if ( original.buttonSize ) then button:SetWidth(original.buttonSize); button:SetHeight(original.buttonSize); end
			if ( points and table.getn(points) > 0 ) then
				button:ClearAllPoints();
				for p = 1, table.getn(points) do local a = points[p]; button:SetPoint(a[1], a[2], a[3], a[4], a[5]); end
			end
		end
	end
	AZSMinorMods_RestoreRacialActionButtonLayout();
	for name, layout in pairs(original.arrows or {}) do
		local arrow = getglobal(name);
		if ( arrow ) then
			arrow:SetWidth(layout.w); arrow:SetHeight(layout.h); arrow:ClearAllPoints();
			for p = 1, table.getn(layout.points) do local a = layout.points[p]; arrow:SetPoint(a[1], a[2], a[3], a[4], a[5]); end
		end
	end
end

function AZSMinorMods_SetRacialAbility(on)
	local d = DB();
	d.showRacialAbility = on and true or false;
	if ( on and CaptureActionBarLayout ) then pcall(CaptureActionBarLayout); end
	if ( not d.showRacialAbility and not d.unlocked and not d.plainBar and d.orientations.MainMenuBar ~= "vertical"
		and type(d.sizes.MainMenuBar) ~= "table" ) then
		pcall(AZSMinorMods_RestoreNativeActionBarLayout);
	else
		pcall(ApplyMovableOrientation, "MainMenuBar");
	end
	PlaceAll();
end

local function Place(name, ignoreAnchor)
	local f = getglobal(name);
	local p = DB().places[name];
	if ( not f ) then return; end
	local w, h = f:GetWidth(), f:GetHeight();
	if ( w and h and w > 0 and h > 0 and not baseSizes[name] ) then baseSizes[name] = { w = w, h = h }; end
	if ( name ~= "GameTooltip" and not basePoints[name] and f.GetNumPoints ) then
		local points = {};
		for i = 1, f:GetNumPoints() do
			local point, relative, relativePoint, x, y = f:GetPoint(i);
			points[i] = { point, relative, relativePoint, x, y };
		end
		if ( table.getn(points) > 0 ) then basePoints[name] = points; end
	end
	if ( name == "MainMenuBar" and CaptureActionBarLayout ) then CaptureActionBarLayout(); end
	ApplySize(name, f);
	-- Our edge clamp uses visible child bounds; let it place an inset parent slightly outside the screen.
	if ( DB().unlocked or type(p) == "table" ) then
		if ( f.SetClampedToScreen ) then f:SetClampedToScreen(false); end
		if ( (name == "BuffFrame" or name == "MainMenuBar") and f.SetUserPlaced ) then f:SetUserPlaced(true); end
	end
	if ( type(p) ~= "table" ) then return; end
	if ( p.coordinateSpace ~= "uiParent" ) then
		local factor = RootFactor(f);
		if ( p.x ) then p.x = p.x * factor; end
		if ( p.y ) then p.y = p.y * factor; end
		if ( p.dx ) then p.dx = p.dx * factor; end
		if ( p.dy ) then p.dy = p.dy * factor; end
		p.coordinateSpace = "uiParent";
	end
	local relative = not ignoreAnchor and p.relative and getglobal(p.relative) or nil;
	if ( relative and p.point and p.relativePoint ) then
		f:ClearAllPoints();
		local factor = RootFactor(f);
		f:SetPoint(p.point, relative, p.relativePoint, K.Num(p.dx) / factor, K.Num(p.dy) / factor);
	elseif ( p.x and p.y ) then
		-- Saved coordinates can come from a taller or wider display. Clamp absolute restores to this UIParent
		-- without rewriting the saved location, so a later return to the larger display restores the user's layout.
		local minX, minY, maxOffsetX, maxOffsetY = VisualInsets(f);
		local maxX = UIParent:GetWidth() - maxOffsetX;
		local maxY = UIParent:GetHeight() - maxOffsetY;
		local x = math.max(-minX, math.min(maxX, K.Num(p.x)));
		local y = math.max(-minY, math.min(maxY, K.Num(p.y)));
		SetRootPosition(f, x, y);
	end
end

local function PlaceAll()
	for i = 1, table.getn(MOVABLE) do pcall(Place, MOVABLE[i][1], true); end
	-- Reapply saved frame-to-frame anchors after every movable frame has its stored size and base position.
	for pass = 1, table.getn(MOVABLE) do
		for i = 1, table.getn(MOVABLE) do pcall(Place, MOVABLE[i][1], false); end
	end
	-- Relative snaps and scale changes must not leave a frame clipped or padded away from the edge.
	for i = 1, table.getn(MOVABLE) do
		local name = MOVABLE[i][1];
		local frame = getglobal(name);
		if ( frame ) then pcall(ClampToClient, name, frame); end
	end
end

local function ApplyTallyLayout(resetSize)
	if ( not tallyFrame ) then return; end
	local vertical = DB().tallyVertical and true or false;
	local padding = FramePadding("AZSMinorModsTallyFrame");
	if ( resetSize ) then
		local w, h = vertical and 104 or 252, vertical and 54 or 28;
		tallyFrame:SetWidth(w); tallyFrame:SetHeight(h);
		DB().sizes.AZSMinorModsTallyFrame = { w = w, h = h };
	end
	if ( vertical ) then
		tallyFrame.text:ClearAllPoints();
		tallyFrame.text:SetPoint("TOPLEFT", tallyFrame, "TOPLEFT", 4 + padding, -(2 + padding));
		tallyFrame.text:SetJustifyH("LEFT");
		tallyFrame.text:SetJustifyV("TOP");
	else
		tallyFrame.text:ClearAllPoints();
		tallyFrame.text:SetPoint("LEFT", tallyFrame, "LEFT", 4 + padding, 0);
		tallyFrame.text:SetPoint("RIGHT", tallyFrame, "RIGHT", -(4 + padding), 0);
		tallyFrame.text:SetJustifyH("LEFT");
		tallyFrame.text:SetJustifyV("MIDDLE");
	end
	PlaceAll();
	UpdateTally();
end

local function ToggleMovableOrientation(name)
	if ( name == "AZSMinorModsItemRowFrame" or name == "AZSMinorModsSpellRowFrame" ) then
		DB().orientations[name] = CurrentOrientation(name) == "vertical" and "horizontal" or "vertical";
		if ( name == "AZSMinorModsItemRowFrame" ) then LayoutItemRow(); else LayoutSpellRow(); end
		DB().sizes[name] = { w = getglobal(name):GetWidth(), h = getglobal(name):GetHeight() };
		PlaceAll();
		return;
	end
	if ( name == "AZSMinorModsTallyFrame" ) then
		DB().tallyVertical = not DB().tallyVertical;
		ApplyTallyLayout(true);
		return;
	end
	local nextOrientation = CurrentOrientation(name) == "vertical" and "horizontal" or "vertical";
	DB().orientations[name] = nextOrientation;
	if ( name == "AZSMinorModsClockFrame" ) then
		local w, h = nextOrientation == "vertical" and 220 or 360, nextOrientation == "vertical" and 54 or 32;
		clockFrame:SetWidth(w); clockFrame:SetHeight(h);
		DB().sizes[name] = { w = w, h = h };
		ApplyClockLayout();
		PlaceAll();
		UpdateClock();
		return;
	end
	if ( name == "BuffFrame" ) then DB().buffCustomLayout = true; end
	if ( name == "AZSMinorModsBossFrames" ) then DB().sizes[name] = nil; end

	if ( name == "MainMenuExpBar" or name == "MainMenuBar" ) then
		local f = getglobal(name);
		if ( f ) then
			-- Rotate the current dimensions, including a user's custom length. Using only the
			-- Blizzard base size here discards resizing and leaves a horizontal footprint behind.
			local saved = DB().sizes[name];
			local w = type(saved) == "table" and saved.w or f:GetWidth();
			local h = type(saved) == "table" and saved.h or f:GetHeight();
			w, h = h, w;
			f:SetWidth(w); f:SetHeight(h); DB().sizes[name] = { w = w, h = h };
		end
	end
	ApplyMovableOrientation(name);
	PlaceAll();
end

local function ApplyMovablePadding(name)
	if ( name == "AZSMinorModsClockFrame" ) then ApplyClockLayout();
	elseif ( name == "AZSMinorModsBossFrames" ) then UpdateBossFrames();
	elseif ( name == "AZSMinorModsTallyFrame" ) then ApplyTallyLayout(false);
	elseif ( name == "AZSMinorModsAnnouncementFrame" and announcementFrame and announcementFrame.text ) then
		local padding = FramePadding(name);
		announcementFrame.text:ClearAllPoints();
		announcementFrame.text:SetPoint("TOPLEFT", announcementFrame, "TOPLEFT", padding + 10, -padding);
		announcementFrame.text:SetPoint("BOTTOMRIGHT", announcementFrame, "BOTTOMRIGHT", -(padding + 10), padding);
	elseif ( name == "BuffFrame" ) then
		DB().buffCustomLayout = true;
		pcall(ApplyBuffOrientation);
	end
end


-- The main chat window is docked by default, with its tab and edit box as separate frames. Undock it for custom
-- placement, then anchor those two companions to the message frame so one green handle moves the whole chat window.
local function PrepareChatWindow()
	local chat = ChatFrame1;
	if ( not chat ) then return; end
	if ( chat.isDocked and FCF_UnDockFrame ) then
		pcall(FCF_UnDockFrame, chat);
		if ( FCF_SetLocked ) then pcall(FCF_SetLocked, chat, false); end
	end
	local chatLeft, chatBottom = chat:GetLeft(), chat:GetBottom();
	if ( not chatLeft or not chatBottom ) then return; end
	local parts = { { "ChatFrame1Tab", ChatFrame1Tab }, { "ChatFrame1EditBox", chat.editBox or getglobal("ChatFrame1EditBox") } };
	for i = 1, table.getn(parts) do
		local name, part = parts[i][1], parts[i][2];
		local offset = DB().chatOffsets[name];
		if ( part and type(offset) ~= "table" ) then
			local left, bottom = part:GetLeft(), part:GetBottom();
			if ( left and bottom ) then
				offset = { x = left - chatLeft, y = bottom - chatBottom };
				DB().chatOffsets[name] = offset;
			end
		end
		if ( part and type(offset) == "table" and type(offset.x) == "number" and type(offset.y) == "number" ) then
			part:ClearAllPoints();
			part:SetPoint("BOTTOMLEFT", chat, "BOTTOMLEFT", offset.x, offset.y);
		end
	end
end

-- The native XP bar is anchored to MainMenuBar. Reparent it while preserving its on-screen bounds so its
-- independent move handle can reposition it without moving or resizing the action buttons.
local function DetachExperienceBar()
	if ( xpBarDetached or not MainMenuExpBar or not UIParent ) then return; end
	local left, right, bottom = RootBounds(MainMenuExpBar);
	local width, height = MainMenuExpBar:GetWidth(), MainMenuExpBar:GetHeight();
	MainMenuExpBar:SetParent(UIParent);
	MainMenuExpBar:ClearAllPoints();
	if ( left and bottom ) then
		SetRootPosition(MainMenuExpBar, left, bottom);
	else
		MainMenuExpBar:SetPoint("BOTTOM", UIParent, "BOTTOM", 0, 8);
	end
	if ( width and width > 0 ) then MainMenuExpBar:SetWidth(width); end
	if ( height and height > 0 ) then MainMenuExpBar:SetHeight(height); end
	xpBarDetached = true;
end

local function ToggleBuildRowsAttached()
	if ( not itemRow or not spellRow or not MainMenuBar ) then return; end
	if ( DB().buildRowsAttached ) then
		for _, name in ipairs({ "AZSMinorModsSpellRowFrame", "AZSMinorModsItemRowFrame" }) do
			local frame = getglobal(name);
			SavePosition(name, frame);
			local p = DB().places[name];
			if ( p ) then p.relative, p.point, p.relativePoint, p.dx, p.dy = nil, nil, nil, nil, nil; end
		end
		DB().buildRowsAttached = false;
	else
		-- Keep the spell row where the player left it; attach only the jewelry row beneath it.
		SavePosition("AZSMinorModsSpellRowFrame", spellRow);
		local spellPlace = DB().places.AZSMinorModsSpellRowFrame;
		if ( spellPlace ) then spellPlace.relative, spellPlace.point, spellPlace.relativePoint, spellPlace.dx, spellPlace.dy = nil, nil, nil, nil, nil; end
		DB().buildRowsAttached = true;
		DB().places.AZSMinorModsItemRowFrame = AZSMinorMods_BuildRowDefaultPlace("AZSMinorModsItemRowFrame");
	end
	PlaceAll();
	if ( sizeMenu and sizeMenu.attachButton ) then
		sizeMenu.attachButton:SetText(DB().buildRowsAttached and "Detach build rows" or "Attach build rows");
	end
end

local function OpenSizeMenu(name, label)
	local f = getglobal(name);
	if ( not f ) then return; end
	if ( not sizeMenu ) then
		sizeMenu = CreateFrame("Frame", nil, UIParent);
		sizeMenu:SetWidth(206);
		sizeMenu:SetHeight(270);
		sizeMenu:SetFrameStrata("DIALOG");
		sizeMenu:SetBackdrop({ bgFile = "Interface\\Tooltips\\UI-Tooltip-Background", edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
		                      tile = true, tileSize = 16, edgeSize = 12, insets = { left = 3, right = 3, top = 3, bottom = 3 } });
		sizeMenu:SetBackdropColor(0.04, 0.035, 0.07, 0.97);
		sizeMenu:SetBackdropBorderColor(0.85, 0.65, 0.2, 1);
		sizeMenuText = sizeMenu:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall");
		sizeMenuText:SetPoint("TOPLEFT", sizeMenu, "TOPLEFT", 12, -10);
		sizeMenu:SetMovable(true);
		if ( sizeMenu.SetClampedToScreen ) then sizeMenu:SetClampedToScreen(true); end
		sizeMenu.dragStrip = CreateFrame("Frame", nil, sizeMenu);
		sizeMenu.dragStrip:SetPoint("TOPLEFT", sizeMenu, "TOPLEFT", 4, -3);
		sizeMenu.dragStrip:SetWidth(124); sizeMenu.dragStrip:SetHeight(20);
		sizeMenu.dragStrip:EnableMouse(true);
		sizeMenu.dragStrip:SetScript("OnMouseDown", function()
			if ( arg1 == "LeftButton" ) then sizeMenu:StartMoving(); sizeMenu.dragging = true; end
		end);
		sizeMenu.dragStrip:SetScript("OnMouseUp", function()
			if ( not sizeMenu.dragging ) then return; end
			sizeMenu:StopMovingOrSizing(); sizeMenu.dragging = nil;
			local left, bottom = sizeMenu:GetLeft(), sizeMenu:GetBottom();
			if ( left and bottom ) then DB().sizeMenuPosition = { x = left, y = bottom }; end
		end);
		sizeMenuWidth = sizeMenu:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
		sizeMenuWidth:SetPoint("TOPLEFT", sizeMenu, "TOPLEFT", 12, -34);
		sizeMenuHeight = sizeMenu:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
		sizeMenuHeight:SetPoint("TOPLEFT", sizeMenu, "TOPLEFT", 12, -63);
		sizeMenuScale = sizeMenu:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
		sizeMenuScale:SetPoint("TOPLEFT", sizeMenu, "TOPLEFT", 12, -92);
		sizeMenuScale:SetText("Scale %:");
		sizeMenuScaleInput = CreateFrame("EditBox", nil, sizeMenu);
		sizeMenuScaleInput:SetPoint("TOPLEFT", sizeMenu, "TOPLEFT", 52, -89);
		sizeMenuScaleInput:SetWidth(46); sizeMenuScaleInput:SetHeight(20);
		sizeMenuScaleInput:SetAutoFocus(false); sizeMenuScaleInput:SetNumeric(true);
		sizeMenuScaleInput:SetJustifyH("CENTER"); sizeMenuScaleInput:SetMaxLetters(3);
		sizeMenuScaleInput:SetBackdrop({ bgFile = "Interface\\Tooltips\\UI-Tooltip-Background", edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
			tile = true, tileSize = 16, edgeSize = 8, insets = { left = 2, right = 2, top = 2, bottom = 2 } });
		sizeMenuScaleInput:SetBackdropColor(0.02, 0.02, 0.03, 0.95);
		sizeMenuScaleInput:SetBackdropBorderColor(0.55, 0.43, 0.2, 1);
		local function ResizeBy(axis, delta)
			if ( not sizeTarget ) then return; end
			local t = getglobal(sizeTarget);
			if ( not t ) then return; end
			local w, h = t:GetWidth(), t:GetHeight();
			local effective = t.GetEffectiveScale and t:GetEffectiveScale() or 1;
			local rootScale = UIParent:GetEffectiveScale() or 1;
			local scaleRatio = effective / rootScale;
			if ( scaleRatio <= 0 ) then scaleRatio = 1; end
			if ( axis == "w" ) then
				local minWidth = sizeTarget == "AZSMinorModsClockFrame" and CurrentOrientation(sizeTarget) == "horizontal" and 280 or 40;
				if ( sizeTarget == "AZSMinorModsItemRowFrame" ) then minWidth = CurrentOrientation(sizeTarget) == "vertical" and 36 or 76; end
				if ( sizeTarget == "AZSMinorModsSpellRowFrame" ) then minWidth = CurrentOrientation(sizeTarget) == "vertical" and 36 or 92; end
				if ( sizeTarget == "GameTooltip" ) then minWidth = 160; end
				if ( sizeTarget == "AZSMinorModsMeterFrame" ) then minWidth = 300; end
				w = math.max(minWidth, math.min((UIParent:GetWidth() or 1920) / scaleRatio, w + delta));
			else
				local minHeight = sizeTarget == "AZSMinorModsBossFrames" and CurrentOrientation(sizeTarget) == "vertical" and 60
					or sizeTarget == "AZSMinorModsClockFrame" and CurrentOrientation(sizeTarget) == "vertical" and 40 or 24;
				if ( sizeTarget == "AZSMinorModsBossFrames" ) then
					local pad = FramePadding(sizeTarget);
					minHeight = CurrentOrientation(sizeTarget) == "vertical" and math.max(60, 52 + (pad * 3)) or math.max(36, 36 + (pad * 4));
				elseif ( sizeTarget == "AZSMinorModsClockFrame" and CurrentOrientation(sizeTarget) == "vertical" ) then
					minHeight = 36 + (FramePadding(sizeTarget) * 3);
				elseif ( sizeTarget == "AZSMinorModsItemRowFrame" and CurrentOrientation(sizeTarget) == "vertical" ) then
					minHeight = 60;
				elseif ( sizeTarget == "AZSMinorModsSpellRowFrame" and CurrentOrientation(sizeTarget) == "vertical" ) then
					minHeight = 92;
				elseif ( sizeTarget == "AZSMinorModsMeterFrame" ) then
					minHeight = 148;
				elseif ( sizeTarget == "GameTooltip" ) then
					minHeight = 80;
				end
				h = math.max(minHeight, math.min((UIParent:GetHeight() or 1080) / scaleRatio, h + delta));
			end
			t:SetWidth(w); t:SetHeight(h);
			if ( sizeTarget == "MainMenuBar" ) then
				local vertical = CurrentOrientation(sizeTarget) == "vertical";
				local side = (axis == "w") and (vertical and w or (w - 16) / 7) or (vertical and (h - 16) / 7 or h);
				side = math.max(12, math.min(side, (UIParent:GetWidth() / scaleRatio - (vertical and 0 or 16)) / (vertical and 1 or 7), (UIParent:GetHeight() / scaleRatio - (vertical and 16 or 0)) / (vertical and 7 or 1)));
				w, h = vertical and side or side * 7 + 16, vertical and side * 7 + 16 or side;
				t:SetWidth(w); t:SetHeight(h);
			end
			DB().sizes[sizeTarget] = { w = w, h = h };
			if ( sizeTarget == "BuffFrame" ) then pcall(ApplyBuffOrientation); end
			if ( sizeTarget == "AZSMinorModsClockFrame" ) then ApplyClockLayout(); end
			PlaceAll();
			sizeMenuWidth:SetText("Width: " .. math.floor(w + 0.5));
			sizeMenuHeight:SetText("Height: " .. math.floor(h + 0.5));
		end
		local narrower = K.Button(sizeMenu, "-", 30, function() ResizeBy("w", -20); end);
		narrower:SetPoint("TOPRIGHT", sizeMenu, "TOPRIGHT", -72, -26);
		local wider = K.Button(sizeMenu, "+", 30, function() ResizeBy("w", 20); end);
		wider:SetPoint("TOPRIGHT", sizeMenu, "TOPRIGHT", -32, -26);
		local shorter = K.Button(sizeMenu, "-", 30, function() ResizeBy("h", -20); end);
		shorter:SetPoint("TOPRIGHT", sizeMenu, "TOPRIGHT", -72, -55);
		local taller = K.Button(sizeMenu, "+", 30, function() ResizeBy("h", 20); end);
		taller:SetPoint("TOPRIGHT", sizeMenu, "TOPRIGHT", -32, -55);
		local function SetScalePercent(percent)
			if ( not sizeTarget ) then return; end
			percent = math.max(1, math.min(200, math.floor(tonumber(percent) or 100)))
			DB().scales[sizeTarget] = percent / 100;
			PlaceAll();
			sizeMenuScaleInput:SetText(tostring(percent));
		end
		sizeMenuScaleInput:SetScript("OnEnterPressed", function()
			this.cancelScaleEdit = true;
			SetScalePercent(this:GetText());
			this:ClearFocus();
		end);
		sizeMenuScaleInput:SetScript("OnEditFocusLost", function()
			if ( this.cancelScaleEdit ) then this.cancelScaleEdit = nil; return; end
			SetScalePercent(this:GetText());
		end);
		sizeMenuScaleInput:SetScript("OnEscapePressed", function()
			this.cancelScaleEdit = true;
			this:ClearFocus();
			local current = math.floor((DB().scales[sizeTarget] or 1) * 100 + 0.5);
			this:SetText(tostring(current));
		end);
		local scaleDown = K.Button(sizeMenu, "-", 30, function()
			local scale = math.floor((DB().scales[sizeTarget] or 1) * 100 + 0.5);
			SetScalePercent(scale - 5);
		end);
		scaleDown:SetPoint("TOPRIGHT", sizeMenu, "TOPRIGHT", -72, -84);
		local scaleUp = K.Button(sizeMenu, "+", 30, function()
			local scale = math.floor((DB().scales[sizeTarget] or 1) * 100 + 0.5);
			SetScalePercent(scale + 5);
		end);
		scaleUp:SetPoint("TOPRIGHT", sizeMenu, "TOPRIGHT", -32, -84);
		-- Labels and their +/- buttons share the same adjustments for wheel and clicks.
		local function WheelPair(y, down, up)
			local zone = CreateFrame("Frame", nil, sizeMenu);
			zone:SetPoint("TOPLEFT", sizeMenu, "TOPLEFT", 10, y);
			zone:SetWidth(90); zone:SetHeight(25); zone:EnableMouseWheel(true);
			local function Wheel()
				if ( not arg1 or arg1 == 0 ) then return; end
				local action = (arg1 > 0 and up or down):GetScript("OnClick");
				if ( action ) then action(); end
			end
			zone:SetScript("OnMouseWheel", Wheel);
			down:EnableMouseWheel(true); down:SetScript("OnMouseWheel", Wheel);
			up:EnableMouseWheel(true); up:SetScript("OnMouseWheel", Wheel);
			return zone;
		end
		WheelPair(-26, narrower, wider); WheelPair(-55, shorter, taller); WheelPair(-84, scaleDown, scaleUp);
		local layout = K.Button(sizeMenu, "Switch to vertical", 138, function()
			if ( not sizeTarget ) then return; end
			ToggleMovableOrientation(sizeTarget);
			local t = getglobal(sizeTarget);
			if ( t ) then
				sizeMenuWidth:SetText("Width: " .. math.floor(t:GetWidth() + 0.5));
				sizeMenuHeight:SetText("Height: " .. math.floor(t:GetHeight() + 0.5));
			end
			local orientation = sizeTarget == "AZSMinorModsTallyFrame" and (DB().tallyVertical and "vertical" or "horizontal") or CurrentOrientation(sizeTarget);
			sizeMenu.layoutButton:SetText(orientation == "vertical" and "Switch to horizontal" or "Switch to vertical");
		end);
		layout:SetPoint("TOPLEFT", sizeMenu, "TOPLEFT", 10, -116);
		sizeMenu.layoutButton = layout;
		local growth;
		growth = K.Button(sizeMenu, "Grow down", 68, function()
			if ( sizeTarget ~= "BuffFrame" ) then return; end
			DB().buffCustomLayout = true;
			DB().buffGrowthDirection = BuffGrowthDirection() == "up" and "down" or "up";
			growth:SetText("Grow " .. BuffGrowthDirection());
			pcall(ApplyBuffOrientation);
			PlaceAll();
		end);
		growth:SetPoint("TOPRIGHT", sizeMenu, "TOPRIGHT", -10, -116);
		growth:Hide();
		sizeMenu.growthButton = growth;
		sizeMenuPadding = sizeMenu:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
		sizeMenuPadding:SetPoint("TOPLEFT", sizeMenu, "TOPLEFT", 12, -148);
		local padDown = K.Button(sizeMenu, "-", 30, function()
			if ( not sizeTarget ) then return; end
			DB().padding[sizeTarget] = math.max(0, FramePadding(sizeTarget) - 2);
			ApplyMovablePadding(sizeTarget);
			sizeMenuPadding:SetText("Padding: " .. FramePadding(sizeTarget));
			local t = getglobal(sizeTarget);
			if ( t ) then sizeMenuWidth:SetText("Width: " .. math.floor(t:GetWidth() + 0.5)); sizeMenuHeight:SetText("Height: " .. math.floor(t:GetHeight() + 0.5)); end
		end);
		padDown:SetPoint("TOPRIGHT", sizeMenu, "TOPRIGHT", -72, -142);
		local padUp = K.Button(sizeMenu, "+", 30, function()
			if ( not sizeTarget ) then return; end
			DB().padding[sizeTarget] = math.min(FramePaddingLimit(sizeTarget), FramePadding(sizeTarget) + 2);
			ApplyMovablePadding(sizeTarget);
			sizeMenuPadding:SetText("Padding: " .. FramePadding(sizeTarget));
			local t = getglobal(sizeTarget);
			if ( t ) then sizeMenuWidth:SetText("Width: " .. math.floor(t:GetWidth() + 0.5)); sizeMenuHeight:SetText("Height: " .. math.floor(t:GetHeight() + 0.5)); end
		end);
		padUp:SetPoint("TOPRIGHT", sizeMenu, "TOPRIGHT", -32, -142);
		sizeMenu.padDown, sizeMenu.padUp = padDown, padUp;
		sizeMenu.paddingWheel = WheelPair(-142, padDown, padUp);
		local muteButton = K.Button(sizeMenu, "Hide feature", 112, function()
			if ( sizeTarget ) then ToggleFeatureMute(sizeTarget); end
		end);
		muteButton:SetPoint("TOPLEFT", sizeMenu, "TOPLEFT", 10, -178);
		sizeMenu.muteButton = muteButton;
		local attachButton = K.Button(sizeMenu, "Detach item row", 184, function() ToggleBuildRowsAttached(); end);
		attachButton:SetPoint("TOPLEFT", sizeMenu, "TOPLEFT", 10, -208);
		sizeMenu.attachButton = attachButton;
		local reset = K.Button(sizeMenu, "Reset", 62, function()
			if ( not AZSMinorMods_ResetElement(sizeTarget) ) then
				K.Print(NAME, "the original size is not available yet; try again after the frame appears.");
				return;
			end
			OpenSizeMenu(sizeTarget, sizeTarget);
		end);
		reset:SetPoint("BOTTOMLEFT", sizeMenu, "BOTTOMLEFT", 10, 8);
		local close = K.Button(sizeMenu, "Done", 62, function() sizeMenu:Hide(); end);
		close:SetPoint("BOTTOMRIGHT", sizeMenu, "BOTTOMRIGHT", -10, 8);
	end
	sizeTarget = name;
	local w, h = f:GetWidth() or 0, f:GetHeight() or 0;
	sizeMenuText:SetText("Resize " .. (label or name));
	sizeMenuWidth:SetText("Width: " .. math.floor(w + 0.5));
	sizeMenuHeight:SetText("Height: " .. math.floor(h + 0.5));
	local scale = DB().scales[name] or 1;
	sizeMenuScaleInput:SetText(tostring(math.max(1, math.min(200, math.floor(scale * 100 + 0.5)))));
	if ( sizeMenu.layoutButton ) then
		local canOrient = name == "AZSMinorModsTallyFrame" or name == "AZSMinorModsClockFrame" or name == "AZSMinorModsBossFrames" or name == "AZSMinorModsSpellRowFrame" or name == "AZSMinorModsItemRowFrame" or name == "MainMenuBar" or name == "MainMenuExpBar" or name == "BuffFrame" or name == "PartyMemberFrame1";
		if ( canOrient ) then
			sizeMenu.layoutButton:Show();
			sizeMenu.layoutButton:SetWidth(name == "BuffFrame" and 116 or 138);
			local orientation = name == "AZSMinorModsTallyFrame" and (DB().tallyVertical and "vertical" or "horizontal") or CurrentOrientation(name);
			sizeMenu.layoutButton:SetText(orientation == "vertical" and "Switch to horizontal" or "Switch to vertical");
		else
			sizeMenu.layoutButton:Hide();
		end
		if ( sizeMenu.growthButton ) then
			if ( name == "BuffFrame" ) then
				sizeMenu.growthButton:Show();
				sizeMenu.growthButton:SetText("Grow " .. BuffGrowthDirection());
			else sizeMenu.growthButton:Hide(); end
		end
		if ( name == "AZSMinorModsSpellRowFrame" ) then
			sizeMenu.attachButton:Show();
			sizeMenu.attachButton:SetText(DB().buildRowsAttached and "Detach build rows" or "Attach build rows");
		else sizeMenu.attachButton:Hide(); end
		local canPad = name == "AZSMinorModsClockFrame" or name == "AZSMinorModsBossFrames" or name == "AZSMinorModsTallyFrame" or name == "AZSMinorModsAnnouncementFrame" or name == "BuffFrame";
		if ( canPad ) then
			sizeMenu.paddingWheel:Show();
			sizeMenuPadding:Show(); sizeMenu.padDown:Show(); sizeMenu.padUp:Show();
			sizeMenuPadding:SetText("Padding: " .. FramePadding(name));
		else
			sizeMenuPadding:Hide(); sizeMenu.padDown:Hide(); sizeMenu.padUp:Hide();
			sizeMenu.paddingWheel:Hide();
		end
		sizeMenu.muteButton:SetText(DB().muted[name] and "Show feature" or "Hide feature");
	end
	sizeMenu:ClearAllPoints();
	local saved = DB().sizeMenuPosition;
	local sw, sh = UIParent:GetWidth(), UIParent:GetHeight();
	local x, y;
	if ( type(saved) == "table" and type(saved.x) == "number" and type(saved.y) == "number" ) then
		x, y = saved.x, saved.y;
	else
		local scale = UIParent:GetEffectiveScale() or 1;
		x, y = GetCursorPosition(); x, y = x / scale, y / scale; x, y = x + 8, y + 8;
	end
	x = math.max(0, math.min(sw - 206, x));
	y = math.max(0, math.min(sh - 270, y));
	sizeMenu:SetPoint("BOTTOMLEFT", UIParent, "BOTTOMLEFT", x, y);
	if ( type(saved) ~= "table" ) then DB().sizeMenuPosition = { x = x, y = y }; end
	sizeMenu:Show();
end

local function ShowLockPanel()
	if ( not lockPanel ) then
		lockPanel = CreateFrame("Frame", "AZSMinorModsMovePanel", UIParent);
		lockPanel:SetWidth(374);
		lockPanel:SetHeight(198);
		lockPanel:SetFrameStrata("DIALOG");
		lockPanel:SetMovable(true);
		if ( lockPanel.SetClampedToScreen ) then lockPanel:SetClampedToScreen(true); end
		lockPanel:SetBackdrop({ bgFile = "Interface\\Tooltips\\UI-Tooltip-Background", edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
		                       tile = true, tileSize = 16, edgeSize = 12, insets = { left = 3, right = 3, top = 3, bottom = 3 } });
		lockPanel:SetBackdropColor(0.04, 0.035, 0.07, 0.97);
		lockPanel:SetBackdropBorderColor(0.85, 0.65, 0.2, 1);
		local header = CreateFrame("Frame", nil, lockPanel);
		header:SetPoint("TOPLEFT", lockPanel, "TOPLEFT", 4, -3);
		header:SetPoint("TOPRIGHT", lockPanel, "TOPRIGHT", -4, -3);
		header:SetHeight(22);
		header:EnableMouse(true);
		header:RegisterForDrag("LeftButton");
		local title = lockPanel:CreateFontString(nil, "OVERLAY", "GameFontNormal");
		title:SetPoint("LEFT", header, "LEFT", 7, 0);
		title:SetText("Move UI settings — drag this header to move");
		header:SetScript("OnDragStart", function() lockPanel:StartMoving(); end);
		header:SetScript("OnDragStop", function()
			lockPanel:StopMovingOrSizing(); lockPanel.dragging = nil;
			local left, top = lockPanel:GetLeft(), lockPanel:GetTop();
			if ( left and top ) then DB().lockPanelPosition = { x = left, y = UIParent:GetHeight() - top }; end
		end);
		local note = lockPanel:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
		note:SetPoint("TOPLEFT", lockPanel, "TOPLEFT", 10, -30);
		note:SetText("Drag to move. Right-click for size, scale, layout and hide options. Shift-left-click also hides/shows.");
		local gridCheck = CreateFrame("CheckButton", nil, lockPanel, "UICheckButtonTemplate");
		gridCheck:SetPoint("TOPLEFT", lockPanel, "TOPLEFT", 6, -52); gridCheck:SetWidth(22); gridCheck:SetHeight(22);
		gridCheck:SetScript("OnShow", function() this:SetChecked(DB().showEditGrid and 1 or nil); end);
		gridCheck:SetScript("OnClick", function() DB().showEditGrid = this:GetChecked() and true or false; if ( DB().unlocked ) then ShowEditGrid(); end end);
		local gridLabel = lockPanel:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
		gridLabel:SetPoint("LEFT", gridCheck, "RIGHT", 0, 0); gridLabel:SetText("Grid");
		local gridSizes = {32, 64, 128, 256};
		lockPanel.gridButtons = {};
		for i = 1, table.getn(gridSizes) do
			local pixels = gridSizes[i];
			local button = K.Button(lockPanel, tostring(pixels), 48, function()
				DB().gridDivisions = pixels;
				for j = 1, table.getn(lockPanel.gridButtons) do lockPanel.gridButtons[j]:UnlockHighlight(); end
				this:LockHighlight();
				if ( DB().unlocked ) then ShowEditGrid(); end
			end);
			button:SetPoint("LEFT", gridLabel, "RIGHT", 5 + (i - 1) * 52, 0);
			table.insert(lockPanel.gridButtons, button);
		end
		local function AddGridLineToggle(x, key, labelText)
			local cb = CreateFrame("CheckButton", nil, lockPanel, "UICheckButtonTemplate");
			cb:SetPoint("TOPLEFT", lockPanel, "TOPLEFT", x, -82); cb:SetWidth(22); cb:SetHeight(22);
			cb:SetScript("OnShow", function() this:SetChecked(DB()[key] and 1 or nil); end);
			cb:SetScript("OnClick", function() DB()[key] = this:GetChecked() and true or false; if ( DB().unlocked ) then ShowEditGrid(); end end);
			local label = lockPanel:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
			label:SetPoint("LEFT", cb, "RIGHT", 0, 0); label:SetText(labelText);
		end
		AddGridLineToggle(8, "gridCenterVertical", "Vertical red center line");
		AddGridLineToggle(184, "gridCenterHorizontal", "Horizontal red center line");
		local snap = CreateFrame("CheckButton", nil, lockPanel, "UICheckButtonTemplate");
		snap:SetPoint("TOPLEFT", lockPanel, "TOPLEFT", 6, -111); snap:SetWidth(22); snap:SetHeight(22);
		snap:SetScript("OnShow", function() this:SetChecked(DB().anchorSnap and 1 or nil); end);
		snap:SetScript("OnClick", function() SetAnchorSnap(this:GetChecked() and true or false); end);
		local snapLabel = lockPanel:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
		snapLabel:SetPoint("LEFT", snap, "RIGHT", 0, 0); snapLabel:SetText("Snap nearby frames together");
		local button = K.Button(lockPanel, "Done", 76, function() AZSMinorMods_SetUnlocked(false); end);
		button:SetPoint("BOTTOMRIGHT", lockPanel, "BOTTOMRIGHT", -8, 7);
		lockPanel.doneButton = button;
		local choose = K.Button(lockPanel, "Choose element", 210, nil);
		choose:SetPoint("TOPLEFT", lockPanel, "TOPLEFT", 10, -141);
		local all;
		local choices = CreateFrame("Frame", nil, lockPanel);
		choices:SetFrameStrata("TOOLTIP"); choices:SetWidth(216);
		choices:SetHeight(table.getn(MOVABLE) * 23 + 8);
		choices:SetPoint("TOPLEFT", choose, "BOTTOMLEFT", 0, -2);
		if ( choices.SetClampedToScreen ) then choices:SetClampedToScreen(true); end
		choices:SetBackdrop({bgFile="Interface\\Tooltips\\UI-Tooltip-Background",edgeFile="Interface\\Tooltips\\UI-Tooltip-Border",edgeSize=12});
		choices:SetBackdropColor(0.04, 0.035, 0.07, 1);
		for i = 1, table.getn(MOVABLE) do
			local name, label = MOVABLE[i][1], MOVABLE[i][2];
			local row = K.Button(choices, label, 208, function()
				editOnly = name; choose:SetText(label .. " ▼"); all:UnlockHighlight(); choices:Hide();
				if ( sizeMenu ) then sizeMenu:Hide(); end
				AZSMinorMods_SetUnlocked(true, true);
			end);
			row:SetPoint("TOPLEFT", choices, "TOPLEFT", 4, -4 - (i - 1) * 23);
		end
		choices:Hide();
		choose:SetScript("OnClick", function() if ( choices:IsShown() ) then choices:Hide(); else choices:Show(); end end);
		all = K.Button(lockPanel, "ALL?", 62, function()
			editOnly = nil; choose:SetText("Choose element ▼"); all:LockHighlight(); choices:Hide();
			if ( sizeMenu ) then sizeMenu:Hide(); end
			AZSMinorMods_SetUnlocked(true, true);
		end);
		all:SetPoint("LEFT", choose, "RIGHT", 6, 0); all:LockHighlight();
	end
	local saved = DB().lockPanelPosition;
	lockPanel:ClearAllPoints();
	if ( type(saved.x) == "number" and type(saved.y) == "number" ) then
		lockPanel:SetPoint("TOPLEFT", UIParent, "TOPLEFT", saved.x, -saved.y);
	else
		lockPanel:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 18, -78);
	end
	for i = 1, table.getn(lockPanel.gridButtons or {}) do
		local pixels = ({32, 64, 128, 256})[i];
		if ( tonumber(DB().gridDivisions) == pixels ) then lockPanel.gridButtons[i]:LockHighlight();
		else lockPanel.gridButtons[i]:UnlockHighlight(); end
	end
	lockPanel:Show();
end

local function HideEditGrid()
	if ( editGrid ) then editGrid:Hide(); end
end

ShowEditGrid = function()
	HideEditGrid();
	if ( not DB().showEditGrid or not DB().unlocked ) then return; end
	if ( not editGrid ) then
		editGrid = CreateFrame("Frame", "AZSMinorModsEditGrid", UIParent);
		editGrid:SetAllPoints(UIParent);
		editGrid:SetFrameStrata("HIGH");
		editGrid:EnableMouse(false);
		editGrid:SetScript("OnSizeChanged", function()
			if ( DB().showEditGrid and DB().unlocked ) then ShowEditGrid(); end
		end);
	end
	local width, height = UIParent:GetWidth(), UIParent:GetHeight();
	local count, i = 1, 1;
	local function Line(x, y, w, h, color)
		local tex = editGridLines[count];
		if ( not tex ) then tex = editGrid:CreateTexture(nil, "OVERLAY"); editGridLines[count] = tex; end
		count = count + 1;
		tex:ClearAllPoints(); tex:SetPoint("TOPLEFT", editGrid, "TOPLEFT", x, -y); tex:SetWidth(w); tex:SetHeight(h);
		local c = color or { 0.95, 0.76, 0.3, 0.26 };
		tex:SetTexture(c[1], c[2], c[3], c[4]); tex:Show();
	end
	local divisions = tonumber(DB().gridDivisions) or 128;
	-- The chosen count is exact on the shorter edge. The other axis gets the nearest
	-- matching count so cells stay square-like, and every edge is a full grid boundary.
	local shorter = math.min(width, height);
	local longerCount = math.max(divisions, math.floor((math.max(width, height) / shorter) * divisions + 0.5));
	local columns, rows = width <= height and divisions or longerCount, height <= width and divisions or longerCount;
	local edge = { 0.95, 0.76, 0.3, 0.34 };
	Line(0, 0, 1, height, edge); Line(math.max(0, width - 1), 0, 1, height, edge);
	Line(0, 0, width, 1, edge); Line(0, math.max(0, height - 1), width, 1, edge);
	for i = 1, columns - 1 do Line(math.floor(i * width / columns + 0.5), 0, 1, height); end
	for i = 1, rows - 1 do Line(0, math.floor(i * height / rows + 0.5), width, 1); end
	if ( DB().gridCenterVertical ) then Line(math.floor(width / 2), 0, 2, height, { 1, 0.12, 0.12, 0.92 }); end
	if ( DB().gridCenterHorizontal ) then Line(0, math.floor(height / 2), width, 2, { 1, 0.12, 0.12, 0.92 }); end
	for i = count, table.getn(editGridLines) do editGridLines[i]:Hide(); end
	editGrid:Show();
end

local function SnapPosition(name, f)
	local left, right, bottom, top = RootBounds(f);
	if ( not left or not right or not bottom or not top ) then SavePosition(name, f); return; end
	local best, bestDistance;
	local threshold = 18;
	for i = 1, table.getn(MOVABLE) do
		local otherName = MOVABLE[i][1];
		local other = otherName ~= name and getglobal(otherName) or nil;
		if ( other ) then
			local ol, oright, ob, ot = RootBounds(other);
			if ( ol and oright and ob and ot ) then
				local overlapX = math.min(right, oright) - math.max(left, ol);
				local overlapY = math.min(top, ot) - math.max(bottom, ob);
				local candidates = {};
				if ( overlapX > 24 ) then
					table.insert(candidates, { math.abs(top - ob), "TOPLEFT", "BOTTOMLEFT", left - ol, top - ob });
					table.insert(candidates, { math.abs(bottom - ot), "BOTTOMLEFT", "TOPLEFT", left - ol, bottom - ot });
				end
				if ( overlapY > 24 ) then
					table.insert(candidates, { math.abs(left - oright), "BOTTOMLEFT", "BOTTOMRIGHT", left - oright, bottom - ob });
					table.insert(candidates, { math.abs(right - ol), "BOTTOMRIGHT", "BOTTOMLEFT", right - ol, bottom - ob });
				end
				for j = 1, table.getn(candidates) do
					local c = candidates[j];
					if ( c[1] <= threshold and (not bestDistance or c[1] < bestDistance) ) then
						bestDistance = c[1]; best = { relative = otherName, point = c[2], relativePoint = c[3], dx = c[4], dy = c[5] };
					end
				end
			end
		end
	end
	local p = { coordinateSpace = "uiParent" };
	if ( best ) then
		for k, v in pairs(best) do p[k] = v; end
	else
		p.x, p.y = left, bottom;
	end
	DB().places[name] = p;
	PlaceAll();
end

local function FinishHandleMove(h, name)
	local t = getglobal(name);
	if ( not t or not h.dragging ) then return; end
	h.dragging = nil;
	if ( DB().anchorSnap ) then SnapPosition(name, t);
	else SavePosition(name, t); DB().places[name].relative = nil; Place(name); end
end

local function BuffHandleBounds(frame)
	local left, right, bottom, top;
	local limit = MAX_PLAYER_BUFFS or 32;
	for i = 1, limit do
		local button = getglobal("BuffButton" .. i);
		if ( button and button:IsShown() ) then
			local l, r, b, t = RootBounds(button);
			if ( l and r and b and t ) then
				left = left and math.min(left, l) or l; right = right and math.max(right, r) or r;
				bottom = bottom and math.min(bottom, b) or b; top = top and math.max(top, t) or t;
			end
		end
	end
	if ( left and right and bottom and top ) then return left, bottom, right - left, top - bottom; end
	local l, r, b = RootBounds(frame);
	return l or 0, b or 0, 32, 32;
end

local function Handle(name, label)
	local f = getglobal(name);
	if ( not f ) then return nil; end
	local h = handles[name];
	if ( not h ) then
		h = CreateFrame("Frame", nil, UIParent);
		h:SetFrameStrata("DIALOG");
		h:SetBackdrop({ bgFile = "Interface\\Tooltips\\UI-Tooltip-Background", edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
		                tile = true, tileSize = 16, edgeSize = 12, insets = { left = 3, right = 3, top = 3, bottom = 3 } });
		if ( name == "GameTooltip" ) then
			h:SetBackdropColor(0, 0, 0, 0); -- keep tooltip text readable while its drag outline is active
			h:SetFrameStrata("TOOLTIP");
			if ( not RootBounds(f) ) then
				local size = DB().sizes.GameTooltip;
				if ( type(size) ~= "table" ) then size = { w = 260, h = 140 }; DB().sizes.GameTooltip = size; end
				f:SetWidth(size.w); f:SetHeight(size.h); f:ClearAllPoints();
				f:SetPoint("CENTER", UIParent, "CENTER", 0, 0); f:Hide();
			end
		else
			h:SetBackdropColor(0.1, 0.6, 0.1, 0.45);
		end
		h:SetBackdropBorderColor(0.3, 1, 0.3, 0.9);
		h:EnableMouse(true);
		h.text = h:CreateFontString(nil, "OVERLAY", "GameFontNormal");
		h.text:SetPoint("CENTER", h, "CENTER", 0, 0);
		if ( name == "GameTooltip" ) then h.text:Hide(); else h.text:SetText(label); end
		h:SetScript("OnMouseDown", function()
			if ( arg1 == "RightButton" ) then
				OpenSizeMenu(name, label);
				return;
			end
			if ( arg1 ~= "LeftButton" ) then return; end
			if ( IsShiftKeyDown and IsShiftKeyDown() ) then
				ToggleFeatureMute(name);
				return;
			end
			local t = getglobal(name);
			if ( not t ) then return; end
			t:SetMovable(true);
			-- We clamp the visible child bounds ourselves below; native clamping would keep the
			-- parent inside the screen and leave a gap when its buttons extend beyond its edges.
			if ( t.SetClampedToScreen ) then t:SetClampedToScreen(false); end
			if ( (name == "BuffFrame" or name == "MainMenuBar" or name == "PartyMemberFrame1") and t.SetUserPlaced ) then t:SetUserPlaced(true); end
			local scale = UIParent:GetEffectiveScale() or 1;
			local x, y = GetCursorPosition(); x, y = x / scale, y / scale;
			this.dragStartX, this.dragStartY = x, y;
			local left, right, bottom = RootBounds(t);
			this.frameStartX, this.frameStartY = left or x, bottom or y;
			local saved = DB().places[name];
			if ( type(saved) == "table" ) then saved.relative, saved.point, saved.relativePoint, saved.dx, saved.dy = nil, nil, nil, nil, nil; end
			this.dragging = true;
		end);
		h:SetScript("OnMouseUp", function()
			if ( arg1 ~= "LeftButton" ) then return; end
			FinishHandleMove(this, name);
		end);
		h:SetScript("OnUpdate", function()
			local t = getglobal(name);
			if ( not t ) then return; end
			if ( this.dragging ) then
				if ( IsMouseButtonDown and not IsMouseButtonDown("LeftButton") ) then
					FinishHandleMove(this, name);
					return;
				end
				local scale = UIParent:GetEffectiveScale() or 1;
				local x, y = GetCursorPosition(); x, y = x / scale, y / scale;
				local deltaX, deltaY = x - this.dragStartX, y - this.dragStartY;
				local nextX, nextY = this.frameStartX + deltaX, this.frameStartY + deltaY;
				local minX, minY, maxOffsetX, maxOffsetY = VisualInsets(t);
				local maxX = UIParent:GetWidth() - maxOffsetX;
				local maxY = UIParent:GetHeight() - maxOffsetY;
				nextX = math.max(-minX, math.min(maxX, nextX));
				nextY = math.max(-minY, math.min(maxY, nextY));
				SetRootPosition(t, nextX, nextY);
				SavePosition(name, t);          -- keep the newest location even if /reload happens mid-session
			end
			if ( name == "GameTooltip" and t:IsShown() ) then
				this:SetFrameStrata("TOOLTIP");
				if ( this:GetFrameLevel() <= t:GetFrameLevel() ) then this:SetFrameLevel(t:GetFrameLevel() + 1); end
			end
			this:ClearAllPoints();
            local l, r, b = RootBounds(t);
            if ( l and b ) then
                local x1, y1, x2, y2 = VisualInsets(t);
                this:SetPoint("BOTTOMLEFT", UIParent, "BOTTOMLEFT", l + x1, b + y1);
                this:SetWidth(math.max(1, x2 - x1)); this:SetHeight(math.max(1, y2 - y1));
            end
		end);
		handles[name] = h;
	end
	return h;
end

-- unlocked: the handles show (the frames are shown too, so even an empty party frame can be placed)
function AZSMinorMods_SetUnlocked(on, quiet)
	DB().unlocked = on and true or false;
	if ( on and GameTooltip ) then
		local size = DB().sizes.GameTooltip;
		if ( type(size) ~= "table" ) then size = { w = 260, h = 140 }; DB().sizes.GameTooltip = size; end
		GameTooltip:SetWidth(size.w); GameTooltip:SetHeight(size.h);
		if ( not RootBounds(GameTooltip) ) then
			GameTooltip:ClearAllPoints(); GameTooltip:SetPoint("CENTER", UIParent, "CENTER", 0, 0);
		end
		local saved = DB().places.GameTooltip;
		if ( type(saved) == "table" and saved.x and saved.y ) then SetRootPosition(GameTooltip, saved.x, saved.y); end
		GameTooltip:Hide();
	end
	if ( on ) then
		pcall(PrepareChatWindow);
		-- Reparent the stock XP bar only after the player explicitly opens the mover.
		pcall(DetachExperienceBar);
		pcall(PlaceAll);
	end
	if ( not on and sizeMenu ) then sizeMenu:Hide(); end
	if ( on ) then
		-- Leave the settings/menu window before presenting the in-world mover and its Done control.
		if ( win ) then win:Hide(); end
		ShowLockPanel();
	elseif ( lockPanel ) then
		lockPanel:Hide();
	end
	if ( on ) then ShowEditGrid(); else HideEditGrid(); end
	for i = 1, table.getn(MOVABLE) do
		local name = MOVABLE[i][1];
		if ( on and (not editOnly or editOnly == name) ) then
			local ok, h = pcall(Handle, name, MOVABLE[i][2]);
			if ( ok and h ) then h:Show(); end
		elseif ( handles[name] ) then
			handles[name]:Hide();
		end
		if ( DB().muted[name] ) then pcall(ApplyFeatureMute, name); end
	end
	pcall(UpdateLevelButton);
	pcall(UpdateMeter);
	if ( not quiet ) then K.Print(NAME, on and "drag a handle to move it; right-click for size, scale, layout or hide. Shift-left-click hides/shows a feature. Use Lock UI when done." or "the UI is locked."); end
end

local function SetMeterReplacement(on)
	DB().moveMeter = on and true or false;
	pcall(UpdateMeter);
end

local tooltipOwnerOriginal;
local tooltipShowOriginal;
local function HookTooltipOwner()
	if ( not GameTooltip or tooltipOwnerOriginal or not GameTooltip.SetOwner ) then return; end
	tooltipOwnerOriginal = GameTooltip.SetOwner;
	GameTooltip.SetOwner = function(self, owner, anchor, x, y)
		tooltipOwnerOriginal(self, owner, anchor, x, y);
		if ( self.SetClampedToScreen ) then self:SetClampedToScreen(true); end
		local size = DB().sizes.GameTooltip;
		if ( type(size) ~= "table" ) then size = { w = 260, h = 140 }; DB().sizes.GameTooltip = size; end
		self:SetWidth(size.w); self:SetHeight(size.h);
		local p = DB().places.GameTooltip;
		if ( DB().unlocked and type(p) == "table" and type(p.x) == "number" and type(p.y) == "number" ) then
			SetRootPosition(self, p.x, p.y);
		end
	end;
	if ( GameTooltip.Show and not tooltipShowOriginal ) then
		tooltipShowOriginal = GameTooltip.Show;
		GameTooltip.Show = function(self)
			local size = DB().sizes.GameTooltip;
			-- Set wrapping width before the game lays out its rows. Applying it only after Show
			-- leaves the old row heights in place and lets long text paint outside the tooltip.
			if ( type(size) == "table" and size.w ) then self:SetWidth(size.w); end
			tooltipShowOriginal(self);
			if ( type(size) == "table" and size.w and size.h ) then
				-- Some game tooltips contain more wrapped rows than the saved preferred height.
				-- Preserve their full measured height instead of letting the last row bleed out.
				local contentHeight = self:GetHeight() or 0;
				self:SetHeight(math.max(size.h, contentHeight));
			end
		end;
	end
end

SetAnchorSnap = function(on)
	DB().anchorSnap = on and true or false;
	if ( not on ) then
		for i = 1, table.getn(MOVABLE) do
			local name = MOVABLE[i][1];
			local p = DB().places[name];
			local f = getglobal(name);
			if ( type(p) == "table" and p.relative and f ) then
				SavePosition(name, f);
				p.relative, p.point, p.relativePoint, p.dx, p.dy = nil, nil, nil, nil, nil;
			end
		end
		PlaceAll();
	end
	K.Print(NAME, on and "nearby frames will latch together when you move them." or "frames will move independently now.");
end

function AZSMinorMods_ResetPlaces()
	for i = 1, table.getn(MOVABLE) do
		local name = MOVABLE[i][1];
		DB().places[name] = nil;
		local frame = getglobal(name);
		local points = basePoints[name];
		if ( frame and points ) then
			frame:ClearAllPoints();
			for j = 1, table.getn(points) do
				local p = points[j]; frame:SetPoint(p[1], p[2], p[3], p[4], p[5]);
			end
		end
	end
	DB().places.AZSMinorModsSpellRowFrame = AZSMinorMods_BuildRowDefaultPlace("AZSMinorModsSpellRowFrame");
	DB().places.AZSMinorModsItemRowFrame = AZSMinorMods_BuildRowDefaultPlace("AZSMinorModsItemRowFrame");
	PlaceAll();
	K.Print(NAME, "movable elements are back at their original locations.");
end

function AZSMinorMods_ResetElement(name)
	local frame = name and getglobal(name);
	if ( not frame ) then return false; end
	local d = DB();
	d.places[name], d.sizes[name], d.scales[name], d.padding[name], d.orientations[name] = nil, nil, nil, nil, nil;
	if ( name == "AZSMinorModsTallyFrame" ) then d.tallyVertical = false; end
	if ( name == "BuffFrame" ) then d.buffCustomLayout = false; d.buffGrowthDirection = "down"; end
	if ( name == "AZSMinorModsSpellRowFrame" or name == "AZSMinorModsItemRowFrame" ) then
		d.places[name] = AZSMinorMods_BuildRowDefaultPlace(name);
	end
	local points = basePoints[name];
	if ( points and name ~= "GameTooltip" ) then
		frame:ClearAllPoints();
		for i = 1, table.getn(points) do
			local p = points[i]; frame:SetPoint(p[1], p[2], p[3], p[4], p[5]);
		end
	end
	if ( name == "MainMenuBar" and actionBarOriginal and actionBarOriginal.buttonSize ) then
		for i = 1, 6 do
			local button = getglobal("ActionButton" .. i);
			if ( button ) then button:SetWidth(actionBarOriginal.buttonSize); button:SetHeight(actionBarOriginal.buttonSize); end
		end
	end
	local base = baseSizes[name];
	if ( base ) then frame:SetWidth(base.w); frame:SetHeight(base.h); end
	if ( name == "GameTooltip" ) then frame:SetWidth(260); frame:SetHeight(140); end
	if ( name == "GameTooltip" ) then frame:Hide(); end
	ApplySize(name, frame);
	if ( name == "AZSMinorModsTallyFrame" ) then ApplyTallyLayout(false); end
	if ( name == "AZSMinorModsBossFrames" ) then UpdateBossFrames(); end
	if ( name == "AZSMinorModsAnnouncementFrame" ) then ApplyMovablePadding(name); end
	if ( name == "BuffFrame" ) then pcall(ApplyBuffOrientation); end
	if ( name == "PartyMemberFrame1" or name == "MainMenuExpBar" or name == "MainMenuBar" ) then pcall(ApplyMovableOrientation, name); end
	if ( d.muted[name] ) then d.muted[name] = nil; pcall(ApplyFeatureMute, name); end
	PlaceAll();
	return true;
end

function AZSMinorMods_ResetUI()
	local d = DB();
	d.places, d.sizes, d.scales, d.padding, d.orientations, d.muted = {}, {}, {}, {}, {}, {};
	d.tallyVertical, d.buffCustomLayout, d.buffGrowthDirection = false, false, "down";
	d.buildRowsAttached = true;
	d.places.AZSMinorModsSpellRowFrame = AZSMinorMods_BuildRowDefaultPlace("AZSMinorModsSpellRowFrame");
	d.places.AZSMinorModsItemRowFrame = AZSMinorMods_BuildRowDefaultPlace("AZSMinorModsItemRowFrame");
	d.chatOffsets = {};
	for i = 1, table.getn(MOVABLE) do
		local name = MOVABLE[i][1];
		local frame, points, base = getglobal(name), basePoints[name], baseSizes[name];
		if ( frame and points and name ~= "GameTooltip" ) then
			frame:ClearAllPoints();
			for j = 1, table.getn(points) do
				local p = points[j]; frame:SetPoint(p[1], p[2], p[3], p[4], p[5]);
			end
		end
		if ( frame and base ) then frame:SetWidth(base.w); frame:SetHeight(base.h); end
		if ( frame and frame.SetScale ) then frame:SetScale(1); end
	end
	if ( actionBarOriginal and actionBarOriginal.buttonSize ) then
		for i = 1, 6 do
			local button = getglobal("ActionButton" .. i);
			if ( button ) then button:SetWidth(actionBarOriginal.buttonSize); button:SetHeight(actionBarOriginal.buttonSize); end
		end
	end
	if ( GameTooltip ) then GameTooltip:SetWidth(260); GameTooltip:SetHeight(140); GameTooltip:Hide(); end
	if ( type(PrepareChatWindow) == "function" ) then pcall(PrepareChatWindow); end
	if ( type(ApplyTallyLayout) == "function" ) then pcall(ApplyTallyLayout, false); end
	if ( type(UpdateBossFrames) == "function" ) then pcall(UpdateBossFrames); end
	pcall(ApplyMovablePadding, "AZSMinorModsAnnouncementFrame");
	if ( type(ApplyBuffOrientation) == "function" ) then pcall(ApplyBuffOrientation); end
	for _, name in ipairs({ "MainMenuBar", "MainMenuExpBar", "PartyMemberFrame1" }) do pcall(ApplyMovableOrientation, name); end
	for i = 1, table.getn(MOVABLE) do
		local name = MOVABLE[i][1];
		pcall(ApplyFeatureMute, name);
	end
	PlaceAll();
	if ( sizeMenu ) then sizeMenu:Hide(); end
	K.Print(NAME, "all movable UI elements are back at their addon defaults.");
end

-- the target frame: hidden for good while the setting is on (this game targets for you)
local targetShow;
local function TargetFrameHidden(on)
	if ( not TargetFrame ) then return; end
	if ( not targetShow ) then targetShow = TargetFrame.Show; end
	if ( on ) then
		TargetFrame.Show = function() end;
		TargetFrame:Hide();
	else
		TargetFrame.Show = targetShow;
		if ( UnitExists("target") ) then TargetFrame:Show(); end
	end
end

-- the plain action bar: the gryphons at its ends and all of the bar's art (the game's slices, its own extra ones and
-- the racial's old rim) made see-through. Keep six spell squares and the native racial button;
-- hide only the character and talent micro buttons.
local PLAIN_HIDE = { "CharacterMicroButton", "TalentMicroButton" };
local plainShow = {};
local function BarArt(alpha)
	local names = { "MainMenuBarLeftEndCap", "MainMenuBarRightEndCap", "AZS_RacialRim" };
	for i = 0, 3 do table.insert(names, "MainMenuBarTexture" .. i); end
	for i = 4, 12 do table.insert(names, "AZS_MainMenuBarTexture" .. i); end
	for i = 1, table.getn(names) do
		local t = getglobal(names[i]);
		if ( t and t.SetAlpha ) then t:SetAlpha(alpha); end
	end
	-- and every other texture straight on the bar's art frame (the end caps and slices sit there too)
	if ( MainMenuBarArtFrame and MainMenuBarArtFrame.GetRegions ) then
		local regions = { MainMenuBarArtFrame:GetRegions() };
		for i = 1, table.getn(regions) do
			local r = regions[i];
			if ( r and r.GetObjectType and r:GetObjectType() == "Texture" ) then r:SetAlpha(alpha); end
		end
	end
end

-- the bar's own width and its page arrows' places, kept to put back when the plain bar is turned off
local barWidth, arrowUp, arrowDown;
local function Narrow(on)
	if ( not MainMenuBar or not ActionButton1 or not ActionButton6 ) then return; end
	if ( not barWidth ) then
		barWidth = MainMenuBar:GetWidth();
		if ( ActionBarUpButton and ActionBarUpButton.GetPoint ) then arrowUp = { ActionBarUpButton:GetPoint(1) }; end
		if ( ActionBarDownButton and ActionBarDownButton.GetPoint ) then arrowDown = { ActionBarDownButton:GetPoint(1) }; end
	end
	if ( on ) then
		-- as wide as the six squares, plus room for the two page arrows on their right, as in the default bar
		local left = ActionButton1:GetLeft() and MainMenuBar:GetLeft() and (ActionButton1:GetLeft() - MainMenuBar:GetLeft()) or 8;
		local squares = ActionButton6:GetRight() and ActionButton1:GetLeft() and (ActionButton6:GetRight() - ActionButton1:GetLeft()) or 252;
		MainMenuBar:SetWidth(left + squares + 34);
		if ( ActionBarUpButton ) then
			ActionBarUpButton:ClearAllPoints();
			ActionBarUpButton:SetPoint("TOPLEFT", ActionButton6, "TOPRIGHT", 2, 6);
		end
		if ( ActionBarDownButton ) then
			ActionBarDownButton:ClearAllPoints();
			ActionBarDownButton:SetPoint("BOTTOMLEFT", ActionButton6, "BOTTOMRIGHT", 2, -6);
		end
	else
		MainMenuBar:SetWidth(barWidth);
		if ( ActionBarUpButton and arrowUp and arrowUp[1] ) then
			ActionBarUpButton:ClearAllPoints();
			ActionBarUpButton:SetPoint(arrowUp[1], arrowUp[2], arrowUp[3], arrowUp[4], arrowUp[5]);
		end
		if ( ActionBarDownButton and arrowDown and arrowDown[1] ) then
			ActionBarDownButton:ClearAllPoints();
			ActionBarDownButton:SetPoint(arrowDown[1], arrowDown[2], arrowDown[3], arrowDown[4], arrowDown[5]);
		end
	end
end

local function PlainBar(on)
	if ( not MainMenuBar ) then return; end
	BarArt(on and 0 or 1);
	pcall(ApplyMovableOrientation, "MainMenuBar");
	for i = 1, table.getn(PLAIN_HIDE) do
		local f = getglobal(PLAIN_HIDE[i]);
		if ( f ) then
			if ( not plainShow[f] ) then plainShow[f] = f.Show; end
			if ( on ) then
				f.Show = function() end;
				f:Hide();
			elseif ( f.Show ~= plainShow[f] ) then
				f.Show = plainShow[f];
				f:Show();
			end
		end
	end
end

-- the cooldown numbers: the seconds until each spell square's spell is ready again. The game's spells (AZS_SPELLS, the
-- racial last) map to ActionButton1 to 6, followed by the native racial ActionButton12.
local cdText = {};
local cdFrame = CreateFrame("Frame");
local cdAcc = 0;
cdFrame:SetScript("OnUpdate", function()
	cdAcc = cdAcc + (arg1 or 0);
	if ( cdAcc < 0.1 ) then return; end
	cdAcc = 0;
	local on = DB().cooldowns;
	local spells = type(AZS_SPELLS) == "table" and AZS_SPELLS or {};
	local n = table.getn(spells);
	for i = 1, 7 do
		local btn = getglobal("ActionButton" .. (i == 7 and 12 or i));
		if ( btn ) then
			local t = cdText[i];
			if ( not t ) then
				t = btn:CreateFontString(nil, "OVERLAY", "NumberFontNormalLarge");
				t:SetPoint("CENTER", btn, "CENTER", 0, 0);
				t:SetTextColor(1, 0.82, 0);
				cdText[i] = t;
			end
			local sp = i == 7 and spells[n] or ((i < n) and spells[i] or nil);      -- (the last is the racial)
			local left = sp and tonumber(sp.cd) or 0;
			if ( on and left and left > 0.1 ) then
				t:SetText(left >= 10 and tostring(math.floor(left + 0.5)) or string.format("%.1f", left));
				t:Show();
			else t:Hide(); end
		end
	end
end);

-- ---------------------------------------------------------------- the settings window
local win;

local function Check(parent, label, y, get, set)
	local cb, ok = K.Templated("CheckButton", nil, parent, "UICheckButtonTemplate");
	if ( not ok or not cb.SetChecked ) then
		cb = CreateFrame("CheckButton", nil, parent);
		cb:SetNormalTexture("Interface\\Buttons\\UI-CheckBox-Up");
		cb:SetPushedTexture("Interface\\Buttons\\UI-CheckBox-Down");
		cb:SetHighlightTexture("Interface\\Buttons\\UI-CheckBox-Highlight");
		cb:SetCheckedTexture("Interface\\Buttons\\UI-CheckBox-Check");
	end
	cb:SetWidth(26);
	cb:SetHeight(26);
	cb:SetPoint("TOPLEFT", parent, "TOPLEFT", 26, y);
	local fs = parent:CreateFontString(nil, "OVERLAY", "GameFontHighlight");
	fs:SetPoint("LEFT", cb, "RIGHT", 4, 1);
	fs:SetWidth(350); fs:SetHeight(28); fs:SetJustifyH("LEFT"); fs:SetText(label);
	cb:SetScript("OnShow", function() this:SetChecked(get() and 1 or nil); end);
	cb:SetScript("OnClick", function()
		set(this:GetChecked() and true or false);
		K.Sound(this:GetChecked() and "igMainMenuOptionCheckBoxOn" or "igMainMenuOptionCheckBoxOff");
	end);
	return cb;
end

local function WinBuild()
	win = K.Window("AZSMinorModsFrame", "Slim Suite", 470, 560);
	local hero = K.Hero(win, 380);
	hero:SetWidth(430);
	hero:SetPoint("TOPLEFT", win, "TOPLEFT", 20, -30);
	K.Icon(hero.icon, "INV_Misc_Gear_02");
	hero.heading:SetText("Slim Suite");
	hero.line:SetText("A Swiss-army knife of useful tools and UI controls.");
	local tabUi = K.Button(win, "Modifying the User Interface", 210, nil);
	tabUi:SetPoint("TOPLEFT", win, "TOPLEFT", 20, -101);
	local tabOther = K.Button(win, "New/Unique, But Minor Features", 210, nil);
	tabOther:SetPoint("TOPLEFT", tabUi, "TOPRIGHT", 4, 0);
	local bodyTop = -132;
	local uiScroll, uiChild = K.Scroll("AZSMinorModsUiScroll", win, 422);
	uiScroll:SetPoint("TOPLEFT", win, "TOPLEFT", 20, bodyTop);
	uiScroll:SetPoint("BOTTOMRIGHT", win, "BOTTOMRIGHT", -30, 48);
	local otherScroll, otherChild = K.Scroll("AZSMinorModsOtherScroll", win, 422);
	otherScroll:SetPoint("TOPLEFT", win, "TOPLEFT", 20, bodyTop);
	otherScroll:SetPoint("BOTTOMRIGHT", win, "BOTTOMRIGHT", -30, 48);
	local addonsScroll, addonsChild = K.Scroll("AZSMinorModsAddonsScroll", win, 422);
	addonsScroll:SetPoint("TOPLEFT", win, "TOPLEFT", 20, bodyTop);
	addonsScroll:SetPoint("BOTTOMRIGHT", win, "BOTTOMRIGHT", -30, 48);
	local function AddRows(parent, entries, startY)
		local y = startY;
		for i = 1, table.getn(entries) do
			local row = entries[i]; Check(parent, row[1], y, row[2], row[3]); y = y - 28;
		end
		parent:SetHeight(math.max(20, -y + 12));
	end
	local function CheckboxRows()
		local noteTitle = uiChild:CreateFontString(nil, "OVERLAY", "GameFontNormal");
		noteTitle:SetPoint("TOPLEFT", uiChild, "TOPLEFT", 10, -8); noteTitle:SetText("About UI replacements");
		local note = uiChild:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
		note:SetPoint("TOPLEFT", noteTitle, "BOTTOMLEFT", 0, -3); note:SetWidth(390); note:SetHeight(38);
		note:SetJustifyH("LEFT"); note:SetJustifyV("TOP"); note:SetText("Most custom game UI is not available as reusable addon art or code. This section recreates it as faithfully as possible so it can be moved; a few implementation choices are unavoidable. <3");
		AddRows(uiChild, {
			{"Toggle UI Settings (drag, resize and scale)", function() return DB().unlocked; end, function(v) AZSMinorMods_SetUnlocked(v); end},
			{"Hide the target frame", function() return DB().hideTarget; end, function(v) DB().hideTarget = v; TargetFrameHidden(v); end},
			{"Replace Default Level Up with Movable Version?", function() return DB().moveLevelButton; end, function(v) DB().moveLevelButton = v; pcall(UpdateLevelButton); end},
			{"Open UI modifier at startup", function() return DB().startUnlocked; end, function(v) DB().startUnlocked = v; end},
			{"Replace Default Build Rows with Movable Versions?", function() return DB().replaceBuildRows; end, function(v) DB().replaceBuildRows = v; ApplyBuildRowReplacement(); end},
			{"Replace Default Damage Meter with Movable Version?", function() return DB().moveMeter; end, function(v) SetMeterReplacement(v); end},
			{"Replace Default Run Clock with Movable Version?", function() return DB().replaceClock;end,function(v) DB().replaceClock=v;UpdateClock();end},
			{"Replace Default Encounter Bars with Movable Versions?", function() return DB().replaceEncounters;end,function(v) DB().replaceEncounters=v;UpdateBossFrames();end},
			{"Replace Default Kills / Gold / Cache with Movable Version?", function() return DB().replaceTally;end,function(v) DB().replaceTally=v;UpdateTally();end},
			{"Replace Default Announcements with Movable Version?", function() return DB().replaceBanners;end,function(v) DB().replaceBanners=v;UpdateAnnouncementFrame();end},
			{"Replace Default Action Bar Art with Plain Version?", function() return DB().plainBar; end, function(v) DB().plainBar = v; PlainBar(v); end},
			{"Add racial ability as a 7th Action Bar 1 button?", function() return DB().showRacialAbility; end, function(v) AZSMinorMods_SetRacialAbility(v); end},
			{"Show empty slots in Action Bar 1", function() return DB().showEmptyActionSlots; end, function(v) DB().showEmptyActionSlots = v; ApplyActionSlotGrid(); end}
		}, -72);
		uiChild:SetHeight(505);
	end
	local function BuildOther()
		local purpose=otherChild:CreateFontString(nil,"OVERLAY","GameFontHighlightSmall");
		purpose:SetPoint("TOPLEFT",otherChild,"TOPLEFT",10,-8);purpose:SetWidth(390);purpose:SetHeight(32);
		purpose:SetJustifyH("LEFT");purpose:SetJustifyV("TOP");purpose:SetText("The goal of clearing up chat is to see your friends' chat better in multiplayer.");
		local title=otherChild:CreateFontString(nil,"OVERLAY","GameFontNormal");title:SetPoint("TOPLEFT",otherChild,"TOPLEFT",10,-43);title:SetText("Chat mods");
		AddRows(otherChild, {
			{"No level-up stat bonuses in chat", function() return DB().hideLevel;end,function(v) DB().hideLevel=v;end},
			{"No new spell lines in chat", function() return DB().hideNewSpells;end,function(v) DB().hideNewSpells=v;end},
			{"Hide racial buff information in chat", function() return DB().hideRacialInfo;end,function(v) DB().hideRacialInfo=v;end},
			{"Hide the Oogax Q opening line", function() return DB().hideOpeningLine;end,function(v) DB().hideOpeningLine=v;end},
			{"Hide 'Who dares to enter my lair'", function() return DB().hideLairLine;end,function(v) DB().hideLairLine=v;end},
			{"Hide in-game roleplay lines from chat", function() return DB().hideRP;end,function(v) DB().hideRP=v;end},
			{"Camera as far out as it goes (50)", function() return DB().camera; end, function(v) DB().camera = v; Camera(v); end},
			{"Mark bosses in spawn order (Skull, X, Circle, Star)", function() return DB().bossMarks; end, function(v) DB().bossMarks = v; pcall(BossMarks); end},
			{"Show the game's nearest Guardian lair flag on minimap", function() return DB().showGuardianFlag; end, function(v) DB().showGuardianFlag = v; ApplyGuardianFlag(); end},
			{"Blink a marker over a friend who falls (16 times)", function() return DB().deathMarks; end, function(v) DB().deathMarks = v; end},
			{"Play a sound when you or a teammate dies", function() return DB().deathSound; end, function(v) DB().deathSound = v; end},
			{"Cooldown numbers on the spell squares", function() return DB().cooldowns; end, function(v) DB().cooldowns = v; end},
			{"Minimap button", function() local m = DB().minimap; return not (m and m.hide); end, function(v) if ( AZSMinorMods_Mini ) then local m = DB().minimap; if ( (m and m.hide and true or false) == v ) then AZSMinorMods_Mini.Toggle(); end end end}
		}, -68);
	end
	local function BuildAddonManager()
		local title = addonsChild:CreateFontString(nil, "OVERLAY", "GameFontNormal");
		title:SetPoint("TOPLEFT", addonsChild, "TOPLEFT", 8, -8); title:SetText("Enabled game addons");
		local note = addonsChild:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
		note:SetPoint("TOPLEFT", title, "BOTTOMLEFT", 0, -6); note:SetText("Changes apply after reload. Uncheck to disable an addon.");
		local n = (GetNumAddOns and GetNumAddOns()) or 0;
		local y = -48;
		for index = 1, n do
			local name, addonTitle, addonNotes, enabled = GetAddOnInfo(index);
			local state = GetAddOnEnableState and GetAddOnEnableState(nil, index) or (enabled and 1 or 0);
			if ( name and string.sub(string.lower(name), 1, 9) ~= "blizzard_" and (state or 0) > 0 ) then
				local cb = CreateFrame("CheckButton", nil, addonsChild, "UICheckButtonTemplate");
				cb:SetPoint("TOPLEFT", addonsChild, "TOPLEFT", 6, y); cb:SetWidth(24); cb:SetHeight(24);
				cb:SetChecked((state or 0) > 0 and 1 or nil);
				cb:SetScript("OnClick", function()
					if ( this:GetChecked() ) then EnableAddOn(name); else DisableAddOn(name); end
				end);
				local label = addonsChild:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
				label:SetPoint("LEFT", cb, "RIGHT", 4, 0); label:SetWidth(372); label:SetJustifyH("LEFT"); label:SetText(addonTitle or name);
				y = y - 26;
			end
		end
		addonsChild:SetHeight(math.max(64, -y + 12));
		local reload = K.Button(win, "Reload for changes", 160, function() if ( ReloadUI ) then ReloadUI(); end end);
		reload:SetPoint("BOTTOMLEFT", win, "BOTTOMLEFT", 28, 17);
		win.reloadButton = reload;
	end
	CheckboxRows(); BuildOther(); BuildAddonManager();
	local tooltipTitle = uiChild:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
	tooltipTitle:SetPoint("TOPLEFT", uiChild, "TOPLEFT", 10, -420);
	tooltipTitle:SetText("Tooltip size");
	local function AdjustTooltipSize(delta)
		local size = DB().sizes.GameTooltip;
		if ( type(size) ~= "table" ) then size = { w = 260, h = 140 }; DB().sizes.GameTooltip = size; end
		size.w = math.max(160, math.min(600, size.w + delta));
		size.h = math.max(80, math.min(500, size.h + delta));
		if ( GameTooltip ) then GameTooltip:SetWidth(size.w); GameTooltip:SetHeight(size.h); end
		PlaceAll();
	end
	local tooltipSmaller = K.Button(uiChild, "Smaller", 82, function() AdjustTooltipSize(-20); end);
	tooltipSmaller:SetPoint("TOPLEFT", uiChild, "TOPLEFT", 10, -438);
	local tooltipLarger = K.Button(uiChild, "Larger", 82, function() AdjustTooltipSize(20); end);
	tooltipLarger:SetPoint("LEFT", tooltipSmaller, "RIGHT", 6, 0);
	local reset = K.Button(uiChild, "Reset positions", 140, function() AZSMinorMods_ResetPlaces(); end);
	reset:SetPoint("TOPLEFT", uiChild, "TOPLEFT", 10, -472);
	local resetAll = K.Button(uiChild, "Reset all UI", 126, function() AZSMinorMods_ResetUI(); end);
	resetAll:SetPoint("LEFT", reset, "RIGHT", 8, 0);
	uiChild:SetHeight(505);
	local function SelectPage(which)
		uiScroll:Hide(); otherScroll:Hide(); addonsScroll:Hide();
		if ( win.reloadButton ) then win.reloadButton:Hide(); end
		if ( which == "ui" ) then uiScroll:Show(); tabUi:LockHighlight(); tabOther:UnlockHighlight();
		elseif ( which == "other" ) then otherScroll:Show(); tabUi:UnlockHighlight(); tabOther:LockHighlight();
		else addonsScroll:Show(); if ( win.reloadButton ) then win.reloadButton:Show(); end end
	end
	tabUi:SetScript("OnClick", function() SelectPage("ui"); end);
	tabOther:SetScript("OnClick", function() SelectPage("other"); end);
	local addonsTab = K.Button(otherChild, "Manage Addons", 132, function() SelectPage("addons"); end);
	addonsTab:SetPoint("TOPLEFT", otherChild, "TOPLEFT", 10, -340);
	otherChild:SetHeight(378);
	SelectPage("other");
end

function AZSMinorMods_Toggle()
	if ( not win ) then
		local ok, err = pcall(WinBuild);
		if ( not ok ) then K.Print(NAME, "|cffff4040the settings window could not be made:|r " .. tostring(err)); win = nil; return; end
	end
	if ( win:IsShown() ) then win:Hide(); else win:Show(); end
end

SLASH_AZSMINORMODS1 = "/azsmods";
SLASH_AZSMINORMODS2 = "/minormods";
SlashCmdList["AZSMINORMODS"] = function(msg)
	msg = msg or "";
	local _, _, cmd, rest = string.find(msg, "^(%S*)%s*(.-)$");
	cmd = string.lower(cmd or "");
	rest = string.lower(rest or "");
	local d = DB();
	if ( cmd == "hide" and rest ~= "" ) then
		table.insert(d.words, rest);
		K.Print(NAME, "chat lines with \"" .. rest .. "\" are hidden now.");
	elseif ( cmd == "unhide" and rest ~= "" ) then
		for i = table.getn(d.words), 1, -1 do if ( d.words[i] == rest ) then table.remove(d.words, i); end end
		K.Print(NAME, "chat lines with \"" .. rest .. "\" are shown again.");
	elseif ( cmd == "minimap" ) then
		local shown = AZSMinorMods_Mini and AZSMinorMods_Mini.Toggle();
		K.Print(NAME, shown and "the minimap button is back." or "the minimap button is hidden. /azsmods minimap brings it back.");
	elseif ( cmd == "unlock" or cmd == "move" ) then
		AZSMinorMods_SetUnlocked(true);
	elseif ( cmd == "lock" ) then
		AZSMinorMods_SetUnlocked(false);
	else
		AZSMinorMods_Toggle();
	end
end

local loader = CreateFrame("Frame");
loader:RegisterEvent("VARIABLES_LOADED");
loader:RegisterEvent("PLAYER_ENTERING_WORLD");
loader:SetScript("OnEvent", function()
	pcall(RegisterAnnouncementHandler);
	local ok, err = pcall(HookChat);
	if ( not ok ) then K.Print(NAME, "|cffff4040the chat could not be changed:|r " .. tostring(err)); end
	-- The editor state is a user choice, not a leftover from an earlier session. Clear legacy
	-- unlocked saves at load; only the explicit startup option may reopen the mover after login.
	if ( event == "VARIABLES_LOADED" ) then DB().unlocked = DB().startUnlocked and true or false; end
	pcall(Camera, event == "PLAYER_ENTERING_WORLD");
	pcall(ApplyGuardianFlag);
	-- the frames back where they were put (the game places some of them again as it loads), and the target frame
	if ( DB().unlocked or DB().startUnlocked or type(DB().places.MainMenuExpBar) == "table"
		or type(DB().sizes.MainMenuExpBar) == "table" or type(DB().scales.MainMenuExpBar) == "number"
		or DB().orientations.MainMenuExpBar ~= nil ) then pcall(DetachExperienceBar); end
	if ( type(DB().places.ChatFrame1) == "table" ) then pcall(PrepareChatWindow); end
	pcall(ApplyTallyLayout, false);
	pcall(PlaceAll);
	pcall(TargetFrameHidden, DB().hideTarget);
	if ( DB().plainBar ) then pcall(PlainBar, true); end
	pcall(ApplyMovableOrientation, "MainMenuBar");
	pcall(ApplyMovableOrientation, "MainMenuExpBar");
	pcall(ApplyMovableOrientation, "BuffFrame");

	pcall(ApplyMovableOrientation, "PartyMemberFrame1");
	pcall(ApplyActionSlotGrid);
	pcall(HookTooltipOwner);
	pcall(PlaceAll);
	pcall(UpdateMeter);
	if ( event == "PLAYER_ENTERING_WORLD" ) then pcall(AZSMinorMods_SetUnlocked, DB().startUnlocked and true or false, true); end
end);
local buffLayoutUpdater = CreateFrame("Frame");
buffLayoutUpdater:RegisterEvent("UNIT_AURA");
buffLayoutUpdater:SetScript("OnEvent", function()
	if ( arg1 == "player" and (CurrentOrientation("BuffFrame") == "vertical" or DB().buffCustomLayout) ) then pcall(ApplyBuffOrientation); end
end);
pcall(HookChat);                                          -- right away too, so lines printed while loading are caught

-- the minimap button: a gear, at the left of the minimap (AZS Stats' is at the bottom, AZS Handbook's at the top
-- left)
local okMini, errMini = pcall(function()
	AZSMinorMods_Mini = K.Minimap({ name = "AZSMinorModsMinimapButton", icon = "INV_Misc_Gear_02", angle = 190, db = DB,
		click = function() AZSMinorMods_Toggle(); end,
		tooltip = function(tt)
			tt:AddLine(NAME);
			tt:AddLine("Gameplay helpers and UI controls.", 1, 1, 1);
			tt:AddLine(" ");
			tt:AddLine("Click: the settings. /azsmods minimap hides this button.", 0.6, 0.6, 0.6);
		end });
end);
if ( not okMini ) then K.Print(NAME, "|cffff4040the minimap button could not be made:|r " .. tostring(errMini)); end
