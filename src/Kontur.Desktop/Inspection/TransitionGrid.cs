namespace Kontur.Desktop;

internal sealed class TransitionGrid : DataGridView
{
    protected override bool ProcessDataGridViewKey(KeyEventArgs e) => e.KeyCode is Keys.Left or Keys.Right || base.ProcessDataGridViewKey(e);
    protected override bool ProcessDialogKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right || base.ProcessDialogKey(keyData);
    protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
        {
            e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.Focus);
            e.Handled = true;
        }
        base.OnCellPainting(e);
    }
}
