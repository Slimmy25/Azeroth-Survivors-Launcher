-- AZS Minor Mods: small changes to the game's chat, in AZS Stats' look (AZSKit.lua).
--   * The level-up lines are taken out of the chat: "You have reached level 12", the stat bonus that comes with it
--     ("You have gained 3% damage, 12 maximum health ...") and the new spell ("You have learned a new spell: Cleave
--     (Level 1)"), in every language the game has. Lines are matched without case; /azsmods hide <words> hides lines
--     with those words too, /azsmods unhide <words> stops that. What players say is never hidden.
--   * The camera goes as far out as the game allows, 50 yards (the Max Camera Distance setting at its top), and starts there. Turn it off in
--     /azsmods to keep the game's own setting.
--   * Boss markers: every boss alive gets a raid marker, the first a Skull, the second a Cross, the third a Diamond.
--   * Miniboss markers: the zone's bosses before its final boss (the Guide's Zones & Bosses list) get a Star, a
--     Triangle and a Diamond instead, so the Skull and Cross are left for the final boss.
--   * In co-op, a friend who falls gets a marker over their head, blinking on and off 16 times. Each friend has their
--     own (Square, Moon, Circle), so several can be down at once. Those three are kept for this alone.
--   * Play the included sound once when you or a teammate dies (off until enabled).
--   * Move, resize and scale the UI: drag a green handle to move it; right-click for width, height and scale.
--     The action bar, cast bar, party frames and kills/gold/cache tally can switch between horizontal and vertical layouts.
--     Bar 2 and Right bar are intentionally not included. Optional anchoring latches nearby movable frames together.
--     Positions and custom sizes are kept (Reset puts positions back; each size menu can reset its own frame).
--     The experience bar is detached from the action bar and gets its own handle.
--     The primary chat window, tab and input box move together from one Chat handle.
--   * The kills, gold and next-cache cost use the documented AZS_RUN values in a movable replacement for the tally.
--     Right-click its green handle to switch between a compact horizontal row and a vertical three-line layout.
--   * The run clock and next-boss countdown stay together in one movable replacement for the game's clock.
--   * Event announcements use the game's localized text, remain visible for 10 seconds, fade at both ends, and move
--     with their own handle. They are separate from the "Move to Begin" prompt.
--   * The stored-levels button is a movable replacement that opens the game's own choices through its documented API.
--   * Active boss, guardian and elite bars use transparent compact cards that grow to the right; right-click to
--     switch to narrow vertical bars. HP uses the documented current/maximum values, refreshed on AZSUNITS updates.
--   * Hide the target frame: this game picks its own targets, so the frame can go.
--   * Plain action bar: the gryphons and the bar's art go, and only the six spell squares are left, with the bar
--     no wider than they are (and the two page arrows), so it leaves no empty space.
--   * Cooldown numbers: each spell square shows the seconds until its spell is ready again.
-- /azsmods (or the gear by the minimap) opens the settings window: every one of these on or off.
-- Lua 5.0 / 1.12 interface: scripts read `this`, `event`, `arg1`, `arg2`; no # operator; only the 1.12 API.

local K = AZSKIT;
local NAME = "AZS Minor Mods";

-- the words that make a chat line a level-up line (matched without case): the game's own two level-up lines,
-- "Congratulations, you have reached level 12!" (its LEVEL_UP text) and "You have gained 3% damage, 12 maximum health
-- and 0.5 health per second.", and the usual ways of saying it
local LEVEL_WORDS = { "you have reached level", "you have gained", "level up", "leveled up", "levelled up", "reached level",
                      "gained a level", "you are now level", "level-up", "ding!" };

-- the stat bonus a level-up gives, in each of the game's languages (a piece of the line that has no number in it), as
-- the game itself writes "You have gained {damage}% damage, {health} maximum health and {regen} health per second."
local STAT_WORDS = { "maximum health and", "maximale gesundheit und", "de salud máxima y", "de vida máxima e",
                     "points de vie maximum et", "maksymalnego zdrowia i", "maximálního zdraví a", "до макс. здоров'я і",
                     "к макс. здоровью и", "、最大体力+", "最大生命值和每秒", ", 최대 생명력 ", " azami can ve saniyede ",
                     " viață maximă și " };
-- the game's "New spell" in its languages
local NEW_SPELL_WORDS = { "new spell", "neuer zauber", "hechizo nuevo", "novo feitiço", "nouveau sort", "nowe zaklęcie",
                          "nové kouzlo", "vrajă nouă", "yeni büyü", "нове закляття", "новое заклинание", "新しい呪文",
                          "新法术", "새 주문" };

-- the game's LEVEL_UP text in the game's language: the part before the number
local function LevelText()
	if ( type(LEVEL_UP) ~= "string" ) then return nil; end
	local s = string.find(LEVEL_UP, "%", 1, true);
	local head = s and string.sub(LEVEL_UP, 1, s - 1) or LEVEL_UP;
	head = string.lower(string.gsub(head, "^%s+", ""));
	if ( string.len(head) < 6 ) then return nil; end
	return head;
end

local function DB()
	if ( type(AZSMinorModsDB) ~= "table" ) then AZSMinorModsDB = {}; end
	local d = AZSMinorModsDB;
	if ( d.hideLevel == nil ) then d.hideLevel = true; end
	if ( d.camera == nil ) then d.camera = true; end
	if ( d.plainBar == nil ) then d.plainBar = false; end
	if ( d.cooldowns == nil ) then d.cooldowns = true; end
	d.box, d.bloat, d.x, d.y = nil, nil, nil, nil;          -- the retired Say & Party box and "Remove bloat"
	if ( d.bossMarks == nil ) then d.bossMarks = true; end
	if ( d.deathMarks == nil ) then d.deathMarks = true; end
	if ( d.deathSound == nil ) then d.deathSound = false; end
	if ( d.miniMarks == nil ) then d.miniMarks = true; end
	if ( d.hideTarget == nil ) then d.hideTarget = false; end
	if ( d.moveLevelButton == nil ) then d.moveLevelButton = true; end
	if ( type(d.places) ~= "table" ) then d.places = {}; end
	if ( type(d.sizes) ~= "table" ) then d.sizes = {}; end
	if ( type(d.scales) ~= "table" ) then d.scales = {}; end
	if ( type(d.orientations) ~= "table" ) then d.orientations = {}; end
	if ( d.tallyVertical == nil ) then d.tallyVertical = false; end
	if ( type(d.chatOffsets) ~= "table" ) then d.chatOffsets = {}; end
	if ( d.anchorSnap == nil ) then d.anchorSnap = false; end
	if ( type(d.words) ~= "table" ) then d.words = {}; end
	return d;
end

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

