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
--   * Move the UI: unlock it, drag the player, target and party frames, the minimap, buffs, cast bar, action bars and
--     where you want them, then lock it again. The places are kept (Reset puts them back).
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
	if ( type(d.places) ~= "table" ) then d.places = {}; end
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
local DEATH_SOUND = "Interface\\AddOns\\AZSMinorMods\\dragon-studio-baby-crying-463213.mp3";
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
                  { "MainMenuBar", "Action bars" }, { "MultiBarBottomLeft", "Bar 2" }, { "MultiBarRight", "Right bar" } };
local handles = {};

local function Place(name)
	local f = getglobal(name);
	local p = DB().places[name];
	if ( not f or type(p) ~= "table" or not p.x or not p.y ) then return; end
	f:ClearAllPoints();
	f:SetPoint("BOTTOMLEFT", UIParent, "BOTTOMLEFT", K.Num(p.x), K.Num(p.y));
end

local function PlaceAll()
	for i = 1, table.getn(MOVABLE) do pcall(Place, MOVABLE[i][1]); end
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
			local t = getglobal(name);
			if ( not t ) then return; end
			t:SetMovable(true);
			if ( t.SetClampedToScreen ) then t:SetClampedToScreen(true); end
			t:StartMoving();
			this.moving = true;
		end);
		h:SetScript("OnMouseUp", function()
			local t = getglobal(name);
			if ( not t or not this.moving ) then return; end
			this.moving = nil;
			t:StopMovingOrSizing();
			DB().places[name] = { x = t:GetLeft(), y = t:GetBottom() };
			Place(name);
		end);
		h:SetScript("OnUpdate", function()
			local t = getglobal(name);
			if ( not t ) then return; end
			this:ClearAllPoints();
			this:SetPoint("TOPLEFT", t, "TOPLEFT", 0, 0);
			this:SetPoint("BOTTOMRIGHT", t, "BOTTOMRIGHT", 0, 0);
		end);
		handles[name] = h;
	end
	local w, ht = f:GetWidth(), f:GetHeight();
	if ( not w or w < 20 ) then f:SetWidth(math.max(w or 0, 120)); end
	if ( not ht or ht < 16 ) then f:SetHeight(math.max(ht or 0, 24)); end
	return h;
end

-- unlocked: the handles show (the frames are shown too, so even an empty party frame can be placed)
function AZSMinorMods_SetUnlocked(on)
	DB().unlocked = on and true or false;
	for i = 1, table.getn(MOVABLE) do
		local name = MOVABLE[i][1];
		if ( on ) then
			local ok, h = pcall(Handle, name, MOVABLE[i][2]);
			if ( ok and h ) then h:Show(); end
		elseif ( handles[name] ) then
			handles[name]:Hide();
		end
	end
	K.Print(NAME, on and "the UI is unlocked: drag the green handles, then lock it in /azsmods." or "the UI is locked.");
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
	Row("Unlock the UI to move it (untick to lock)", function() return DB().unlocked; end, function(v) AZSMinorMods_SetUnlocked(v); end);
	Row("Minimap button", function() local m = DB().minimap; return not (m and m.hide); end,
	    function(v) if ( AZSMinorMods_Mini ) then local m = DB().minimap; if ( (m and m.hide and true or false) == v ) then AZSMinorMods_Mini.Toggle(); end end end);
	local note = win:CreateFontString(nil, "OVERLAY", "GameFontDisableSmall");
	note:SetPoint("TOPLEFT", win, "TOPLEFT", 30, y - 4);
	note:SetWidth(360);
	note:SetJustifyH("LEFT");
	note:SetText("Unlocked, drag the green handles; lock it again when done.");
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
	local ok, err = pcall(HookChat);
	if ( not ok ) then K.Print(NAME, "|cffff4040the chat could not be changed:|r " .. tostring(err)); end
	pcall(Camera, event == "PLAYER_ENTERING_WORLD");
	if ( event == "VARIABLES_LOADED" ) then
		DB().unlocked = false;                             -- every session starts locked
	end
	-- the frames back where they were put (the game places some of them again as it loads), and the target frame
	pcall(PlaceAll);
	pcall(TargetFrameHidden, DB().hideTarget);
	if ( DB().plainBar ) then pcall(PlainBar, true); end
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
