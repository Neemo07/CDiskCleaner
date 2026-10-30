namespace CDiskCleaner.Models
{
    public sealed class DeletionOutcome
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public bool RecycleRequested { get; set; }
        public bool LikelyPermanentlyDeleted { get; set; }

        public long Bytes { get; set; }
        public long FreeBefore { get; set; }
        public long FreeAfter { get; set; }

        public long FreeDelta
        {
            get { return FreeAfter - FreeBefore; }
        }

        public int TotalTargets { get; set; }
        public int SucceededTargets { get; set; }
    }
}