local function Hidden(msg)
	if ( type(msg) ~= "string" ) then return false; end
	if ( string.find(msg, "|Hplayer:", 1, true) ) then return false; end   -- a player's own words
	local d = DB();
	local low = string.lower(string.gsub(msg, "|c%x%x%x%x%x%x%x%x", ""));
	if ( d.hideLevel ) then
		-- the level-up, its stat bonus and the new spell it taught
		local own = LevelText();
		if ( own and string.find(low, own, 1, true) ) then return true; end
		if ( Any(low, LEVEL_WORDS) or Any(low, STAT_WORDS) or Any(low, NEW_SPELL_WORDS) or LearnLine(low) ) then return true; end
		if ( string.find(low, "%(level %d+%)") ) then return true; end
	end
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

local function HookChat()
	for i = 1, 7 do pcall(HookFrame, getglobal("ChatFrame" .. i)); end
	pcall(HookFrame, DEFAULT_CHAT_FRAME);
	if ( type(ChatFrame_OnEvent) == "function" and not hooked.event ) then
		hooked.event = true;
		local orig = ChatFrame_OnEvent;
		ChatFrame_OnEvent = function(ev)
			local e = ev or event;
			-- what players say, yell, whisper or write in a channel is theirs: only the game's own lines are hidden
			local players = e == "CHAT_MSG_SAY" or e == "CHAT_MSG_YELL" or e == "CHAT_MSG_WHISPER" or e == "CHAT_MSG_PARTY" or
			                e == "CHAT_MSG_PARTY_LEADER" or e == "CHAT_MSG_RAID" or e == "CHAT_MSG_GUILD" or e == "CHAT_MSG_CHANNEL" or
			                e == "CHAT_MSG_EMOTE" or e == "CHAT_MSG_OFFICER" or e == "CHAT_MSG_RAID_LEADER";
			if ( not players and type(arg1) == "string" and e and string.find(e, "^CHAT_MSG_") and Hidden(arg1) ) then return; end
			return orig(ev);
		end;
	end
end

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

-- ---------------------------------------------------------------- raid markers on bosses, and on friends who fall
-- The game's raid markers (they work alone and in co-op): the bosses alive get a Skull, a Cross and a Diamond, in the
-- order they came (AZS_GetBosses). With miniboss markers on, the minibosses among them get a Star, a Triangle and a
-- Diamond, and the Skull and Cross go to the rest (the final boss, and whoever it brings). A marker needs the boss as
-- a unit: the target, the mouseover, a friend's target, or, when none of those is it, a quick target-and-back by its
-- name. Square, Moon and Circle are never put on a boss: they blink over friends who fall.
local BOSS_ICONS = { 8, 7, 3 };                          -- Skull, Cross, Diamond
local FINAL_ICONS = { 8, 7 };                            -- Skull, Cross (with miniboss markers on)
local MINI_ICONS = { 1, 4, 3 };                          -- Star, Triangle, Diamond
local DEATH_ICONS = { 6, 5, 2 };                         -- Square, Moon, Circle: one for each friend who falls
local DEATH_BLINKS = 16;                                 -- on and off this many times
local DEATH_SOUND = "Interface\\AddOns\\AZSMinorMods\\Baby Crying.mp3";
local function PlayDeathSound()
	if ( not DB().deathSound or not PlaySoundFile ) then return; end
	pcall(PlaySoundFile, DEATH_SOUND);
end

local deathSoundFrame = CreateFrame("Frame");
deathSoundFrame:RegisterEvent("PLAYER_DEAD");
deathSoundFrame:SetScript("OnEvent", function() PlayDeathSound(); end);

-- the minibosses: every zone's bosses before its final boss, as the Guide lists them (names without case)
local MINIBOSSES = {
	-- Elwynn Forest (final boss Edwin VanCleef is left to the boss markers)
	["hogger"] = true, ["mother fang"] = true, ["rhahk'zor"] = true, ["mr. smite"] = true,
	-- Duskwood (final boss Morbent Fel is left to the boss markers)
	["naraxis"] = true, ["mor'ladim"] = true, ["stitches"] = true, ["lupos"] = true,
	-- Warsong Gulch (final boss Lord Kazzak is left to the boss markers)
	["doomguard commander"] = true, ["mannoroth's herald"] = true, ["varimathras"] = true, ["lord hel'nurath"] = true,
	-- Un'Goro Crater (final boss King Mosh is left to the boss markers)
	["ravasaur matriarch"] = true, ["uhk'loc"] = true, ["lar'korwi"] = true, ["tyrant devilsaur"] = true,
	-- Arathi Basin (final boss Lord Aliden Perenolde is left to the boss markers)
	["kovork"] = true, ["nimar the slayer"] = true, ["marez cowl"] = true, ["lord falconcrest"] = true,
	-- Feralas (final boss King Gordok is left to the boss markers)
	["zevrim thornhoof"] = true, ["alzzin the wildshaper"] = true, ["tendris warpwood"] = true, ["immol'thar"] = true,
	-- Gurubashi Arena (final boss Hakkar the Soulflayer is left to the boss markers)
	["high priestess jeklik"] = true, ["high priest venoxis"] = true, ["bloodlord mandokir"] = true, ["jin'do the hexxer"] = true,
	-- Scarlet Monastery (final boss High Inquisitor Whitemane is left to the boss markers)
	["houndmaster loksey"] = true, ["arcanist doan"] = true, ["herod"] = true, ["scarlet commander mograine"] = true,
	-- Alterac Valley (final boss Azuregos is left to the boss markers)
	["korrak the bloodrager"] = true, ["ivus the forest lord"] = true, ["lokholar the ice lord"] = true, ["glacius the frozen titan"] = true,
	-- Blackrock Mountain (final boss Nefarian is left to the boss markers)
	["general angerforge"] = true, ["warchief rend blackhand"] = true, ["emperor dagran thaurissan"] = true, ["majordomo executus"] = true,
	-- Gates of Ahn'Qiraj (final boss C'Thun is left to the boss markers)
	["kurinnaxx"] = true, ["general rajaxx"] = true, ["princess huhuran"] = true, ["ossirian the unscarred"] = true,
	-- Naxxramas (final boss Kel'Thuzad is left to the boss markers)
	["anub'rekhan"] = true, ["noth the plaguebringer"] = true, ["patchwerk"] = true, ["sapphiron"] = true,
};
local function IsMini(name) return name and MINIBOSSES[string.lower(name)] and true or false; end
local marked = {};                                        -- boss id -> its marker
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

