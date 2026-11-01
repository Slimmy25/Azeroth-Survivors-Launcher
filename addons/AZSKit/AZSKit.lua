-- AZS Kit: the look every AZS addon shares, the one AZS Stats has (its window, cards, buttons, scroll list and minimap
-- button), so AZS Handbook, Slim Suite and any later AZS addon look the same. Each of those
-- addons carries its own copy of this file (the launcher writes the same file into each); whichever copy loads first
-- makes AZSKIT, and a newer copy replaces an older one. When the look changes here, it changes for all of them;
-- AZS Stats has the same pieces in AZSStatsUI.lua and is kept to match.
-- Lua 5.0 / 1.12 interface: scripts read `this`, `arg1`; no # operator; only the 1.12 API.

local VERSION = 1;
if ( type(AZSKIT) == "table" and (AZSKIT.version or 0) >= VERSION ) then return; end

local K = { version = VERSION };
AZSKIT = K;

K.GOLD = { 0.95, 0.76, 0.3 };
K.QUESTION = "Interface\\Icons\\INV_Misc_QuestionMark";

function K.Num(v) return tonumber(v) or 0; end
function K.C(hex, text) return "|cff" .. hex .. tostring(text) .. "|r"; end

-- "a1b2c3" -> r, g, b (0..1)
function K.RGB(hex)
	local function h(i) return (tonumber(string.sub(hex, i, i + 1), 16) or 255) / 255; end
	return h(1), h(3), h(5);
end

function K.Sound(name) if ( PlaySound ) then pcall(PlaySound, name); end end

function K.Print(addon, msg)
	if ( DEFAULT_CHAT_FRAME ) then DEFAULT_CHAT_FRAME:AddMessage("|cffffd100" .. addon .. "|r " .. tostring(msg)); end
end

-- an icon texture: "Spell_Fire_Fireball02" (in Interface\Icons) or a whole path; nil hides it
function K.Icon(tex, icon)
	if ( not icon or icon == "" ) then tex:Hide(); return; end
	if ( not string.find(icon, "\\", 1, true) ) then icon = "Interface\\Icons\\" .. icon; end
	tex:SetTexture(icon);
	tex:SetTexCoord(0.07, 0.93, 0.07, 0.93);
	tex:Show();
end

-- a card in the tooltip style: dark inside, a thin border
function K.Card(f, r, g, b)
	f:SetBackdrop({ bgFile = "Interface\\Tooltips\\UI-Tooltip-Background", edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
	                tile = true, tileSize = 16, edgeSize = 12, insets = { left = 3, right = 3, top = 3, bottom = 3 } });
	f:SetBackdropColor(0.08, 0.07, 0.11, 0.92);
	f:SetBackdropBorderColor(r or 0.35, g or 0.33, b or 0.4);
end

-- a frame from one of the game's templates, or a plain one if the game does not have that template
function K.Templated(kind, name, parent, template, part)
	local ok, f = pcall(CreateFrame, kind, name, parent, template);
	if ( ok and f ) then
		if ( not part or (name and getglobal(name .. part)) ) then return f, true; end
		return f, false;
	end
	return CreateFrame(kind, name, parent), false;
end

-- the game's red panel button (or the same art by hand where the template does not take)
function K.Button(parent, text, width, onClick)
	local ok, b = pcall(CreateFrame, "Button", nil, parent, "UIPanelButtonTemplate");
	if ( not ok or not b ) then b = CreateFrame("Button", nil, parent); end
	if ( not b:GetFontString() ) then
		local fs = b:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall");
		fs:SetPoint("CENTER", b, "CENTER", 0, 1);
		b:SetFontString(fs);
		b:SetNormalTexture("Interface\\Buttons\\UI-Panel-Button-Up");
		b:SetPushedTexture("Interface\\Buttons\\UI-Panel-Button-Down");
		b:SetHighlightTexture("Interface\\Buttons\\UI-Panel-Button-Highlight");
		local t = { b:GetNormalTexture(), b:GetPushedTexture(), b:GetHighlightTexture() };
		for i = 1, 3 do if ( t[i] ) then t[i]:SetTexCoord(0, 0.625, 0, 0.6875); end end
	end
	b:SetWidth(width);
	b:SetHeight(22);
	b:SetText(text);
	if ( onClick ) then b:SetScript("OnClick", onClick); end
	return b;
end

