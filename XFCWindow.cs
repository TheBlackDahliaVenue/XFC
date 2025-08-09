using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Linq;
using Dalamud.Plugin.Services;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text;

namespace XFCBrawl
{
    public class XFCWindow : Window
    {
        private readonly BrawlMatch match;
        private readonly IObjectTable objectTable;
        private readonly IClientState clientState;
        private readonly IChatGui chatGui;
        private readonly IPluginLog pluginLog;

        private bool isTagMatch = false;

        private int selectedBlackIndex = 0;
        private int selectedBlueIndex = 0;

        private List<string> blackTeam = new();
        private List<string> blueTeam = new();

        private List<string> normalizedNames = new();
        private Dictionary<string, string> normalizedToOriginal = new();

        public XFCWindow(BrawlMatch match, IObjectTable objectTable, IClientState clientState, IChatGui chatGui, IPluginLog pluginLog)
            : base("XFC Brawl Match", ImGuiWindowFlags.AlwaysAutoResize)
        {
            this.match = match;
            this.objectTable = objectTable;
            this.clientState = clientState;
            this.chatGui = chatGui;
            this.pluginLog = pluginLog;
            IsOpen = true;
        }

        public override void Draw()
        {
            UpdatePlayerList();

            var lightBlue = new Vector4(0.5f, 0.7f, 1f, 1f);

            // Toggle match type
            if (ImGui.Button(isTagMatch ? "Switch to 1v1 Match" : "Switch to Tag Match"))
            {
                isTagMatch = !isTagMatch;
                blackTeam.Clear();
                blueTeam.Clear();
                selectedBlackIndex = 0;
                selectedBlueIndex = 0;
            }

            ImGui.Separator();

            // Black side selection
            string blackDisplay = isTagMatch
                ? (blackTeam.Count > 0 ? string.Join(", ", blackTeam.Select(n => normalizedToOriginal[n])) : "Select Players")
                : (normalizedNames.Count > selectedBlackIndex ? normalizedToOriginal[normalizedNames[selectedBlackIndex]] : "Select Player");

            if (ImGui.BeginCombo(isTagMatch ? "Black Side Player(s)" : "Black Side", blackDisplay))
            {
                for (int i = 0; i < normalizedNames.Count; i++)
                {
                    var originalName = normalizedToOriginal[normalizedNames[i]];

                    if (isTagMatch)
                    {
                        bool isSelected = blackTeam.Contains(normalizedNames[i]);
                        if (ImGui.Selectable(originalName, isSelected))
                        {
                            if (isSelected)
                            {
                                blackTeam.Remove(normalizedNames[i]);
                                pluginLog.Debug($"[XFCWindow] Removed player '{originalName}' from Black Team");
                            }
                            else
                            {
                                blackTeam.Add(normalizedNames[i]);
                                pluginLog.Debug($"[XFCWindow] Added player '{originalName}' to Black Team");
                            }
                        }
                    }
                    else
                    {
                        bool isSelected = (i == selectedBlackIndex);
                        if (ImGui.Selectable(originalName, isSelected))
                            selectedBlackIndex = i;
                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                }
                ImGui.EndCombo();
            }

            // Blue side selection
            string blueDisplay = isTagMatch
                ? (blueTeam.Count > 0 ? string.Join(", ", blueTeam.Select(n => normalizedToOriginal[n])) : "Select Players")
                : (normalizedNames.Count > selectedBlueIndex ? normalizedToOriginal[normalizedNames[selectedBlueIndex]] : "Select Player");

            if (ImGui.BeginCombo(isTagMatch ? "Blue Side Player(s)" : "Blue Side", blueDisplay))
            {
                for (int i = 0; i < normalizedNames.Count; i++)
                {
                    var originalName = normalizedToOriginal[normalizedNames[i]];

                    if (isTagMatch)
                    {
                        bool isSelected = blueTeam.Contains(normalizedNames[i]);
                        if (ImGui.Selectable(originalName, isSelected))
                        {
                            if (isSelected)
                            {
                                blueTeam.Remove(normalizedNames[i]);
                                pluginLog.Debug($"[XFCWindow] Removed player '{originalName}' from Blue Team");
                            }
                            else
                            {
                                blueTeam.Add(normalizedNames[i]);
                                pluginLog.Debug($"[XFCWindow] Added player '{originalName}' to Blue Team");
                            }
                        }
                    }
                    else
                    {
                        bool isSelected = (i == selectedBlueIndex);
                        if (ImGui.Selectable(originalName, isSelected))
                            selectedBlueIndex = i;
                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                }
                ImGui.EndCombo();
            }

            // Start match button
            if (ImGui.Button("Start Match"))
            {
                if (isTagMatch)
                {
                    pluginLog.Debug($"[XFCWindow] Starting Tag Match with Black Team: {string.Join(", ", blackTeam.Select(n => normalizedToOriginal[n]))} and Blue Team: {string.Join(", ", blueTeam.Select(n => normalizedToOriginal[n]))}");
                    match.SetTeams(new List<string>(blackTeam), new List<string>(blueTeam));
                }
                else
                {
                    var blackName = normalizedNames.Count > selectedBlackIndex ? normalizedNames[selectedBlackIndex] : "";
                    var blueName = normalizedNames.Count > selectedBlueIndex ? normalizedNames[selectedBlueIndex] : "";
                    pluginLog.Debug($"[XFCWindow] Starting 1v1 Match with Black: {blackName}, Blue: {blueName}");
                    match.SetPlayers(blackName, blueName);
                }
            }

            if (!match.IsStarted)
                return;

            ImGui.Separator();
            ImGui.Text($"Round {match.CurrentRound}/{match.RoundsToPlay}");

            string currentAttackerNorm = "";
            string currentDefenderNorm = "";

            if (match.Rounds.Count >= match.CurrentRound && match.CurrentRound > 0)
            {
                var roundInfo = match.Rounds[match.CurrentRound - 1];
                currentAttackerNorm = NormalizeName(roundInfo.Attacker);
                currentDefenderNorm = NormalizeName(roundInfo.Defender);
            }

            var currentRolls = match.CurrentRolls;
            if (currentRolls.Count > 0)
            {
                ImGui.Text("Current Rolls:");
                foreach (var (normName, roll, side) in currentRolls)
                {
                    string originalName = normalizedToOriginal.ContainsKey(normName) ? normalizedToOriginal[normName] : normName;

                    if (side == "Black" && normName == currentAttackerNorm)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0f, 0f, 1f));
                        ImGui.Text($"  Attacker (Black): {originalName}: {roll}");
                        ImGui.PopStyleColor();
                    }
                    else if (side == "Blue" && normName == currentDefenderNorm)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, lightBlue);
                        ImGui.Text($"  Defender (Blue): {originalName}: {roll}");
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        ImGui.Text($"  {side}: {originalName}: {roll}");
                    }
                }
            }

            foreach (var round in match.Rounds)
            {
                string attackerNorm = NormalizeName(round.Attacker);
                string defenderNorm = NormalizeName(round.Defender);

                string attackerOriginal = normalizedToOriginal.ContainsKey(attackerNorm) ? normalizedToOriginal[attackerNorm] : round.Attacker;
                string defenderOriginal = normalizedToOriginal.ContainsKey(defenderNorm) ? normalizedToOriginal[defenderNorm] : round.Defender;

                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0f, 0f, 1f));
                ImGui.Text($"Round {round.RoundNumber}: {attackerOriginal}");
                ImGui.PopStyleColor();

                ImGui.Text($" ({round.AttackerRoll}) vs ");

                ImGui.PushStyleColor(ImGuiCol.Text, lightBlue);
                ImGui.Text($"{defenderOriginal}");
                ImGui.PopStyleColor();

                ImGui.Text($" ({round.DefenderRoll}) → Damage: {round.DamageDealt}");
            }

            string blackOriginal = normalizedToOriginal.ContainsKey(match.BlackName) ? normalizedToOriginal[match.BlackName] : match.BlackName;
            string blueOriginal = normalizedToOriginal.ContainsKey(match.BlueName) ? normalizedToOriginal[match.BlueName] : match.BlueName;

            ImGui.TextColored(new Vector4(1, 1, 0, 1), $"{blackOriginal} Total Damage Dealt: {match.BlackTotalDamage}");
            ImGui.TextColored(lightBlue, $"{blueOriginal} Total Damage Dealt: {match.BlueTotalDamage}");

            if (match.IsFinished)
            {
                ImGui.Separator();

                string winnerMessage = match.GetWinner();
                string winnerNormalized = "";

                if (winnerMessage.Contains("wins!"))
                {
                    if (winnerMessage.StartsWith(match.BlackName))
                        winnerNormalized = match.BlackName;
                    else if (winnerMessage.StartsWith(match.BlueName))
                        winnerNormalized = match.BlueName;
                }

                if (!string.IsNullOrEmpty(winnerNormalized) && normalizedToOriginal.ContainsKey(winnerNormalized))
                {
                    ImGui.Text($"Winner: {normalizedToOriginal[winnerNormalized]}");
                }
                else
                {
                    ImGui.Text($"Winner: {winnerMessage}");
                }

                if (ImGui.Button("Announce Winner to Party Chat"))
                {
                    if (winnerNormalized == match.BlackName)
                    {
                        string message = $"{blackOriginal} wins the Brawl with {match.BlackTotalDamage} total damage!";
                        chatGui.Print(new XivChatEntry
                        {
                            Message = message,
                            Type = XivChatType.Party
                        });
                    }
                    else if (winnerNormalized == match.BlueName)
                    {
                        string message = $"{blueOriginal} wins the Brawl with {match.BlueTotalDamage} total damage!";
                        chatGui.Print(new XivChatEntry
                        {
                            Message = message,
                            Type = XivChatType.Party
                        });
                    }
                }
            }
        }

        private void UpdatePlayerList()
        {
            normalizedNames.Clear();
            normalizedToOriginal.Clear();

            var localPlayer = clientState.LocalPlayer;
            if (localPlayer != null)
            {
                var localName = localPlayer.Name?.TextValue;
                if (!string.IsNullOrEmpty(localName))
                {
                    var norm = NormalizeName(localName);
                    if (!normalizedNames.Contains(norm))
                    {
                        normalizedNames.Add(norm);
                        normalizedToOriginal[norm] = localName;
                    }
                }
            }

            for (int i = 0; i < objectTable.Length; i++)
            {
                var obj = objectTable[i];
                if (obj == null) continue;

                if (obj.ObjectKind == ObjectKind.Player)
                {
                    var originalName = obj.Name?.TextValue;
                    if (!string.IsNullOrEmpty(originalName))
                    {
                        var norm = NormalizeName(originalName);
                        if (!normalizedNames.Contains(norm))
                        {
                            normalizedNames.Add(norm);
                            normalizedToOriginal[norm] = originalName;
                        }
                    }
                }
            }

            normalizedNames.Sort();
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
    }
}
