using System.Collections.Generic;

namespace XFCBrawl
{
    public class BrawlMatchHistory
    {
        public string BlackName { get; set; } = "";
        public string BlueName { get; set; } = "";
        public int BlackDamage { get; set; }
        public int BlueDamage { get; set; }
        public List<BrawlRound> Rounds { get; set; } = new();
    }
}
