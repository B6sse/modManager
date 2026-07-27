namespace BassesModManager
{
    // Minimal again - this app only ever has zero or one entries (Star Wars Battlefront),
    // so there's no need for the removed multi-game bookkeeping this class used to carry.
    public class GameEntry
    {
        public string Path { get; set; }
        public string BannerPath { get; set; }
    }
}
