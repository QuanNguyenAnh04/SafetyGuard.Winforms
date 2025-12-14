using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using SafetyGuard.WinForms.UI;

namespace SafetyGuard.WinForms.Pages;

public sealed class OfflinePage : UserControl
{
    private readonly AppBootstrap _app;

    private readonly Label _lblFile = ControlFactory.Muted("No file selected.", 9);
    private readonly ProgressBar _progress = new() { Height = 16, Dock = DockStyle.Bottom };
    private string? _selected;

    public OfflinePage(AppBootstrap app)
    {
        _app = app;
        Dock = DockStyle.Fill;
        BackColor = AppColors.ContentBg;

        BuildUI();
    }

    private void BuildUI()
    {   
        
        var card = ControlFactory.Card();
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(16);
        Controls.Add(card);

        var drop = new Guna2Panel
        {
            BorderRadius = 14,
            FillColor = Color.White,
            Location = new Point(16, 80),
            Size = new Size(860, 220)
        };
        card.Controls.Add(drop);

        var lbl = ControlFactory.Muted("Drag & drop or click Browse", 10, true);
        lbl.Location = new Point(22, 24);
        drop.Controls.Add(lbl);

        _lblFile.Location = new Point(22, 58);
        drop.Controls.Add(_lblFile);

        var btnBrowse = new Guna2Button { Text = "Browse", BorderRadius = 10, FillColor = AppColors.PrimaryBlue, ForeColor = Color.White };
        btnBrowse.Location = new Point(22, 120);
        btnBrowse.Size = new Size(110, 38);
        btnBrowse.Click += (_, _) => Browse();
        drop.Controls.Add(btnBrowse);

        var btnRun = new Guna2Button { Text = "Run Detection", BorderRadius = 10, FillColor = AppColors.GoodGreen, ForeColor = Color.White };
        btnRun.Location = new Point(140, 120);
        btnRun.Size = new Size(130, 38);
        btnRun.Click += (_, _) => Run();
        drop.Controls.Add(btnRun);

        drop.AllowDrop = true;
        drop.DragEnter += (_, e) =>
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        };
        drop.DragDrop += (_, e) =>
        {
            var files = (string[]?)e.Data?.GetData(DataFormats.FileDrop);
            if (files is { Length: > 0 })
            {
                _selected = files[0];
                _lblFile.Text = _selected;
            }
        };

        card.Controls.Add(_progress);
        _progress.Visible = false;
    }

    private void Browse()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Video/Image|*.mp4;*.avi;*.mkv;*.mov;*.jpg;*.jpeg;*.png",
            Multiselect = false
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        _selected = ofd.FileName;
        _lblFile.Text = _selected;
    }

    private void Run()
    {
        if (string.IsNullOrWhiteSpace(_selected) || !File.Exists(_selected))
        {
            MessageBox.Show(this, "Please select a file.", "Offline", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Offline uses a pseudo camera
        var camId = "offline";
        var camName = "Offline Import";

        _progress.Visible = true;
        _progress.Value = 0;

        var ext = Path.GetExtension(_selected).ToLowerInvariant();
        try
        {
            if (ext is ".jpg" or ".jpeg" or ".png")
            {
                _app.Offline.AnalyzeImage(_selected, camId, camName);
                _progress.Value = 100;
            }
            else
            {
                _app.Offline.AnalyzeVideo(_selected, camId, camName, 10, (i, total) =>
                {
                    this.SafeInvoke(() =>
                    {
                        var pct = Math.Min(100, (int)(i * 100.0 / Math.Max(1, total)));
                        _progress.Value = pct;
                    });
                });
            }

            MessageBox.Show(this, "Offline analysis complete.\nCheck History & Evidence.", "Offline", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _app.Logs.Error("Offline run failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Offline", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _progress.Visible = false;
        }
    }
}
