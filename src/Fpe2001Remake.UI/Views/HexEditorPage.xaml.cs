using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Fpe2001Remake.BinaryEditor.Core;
using Fpe2001Remake.UI.ViewModels.Modules;

namespace Fpe2001Remake.UI.Views;

public partial class HexEditorPage : UserControl
{
    public HexEditorPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>页面加载完成（含每次切回模块重建时）：消费缓存的定位请求并滚动到目标字节。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is HexEditorViewModel vm)
        {
            vm.ConsumePendingFocus();
        }
    }

    /// <summary>订阅/退订视图模型的滚动请求（模块切换会重建页面，DataContext 每次都会变化）。</summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is HexEditorViewModel oldVm)
        {
            oldVm.ScrollRequested -= OnScrollRequested;
        }
        if (e.NewValue is HexEditorViewModel vm)
        {
            vm.ScrollRequested += OnScrollRequested;
            // 页面就绪立即消费（覆盖“请求早于页面创建”的时序竞争）
            vm.ConsumePendingFocus();
        }
    }

    /// <summary>滚动定位：目标字节所在行成为可视区第一行、目标字节列成为最左列（“左上角第一位”），
    /// 并高亮该字节。注意：SelectionUnit=Cell 下必须先清空选中再滚动，
    /// 否则容器生成时会尝试选中整行并抛 InvalidOperationException（历史崩溃根因）。</summary>
    private void OnScrollRequested()
    {
        if (DataContext is not HexEditorViewModel vm || vm.SelectedRow is null)
        {
            return;
        }
        var row = vm.SelectedRow;
        var columnIndex = vm.FocusByteColumn + 1; // 第 0 列是“偏移”，1..16 是字节列
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (columnIndex < 1 || columnIndex >= HexGrid.Columns.Count)
            {
                return;
            }
            var column = HexGrid.Columns[columnIndex];

            // 先清空选中单元格：SelectedItem 归 null，避免滚动时选择整行崩溃
            HexGrid.SelectedCells.Clear();
            HexGrid.SelectedItem = null;

            // 先滚动到目标单元格（保证行容器生成），再精确对齐
            HexGrid.ScrollIntoView(row, column);
            HexGrid.UpdateLayout();
            HexGrid.ScrollIntoView(row, column);
            HexGrid.UpdateLayout();

            // 精确滚动：目标行 -> 可视区第一行；目标字节列 -> 可视区最左列
            if (FindVisualChild<ScrollViewer>(HexGrid) is { } sv)
            {
                var byteOffset = vm.SelectedByteOffset ?? vm.SelectedRow.Offset;
                var rowIndex = (int)(byteOffset / 16);
                sv.ScrollToVerticalOffset(rowIndex * HexGrid.RowHeight);

                var targetColIndex = columnIndex; // 1..16（字节列）
                var colLeft = 0.0;
                for (var i = 0; i < targetColIndex; i++)
                {
                    colLeft += HexGrid.Columns[i].ActualWidth;
                }
                sv.ScrollToHorizontalOffset(colLeft - HexGrid.Columns[targetColIndex].ActualWidth);
                HexGrid.UpdateLayout();
            }

            // 高亮目标字节单元格
            var cell = new DataGridCellInfo(row, column);
            HexGrid.SelectedCells.Add(cell);
            HexGrid.CurrentCell = cell;
        }), DispatcherPriority.Loaded);
    }

    /// <summary>在可视树中查找指定类型的子元素（滚动定位需要 DataGrid 内部的 ScrollViewer）。</summary>
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                return typed;
            }
            if (FindVisualChild<T>(child) is { } nested)
            {
                return nested;
            }
        }
        return null;
    }

    /// <summary>单元格点击：把选中的字节同步到视图模型（字节级编辑起点）。</summary>
    private void HexGrid_CurrentCellChanged(object sender, EventArgs e)
    {
        if (DataContext is not HexEditorViewModel vm)
        {
            return;
        }
        if (HexGrid.CurrentCell.Item is HexRow row && HexGrid.CurrentCell.Column is { } column)
        {
            var columnIndex = HexGrid.Columns.IndexOf(column);
            var byteIndex = columnIndex - 1; // 第 0 列是“偏移”
            if (byteIndex >= 0 && byteIndex < 16)
            {
                vm.SelectByte(row.Offset + (ulong)byteIndex);
            }
        }
    }
}
