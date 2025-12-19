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

    // Match RealtimePage offline identifiers (so realtime "Live Events" can filter them out)
    private const string OfflineCameraId = "offline";
    private const string OfflineCameraName = "Offline Import";

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

        BuildUi();

        // show OFFLINE violations (log-once-per-track) in this page
        _app.Engine.OnViolationCreated += v =>
        {
            if (!_running) return;
            if (!string.Equals(v.CameraId, OfflineCameraId, StringComparison.OrdinalIgnoreCase)) return;
            BeginInvoke((Action)(() => AddViolationEvent(v)));
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(18),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        // top bar
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            BackColor = AppColors.ContentBg,
            Padding = new Padding(0, 0, 0, 10)
        };
        root.Controls.Add(bar, 0, 0);
        root.SetColumnSpan(bar, 2);

        _btnBrowse = new Guna2Button
        {
            Text = "Browser",
            BorderRadius = 10,
            Height = 36,
            Width = 110,
            FillColor = AppColors.PrimaryBlue,
            ForeColor = Color.White,
        };
        _btnBrowse.Click += (_, _) => Browse();
        bar.Controls.Add(_btnBrowse);

        _btnRun = new Guna2Button
        {
            Text = "Run",
            BorderRadius = 10,
            Height = 36,
            Width = 90,
            FillColor = AppColors.GoodGreen,
            ForeColor = Color.White,
        };
        _btnRun.Click += async (_, _) => await RunAsync();
        bar.Controls.Add(_btnRun);

        _btnClear = new Guna2Button
        {
            Text = "Clear events",
            BorderRadius = 10,
            Height = 36,
            Width = 120,
            FillColor = AppColors.ContentBg,     // thay CardBorder
            ForeColor = AppColors.MutedText,     // thay TextPrimary
            BorderThickness = 1,
            BorderColor = Color.FromArgb(210, 220, 235)
        };
        _btnClear.Click += (_, _) => _events.Items.Clear();
        bar.Controls.Add(_btnClear);

        _lblFile = new Label
        {
            AutoSize = true,
            ForeColor = AppColors.MutedText,
            Padding = new Padding(10, 8, 0, 0),
            Text = "File has not been selected yet!"
        };
        bar.Controls.Add(_lblFile);

        _lblStatus = new Label
        {
            AutoSize = true,
            ForeColor = AppColors.MutedText,
            Padding = new Padding(10, 8, 0, 0),
            Text = ""
        };
        bar.Controls.Add(_lblStatus);

        _progress = new ProgressBar
        {
            Width = 220,
            Height = 16,
            Visible = false
        };
        bar.Controls.Add(_progress);

        // left preview
        var leftCard = new Guna2Panel
        {
            Dock = DockStyle.Fill,
            BorderRadius = 14,
            FillColor = AppColors.CardBg,
            Padding = new Padding(12)
        };
        root.Controls.Add(leftCard, 0, 1);

        _preview = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        leftCard.Controls.Add(_preview);

        // right list
        var rightCard = new Guna2Panel
        {
            Dock = DockStyle.Fill,
            BorderRadius = 14,
            FillColor = AppColors.CardBg,
            Padding = new Padding(12)
        };
        root.Controls.Add(rightCard, 1, 1);

        _events = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };
        _events.Columns.Add("Time", 90);
        _events.Columns.Add("Class", 140);
        _events.Columns.Add("Level", 90);
        _events.Columns.Add("Conf", 70);
        rightCard.Controls.Add(_events);
    }

    private void Browse()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Media (*.mp4;*.avi;*.mkv;*.jpg;*.png)|*.mp4;*.avi;*.mkv;*.jpg;*.png|All files (*.*)|*.*",
            Title = "Choose a video or photo"
        };

        if (dlg.ShowDialog() != DialogResult.OK) return;

        _selected = dlg.FileName;
        _lblFile.Text = Path.GetFileName(_selected);
        _lblStatus.Text = "";
    }

    private async Task RunAsync()
    {
        if (_running) return;
        if (string.IsNullOrWhiteSpace(_selected) || !File.Exists(_selected))
        {
            _lblStatus.Text = "Have not selected a valid file.";
            return;
        }

        _running = true;
        ToggleUi(false);
        _events.Items.Clear();

        _app.Engine.ResetSession(OfflineCameraId);

        _progress.Visible = true;
        _progress.Value = 0;

        _lblStatus.Text = "Running...";

        try
        {
            var ext = Path.GetExtension(_selected).ToLowerInvariant();
            bool isImage = ext is ".jpg" or ".jpeg" or ".png" or ".bmp";
            bool isVideo = ext is ".mp4" or ".avi" or ".mkv" or ".mov";

            if (isImage)
            {
                await Task.Run(() =>
                {
                    _app.Offline.AnalyzeImage(
                        _selected!,
                        cameraId: OfflineCameraId,
                        cameraName: OfflineCameraName,
                        onFrame: (frame, dets) =>
                        {
                            BeginInvoke((Action)(() =>
                            {
                                UpdatePreview(frame, dets);
                            }));
                        },
                        forceCreate: true);
                });
            }
            else if (isVideo)
            {
                await Task.Run(() =>
                {
                    _app.Offline.AnalyzeVideo(
                        _selected!,
                        cameraId: OfflineCameraId,
                        cameraName: OfflineCameraName,
                        sampleEveryNFrames: 10,
                        progress: (i, total) =>
                        {
                            BeginInvoke((Action)(() =>
                            {
                                _progress.Maximum = Math.Max(1, total);
                                _progress.Value = Math.Min(_progress.Maximum, Math.Max(0, i));
                                _lblStatus.Text = $"Frame {i}/{total}";
                            }));
                        },
                        onFrame: (frame, dets) =>
                        {
                            BeginInvoke((Action)(() =>
                            {
                                UpdatePreview(frame, dets);
                            }));
                        },
                        forceCreate: true);
                });
            }
            else
            {
                _lblStatus.Text = "only support popular photos/videos.";
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Error: " + ex.Message;
        }
        finally
        {
            _progress.Visible = false;
            ToggleUi(true);
            _running = false;
            _lblStatus.Text = "Done.";
        }
    }

    private void ToggleUi(bool enabled)
    {
        _btnBrowse.Enabled = enabled;
        _btnRun.Enabled = enabled;
        _btnClear.Enabled = enabled;
    }

    private void UpdatePreview(Bitmap frame, DetectionResult[] dets)
    {
        // vẽ overlay đơn giản
        var bmp = (Bitmap)frame.Clone();
        using var g = Graphics.FromImage(bmp);
        g.DrawString($"dets={dets.Length}", new Font("Segoe UI", 12, FontStyle.Bold), Brushes.Yellow, 10, 10);

        using var font = new Font("Segoe UI", 10, FontStyle.Bold);

        foreach (var d in dets.OrderByDescending(x => x.Confidence).Take(30))
        {
            var rect = d.Box.ToRectClamped(bmp.Width, bmp.Height);
            var color = ColorFor(d.Class);
            using var pen = new Pen(color, 2);
            g.DrawRectangle(pen, rect);

            var label = $"{d.Class} {(d.Confidence * 100):0}%";
            var size = g.MeasureString(label, font);
            g.FillRectangle(new SolidBrush(Color.FromArgb(160, 0, 0, 0)), rect.X, Math.Max(0, rect.Y - size.Height), size.Width + 8, size.Height + 2);
            g.DrawString(label, font, Brushes.White, rect.X + 4, Math.Max(0, rect.Y - size.Height));
        }

        var old = _preview.Image;
        _preview.Image = bmp;
        old?.Dispose();
        frame.Dispose();
    }

    private void AddViolationEvent(ViolationRecord v)
    {
        // ✅ Only log violations created by ViolationEngine (đã chống spam theo TrackId)
        var it = new ListViewItem(v.TimeUtc.ToLocalTime().ToString("HH:mm:ss"));

        // show type + track id for clarity
        var cls = v.TrackId.HasValue ? $"{v.Type} (track {v.TrackId.Value})" : v.Type.ToString();
        it.SubItems.Add(cls);
        it.SubItems.Add(v.Level.ToString());
        it.SubItems.Add($"{v.Confidence * 100:0}%");

        it.ForeColor = v.Type switch
        {
            ViolationType.NoHelmet => Color.Red,
            ViolationType.NoVest => Color.Orange,
            ViolationType.NoGloves => Color.DeepSkyBlue,
            ViolationType.Smoking => Color.MediumPurple,
            _ => Color.White
        };

        _events.Items.Insert(0, it);
    }

    private static Color ColorFor(ObjectClass c) => c switch
    {
        ObjectClass.NoHelmet => Color.Red,
        ObjectClass.NoVest => Color.Orange,
        ObjectClass.NoGloves => Color.DeepSkyBlue,
        ObjectClass.Smoking => Color.MediumPurple,
        ObjectClass.Person => Color.Lime,
        _ => Color.White
    };

    private static ViolationType? ToViolationType(ObjectClass c) => c switch
    {
        ObjectClass.NoHelmet => ViolationType.NoHelmet,
        ObjectClass.NoVest => ViolationType.NoVest,
        ObjectClass.NoGloves => ViolationType.NoGloves,
        ObjectClass.Smoking => ViolationType.Smoking,
        _ => null
    };
}
