namespace CDiskCleaner.Models
{
    public sealed class ReportData
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Text { get; set; }

        public string Before { get; set; }
        public string Now { get; set; }
        public string Net { get; set; }
        public string Recovered { get; set; }
        public string Recycled { get; set; }
        public string DeletedCount { get; set; }
        public string RecycledCount { get; set; }
        public string SkippedCount { get; set; }
        public string FailedCount { get; set; }
        public string RemainingCount { get; set; }
    }
}