local function MarkBoss(name, icon, id)
	local list = Units();
	for i = 1, table.getn(list) do
		local u = list[i];
		if ( UnitExists(u) and UnitName(u) == name and not UnitIsPlayer(u) and not UnitIsDead(u) ) then
			return Mark(u, icon);
		end
	end
	-- not one of them: target it by its name, mark it, and give back the target there was
	if ( not TargetByName or (tries[id] or 0) >= 3 ) then return false; end
	tries[id] = (tries[id] or 0) + 1;
	local had = UnitExists("target") and UnitName("target") or nil;
	if ( not pcall(TargetByName, name, true) ) then return false; end
	local ok = false;
	if ( UnitExists("target") and UnitName("target") == name ) then ok = Mark("target", icon); end
	if ( had and had ~= name ) then
		if ( TargetLastTarget ) then pcall(TargetLastTarget); end
	elseif ( not had and ClearTarget ) then
		pcall(ClearTarget);
	end
	return ok;
end

local function BossMarks()
	local d = DB();
	if ( not (d.bossMarks or d.miniMarks) or type(AZS_GetBosses) ~= "function" ) then return; end
	-- only while a run is being played: out of it the list can still hold the last run's bosses
	if ( type(AZS_GetPhase) == "function" and AZS_GetPhase() ~= "playing" ) then return; end
	local ok, bosses = pcall(AZS_GetBosses);
	if ( not ok or type(bosses) ~= "table" ) then return; end
	local alive = {};
	local bossList = d.miniMarks and FINAL_ICONS or BOSS_ICONS;
	local nb, nm = 0, 0;                                   -- bosses and minibosses so far, each in the order they came
	for i = 1, table.getn(bosses) do
		local b = bosses[i];
		if ( b.id ) then alive[b.id] = true; end
		local icon = nil;
		if ( d.miniMarks and IsMini(b.name) ) then
			nm = nm + 1;
			icon = MINI_ICONS[nm];
		else
			nb = nb + 1;
			if ( d.bossMarks ) then icon = bossList[nb]; end
		end
		if ( icon and b.id and b.name and marked[b.id] ~= icon ) then
			if ( MarkBoss(b.name, icon, b.id) ) then marked[b.id] = icon; end
		end
	end
	for id in pairs(marked) do if ( not alive[id] ) then marked[id] = nil; end end
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
-- follows. Locked, the handles go and the frames stay where they were put; the places are put back at every login.
local MOVABLE = { { "PlayerFrame", "Player" }, { "TargetFrame", "Target" }, { "PartyMemberFrame1", "Party" },
                  { "MinimapCluster", "Minimap" }, { "BuffFrame", "Buffs" }, { "CastingBarFrame", "Cast bar" },
                  { "MainMenuBar", "Action bars" }, { "MainMenuExpBar", "XP bar" }, { "ChatFrame1", "Chat" },
                  { "AZSMinorModsStoredLevelsButton", "Level Up" },
                  { "AZSMinorModsClockFrame", "Run clock / Next boss" },
                  { "AZSMinorModsBossFrames", "Encounter bars" },
                  { "AZSMinorModsAnnouncementFrame", "Event announcements" },
                  { "AZSMinorModsTallyFrame", "Kills / Gold / Cache" } };
local handles = {};
local baseSizes = {};
local sizeMenu, sizeMenuText, sizeMenuWidth, sizeMenuHeight, sizeMenuScale;
local sizeTarget;
local lockPanel;
local xpBarDetached = false;
local tallyFrame;
local tallyNativeWasShown;
local tallyNativeOverridden = false;
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
	if ( not tallyFrame ) then return; end
	local phase = type(AZS_GetPhase) == "function" and AZS_GetPhase() or nil;
	local active = phase and phase ~= "ready";
	local run = type(AZS_RUN) == "table" and AZS_RUN or nil;
	if ( active and run and (tallyNativeWasShown or DB().unlocked) ) then
		local kills, gold, cache = TallyValue(run.kills), TallyValue(run.gold), TallyValue(run.chest);
		if ( DB().tallyVertical ) then
			tallyFrame.text:SetText("Kills  " .. kills .. "\nGold  " .. gold .. "\nCache  " .. cache);
		else
			tallyFrame.text:SetText("Kills " .. kills .. "    Gold " .. gold .. "    Cache " .. cache);
		end
		tallyFrame:Show();
		SetNativeTallyHidden(true);
	elseif ( DB().unlocked ) then
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
	local canReplace = type(AZS_GetStoredLevels) == "function"
		and type(AZS_OpenStoredLevels) == "function" and type(AZS_SetHudShown) == "function";
	local replacing = DB().moveLevelButton and canReplace and levelNativeWasShown == true;
	SetNativeLevelButtonHidden(replacing);
	if ( not replacing ) then levelButton:Hide(); return; end
	local count = 0;
	local ok, value = pcall(AZS_GetStoredLevels);
	if ( ok and type(value) == "number" ) then count = math.max(0, math.floor(value)); end
	local level = type(UnitLevel) == "function" and UnitLevel("player") or nil;
	levelButton.level:SetText("LEVEL " .. (level and tostring(level) or ""));
	levelButton.count:SetText(count > 0 and tostring(count) or "");
	if ( count > 0 or DB().unlocked ) then levelButton:Show(); else levelButton:Hide(); end
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
	levelButton:SetHighlightTexture("Interface\\Buttons\\UI-Panel-Button-Highlight");
	levelButton.level = levelButton:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall");
	levelButton.level:SetPoint("TOPLEFT", levelButton, "TOPLEFT", 12, -8);
	levelButton.level:SetTextColor(1, 0.78, 0.22);
	levelButton.text = levelButton:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge");
	levelButton.text:SetPoint("BOTTOMLEFT", levelButton, "BOTTOMLEFT", 12, 8);
	levelButton.text:SetText("Level Up!");
	levelButton:SetScript("OnClick", function()
		if ( type(AZS_OpenStoredLevels) == "function" ) then pcall(AZS_OpenStoredLevels); end
	end);
	levelButton.count = levelButton:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall");
	levelButton.count:SetPoint("TOPRIGHT", levelButton, "TOPRIGHT", -12, -8);
	levelButton.count:SetTextColor(1, 0.78, 0.18);
	if ( MainMenuBar ) then levelButton:SetPoint("BOTTOMLEFT", MainMenuBar, "TOPLEFT", 0, 6);
	else levelButton:SetPoint("BOTTOMLEFT", UIParent, "BOTTOMLEFT", 32, 100); end
	levelButton:Hide();
end

local function ClockText(seconds)
	if ( type(seconds) ~= "number" or seconds < 0 ) then return "--:--"; end
	seconds = math.floor(seconds);
	return string.format("%02d:%02d", math.floor(seconds / 60), math.mod(seconds, 60));
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

