using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CDiskCleaner.Models;

namespace CDiskCleaner.Services
{
    public static class ReportBuilder
    {
        public static ReportData Build(
            SessionState session,
            ScanResult scan,
            IList<ScanItem> pending,
            DriveSnapshot now)
        {
            DriveSnapshot before = session != null
                ? session.StartSnapshot
                : (scan != null ? scan.Snapshot : new DriveSnapshot("C:", 0, 0));
            now = now ?? DriveSnapshot.Capture(@"C:\");
            pending = pending ?? new List<ScanItem>();

            int deletedCount = session == null ? 0 : session.DeletedCount;
            long deletedBytes = session == null ? 0 : session.DeletedBytes;
            int recycledCount = session == null ? 0 : session.RecycledCount;
            long recycledBytes = session == null ? 0 : session.RecycledBytes;
            int skippedCount = session == null ? 0 : session.SkippedCount;
            long skippedBytes = session == null ? 0 : session.SkippedBytes;
            int failedCount = session == null ? 0 : session.FailedCount;
            long failedBytes = session == null ? 0 : session.FailedBytes;
            long net = now.FreeBytes - before.FreeBytes;

            var text = new StringBuilder();
            text.AppendLine(Loc.T("Report.Title"));
            text.AppendLine(new string('=', 48));
            text.AppendLine(Loc.T("Report.Generated", DateTime.Now.ToString("yyyy-MM-dd HH:mm")));
            text.AppendLine();

            text.AppendLine(Loc.T("Report.SectionSpace"));
            text.AppendLine(Loc.T("Report.Drive") + ": " + now.Name);
            text.AppendLine(Loc.T("Report.Total") + ": " + Format.Size(now.TotalBytes));
            text.AppendLine(Loc.T("Report.BeforeLine", Format.Size(before.FreeBytes)));
            text.AppendLine(Loc.T("Report.NowLine", Format.Size(now.FreeBytes)));
            text.AppendLine(Loc.T("Report.NetLine", Format.SizeSigned(net)));
            text.AppendLine(Loc.T("Report.PermanentLine", Format.Count(deletedCount), Format.Size(deletedBytes)));
            text.AppendLine(Loc.T("Report.RecycledLine", Format.Count(recycledCount), Format.Size(recycledBytes)));
            text.AppendLine(Loc.T("Report.SkippedLine", Format.Count(skippedCount), Format.Size(skippedBytes)));
            text.AppendLine(Loc.T("Report.FailedLine", Format.Count(failedCount), Format.Size(failedBytes)));
            text.AppendLine();

            text.AppendLine(Loc.T("Report.SectionOperations"));
            if (scan != null)
            {
                text.AppendLine(Loc.T(
                    "Report.ScanLine",
                    Format.Count(scan.FilesScanned),
                    Format.Count(scan.DirectoriesScanned),
                    Format.Count(scan.CandidateCount)));
                if (scan.InaccessibleDirectories > 0)
                {
                    text.AppendLine(Loc.T("Report.InaccessibleLine", Format.Count(scan.InaccessibleDirectories)));
                }

                if (scan.Options != null && scan.Options.FindDuplicates)
                {
                    text.AppendLine(Loc.T("Report.DuplicateLine", Format.Size(scan.DuplicateHashedBytes)));
                    if (scan.DuplicateCapReached)
                    {
                        text.AppendLine(Loc.T("Scan.DuplicateCap"));
                    }

                    if (scan.DuplicatePoolTruncated)
                    {
                        text.AppendLine(Loc.T("Scan.DuplicatePoolTruncated"));
                    }
                }
            }

            text.AppendLine();

            text.AppendLine(Loc.T("Report.SectionRemaining"));
            var top = pending
                .Where(i => i.Category != ScanCategory.C && i.SizeBytes > 0)
                .OrderByDescending(i => i.SizeBytes)
                .Take(10)
                .ToList();
            if (top.Count == 0)
            {
                text.AppendLine(Loc.T("Report.NoneRemaining"));
            }
            else
            {
                for (int i = 0; i < top.Count; i++)
                {
                    ScanItem item = top[i];
                    text.AppendLine(Loc.T(
                        "Report.RemainingLine",
                        (i + 1).ToString(),
                        item.Category.ToString(),
                        item.Path,
                        Format.Size(item.SizeBytes)));
                }
            }

            text.AppendLine();
            text.AppendLine(Loc.T("Report.SectionNotes"));
            text.AppendLine("- " + Loc.T("Report.NoteRecycle"));
            text.AppendLine("- " + Loc.T("Report.NoteDelta"));
            text.AppendLine("- " + Loc.T("Report.NoteAdmin"));
            text.AppendLine("- " + Loc.T("Report.NoteNoAuto"));

            return new ReportData
            {
                Title = Loc.T("Report.Title"),
                Subtitle = Loc.T("Report.Subtitle"),
                Text = text.ToString(),
                Before = Format.Size(before.FreeBytes),
                Now = Format.Size(now.FreeBytes),
                Net = Format.SizeSigned(net),
                Recovered = Format.Size(deletedBytes),
                Recycled = Format.Size(recycledBytes),
                DeletedCount = Format.Count(deletedCount),
                RecycledCount = Format.Count(recycledCount),
                SkippedCount = Format.Count(skippedCount),
                FailedCount = Format.Count(failedCount),
                RemainingCount = Format.Count(pending.Count(i => i.Category != ScanCategory.C))
            };
        }
    }
}
