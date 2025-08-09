namespace XFCBrawl
{
    public class BrawlRound
    {
        public int RoundNumber { get; set; }
        public string Attacker { get; set; } = "";
        public int AttackerRoll { get; set; }
        public string Defender { get; set; } = "";
        public int DefenderRoll { get; set; }
        public int DamageDealt { get; set; }
    }
}
