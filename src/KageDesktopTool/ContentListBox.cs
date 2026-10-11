using System.Windows.Controls;
using System.Windows.Input;

namespace Kage.Desktop;

// 通过 WPF 正式的 protected 锚点入口协调鼠标／键盘，清除不重建列表或改变滚动位置。
internal sealed class ContentListBox : ListBox
{
    internal void ResetSelection()
    {
        UnselectAll();
        AnchorItem = null;
    }

    internal void SetSelectionAnchor(ListBoxItem item) => AnchorItem = item;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var newRange = SelectionMode == SelectionMode.Extended && AnchorItem == null && SelectedItems.Count == 0
            && (Keyboard.Modifiers & ModifierKeys.Shift) != 0
            && e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Space;
        if (!newRange) { base.OnKeyDown(e); return; }
        // 首次 Shift 导航仍由 WPF 定位，只从新目标建立范围，不使用默认首项或旧焦点作为锚点。
        SelectionMode = SelectionMode.Multiple;
        try { base.OnKeyDown(e); }
        finally { SelectionMode = SelectionMode.Extended; }
        if (e.Handled && Keyboard.FocusedElement is ListBoxItem target && Items.Contains(target))
        {
            target.IsSelected = true;
            AnchorItem = target;
        }
    }
}
