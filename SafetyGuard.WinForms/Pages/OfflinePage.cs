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

        // ===== TOP: Drop + 2 buttons =====
        var top = new Guna2Panel
        {
            BorderRadius = 14,
            FillColor = Color.White,
            Height = 132,
            Dock = DockStyle.Top,
            Padding = new Padding(16)
        };
        top.ShadowDecoration.Enabled = false;
        card.Controls.Add(top);

        var title = ControlFactory.Muted("Offline Analysis", 12, true);
        title.Location = new Point(10, 10);
        top.Controls.Add(title);

        _lblFile = ControlFactory.Muted("No file selected.", 9);
        _lblFile.AutoEllipsis = true;
        _lblFile.Width = 900;
        _lblFile.Location = new Point(10, 40);
        top.Controls.Add(_lblFile);

        _lblStatus = ControlFactory.Muted("Idle", 9);
        _lblStatus.Location = new Point(10, 64);
        top.Controls.Add(_lblStatus);

        _btnBrowse = new Guna2Button
        {
            Text = "Browse",
            BorderRadius = 10,
            FillColor = AppColors.PrimaryBlue,
            ForeColor = Color.White,
            Size = new Size(110, 38),
            Location = new Point(10, 88)
        };
        _btnBrowse.Click += (_, _) => Browse();
        top.Controls.Add(_btnBrowse);

        _btnRun = new Guna2Button
        {
            Text = "Run Detection",
            BorderRadius = 10,
            FillColor = AppColors.GoodGreen,
            ForeColor = Color.White,
            Size = new Size(140, 38),
            Location = new Point(_btnBrowse.Right + 10, 88)
        };
        _btnRun.Click += async (_, _) => await RunAsync();
        top.Controls.Add(_btnRun);

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
            if (files is { Length: > 0 })
            {
                SetSelected(files[0]);
            }
        };

        // ===== MAIN: Preview + Event Log =====
        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 14, 0, 0)
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        card.Controls.Add(main);

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

        var pvTitle = ControlFactory.Muted("Preview", 10, true);
        pvTitle.Location = new Point(10, 10);
        previewCard.Controls.Add(pvTitle);

        _preview = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 22, 22),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        previewCard.Controls.Add(_preview);
        _preview.BringToFront();

        // Event card
        var eventCard = new Guna2Panel
        {
            BorderRadius = 14,
            FillColor = Color.White,
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        eventCard.ShadowDecoration.Enabled = false;
        main.Controls.Add(eventCard, 1, 0);

        var evTitle = ControlFactory.Muted("Live Event Log", 10, true);
        evTitle.Location = new Point(10, 10);
        eventCard.Controls.Add(evTitle);

        _events = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false
        };
        _events.Columns.Add("Time", 80);
        _events.Columns.Add("Type", 115);
        _events.Columns.Add("Level", 75);
        _events.Columns.Add("Conf", 60);
        eventCard.Controls.Add(_events);
        _events.BringToFront();

        // Bottom progress
        _progress = new ProgressBar
        {
            Dock = DockStyle.Bottom,
            Height = 16,
            Visible = false
        };
        card.Controls.Add(_progress);
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

        _events.Items.Clear();
        _progress.Value = 0;

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

                    // sample every N frames (10 = nhẹ, 5 = nặng hơn)
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

                // ✅ FIX: dùng W/H + cast int
                var r = new Rectangle(
                    (int)d.Box.X,
                    (int)d.Box.Y,
                    (int)d.Box.W,
                    (int)d.Box.H
                );

                // ✅ Clamp
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
}