local ENCOUNTER_FONT = "Interface\\AddOns\\AZSMinorMods\\SourceSerif4SmText-Bold.ttf";
local function SetEncounterFont(fontString, size)
	local ok, result = pcall(fontString.SetFont, fontString, ENCOUNTER_FONT, size, "OUTLINE");
	if ( not ok or result == false ) then
		pcall(fontString.SetFont, fontString, "Fonts\\FRIZQT__.TTF", size, "OUTLINE");
	end
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
			table.insert(list, { id = unit.id, name = unit.name, hp = unit.hp, hpm = unit.hpm, priority = priority, order = i });
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
	local uiScale = UIParent:GetEffectiveScale() or 1;
	local frameScale = f:GetEffectiveScale() or uiScale;
	local factor = frameScale / uiScale;
	local width = (f:GetWidth() or 0) * factor;
	local height = (f:GetHeight() or 0) * factor;
	local maxX = math.max(0, UIParent:GetWidth() - width);
	local maxY = math.max(0, UIParent:GetHeight() - height);
	local x, y = f:GetLeft(), f:GetBottom();
	if ( x == nil and type(saved) == "table" ) then x = saved.x; end
	if ( y == nil and type(saved) == "table" ) then y = saved.y; end
	if ( type(x) ~= "number" or type(y) ~= "number" ) then return; end
	local nx = math.max(0, math.min(maxX, x));
	local ny = math.max(0, math.min(maxY, y));
	if ( nx ~= x or ny ~= y ) then
		f:ClearAllPoints();
		f:SetPoint("BOTTOMLEFT", UIParent, "BOTTOMLEFT", nx, ny);
	end
end

local function EncounterRow(index)
	if ( encounterRows[index] ) then return encounterRows[index]; end
	local row = CreateFrame("Frame", nil, bossFrame);
	row:SetWidth(184);
	row:SetHeight(40);
	row.name = row:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall");
	row.name:SetPoint("TOPLEFT", row, "TOPLEFT", 4, -2);
	row.name:SetWidth(176);
	row.name:SetHeight(22);
	row.name:SetJustifyH("LEFT");
	row.name:SetJustifyV("TOP");
	row.name:SetWordWrap(true);
	SetEncounterFont(row.name, 10);
	row.name:SetTextColor(1, 0.26, 0.16);
	row.bar = CreateFrame("StatusBar", nil, row);
	row.bar:SetWidth(176);
	row.bar:SetHeight(8);
	row.bar:SetPoint("TOPLEFT", row, "TOPLEFT", 4, -28);
	row.bar:SetStatusBarTexture("Interface\\TargetingFrame\\UI-StatusBar");
	row.bar:SetStatusBarColor(0.88, 0.08, 0.035, 1);
	row.bar:SetMinMaxValues(0, 1);
	row.bar:SetValue(1);
	row.bar.background = row.bar:CreateTexture(nil, "BACKGROUND");
	row.bar.background:SetTexture("Interface\\TargetingFrame\\UI-StatusBar");
	row.bar.background:SetAllPoints(row.bar);
	row.bar.background:SetVertexColor(0.04, 0.03, 0.03, 0.28);
	row:EnableMouse(false);
	encounterRows[index] = row;
	return row;
end

local function UpdateEncounterRows(list)
	local count = table.getn(list);
	if ( count == 0 ) then return 0; end
	local vertical = EncounterVertical();
	local gap = 8;
	local configured = DB().sizes.AZSMinorModsBossFrames;
	local defaultCardWidth = vertical and 76 or 184;
	local minCardWidth = vertical and 56 or 120;
	local frameWidth = type(configured) == "table" and configured.w or (count * defaultCardWidth + ((count - 1) * gap));
	local cardWidth = math.max(minCardWidth, (frameWidth - ((count - 1) * gap)) / count);
	frameWidth = count * cardWidth + ((count - 1) * gap);
	local frameHeight = type(configured) == "table" and configured.h or (vertical and 140 or 40);
	frameHeight = math.max(vertical and 100 or 36, frameHeight);
	bossFrame:SetWidth(frameWidth);
	bossFrame:SetHeight(frameHeight);
	for i = 1, table.getn(list) do
		local unit = list[i];
		local row = EncounterRow(i);
		row:SetWidth(cardWidth);
		row:SetHeight(frameHeight);
		row.name:ClearAllPoints();
		row.bar:ClearAllPoints();
		row:ClearAllPoints();
		row:SetPoint("TOPLEFT", bossFrame, "TOPLEFT", (i - 1) * (cardWidth + gap), 0);
		row.name:SetText(unit.name);
		if ( vertical ) then
			SetEncounterFont(row.name, 8.5);
			row.name:SetWidth(cardWidth - 4);
			row.name:SetHeight(30);
			row.name:SetPoint("TOPLEFT", row, "TOPLEFT", 2, -2);
			if ( row.bar.SetOrientation ) then pcall(row.bar.SetOrientation, row.bar, "VERTICAL"); end
			row.bar:SetWidth(math.min(12, cardWidth - 12));
			row.bar:SetHeight(math.max(24, frameHeight - 40));
			row.bar:SetPoint("TOP", row, "TOP", 0, -36);
		else
			SetEncounterFont(row.name, 10);
			row.name:SetWidth(cardWidth - 8);
			row.name:SetHeight(22);
			row.name:SetPoint("TOPLEFT", row, "TOPLEFT", 4, -2);
			if ( row.bar.SetOrientation ) then pcall(row.bar.SetOrientation, row.bar, "HORIZONTAL"); end
			row.bar:SetWidth(math.max(36, cardWidth - 8));
			row.bar:SetHeight(math.min(8, math.max(5, frameHeight - 30)));
			row.bar:SetPoint("TOPLEFT", row, "TOPLEFT", 4, -28);
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
	if ( not clockFrame ) then return; end
	local phase = type(AZS_GetPhase) == "function" and AZS_GetPhase() or nil;
	local live = phase == "ready" or phase == "playing" or phase == "choosing" or phase == "paused";
	local showClock = clockNativeWasShown == true and live;
	local preview = DB().unlocked and not showClock;
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
	if ( not bossFrame ) then return; end
	local phase = type(AZS_GetPhase) == "function" and AZS_GetPhase() or nil;
	local live = phase == "playing" or phase == "choosing" or phase == "paused";
	if ( bossNativeWasShown == true and live ) then
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
	elseif ( DB().unlocked ) then
		for i = 1, table.getn(encounterRows) do encounterRows[i]:Hide(); end
		local vertical = EncounterVertical();
		local configured = DB().sizes.AZSMinorModsBossFrames;
		bossFrame:SetWidth(type(configured) == "table" and configured.w or (vertical and 76 or 184));
		bossFrame:SetHeight(type(configured) == "table" and configured.h or (vertical and 140 or 40));
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
bossFrame:SetWidth(440);
bossFrame:SetHeight(30);
bossFrame:SetPoint("TOP", UIParent, "TOP", 0, -64);
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
	announcementFrame.text:SetWordWrap(true);
	SetEncounterFont(announcementFrame.text, 22);
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

