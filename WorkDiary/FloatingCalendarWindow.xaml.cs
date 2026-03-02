using System.Windows;
using System.Windows.Controls;

namespace WorkDiary;

public partial class FloatingCalendarWindow : Window
{
    /// <summary>使用者在月曆上點選日期時觸發</summary>
    public event Action<DateTime>? DateSelected;

    public FloatingCalendarWindow()
    {
        InitializeComponent();
    }

    /// <summary>設定月曆顯示的選取日期（不觸發 DateSelected 事件）</summary>
    public void SetSelectedDate(DateTime date)
    {
        FloatingCalendar.SelectedDatesChanged -= FloatingCalendar_SelectedDatesChanged;
        FloatingCalendar.SelectedDate = date;
        FloatingCalendar.DisplayDate = date;
        FloatingCalendar.SelectedDatesChanged += FloatingCalendar_SelectedDatesChanged;
    }

    // ── 拖曳把手：按住即可移動整個視窗 ──
    private void DragHandle_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    // ── 關閉按鈕：隱藏而非關閉，保留狀態 ──
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    // ── 日期選擇：通知 MainWindow ──
    private void FloatingCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FloatingCalendar.SelectedDate is { } selected)
            DateSelected?.Invoke(selected.Date);
    }
}
