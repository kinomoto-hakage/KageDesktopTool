using System.Runtime.InteropServices;

namespace Kage.Workspace;

internal static class ContentOrdering
{
    internal static ContentEntry[] Sort(IEnumerable<ContentEntry> entries, FolderRecord folder)
    {
        var all = entries.ToArray();
        if (folder.SortKey == ContentSortKey.Custom) return Reconcile(all, folder.CustomOrder);
        return all.OrderBy(entry => entry, Comparer<ContentEntry>.Create((left, right) =>
        {
            if (left.IsDirectory != right.IsDirectory) return left.IsDirectory ? -1 : 1;
            var compared = folder.SortKey switch
            {
                ContentSortKey.Modified => CompareMetadata(left.ModifiedTicks, right.ModifiedTicks, folder.SortDescending),
                ContentSortKey.Size when !left.IsDirectory => CompareMetadata(left.Length, right.Length, folder.SortDescending),
                _ => folder.SortDescending ? NaturalNames.Compare(right.Name, left.Name) : NaturalNames.Compare(left.Name, right.Name)
            };
            if (compared != 0) return compared;
            compared = NaturalNames.Compare(left.Name, right.Name);
            return compared != 0 ? compared : StringComparer.OrdinalIgnoreCase.Compare(left.ActualPath, right.ActualPath);
        })).ToArray();
    }

    internal static ContentEntry[] Reconcile(ContentEntry[] entries, ContentOrderItem[]? order)
    {
        var remaining = entries.ToList();
        var sorted = new List<ContentEntry>();
        foreach (var saved in order ?? [])
        {
            var entry = remaining.FirstOrDefault(item => string.Equals(item.Name, saved.Name, StringComparison.OrdinalIgnoreCase)
                && (saved.Identity == null || item.Identity == null || saved.Identity == item.Identity))
                ?? remaining.FirstOrDefault(item => saved.Identity != null && item.Identity == saved.Identity);
            if (entry == null) continue;
            sorted.Add(entry);
            remaining.Remove(entry);
        }
        sorted.AddRange(remaining.OrderBy(entry => entry.Name, NaturalNames).ThenBy(entry => entry.ActualPath, StringComparer.OrdinalIgnoreCase));
        return sorted.ToArray();
    }

    private static int CompareMetadata(long? left, long? right, bool descending)
    {
        // 缺失元数据固定放在本类末尾，不伪造成零；同键以名称确定稳定顺序。
        if (!left.HasValue || !right.HasValue) return left.HasValue ? -1 : right.HasValue ? 1 : 0;
        return descending ? right.Value.CompareTo(left.Value) : left.Value.CompareTo(right.Value);
    }

    internal static readonly IComparer<string> NaturalNames = Comparer<string>.Create((left, right) =>
        OperatingSystem.IsWindows() ? StrCmpLogicalW(left, right) : StringComparer.OrdinalIgnoreCase.Compare(left, right));
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int StrCmpLogicalW(string left, string right);
}
