using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.UI;

namespace SafetyGuard.WinForms.Pages;

public sealed class OfflinePage : UserControl
{
    private readonly AppBootstrap _app;

    private string? _selected;
    private bool _running;

    // UI
    private Label _lblFile = null!;
    private Label _lblStatus = null!;
    private ProgressBar _progress = null!;
    private Guna2Button _btnBrowse = null!;
    private Guna2Button _btnRun = null!;
    private Guna2Button _btnClear = null!;

    private PictureBox _preview = null!;
    private ListView _events = null!;

    public OfflinePage(AppBootstrap app)
    {
        _app = app;
        Dock = DockStyle.Fill;
        BackColor = AppColors.ContentBg;

        BuildUI();
    }

    private void BuildUI()
    {
        Controls.Clear();

        var card = ControlFactory.Card();
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(16);
        Controls.Add(card);

        // Root layout: TOP (132) + MAIN (Fill) + PROGRESS (16)
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        card.Controls.Add(root);

        // ===== TOP =====
        var top = new Guna2Panel
        {
            BorderRadius = 14,
            FillColor = Color.White,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            Margin = Padding.Empty
        };
        top.ShadowDecoration.Enabled = false;
        root.Controls.Add(top, 0, 0);

        var topLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(topLayout);

        var info = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        var title = ControlFactory.Muted("Offline Analysis", 12, true);
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleLeft;

        _lblFile = ControlFactory.Muted("No file selected.", 9);
        _lblFile.Dock = DockStyle.Fill;
        _lblFile.AutoEllipsis = true;

        _lblStatus = ControlFactory.Muted("Idle", 9);
        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.AutoEllipsis = true;

        info.Controls.Add(title, 0, 0);
        info.Controls.Add(_lblFile, 0, 1);
        info.Controls.Add(_lblStatus, 0, 2);

        topLayout.Controls.Add(info, 0, 0);

        var btns = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Fill,
            Margin = new Padding(12, 0, 0, 0),
            Padding = Padding.Empty
        };
        topLayout.Controls.Add(btns, 1, 0);

        _btnBrowse = new Guna2Button
        {
            Text = "Browse",
            BorderRadius = 10,
            FillColor = AppColors.PrimaryBlue,
            ForeColor = Color.White,
            Size = new Size(110, 38),
            Margin = new Padding(0, 0, 10, 0)
        };
        _btnBrowse.Click += (_, _) => Browse();

        _btnRun = new Guna2Button
        {
            Text = "Run Detection",
            BorderRadius = 10,
            FillColor = AppColors.GoodGreen,
            ForeColor = Color.White,
            Size = new Size(140, 38),
            Margin = new Padding(0, 0, 10, 0)
        };
        _btnRun.Click += async (_, _) => await RunAsync();

        _btnClear = new Guna2Button
        {
            Text = "Clear Log",
            BorderRadius = 10,
            FillColor = Color.FromArgb(238, 242, 248),
            ForeColor = Color.FromArgb(60, 70, 90),
            Size = new Size(110, 38),
            Margin = new Padding(0, 0, 0, 0)
        };
        _btnClear.Click += (_, _) => Ui(() => _events.Items.Clear());

        btns.Controls.Add(_btnBrowse);
        btns.Controls.Add(_btnRun);
        btns.Controls.Add(_btnClear);

