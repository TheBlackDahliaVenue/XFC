using XFCBrawl;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Text.RegularExpressions;
using System.Text;

namespace XFCBrawl
{
    public sealed class Plugin : IDalamudPlugin, IDisposable
    {
        public string Name => "XFC Brawl Plugin";

        private readonly IDalamudPluginInterface pluginInterface;
        private readonly IChatGui chatGui;
        private readonly IClientState clientState;
        private readonly IObjectTable objectTable;
        private readonly IPluginLog log;

        private readonly WindowSystem windowSystem = new("XFCBrawlPlugin");
        private readonly XFCWindow window;
        private readonly BrawlMatch match;

        private static readonly Regex RollRegex = new(@"^Random!\s+(?<name>.+?)\s+rolls?\s+a\s+(?<roll>\d+)\.$", RegexOptions.Compiled);

        private const ushort RandomRollChatType = 2122;
        private const ushort OtherRandomRollChatType = 8266;
        private const ushort GeneralRandomRollChatType = 4170;

        public Plugin(
            IDalamudPluginInterface pluginInterface,
            IChatGui chatGui,
            IClientState clientState,
            IObjectTable objectTable,
            IPluginLog log)
        {
            this.pluginInterface = pluginInterface;
            this.chatGui = chatGui;
            this.clientState = clientState;
            this.objectTable = objectTable;
            this.log = log;

            match = new BrawlMatch(pluginInterface.ConfigDirectory.FullName, log, clientState);
            window = new XFCWindow(match, objectTable, clientState, chatGui, log); // ✅ FIXED: now passes chatGui
            windowSystem.AddWindow(window);

            this.chatGui.ChatMessage += OnChatMessage;

            this.pluginInterface.UiBuilder.Draw += windowSystem.Draw;
            this.pluginInterface.UiBuilder.OpenConfigUi += () => window.IsOpen = true;

            match.LoadHistory();
        }

        private void OnChatMessage(
            XivChatType type,
            int timestamp,
            ref SeString sender,
            ref SeString message,
            ref bool isHandled)
        {
            if (match.IsFinished)
                return;

            ushort rawType = (ushort)type;
            if (rawType != RandomRollChatType &&
                rawType != OtherRandomRollChatType &&
                rawType != GeneralRandomRollChatType)
                return;

            string text = message.TextValue?.Trim() ?? "";
            if (string.IsNullOrEmpty(text))
                return;

            log.Debug($"[XFCBrawl] Roll message detected: [{type}] {text}");

            var matchResult = RollRegex.Match(text);
            if (!matchResult.Success)
            {
                log.Debug("[XFCBrawl] Regex did not match expected roll format.");
                return;
            }

            string originalName = matchResult.Groups["name"].Value.Trim();

            if (originalName.Equals("You", StringComparison.OrdinalIgnoreCase))
            {
                if (clientState.LocalPlayer != null)
                {
                    originalName = clientState.LocalPlayer.Name.TextValue;
                }
                else
                {
                    log.Debug("[XFCBrawl] Local player info not available to resolve 'You'.");
                    return;
                }
            }

            if (!int.TryParse(matchResult.Groups["roll"].Value, out int roll))
            {
                log.Debug($"[XFCBrawl] Failed to parse roll number: {text}");
                return;
            }

            string cleanedName = StripWorldName(originalName);
            string normalizedName = NormalizeName(cleanedName);

            log.Debug($"[XFCBrawl] Incoming roll: '{originalName}' → stripped: '{cleanedName}', normalized: '{normalizedName}'");

            if (match.Mode == BrawlMode.Solo)
{
    if (normalizedName != match.BlackName && normalizedName != match.BlueName)
    {
        log.Debug($"Ignored roll by '{originalName}' (normalized: '{normalizedName}'), not BlackSide '{match.BlackName}' or BlueSide '{match.BlueName}'.");
        return;
    }
}
else if (match.Mode == BrawlMode.Tag)
{
    if (!match.BlackTeam.Contains(normalizedName) && !match.BlueTeam.Contains(normalizedName))
    {
        log.Debug($"Ignored roll by '{originalName}' (normalized: '{normalizedName}'), not in BlackTeam or BlueTeam.");
        return;
    }
}


            log.Debug($"[XFCBrawl] Captured roll: {originalName} rolled {roll}");
            match.HandleRoll(originalName, roll);
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "";

            name = name.ToLowerInvariant();
            var sb = new StringBuilder();
            foreach (var c in name)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static string StripWorldName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return fullName;

            // Matches a name like "Na'talee RiverspearGilgamesh" → "Na'talee Riverspear"
            var match = Regex.Match(fullName, @"^(.+?\s+.+?)([A-Z][a-z]+)?$");
            return match.Success ? match.Groups[1].Value.Trim() : fullName;
        }

        public void Dispose()
        {
            chatGui.ChatMessage -= OnChatMessage;
            windowSystem.RemoveAllWindows();
            pluginInterface.UiBuilder.Draw -= windowSystem.Draw;
            pluginInterface.UiBuilder.OpenConfigUi -= () => window.IsOpen = true;
        }
    }
}
