using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;

namespace XFCBrawl
{
    public enum BrawlMode
    {
        Solo,
        Tag
    }

    public class BrawlMatch
    {
        private readonly string configDirectory;
        private readonly IPluginLog pluginLog;
        private readonly IClientState clientState;

        // Keep these public so XFCWindow.cs compiles
        public string BlackName { get; private set; } = "";
        public string BlueName { get; private set; } = "";

        public int BlackTotalDamage { get; private set; }
        public int BlueTotalDamage { get; private set; }

        private readonly List<(string name, int roll, string side)> currentRolls = new();

        public List<BrawlRound> Rounds { get; } = new();
        public List<BrawlMatchHistory> History { get; } = new();

        public int CurrentRound => Rounds.Count + 1;
        public bool IsFinished => Rounds.Count >= RoundsToPlay;
        public bool IsStarted => Mode == BrawlMode.Solo
            ? !string.IsNullOrEmpty(BlackName) && !string.IsNullOrEmpty(BlueName)
            : BlackTeam.Count > 0 && BlueTeam.Count > 0;

        public BrawlMode Mode { get; private set; } = BrawlMode.Solo;
        public int RoundsToPlay { get; private set; } = 8;

        // In Tag mode
        public List<string> BlackTeam { get; private set; } = new();
        public List<string> BlueTeam { get; private set; } = new();

        public BrawlMatch(string configDirectory, IPluginLog pluginLog, IClientState clientState)
        {
            this.configDirectory = configDirectory;
            this.pluginLog = pluginLog;
            this.clientState = clientState;
        }

        public void SetPlayers(string black, string blue)
        {
            Mode = BrawlMode.Solo;
            RoundsToPlay = 8;

            BlackName = NormalizeName(black);
            BlueName = NormalizeName(blue);

            ResetMatch();
        }

        public void SetTeams(List<string> blackTeam, List<string> blueTeam)
        {
            Mode = BrawlMode.Tag;
            RoundsToPlay = 6;

            BlackTeam = blackTeam.Select(NormalizeName).ToList();
            BlueTeam = blueTeam.Select(NormalizeName).ToList();

            // Optional: Use first names as "team name" for display
            BlackName = "Black Team";
            BlueName = "Blue Team";

            ResetMatch();
        }

        private void ResetMatch()
        {
            BlackTotalDamage = 0;
            BlueTotalDamage = 0;
            Rounds.Clear();
            currentRolls.Clear();
        }

        public void HandleRoll(string name, int roll)
        {
            if (!IsStarted || IsFinished) return;

            string resolvedName = name.Trim();

            // Replace "You" with local player name
            if (resolvedName.Equals("You", StringComparison.OrdinalIgnoreCase))
            {
                if (clientState.LocalPlayer != null)
                    resolvedName = clientState.LocalPlayer.Name.TextValue;
                else
                {
                    pluginLog.Debug("[XFCBrawl] Local player info not available to resolve 'You'.");
                    return;
                }
            }

            var stripped = StripWorldName(resolvedName);
            var normalized = NormalizeName(stripped);

            pluginLog.Debug($"[XFCBrawl] Incoming roll: '{name}' → stripped: '{stripped}', normalized: '{normalized}'");

            if (Mode == BrawlMode.Solo)
            {
                if (normalized != BlackName && normalized != BlueName)
                {
                    pluginLog.Debug($"[XFCBrawl] Ignored roll by '{stripped}', not matching solo players.");
                    return;
                }

                if (currentRolls.Any(r => r.name == normalized)) return;

                var side = normalized == BlackName ? "Black" : "Blue";
                currentRolls.Add((normalized, roll, side));

                if (currentRolls.Count == 2)
                    ProcessRound();
            }
            else // Tag mode
            {
                if (!BlackTeam.Contains(normalized) && !BlueTeam.Contains(normalized))
                {
                    pluginLog.Debug($"[XFCBrawl] Ignored roll by '{stripped}', not in teams.");
                    return;
                }

                if (currentRolls.Any(r => r.name == normalized)) return;

                var side = BlackTeam.Contains(normalized) ? "Black" : "Blue";
                currentRolls.Add((normalized, roll, side));

                // All players on both sides have rolled
                if (BothSidesComplete())
                    ProcessRound();
            }
        }

        private bool BothSidesComplete()
        {
            var blackCount = currentRolls.Count(r => r.side == "Black");
            var blueCount = currentRolls.Count(r => r.side == "Blue");
            return blackCount == (Mode == BrawlMode.Solo ? 1 : BlackTeam.Count)
                && blueCount == (Mode == BrawlMode.Solo ? 1 : BlueTeam.Count);
        }