        // Drag/drop
        top.AllowDrop = true;
        top.DragEnter += (_, e) =>
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        };
        top.DragDrop += (_, e) =>
        {
            var files = (string[]?)e.Data?.GetData(DataFormats.FileDrop);
            if (files is { Length: > 0 }) SetSelected(files[0]);
        };

        // ===== MAIN =====
        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 14, 0, 0),
            Margin = Padding.Empty
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        root.Controls.Add(main, 0, 1);

        // Preview card
        var previewCard = new Guna2Panel
        {
            BorderRadius = 14,
            FillColor = Color.White,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 12, 0)
        };
        previewCard.ShadowDecoration.Enabled = false;
        main.Controls.Add(previewCard, 0, 0);

        var pvLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        pvLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        pvLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewCard.Controls.Add(pvLayout);

        var pvTitle = ControlFactory.Muted("Preview", 10, true);
        pvTitle.Dock = DockStyle.Fill;
        pvTitle.TextAlign = ContentAlignment.MiddleLeft;
        pvLayout.Controls.Add(pvTitle, 0, 0);

        _preview = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 22, 22),
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = Padding.Empty
        };
        pvLayout.Controls.Add(_preview, 0, 1);

        // Event card
        var eventCard = new Guna2Panel
        {
            BorderRadius = 14,
            FillColor = Color.White,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            Margin = Padding.Empty
        };
        eventCard.ShadowDecoration.Enabled = false;
        main.Controls.Add(eventCard, 1, 0);

        var evLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        evLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        evLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        eventCard.Controls.Add(evLayout);

        var evHeader = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty };

        var evTitle = ControlFactory.Muted("Live Event Log", 10, true);
        evTitle.Dock = DockStyle.Left;
        evTitle.Width = 160;
        evTitle.TextAlign = ContentAlignment.MiddleLeft;

        var btnClearMini = new Guna2Button
        {
            Text = "Clear",
            BorderRadius = 8,
            Size = new Size(80, 28),
            Dock = DockStyle.Right,
            FillColor = Color.FromArgb(238, 242, 248),
            ForeColor = Color.FromArgb(60, 70, 90),
            Margin = Padding.Empty
        };
        btnClearMini.Click += (_, _) => Ui(() => _events.Items.Clear());

        evHeader.Controls.Add(btnClearMini);
        evHeader.Controls.Add(evTitle);

        evLayout.Controls.Add(evHeader, 0, 0);

        _events = new DoubleBufferedListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            HideSelection = false,
            MultiSelect = false,
            Margin = Padding.Empty
        };
        _events.Columns.Add("Time", 80);
        _events.Columns.Add("Type", 115);
        _events.Columns.Add("Level", 75);
        _events.Columns.Add("Conf", 60);

        _events.Resize += (_, _) =>
        {
            var w = _events.ClientSize.Width;
            if (w <= 10 || _events.Columns.Count < 4) return;

            _events.Columns[0].Width = 80;
            _events.Columns[2].Width = 75;
            _events.Columns[3].Width = 60;
            _events.Columns[1].Width = Math.Max(120, w - (80 + 75 + 60 + 8));
        };

        evLayout.Controls.Add(_events, 0, 1);

        // ===== PROGRESS =====
        _progress = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 16,
            Visible = false,
            Margin = Padding.Empty
        };
        root.Controls.Add(_progress, 0, 2);
    }

    private void Browse()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Video/Image|*.mp4;*.avi;*.mkv;*.mov;*.jpg;*.jpeg;*.png",
            Multiselect = false
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        SetSelected(ofd.FileName);
    }

    private void SetSelected(string path)
    {
        _selected = path;
        _lblFile.Text = path;
        _lblStatus.Text = "Ready";
    }

    private async Task RunAsync()
    {
        if (_running) return;

        if (string.IsNullOrWhiteSpace(_selected) || !File.Exists(_selected))
        {
            MessageBox.Show(this, "Please select a file first.", "Offline", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _running = true;
        SetRunningUI(true);

        Ui(() =>
        {
            _events.Items.Clear();
            _progress.Value = 0;
        });

        var camId = "offline";
        var camName = "Offline Import";

        var ext = Path.GetExtension(_selected).ToLowerInvariant();
        var isImage = ext is ".jpg" or ".jpeg" or ".png";

        try
        {
            await Task.Run(() =>
            {
                if (isImage)
                {
                    Ui(() => _lblStatus.Text = "Analyzing image...");
                    _app.Offline.AnalyzeImage(
                        _selected!,
                        camId,
                        camName,
                        onFrame: (bmp, dets) =>
                        {
                            UpdatePreview(bmp, dets);
                            AddEvents(dets);
                            bmp.Dispose();
                        },
                        forceCreate: true
                    );
                    Ui(() => _progress.Value = 100);
                }
                else
                {
                    Ui(() => _lblStatus.Text = "Analyzing video...");

                    int sampleEveryNFrames = 10;

                    _app.Offline.AnalyzeVideo(
                        _selected!,
                        camId,
                        camName,
                        sampleEveryNFrames: sampleEveryNFrames,
                        progress: (i, total) =>
                        {
                            Ui(() =>
                            {
                                _progress.Visible = true;
                                var pct = Math.Min(100, (int)(i * 100.0 / Math.Max(1, total)));
                                _progress.Value = pct;
                            });
                        },
                        onFrame: (bmp, dets) =>
                        {
                            UpdatePreview(bmp, dets);
                            AddEvents(dets);
                            bmp.Dispose();
                        },
                        forceCreate: true
                    );
                }
            });

            Ui(() => _lblStatus.Text = "Done. Events saved to History.");
            MessageBox.Show(this, "Offline analysis complete.\nCheck History & Evidence.", "Offline",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _app.Logs.Error("Offline run failed: " + ex);
            Ui(() => _lblStatus.Text = "Failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Offline", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _running = false;
            SetRunningUI(false);
        }
    }

    private void SetRunningUI(bool running)
    {
        Ui(() =>
        {
            _btnBrowse.Enabled = !running;
            _btnRun.Enabled = !running;
            _btnClear.Enabled = !running;

            _progress.Visible = running;
            if (!running) _progress.Value = 0;
        });
    }

    private void UpdatePreview(Bitmap frame, Detection[] dets)
    {
        var annotated = (Bitmap)frame.Clone();
        using (var g = Graphics.FromImage(annotated))
        using (var font = new Font("Segoe UI", 10, FontStyle.Bold))
        {
            foreach (var d in dets)
            {
                var color = ColorFor(d.Type);
                using var pen = new Pen(color, 2);

                var r = new Rectangle(
                    (int)d.Box.X,
                    (int)d.Box.Y,
                    (int)d.Box.W,
                    (int)d.Box.H
                );

                r = Rectangle.Intersect(r, new Rectangle(0, 0, annotated.Width - 1, annotated.Height - 1));
                if (r.Width <= 1 || r.Height <= 1) continue;

                g.DrawRectangle(pen, r);

                var label = $"{d.Type} {(d.Confidence * 100):0}%";
                var sz = g.MeasureString(label, font);

                var bgRect = new RectangleF(r.X, Math.Max(0, r.Y - sz.Height - 4), sz.Width + 10, sz.Height + 6);
                using var bg = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
                using var fg = new SolidBrush(Color.White);

                g.FillRectangle(bg, bgRect);
                g.DrawString(label, font, fg, bgRect.X + 5, bgRect.Y + 3);
            }
        }

        Ui(() =>
        {
            _preview.Image?.Dispose();
            _preview.Image = annotated;
        });
    }

    private void AddEvents(Detection[] dets)
    {
        if (dets == null || dets.Length == 0) return;

        var rules = _app.Settings.Current.Rules;

        Ui(() =>
        {
            foreach (var d in dets.OrderByDescending(x => x.Confidence).Take(6))
            {
                var lvl = rules.FirstOrDefault(r => r.Type == d.Type)?.Level ?? ViolationLevel.Warning;

                var it = new ListViewItem(DateTime.Now.ToString("HH:mm:ss"));
                it.SubItems.Add(d.Type.ToString());
                it.SubItems.Add(lvl.ToString());
                it.SubItems.Add($"{d.Confidence * 100:0}%");
                it.ForeColor = ColorFor(d.Type);

                _events.Items.Insert(0, it);
            }

            while (_events.Items.Count > 250)
                _events.Items.RemoveAt(_events.Items.Count - 1);
        });
    }

    private static Color ColorFor(ViolationType t) => t switch
    {
        ViolationType.NoHelmet => Color.Red,
        ViolationType.NoVest => Color.Orange,
        ViolationType.Smoking => Color.MediumPurple,
        _ => Color.DeepSkyBlue
    };

    private void Ui(Action a)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(a);
        else a();
    }

    // Giảm flicker khi insert items liên tục
    private sealed class DoubleBufferedListView : ListView
    {
        public DoubleBufferedListView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
        }
    }
}