local function ShowAnnouncement(banner)
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
	elseif ( DB().unlocked ) then
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
		if ( bannerNativeWasShown == true ) then SetNativeBannersHidden(true); end
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
	end
end);

local function SavePosition(name, f)
	local p = DB().places[name];
	if ( type(p) ~= "table" ) then p = {}; DB().places[name] = p; end
	p.x, p.y = f:GetLeft(), f:GetBottom();
end

local function ApplySize(name, f)
	local s = DB().sizes[name];
	if ( type(s) == "table" and s.w and s.h and s.w > 0 and s.h > 0 ) then
		f:SetWidth(s.w);
		f:SetHeight(s.h);
	end
	local scale = DB().scales[name];
	local value = type(scale) == "number" and scale or 1;
	if ( name == "PartyMemberFrame1" ) then
		for i = 1, 4 do local party = getglobal("PartyMemberFrame" .. i); if ( party and party.SetScale ) then party:SetScale(value); end end
	elseif ( f.SetScale ) then f:SetScale(value); end
end

local function CurrentOrientation(name)
	local orientation = DB().orientations[name];
	if ( orientation == "vertical" or orientation == "horizontal" ) then return orientation; end
	if ( name == "PartyMemberFrame1" ) then return "vertical"; end
	return "horizontal";
end

local function CaptureActionBarLayout()
	if ( actionBarOriginal or not MainMenuBar ) then return; end
	local saved = DB().actionBarHorizontalSize;
	local w, h = MainMenuBar:GetWidth(), MainMenuBar:GetHeight();
	if ( type(saved) == "table" and saved.w and saved.h ) then w, h = saved.w, saved.h;
	else DB().actionBarHorizontalSize = { w = w, h = h }; end
	actionBarOriginal = { w = w, h = h, buttons = {} };
	for i = 1, 6 do
		local button = getglobal("ActionButton" .. i);
		if ( button and button.GetNumPoints ) then
			local points = {};
			for p = 1, button:GetNumPoints() do
				local point, relative, relativePoint, x, y = button:GetPoint(p);
				points[p] = { point, relative, relativePoint, x, y };
			end
			actionBarOriginal.buttons[i] = points;
		end
	end
end

local function ApplyMovableOrientation(name)
	local orientation = CurrentOrientation(name);
	if ( name == "MainMenuBar" ) then
		if ( orientation == "vertical" ) then
			CaptureActionBarLayout();
			local first = getglobal("ActionButton1");
			if ( not first ) then return; end
			local bw, bh, count = first:GetWidth(), first:GetHeight(), 0;
			for i = 1, 6 do if ( getglobal("ActionButton" .. i) ) then count = count + 1; end end
			for i = 1, 6 do
				local button = getglobal("ActionButton" .. i);
				if ( button ) then
					button:ClearAllPoints();
					button:SetPoint("TOPLEFT", MainMenuBar, "TOPLEFT", 8, -8 - ((i - 1) * (bh + 4)));
				end
			end
			MainMenuBar:SetWidth(bw + 16);
			MainMenuBar:SetHeight((count * (bh + 4)) + 8);
			DB().sizes.MainMenuBar = { w = MainMenuBar:GetWidth(), h = MainMenuBar:GetHeight() };
		elseif ( actionBarOriginal ) then
			for i = 1, 6 do
				local button, points = getglobal("ActionButton" .. i), actionBarOriginal.buttons[i];
				if ( button and points ) then
					button:ClearAllPoints();
					for p = 1, table.getn(points) do
						local a = points[p];
						button:SetPoint(a[1], a[2], a[3], a[4], a[5]);
					end
				end
			end
			MainMenuBar:SetWidth(actionBarOriginal.w); MainMenuBar:SetHeight(actionBarOriginal.h);
			DB().sizes.MainMenuBar = { w = actionBarOriginal.w, h = actionBarOriginal.h };
		end
	elseif ( name == "CastingBarFrame" ) then
		local cast = CastingBarFrame;
		if ( cast and cast.SetOrientation ) then
			cast:SetOrientation(string.upper(orientation));
		end
	elseif ( name == "AZSMinorModsBossFrames" ) then
		UpdateBossFrames();
	elseif ( name == "PartyMemberFrame1" ) then
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

local function Place(name, ignoreAnchor)
	local f = getglobal(name);
	local p = DB().places[name];
	if ( not f ) then return; end
	local w, h = f:GetWidth(), f:GetHeight();
	if ( w and h and w > 0 and h > 0 and not baseSizes[name] ) then baseSizes[name] = { w = w, h = h }; end
	ApplySize(name, f);
	if ( (name == "BuffFrame" or name == "MainMenuBar") and f.SetUserPlaced ) then f:SetUserPlaced(true); end
	if ( type(p) ~= "table" ) then return; end
	local relative = not ignoreAnchor and p.relative and getglobal(p.relative) or nil;
	if ( relative and p.point and p.relativePoint ) then
		f:ClearAllPoints();
		f:SetPoint(p.point, relative, p.relativePoint, K.Num(p.dx), K.Num(p.dy));
	elseif ( p.x and p.y ) then
		-- Saved coordinates can come from a taller or wider display. Clamp absolute restores to this UIParent
		-- without rewriting the saved location, so a later return to the larger display restores the user's layout.
		local uiScale = UIParent:GetEffectiveScale() or 1;
		local frameScale = f:GetEffectiveScale() or uiScale;
		local factor = frameScale / uiScale;
		local maxX = math.max(0, UIParent:GetWidth() - ((f:GetWidth() or 0) * factor));
		local maxY = math.max(0, UIParent:GetHeight() - ((f:GetHeight() or 0) * factor));
		local x = math.max(0, math.min(maxX, K.Num(p.x)));
		local y = math.max(0, math.min(maxY, K.Num(p.y)));
		f:ClearAllPoints();
		f:SetPoint("BOTTOMLEFT", UIParent, "BOTTOMLEFT", x, y);
	end
end

local function PlaceAll()
	for i = 1, table.getn(MOVABLE) do pcall(Place, MOVABLE[i][1], true); end
	-- Reapply saved frame-to-frame anchors after every movable frame has its stored size and base position.
	for pass = 1, table.getn(MOVABLE) do
		for i = 1, table.getn(MOVABLE) do pcall(Place, MOVABLE[i][1], false); end
	end
end