-- the window: the game's dialog frame with its header and title, a close button, dragged by its body, closed by Escape
function K.Window(name, title, w, h)
	local f = CreateFrame("Frame", name, UIParent);
	f:SetWidth(w);
	f:SetHeight(h);
	f:SetPoint("CENTER", UIParent, "CENTER", 0, 20);
	f:SetFrameStrata("DIALOG");
	f:SetBackdrop({ bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
	                edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
	                tile = true, tileSize = 32, edgeSize = 32, insets = { left = 11, right = 12, top = 12, bottom = 11 } });
	f:EnableMouse(true);
	f:SetMovable(true);
	if ( f.SetClampedToScreen ) then f:SetClampedToScreen(true); end
	f:RegisterForDrag("LeftButton");
	f:SetScript("OnDragStart", function() this:StartMoving(); end);
	f:SetScript("OnDragStop", function() this:StopMovingOrSizing(); end);
	f:Hide();
	table.insert(UISpecialFrames, name);
	local header = f:CreateTexture(nil, "ARTWORK");
	header:SetTexture("Interface\\DialogFrame\\UI-DialogBox-Header");
	header:SetWidth(300);
	header:SetHeight(64);
	header:SetPoint("TOP", f, "TOP", 0, 12);
	f.title = f:CreateFontString(nil, "OVERLAY", "GameFontNormal");
	f.title:SetPoint("TOP", header, "TOP", 0, -14);
	f.title:SetText(title);
	local close = CreateFrame("Button", nil, f, "UIPanelCloseButton");
	close:SetPoint("TOPRIGHT", f, "TOPRIGHT", -6, -6);
	return f;
end

-- the header card: an icon in the minimap's gold ring (as AZS Stats' portrait), a heading and a line under it
function K.Hero(parent, w)
	local hero = CreateFrame("Frame", nil, parent);
	hero:SetWidth(w);
	hero:SetHeight(64);
	K.Card(hero, 0.6, 0.5, 0.3);
	hero.icon = hero:CreateTexture(nil, "ARTWORK");
	hero.icon:SetWidth(40);
	hero.icon:SetHeight(40);
	hero.icon:SetPoint("CENTER", hero, "TOPLEFT", 36, -32);
	local ring = hero:CreateTexture(nil, "OVERLAY");
	ring:SetTexture("Interface\\Minimap\\MiniMap-TrackingBorder");
	ring:SetTexCoord(0.05, 0.53, 0, 0.47);
	ring:SetWidth(54);
	ring:SetHeight(54);
	ring:SetPoint("CENTER", hero.icon, "CENTER", 0, 0);
	hero.heading = hero:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge");
	hero.heading:SetPoint("TOPLEFT", hero, "TOPLEFT", 70, -12);
	hero.heading:SetJustifyH("LEFT");
	hero.line = hero:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall");
	hero.line:SetPoint("TOPLEFT", hero.heading, "BOTTOMLEFT", 0, -4);
	hero.line:SetJustifyH("LEFT");
	hero.line:SetWidth(w - 90);
	return hero;
end

-- the list: the game's scroll frame and scroll bar (the mouse wheel scrolls it either way)
function K.Scroll(name, parent, width)
	local scroll, ok = K.Templated("ScrollFrame", name, parent, "UIPanelScrollFrameTemplate", "ScrollBar");
	local child = CreateFrame("Frame", nil, scroll);
	child:SetWidth(width);
	child:SetHeight(10);
	scroll:SetScrollChild(child);
	if ( not ok ) then
		scroll:EnableMouseWheel(true);
		scroll:SetScript("OnMouseWheel", function()
			local max = math.max(0, child:GetHeight() - this:GetHeight());
			this:SetVerticalScroll(math.max(0, math.min(max, this:GetVerticalScroll() - arg1 * 40)));
		end);
	end
	scroll.child = child;
	scroll.Top = function()
		if ( scroll.UpdateScrollChildRect ) then scroll:UpdateScrollChildRect(); end
		scroll:SetVerticalScroll(0);
		local bar = getglobal(name .. "ScrollBar");
		if ( bar ) then bar:SetValue(0); end
	end;
	return scroll, child;
end

-- how tall a wrapped font string is (some clients give no wrapped height: then it is worked out from its width)
function K.TextHeight(fs, width)
	local h = fs:GetHeight() or 0;
	local line = 13;
	local _, size = fs:GetFont();
	if ( size and size > 0 ) then line = size + 2; end
	local est = 0;
	local text = fs:GetText() or "";
	local w = fs:GetStringWidth() or 0;
	local _, breaks = string.gsub(text, "\n", "\n");
	est = (math.ceil(w * 1.08 / math.max(1, width)) + breaks) * line;
	return math.max(h, est, line);
end

-- ---------------------------------------------------------------- the minimap button
-- The tracking-button look of AZS Stats': a round icon in the minimap's gold ring, made the way the Peggle port
-- does it on this client (a plain Frame on the Minimap, clicks from OnMouseDown / OnMouseUp). Drag it around the
-- minimap (the angle is saved); drag it well away and it stays where you drop it.
--   o = { name = frame name, icon = icon, angle = default angle, db = function() return saved table end,
--         click = function(button) end, tooltip = function(tooltip) end }
local RING = 78;

