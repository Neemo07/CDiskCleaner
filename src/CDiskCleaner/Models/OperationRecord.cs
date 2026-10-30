using System;

namespace CDiskCleaner.Models
{
    public sealed class OperationRecord
    {
        public Guid ItemId { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public long SizeBytes { get; set; }
        public OperationMethod Method { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public DateTime TimestampUtc { get; set; }
    }
}