local function ApplyTallyLayout(resetSize)
	if ( not tallyFrame ) then return; end
	local vertical = DB().tallyVertical and true or false;
	if ( resetSize ) then
		local w, h = vertical and 104 or 252, vertical and 54 or 28;
		tallyFrame:SetWidth(w); tallyFrame:SetHeight(h);
		DB().sizes.AZSMinorModsTallyFrame = { w = w, h = h };
	end
	if ( vertical ) then
		tallyFrame.text:ClearAllPoints();
		tallyFrame.text:SetPoint("TOPLEFT", tallyFrame, "TOPLEFT", 4, -2);
		tallyFrame.text:SetJustifyH("LEFT");
		tallyFrame.text:SetJustifyV("TOP");
	else
		tallyFrame.text:ClearAllPoints();
		tallyFrame.text:SetPoint("LEFT", tallyFrame, "LEFT", 4, 0);
		tallyFrame.text:SetPoint("RIGHT", tallyFrame, "RIGHT", -4, 0);
		tallyFrame.text:SetJustifyH("LEFT");
		tallyFrame.text:SetJustifyV("MIDDLE");
	end
	PlaceAll();
	UpdateTally();
end

local function ToggleMovableOrientation(name)
	if ( name == "AZSMinorModsTallyFrame" ) then
		DB().tallyVertical = not DB().tallyVertical;
		ApplyTallyLayout(true);
		return;
	end
	local nextOrientation = CurrentOrientation(name) == "vertical" and "horizontal" or "vertical";
	DB().orientations[name] = nextOrientation;
	if ( name == "AZSMinorModsBossFrames" ) then DB().sizes[name] = nil; end
	if ( name == "CastingBarFrame" ) then
		local f = CastingBarFrame;
		if ( f ) then
			local w, h = f:GetWidth(), f:GetHeight();
			f:SetWidth(h); f:SetHeight(w);
			DB().sizes[name] = { w = h, h = w };
		end
	end
	ApplyMovableOrientation(name);
	PlaceAll();
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
	local left, bottom = MainMenuExpBar:GetLeft(), MainMenuExpBar:GetBottom();
	local width, height = MainMenuExpBar:GetWidth(), MainMenuExpBar:GetHeight();
	MainMenuExpBar:SetParent(UIParent);
	MainMenuExpBar:ClearAllPoints();
	if ( left and bottom ) then
		MainMenuExpBar:SetPoint("BOTTOMLEFT", UIParent, "BOTTOMLEFT", left, bottom);
	else
		MainMenuExpBar:SetPoint("BOTTOM", UIParent, "BOTTOM", 0, 8);
	end
	if ( width and width > 0 ) then MainMenuExpBar:SetWidth(width); end
	if ( height and height > 0 ) then MainMenuExpBar:SetHeight(height); end
	xpBarDetached = true;
end

local function OpenSizeMenu(name, label)
	local f = getglobal(name);
	if ( not f ) then return; end
	if ( not sizeMenu ) then
		sizeMenu = CreateFrame("Frame", nil, UIParent);
		sizeMenu:SetWidth(206);
		sizeMenu:SetHeight(180);
		sizeMenu:SetFrameStrata("DIALOG");
		sizeMenu:SetBackdrop({ bgFile = "Interface\\Tooltips\\UI-Tooltip-Background", edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
		                      tile = true, tileSize = 16, edgeSize = 12, insets = { left = 3, right = 3, top = 3, bottom = 3 } });
		sizeMenu:SetBackdropColor(0.04, 0.035, 0.07, 0.97);
		sizeMenu:SetBackdropBorderColor(0.85, 0.65, 0.2, 1);
		sizeMenuText = sizeMenu:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall");
		sizeMenuText:SetPoint("TOPLEFT", sizeMenu, "TOPLEFT", 12, -10);
		sizeMenuWidth = sizeMenu:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
		sizeMenuWidth:SetPoint("TOPLEFT", sizeMenu, "TOPLEFT", 12, -34);
		sizeMenuHeight = sizeMenu:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
		sizeMenuHeight:SetPoint("TOPLEFT", sizeMenu, "TOPLEFT", 12, -63);
		sizeMenuScale = sizeMenu:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
		sizeMenuScale:SetPoint("TOPLEFT", sizeMenu, "TOPLEFT", 12, -92);
		local function ResizeBy(axis, delta)
			if ( not sizeTarget ) then return; end
			local t = getglobal(sizeTarget);
			if ( not t ) then return; end
			local w, h = t:GetWidth(), t:GetHeight();
			if ( axis == "w" ) then w = math.max(40, math.min((UIParent:GetWidth() or 1920), w + delta));
			else h = math.max(24, math.min((UIParent:GetHeight() or 1080), h + delta)); end
			t:SetWidth(w); t:SetHeight(h);
			DB().sizes[sizeTarget] = { w = w, h = h };
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
		local scaleDown = K.Button(sizeMenu, "-", 30, function()
			if ( not sizeTarget ) then return; end
			local scale = math.max(0.5, math.floor(((DB().scales[sizeTarget] or 1) - 0.1) * 10 + 0.5) / 10);
			DB().scales[sizeTarget] = scale;
			PlaceAll();
			sizeMenuScale:SetText("Scale: " .. math.floor(scale * 100 + 0.5) .. "%");
		end);
		scaleDown:SetPoint("TOPRIGHT", sizeMenu, "TOPRIGHT", -72, -84);
		local scaleUp = K.Button(sizeMenu, "+", 30, function()
			if ( not sizeTarget ) then return; end
			local scale = math.min(2, math.floor(((DB().scales[sizeTarget] or 1) + 0.1) * 10 + 0.5) / 10);
			DB().scales[sizeTarget] = scale;
			PlaceAll();
			sizeMenuScale:SetText("Scale: " .. math.floor(scale * 100 + 0.5) .. "%");
		end);
		scaleUp:SetPoint("TOPRIGHT", sizeMenu, "TOPRIGHT", -32, -84);
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
		local reset = K.Button(sizeMenu, "Reset", 62, function()
			local t = sizeTarget and getglobal(sizeTarget);
			local s = sizeTarget and baseSizes[sizeTarget];
			if ( t and sizeTarget == "AZSMinorModsTallyFrame" ) then
				DB().scales[sizeTarget] = nil;
				t:SetScale(1);
				ApplyTallyLayout(true);
				OpenSizeMenu(sizeTarget, sizeTarget);
			elseif ( t and sizeTarget == "AZSMinorModsBossFrames" ) then
				DB().scales[sizeTarget], DB().sizes[sizeTarget] = nil, nil;
				t:SetScale(1);
				ApplyMovableOrientation(sizeTarget);
				PlaceAll();
				OpenSizeMenu(sizeTarget, sizeTarget);
			elseif ( t and s ) then
				DB().scales[sizeTarget] = nil;
				if ( sizeTarget == "CastingBarFrame" and CurrentOrientation(sizeTarget) == "vertical" ) then
					t:SetWidth(s.h); t:SetHeight(s.w);
				else t:SetWidth(s.w); t:SetHeight(s.h); end
				DB().sizes[sizeTarget] = nil;
				if ( sizeTarget == "MainMenuBar" and CurrentOrientation(sizeTarget) == "vertical" ) then ApplyMovableOrientation(sizeTarget); end
				PlaceAll();
				OpenSizeMenu(sizeTarget, sizeTarget);
			else
				K.Print(NAME, "the original size is not available yet; try again after the frame appears.");
			end
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
	sizeMenuScale:SetText("Scale: " .. math.floor(scale * 100 + 0.5) .. "%");
	if ( sizeMenu.layoutButton ) then
		local canOrient = name == "AZSMinorModsTallyFrame" or name == "AZSMinorModsBossFrames" or name == "MainMenuBar" or name == "PartyMemberFrame1" or (name == "CastingBarFrame" and CastingBarFrame and CastingBarFrame.SetOrientation);
		if ( canOrient ) then
			sizeMenu.layoutButton:Show();
			local orientation = name == "AZSMinorModsTallyFrame" and (DB().tallyVertical and "vertical" or "horizontal") or CurrentOrientation(name);
			sizeMenu.layoutButton:SetText(orientation == "vertical" and "Switch to horizontal" or "Switch to vertical");
		else
			sizeMenu.layoutButton:Hide();
		end
	end
	sizeMenu:ClearAllPoints();
	local scale = UIParent:GetEffectiveScale() or 1;
	local x, y = GetCursorPosition(); x, y = x / scale, y / scale;
	local sw, sh = UIParent:GetWidth(), UIParent:GetHeight();
	x = math.max(0, math.min(sw - 206, x + 8));
	y = math.max(0, math.min(sh - 180, y + 8));
	sizeMenu:SetPoint("BOTTOMLEFT", UIParent, "BOTTOMLEFT", x, y);
	sizeMenu:Show();
end

local function ShowLockPanel()
	if ( not lockPanel ) then
		lockPanel = CreateFrame("Frame", "AZSMinorModsMovePanel", UIParent);
		lockPanel:SetWidth(250);
		lockPanel:SetHeight(58);
		lockPanel:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 18, -78);
		lockPanel:SetFrameStrata("DIALOG");
		lockPanel:SetBackdrop({ bgFile = "Interface\\Tooltips\\UI-Tooltip-Background", edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
		                       tile = true, tileSize = 16, edgeSize = 12, insets = { left = 3, right = 3, top = 3, bottom = 3 } });
		lockPanel:SetBackdropColor(0.04, 0.035, 0.07, 0.97);
		lockPanel:SetBackdropBorderColor(0.85, 0.65, 0.2, 1);
		local note = lockPanel:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
		note:SetPoint("TOPLEFT", lockPanel, "TOPLEFT", 10, -8);
		note:SetText("Drag a green handle to move. Right-click to resize, scale or change layout.");
		local button = K.Button(lockPanel, "Lock UI", 76, function() AZSMinorMods_SetUnlocked(false); end);
		button:SetPoint("BOTTOMRIGHT", lockPanel, "BOTTOMRIGHT", -8, 6);
	end
	lockPanel:Show();