function K.Minimap(o)
	local mini = CreateFrame("Frame", o.name, Minimap or UIParent);
	mini:SetWidth(33);
	mini:SetHeight(33);
	mini:SetFrameStrata("MEDIUM");
	mini:SetFrameLevel(20);
	mini:EnableMouse(true);
	if ( mini.SetClampedToScreen ) then mini:SetClampedToScreen(true); end
	mini.icon = mini:CreateTexture(nil, "BACKGROUND");
	mini.icon:SetWidth(21);
	mini.icon:SetHeight(21);
	mini.icon:SetPoint("CENTER", mini, "CENTER", -1, 1);
	mini.icon:SetTexture("Interface\\Icons\\" .. o.icon);
	mini.icon:SetTexCoord(0.07, 0.93, 0.07, 0.93);
	mini.border = mini:CreateTexture(nil, "ARTWORK");
	mini.border:SetWidth(52);
	mini.border:SetHeight(52);
	mini.border:SetPoint("TOPLEFT", mini, "TOPLEFT", 0, 0);
	mini.border:SetTexture("Interface\\Minimap\\MiniMap-TrackingBorder");
	mini.glow = mini:CreateTexture(nil, "OVERLAY");
	mini.glow:SetWidth(32);
	mini.glow:SetHeight(32);
	mini.glow:SetPoint("CENTER", mini, "CENTER", 0, 0);
	mini.glow:SetTexture("Interface\\Minimap\\UI-Minimap-ZoomButton-Highlight");
	mini.glow:SetBlendMode("ADD");
	mini.glow:Hide();

	local function M()
		local d = o.db();
		if ( type(d.minimap) ~= "table" ) then d.minimap = { angle = o.angle }; end
		return d.minimap;
	end

	function mini.Place()
		local m = M();
		mini:ClearAllPoints();
		if ( m.detached and m.x and m.y ) then
			mini:SetPoint("CENTER", UIParent, "BOTTOMLEFT", K.Num(m.x), K.Num(m.y));
		elseif ( Minimap ) then
			local a = math.rad(K.Num(m.angle or o.angle));
			mini:SetPoint("CENTER", Minimap, "CENTER", RING * math.cos(a), RING * math.sin(a));
		else
			mini:SetPoint("CENTER", UIParent, "TOPRIGHT", -60, -60);
		end
		if ( m.hide ) then mini:Hide(); else mini:Show(); end
	end

	function mini.Toggle()
		local m = M();
		m.hide = not m.hide;
		mini.Place();
		return not m.hide;
	end

	local function Drag()
		local x, y = GetCursorPosition();
		local s = UIParent:GetEffectiveScale();
		x, y = x / s, y / s;
		if ( not mini.dragging ) then
			if ( math.abs(x - mini.downX) + math.abs(y - mini.downY) < 6 ) then return; end
			mini.dragging = true;
			GameTooltip:Hide();
		end
		local m = M();
		local cx, cy;
		if ( Minimap ) then cx, cy = Minimap:GetCenter(); end
		if ( cx and cy ) then
			local ms = Minimap:GetEffectiveScale() / s;
			cx, cy = cx * ms, cy * ms;
		end
		if ( cx and cy and (x - cx) * (x - cx) + (y - cy) * (y - cy) < 140 * 140 ) then
			m.detached = nil;
			m.angle = math.deg(math.atan2(y - cy, x - cx));
		else
			m.detached = true;
			m.x, m.y = x, y;
		end
		mini.Place();
	end

	mini:SetScript("OnMouseDown", function()
		mini.icon:ClearAllPoints();
		mini.icon:SetPoint("CENTER", mini, "CENTER", 0, 0);
		local x, y = GetCursorPosition();
		local s = UIParent:GetEffectiveScale();
		mini.downX, mini.downY, mini.dragging, mini.down = x / s, y / s, false, true;
	end);
	mini:SetScript("OnMouseUp", function()
		local button, dragged = arg1, mini.dragging;
		mini.down, mini.dragging = false, false;
		mini.icon:ClearAllPoints();
		mini.icon:SetPoint("CENTER", mini, "CENTER", -1, 1);
		if ( dragged ) then return; end
		o.click(button);
	end);
	mini:SetScript("OnUpdate", function() if ( mini.down ) then Drag(); end end);
	mini:SetScript("OnEnter", function()
		mini.glow:Show();
		GameTooltip:SetOwner(mini, "ANCHOR_LEFT");
		o.tooltip(GameTooltip);
		GameTooltip:AddLine("Drag to move this button.", 0.6, 0.6, 0.6);
		GameTooltip:Show();
	end);
	mini:SetScript("OnLeave", function() mini.glow:Hide(); GameTooltip:Hide(); end);
	mini:SetPoint("CENTER", Minimap or UIParent, "CENTER", RING * math.cos(math.rad(o.angle)), RING * math.sin(math.rad(o.angle)));
	mini:Show();

	-- once the saved variables are in: the saved spot
	local loader = CreateFrame("Frame");
	loader:RegisterEvent("VARIABLES_LOADED");
	loader:SetScript("OnEvent", function() pcall(mini.Place); end);
	return mini;
end
