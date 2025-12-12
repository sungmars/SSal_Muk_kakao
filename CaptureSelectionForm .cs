using System;
using System.Drawing;
using System.Windows.Forms;

public class CaptureSelectionForm : Form
{
    private bool _isDragging = false;
    private Point _startPoint;
    private Point _currentPoint;

    public Rectangle SelectedRectScreen { get; private set; } = Rectangle.Empty;

    public CaptureSelectionForm()
    {
        // 전체 화면 덮는 반투명 폼
        this.FormBorderStyle = FormBorderStyle.None;
        this.WindowState = FormWindowState.Maximized;
        this.TopMost = true;
        this.ShowInTaskbar = false;

        this.BackColor = Color.Black;
        this.Opacity = 0.2;
        this.DoubleBuffered = true;
        this.Cursor = Cursors.Cross;
        this.KeyPreview = true;

        this.MouseDown += OnMouseDown;
        this.MouseMove += OnMouseMove;
        this.MouseUp += OnMouseUp;
        this.KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }

    private void OnMouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        _isDragging = true;
        _startPoint = e.Location;
        _currentPoint = e.Location;
        Invalidate();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        _currentPoint = e.Location;
        Invalidate();
    }

    private void OnMouseUp(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        _isDragging = false;
        _currentPoint = e.Location;
        Invalidate();

        int x = Math.Min(_startPoint.X, _currentPoint.X);
        int y = Math.Min(_startPoint.Y, _currentPoint.Y);
        int w = Math.Abs(_currentPoint.X - _startPoint.X);
        int h = Math.Abs(_currentPoint.Y - _startPoint.Y);

        if (w < 5 || h < 5)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
            return;
        }

        Rectangle clientRect = new Rectangle(x, y, w, h);
        Point screenPos = this.PointToScreen(clientRect.Location);
        SelectedRectScreen = new Rectangle(screenPos, clientRect.Size);

        this.DialogResult = DialogResult.OK;
        this.Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (_isDragging)
        {
            int x = Math.Min(_startPoint.X, _currentPoint.X);
            int y = Math.Min(_startPoint.Y, _currentPoint.Y);
            int w = Math.Abs(_currentPoint.X - _startPoint.X);
            int h = Math.Abs(_currentPoint.Y - _startPoint.Y);

            if (w > 0 && h > 0)
            {
                Rectangle rect = new Rectangle(x, y, w, h);

                using (Brush fill = new SolidBrush(Color.FromArgb(60, Color.LightSkyBlue)))
                    e.Graphics.FillRectangle(fill, rect);

                using (Pen pen = new Pen(Color.DeepSkyBlue, 2))
                    e.Graphics.DrawRectangle(pen, rect);
            }
        }
    }
}