end

local function SnapPosition(name, f)
	local left, right, bottom, top = f:GetLeft(), f:GetRight(), f:GetBottom(), f:GetTop();
	if ( not left or not right or not bottom or not top ) then SavePosition(name, f); return; end
	local best, bestDistance;
	local threshold = 18;
	for i = 1, table.getn(MOVABLE) do
		local otherName = MOVABLE[i][1];
		local other = otherName ~= name and getglobal(otherName) or nil;
		if ( other ) then
			local ol, oright, ob, ot = other:GetLeft(), other:GetRight(), other:GetBottom(), other:GetTop();
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
	local p = {};
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

local function Handle(name, label)
	local f = getglobal(name);
	if ( not f ) then return nil; end
	local h = handles[name];
	if ( not h ) then
		h = CreateFrame("Frame", nil, UIParent);
		h:SetFrameStrata("DIALOG");
		h:SetBackdrop({ bgFile = "Interface\\Tooltips\\UI-Tooltip-Background", edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
		                tile = true, tileSize = 16, edgeSize = 12, insets = { left = 3, right = 3, top = 3, bottom = 3 } });
		h:SetBackdropColor(0.1, 0.6, 0.1, 0.45);
		h:SetBackdropBorderColor(0.3, 1, 0.3, 0.9);
		h:EnableMouse(true);
		h.text = h:CreateFontString(nil, "OVERLAY", "GameFontNormal");
		h.text:SetPoint("CENTER", h, "CENTER", 0, 0);
		h.text:SetText(label);
		h:SetScript("OnMouseDown", function()
			if ( arg1 == "RightButton" ) then
				OpenSizeMenu(name, label);
				return;
			end
			if ( arg1 ~= "LeftButton" ) then return; end
			local t = getglobal(name);
			if ( not t ) then return; end
			t:SetMovable(true);
			if ( t.SetClampedToScreen ) then t:SetClampedToScreen(true); end
			if ( (name == "BuffFrame" or name == "MainMenuBar" or name == "PartyMemberFrame1") and t.SetUserPlaced ) then t:SetUserPlaced(true); end
			local scale = UIParent:GetEffectiveScale() or 1;
			local x, y = GetCursorPosition(); x, y = x / scale, y / scale;
			this.dragOffsetX, this.dragOffsetY = x - (t:GetLeft() or x), y - (t:GetBottom() or y);
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
				local maxX = math.max(0, UIParent:GetWidth() - ((t:GetWidth() or 0) * ((t:GetEffectiveScale() or scale) / scale)));
				local maxY = math.max(0, UIParent:GetHeight() - ((t:GetHeight() or 0) * ((t:GetEffectiveScale() or scale) / scale)));
				x = math.max(0, math.min(maxX, x - this.dragOffsetX));
				y = math.max(0, math.min(maxY, y - this.dragOffsetY));
				t:ClearAllPoints();
				t:SetPoint("BOTTOMLEFT", UIParent, "BOTTOMLEFT", x, y);
			end
			this:ClearAllPoints();
			this:SetPoint("TOPLEFT", t, "TOPLEFT", 0, 0);
			this:SetPoint("BOTTOMRIGHT", t, "BOTTOMRIGHT", 0, 0);
		end);
		handles[name] = h;
	end
	return h;
end

-- unlocked: the handles show (the frames are shown too, so even an empty party frame can be placed)
function AZSMinorMods_SetUnlocked(on)
	DB().unlocked = on and true or false;
	if ( on ) then pcall(PrepareChatWindow); end
	if ( not on and sizeMenu ) then sizeMenu:Hide(); end
	if ( on ) then
		if ( win and win:IsShown() ) then win:Hide(); end
		ShowLockPanel();
	elseif ( lockPanel ) then
		lockPanel:Hide();
	end
	for i = 1, table.getn(MOVABLE) do
		local name = MOVABLE[i][1];
		if ( on ) then
			local ok, h = pcall(Handle, name, MOVABLE[i][2]);
			if ( ok and h ) then h:Show(); end
		elseif ( handles[name] ) then
			handles[name]:Hide();
		end
	end
	pcall(UpdateLevelButton);
	K.Print(NAME, on and "drag a green handle to move it; right-click one to resize, scale or change layout. Use Lock UI when done." or "the UI is locked.");
