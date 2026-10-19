using System.ComponentModel;

namespace BassesModManager
{
    /// <summary>
    /// One place the game was found (or the folder the user picked), as a row on the
    /// first-run screen: the folder, where the knowledge came from, and whether it is the
    /// one CONTINUE will use.
    /// </summary>
    public sealed class GameCandidate : INotifyPropertyChanged
    {
        private bool isChosen;

        public GameCandidate(string path, string source)
        {
            Path = path;
            Source = source;
        }

        public string Path { get; }

        /// <summary>"EA app", "Steam", "Found on D:" or "Chosen by you".</summary>
        public string Source { get; }

        public bool IsChosen
        {
            get => isChosen;
            set
            {
                if (isChosen == value)
                    return;
                isChosen = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChosen)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