        private void ProcessRound()
        {
            var (attackerSide, defenderSide) = GetRolesThisRound();

            int attackerRoll = currentRolls
                .Where(r => r.side == attackerSide)
                .Sum(r => r.roll);
            int defenderRoll = currentRolls
                .Where(r => r.side == defenderSide)
                .Sum(r => r.roll);

            var damage = attackerRoll > defenderRoll ? attackerRoll - defenderRoll : 0;
            if (attackerSide == "Black") BlackTotalDamage += damage;
            else BlueTotalDamage += damage;

            Rounds.Add(new BrawlRound
            {
                RoundNumber = CurrentRound,
                Attacker = attackerSide == "Black" ? BlackName : BlueName,
                AttackerRoll = attackerRoll,
                Defender = defenderSide == "Black" ? BlackName : BlueName,
                DefenderRoll = defenderRoll,
                DamageDealt = damage
            });

            currentRolls.Clear();

            if (IsFinished) SaveHistory();
        }

        private (string attacker, string defender) GetRolesThisRound()
        {
            // Odd rounds: Black attacks, even rounds: Blue attacks
            return CurrentRound % 2 == 1 ? ("Black", "Blue") : ("Blue", "Black");
        }

        public string GetWinner()
        {
            if (!IsFinished) return "Match in progress.";
            return BlackTotalDamage == BlueTotalDamage
                ? "It's a tie!"
                : (BlackTotalDamage > BlueTotalDamage ? $"{BlackName} wins!" : $"{BlueName} wins!");
        }

        private void SaveHistory()
        {
            var summary = new BrawlMatchHistory
            {
                BlackName = BlackName,
                BlueName = BlueName,
                BlackDamage = BlackTotalDamage,
                BlueDamage = BlueTotalDamage,
                Rounds = Rounds.ToList()
            };

            History.Add(summary);
            File.WriteAllText(Path.Combine(configDirectory, "brawl_history.json"),
                JsonSerializer.Serialize(History, new JsonSerializerOptions { WriteIndented = true }));
        }

        public void LoadHistory()
        {
            var path = Path.Combine(configDirectory, "brawl_history.json");
            if (!File.Exists(path) || string.IsNullOrWhiteSpace(File.ReadAllText(path))) return;
            var history = JsonSerializer.Deserialize<List<BrawlMatchHistory>>(File.ReadAllText(path));
            if (history != null) History.AddRange(history);
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return new string(name
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());
        }

        private static string StripWorldName(string fullName)
        {
            string[] worlds =
            {
                // Aether
                "Adamantoise","Cactuar","Faerie","Gilgamesh","Jenova","Midgardsormr","Sargatanas","Siren",
                // Primal
                "Behemoth","Excalibur","Exodus","Famfrit","Hyperion","Lamia","Leviathan","Ultros",
                // Crystal
                "Balmung","Brynhildr","Coeurl","Diabolos","Goblin","Malboro","Mateus","Zalera",
                // Dynamis
                "Halicarnassus","Maduin","Marilith","Seraph","Cuchulainn","Golem","Kraken","Rafflesia",
                // Mana
                "Anima","Asura","Chocobo","Hades","Ixion","Masamune","Pandaemonium","Titan",
                // Meteor
                "Belias","Mandragora","Ramuh","Shinryu","Unicorn","Valefor","Yojimbo","Zeromus",
                // Gaia
                "Alexander","Bahamut","Durandal","Fenrir","Ifrit","Ridill","Tiamat","Ultima",
                // Elemental
                "Aegis","Atomos","Carbuncle","Garuda","Gungnir","Kujata","Tonberry","Typhon",
                // European
                "Cerberus","Louisoix","Moogle","Omega","Phantom","Ragnarok","Raiden","Spriggan","Shiva","Twintania","Lich","Odin","Zodiark",
                // Oceanian
                "Bismarck","Ravana","Sephirot","Sophia","Zurvan"
            };

            foreach (var world in worlds)
            {
                if (fullName.EndsWith(world, StringComparison.OrdinalIgnoreCase))
                    return fullName[..^world.Length].Trim();
            }

            // Fallback: pick first two name parts
            var m = Regex.Match(fullName, @"^([A-Za-z'\-]+)\s+([A-Za-z'\-]+)");
            return m.Success ? $"{m.Groups[1].Value} {m.Groups[2].Value}" : fullName;
        }

        public IReadOnlyList<(string Name, int Roll, string Side)> CurrentRolls => currentRolls.AsReadOnly();
    }
}
