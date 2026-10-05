using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Kage.Workspace;

namespace Kage.Desktop;

internal sealed record OperationDetails(Guid Id, DateTimeOffset Time, string Title, string Summary, string Details,
    string Delivery = "等待投递");

// 独立于业务状态保存结果；投递失败不能改变文件操作或工作区恢复意图。
internal sealed class OperationFeedback
{
    private readonly string? path;
    private readonly Action<OperationDetails>? deliver;
    private readonly List<OperationDetails> entries = new();
    private bool damaged;
    internal IReadOnlyList<OperationDetails> Entries => entries.AsReadOnly();
    internal string? StorageNotice { get; private set; }

    internal OperationFeedback(string? directory = null, Action<OperationDetails>? deliver = null)
    {
        this.deliver = deliver;
        path = directory == null ? null : Path.Combine(directory, "operation-results.json");
        if (path == null || !File.Exists(path)) return;
        try { entries.AddRange(JsonSerializer.Deserialize<OperationDetails[]>(File.ReadAllText(path)) ?? []); }
        catch (Exception e) { damaged = true; StorageNotice = $"历史结果无法读取，原文件保留；本次结果仍可查看：{e.Message}"; }
    }

    internal OperationDetails Record(string title, OperationResult result, bool enabled)
        => Record(title, result.Message, result.Message + (result.ActualPath == null ? "" : $"\n实际位置：{result.ActualPath}"), enabled);

    internal OperationDetails Record(BatchMoveResult batch, bool enabled, string? targetNotice = null)
    {
        var success = batch.Items.Count(item => item.Outcome == Outcome.Success);
        var skipped = batch.Items.Count(item => item.Outcome == Outcome.Skipped);
        var cancelled = batch.Items.Count(item => item.Outcome == Outcome.Cancelled);
        var failures = batch.Items.Where(item => item.Outcome is Outcome.Failed or Outcome.Conflict or Outcome.RecoveryRequired).ToArray();
        var summary = $"成功 {success}，跳过 {skipped}，取消 {cancelled}，失败 {failures.Length}。";
        if (failures.Length != 0) summary += " " + failures[0].Message;
        if (!batch.StateCommit.Succeeded) summary += " " + batch.StateCommit.Message;
        var details = string.Join("\n\n", batch.Items.Select(item => $"{item.Outcome}：{item.SourcePath}\n{item.Message}\n实际位置：{item.ActualPath ?? "未确认"}"))
            + $"\n\n状态提交：{batch.StateCommit.Message}" + (targetNotice == null ? "" : "\n" + targetNotice);
        return Record("文件移动", summary, details, enabled);
    }

    private OperationDetails Record(string title, string summary, string details, bool enabled)
    {
        var entry = new OperationDetails(Guid.NewGuid(), DateTimeOffset.Now, title, summary, details);
        entries.Insert(0, entry);
        if (entries.Count > 100) entries.RemoveRange(100, entries.Count - 100);
        Save();
        try
        {
            if (enabled && deliver != null) { deliver(entry); entry = entry with { Delivery = "已提交 Windows；实际显示由系统通知设置决定" }; }
            else entry = entry with { Delivery = enabled ? "当前会话未启用系统投递" : "消息提示已关闭" };
        }
        catch (Exception e) { entry = entry with { Delivery = $"通知未投递：{e.Message}；操作结果保留" }; }
        entries[0] = entry;
        Save();
        return entry;
    }

    private void Save()
    {
        if (path == null || damaged) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries));
            File.Move(temporary, path, true);
            StorageNotice = null;
        }
        catch (Exception e) { StorageNotice = $"结果历史保存失败；本次详情仍可查看：{e.Message}"; }
    }
}
