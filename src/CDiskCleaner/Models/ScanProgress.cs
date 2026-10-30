namespace CDiskCleaner.Models
{
    public sealed class ScanProgress
    {
        public ScanProgress(
            string phaseKey,
            string currentPath,
            long filesScanned,
            long directoriesScanned,
            int inaccessibleDirectories,
            int candidates,
            long candidateBytes)
        {
            PhaseKey = phaseKey;
            CurrentPath = currentPath;
            FilesScanned = filesScanned;
            DirectoriesScanned = directoriesScanned;
            InaccessibleDirectories = inaccessibleDirectories;
            Candidates = candidates;
            CandidateBytes = candidateBytes;
        }

        public string PhaseKey { get; private set; }
        public string CurrentPath { get; private set; }
        public long FilesScanned { get; private set; }
        public long DirectoriesScanned { get; private set; }
        public int InaccessibleDirectories { get; private set; }
        public int Candidates { get; private set; }
        public long CandidateBytes { get; private set; }
    }
}