end

local function SetAnchorSnap(on)
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
	DB().places = {};
	K.Print(NAME, "the frames go back to their own places after a /reload.");
	if ( ReloadUI ) then ReloadUI(); end
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
-- the racial's rim) made see-through, and the racial button and the character and talent buttons hidden, so only
-- the six spell squares (ActionButton1-6) are left. Untick and everything comes back as it was.
local PLAIN_HIDE = { "ActionButton12", "CharacterMicroButton", "TalentMicroButton" };
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
	pcall(Narrow, on);
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
-- racial last) are in the order of the squares, ActionButton1 to 6.
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
	for i = 1, 6 do
		local btn = getglobal("ActionButton" .. i);
		if ( btn ) then
			local t = cdText[i];
			if ( not t ) then
				t = btn:CreateFontString(nil, "OVERLAY", "NumberFontNormalLarge");
				t:SetPoint("CENTER", btn, "CENTER", 0, 0);
				t:SetTextColor(1, 0.82, 0);
				cdText[i] = t;
			end
			local sp = (i < n) and spells[i] or nil;      -- (the last is the racial)
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
	fs:SetText(label);
	cb:SetScript("OnShow", function() this:SetChecked(get() and 1 or nil); end);
	cb:SetScript("OnClick", function()
		set(this:GetChecked() and true or false);
		K.Sound(this:GetChecked() and "igMainMenuOptionCheckBoxOn" or "igMainMenuOptionCheckBoxOff");
	end);
	return cb;
end

local function WinBuild()
	win = K.Window("AZSMinorModsFrame", NAME, 420, 528);
	local hero = K.Hero(win, 380);
	hero:SetPoint("TOPLEFT", win, "TOPLEFT", 20, -30);
	K.Icon(hero.icon, "INV_Misc_Gear_02");
	hero.heading:SetText("Minor Mods");
	hero.line:SetText("Every change on its own: tick what you want.");
	local y = -104;
	local function Row(label, get, set)
		Check(win, label, y, get, set);
		y = y - 28;
	end
	Row("No level-up, stat bonus or new spell lines in chat", function() return DB().hideLevel; end, function(v) DB().hideLevel = v; end);
	Row("Camera as far out as it goes (50)", function() return DB().camera; end,
	    function(v) DB().camera = v; Camera(v); end);
	Row("Mark bosses: Skull, Cross, Diamond", function() return DB().bossMarks; end, function(v) DB().bossMarks = v; end);
	Row("Mark minibosses: Star, Triangle, Diamond", function() return DB().miniMarks; end, function(v) DB().miniMarks = v; end);
	Row("Blink a marker over a friend who falls (16 times)", function() return DB().deathMarks; end, function(v) DB().deathMarks = v; end);
	Row("Play a sound when you or a teammate dies", function() return DB().deathSound; end, function(v) DB().deathSound = v; end);
	Row("Hide the target frame", function() return DB().hideTarget; end, function(v) DB().hideTarget = v; TargetFrameHidden(v); end);
	Row("Plain action bar: no gryphons, just the 6 squares", function() return DB().plainBar; end, function(v) DB().plainBar = v; PlainBar(v); end);
	Row("Cooldown numbers on the spell squares", function() return DB().cooldowns; end, function(v) DB().cooldowns = v; end);
	Row("Replace Level Up button with movable version", function() return DB().moveLevelButton; end, function(v) DB().moveLevelButton = v; pcall(UpdateLevelButton); end);
	Row("Unlock UI (drag moves; right-click resizes / scales)", function() return DB().unlocked; end, function(v) AZSMinorMods_SetUnlocked(v); end);
	Row("Anchor moved frames to nearby frames", function() return DB().anchorSnap; end, function(v) SetAnchorSnap(v); end);
	Row("Minimap button", function() local m = DB().minimap; return not (m and m.hide); end,
	    function(v) if ( AZSMinorMods_Mini ) then local m = DB().minimap; if ( (m and m.hide and true or false) == v ) then AZSMinorMods_Mini.Toggle(); end end end);
	local note = win:CreateFontString(nil, "OVERLAY", "GameFontDisableSmall");
	note:SetPoint("TOPLEFT", win, "TOPLEFT", 30, y - 4);
	note:SetWidth(360);
	note:SetJustifyH("LEFT");
	note:SetText("Drag green handles to move. Right-click to resize, scale or change layout. Anchoring is optional.");
	local reset = K.Button(win, "Reset positions", 150, function() AZSMinorMods_ResetPlaces(); end);
	reset:SetPoint("BOTTOMRIGHT", win, "BOTTOMRIGHT", -24, 20);
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
	pcall(Camera, event == "PLAYER_ENTERING_WORLD");
	if ( event == "VARIABLES_LOADED" ) then
		DB().unlocked = false;                             -- every session starts locked
	end
	-- the frames back where they were put (the game places some of them again as it loads), and the target frame
	pcall(DetachExperienceBar);
	if ( type(DB().places.ChatFrame1) == "table" ) then pcall(PrepareChatWindow); end
	pcall(ApplyTallyLayout, false);
	pcall(PlaceAll);
	pcall(TargetFrameHidden, DB().hideTarget);
	if ( DB().plainBar ) then pcall(PlainBar, true); end
	pcall(ApplyMovableOrientation, "MainMenuBar");
	pcall(ApplyMovableOrientation, "CastingBarFrame");
	pcall(ApplyMovableOrientation, "PartyMemberFrame1");
	pcall(PlaceAll);
end);
pcall(HookChat);                                          -- right away too, so lines printed while loading are caught

-- the minimap button: a gear, at the left of the minimap (AZS Stats' is at the bottom, AZS Handbook's at the top
-- left)
local okMini, errMini = pcall(function()
	AZSMinorMods_Mini = K.Minimap({ name = "AZSMinorModsMinimapButton", icon = "INV_Misc_Gear_02", angle = 190, db = DB,
		click = function() AZSMinorMods_Toggle(); end,
		tooltip = function(tt)
			tt:AddLine(NAME);
			tt:AddLine("Small changes, each on or off.", 1, 1, 1);
			tt:AddLine(" ");
			tt:AddLine("Click: the settings. /azsmods minimap hides this button.", 0.6, 0.6, 0.6);
		end });
end);
if ( not okMini ) then K.Print(NAME, "|cffff4040the minimap button could not be made:|r " .. tostring(errMini)); end
