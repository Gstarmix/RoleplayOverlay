using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RoleplayOverlay.Cam;
namespace RoleplayOverlay
{
  internal sealed class TakeManagerForm : Form
  {
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] Exts = { ".mp4", ".mov", ".mkv", ".webm" };
    private readonly string _dir;
    private readonly string _ffmpegExe;
    public string? SelectedPath { get; private set; }
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(24, 24, 30), ForeColor = Color.Gainsboro, IntegralHeight = false };
    private readonly List<string> _files = new();
    private readonly TlPanel _preview = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    private readonly Label _info = new() { Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.Gray };
    private Bitmap? _thumb;
    private VideoFileReader? _player;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 40 };
    private Button _playBtn = null!;
    private string? _sel;
    public TakeManagerForm(string dir, string ffmpegDir)
    {
      _dir = dir;
      _ffmpegExe = File.Exists(Path.Combine(ffmpegDir, "ffmpeg.exe"))
        ? Path.Combine(ffmpegDir, "ffmpeg.exe")
        : (File.Exists(@"C:\ffmpeg\bin\ffmpeg.exe") ? @"C:\ffmpeg\bin\ffmpeg.exe" : "ffmpeg");
      Text = "Gestionnaire de prises — " + dir;
      StartPosition = FormStartPosition.CenterParent;
      Size = new Size(880, 620);
      MinimumSize = new Size(640, 460);
      BackColor = Color.FromArgb(18, 18, 22);
      ForeColor = Color.Gainsboro;
      Font = new Font("Segoe UI", 9f);
      BuildUi();
      _timer.Tick += (_, _) => { if (_player != null) _preview.Invalidate(); };
      LoadTakes();
    }
    private void BuildUi()
    {
      var top = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Color.FromArgb(28, 28, 34) };
      int x = 8;
      AddBtn(top, ref x, "Actualiser", (_, _) => LoadTakes());
      AddBtn(top, ref x, "Ouvrir le dossier", (_, _) => { try { Process.Start(new ProcessStartInfo("explorer.exe", _dir) { UseShellExecute = true }); } catch { } });
      var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44, BackColor = Color.FromArgb(28, 28, 34) };
      int bx = 8;
      _playBtn = MakeBtn("▶ Lire", (_, _) => TogglePlay()); _playBtn.SetBounds(bx, 8, 84, 28); bottom.Controls.Add(_playBtn); bx += 90;
      var del = MakeBtn("🗑 Supprimer", (_, _) => DeleteSelected()); del.SetBounds(bx, 8, 100, 28); bottom.Controls.Add(del); bx += 106;
      var assign = MakeBtn("✔ Assigner à la ligne", (_, _) => Assign()); assign.SetBounds(bx, 8, 160, 28); assign.BackColor = Color.FromArgb(45, 110, 60); bottom.Controls.Add(assign); bx += 166;
      var close = MakeBtn("Fermer", (_, _) => Close()); close.Dock = DockStyle.Right; close.Width = 90; bottom.Controls.Add(close);
      var left = new Panel { Dock = DockStyle.Left, Width = 300, BackColor = Color.FromArgb(24, 24, 30), Padding = new Padding(6) };
      _list.SelectedIndexChanged += (_, _) => OnSelect();
      _list.DoubleClick += (_, _) => Assign();
      left.Controls.Add(_list);
      _preview.Paint += PaintPreview;
      Controls.Add(_preview);
      Controls.Add(_info);
      Controls.Add(left);
      Controls.Add(bottom);
      Controls.Add(top);
    }
    private Button MakeBtn(string text, EventHandler onClick)
    {
      var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(55, 55, 62), ForeColor = Color.White };
      b.FlatAppearance.BorderSize = 0; b.Click += onClick; return b;
    }
    private void AddBtn(Panel p, ref int x, string text, EventHandler onClick)
    {
      var b = MakeBtn(text, onClick); b.SetBounds(x, 6, Math.Max(90, text.Length * 8 + 20), 26); p.Controls.Add(b); x += b.Width + 6;
    }
    private void LoadTakes()
    {
      StopPlay();
      _files.Clear(); _list.Items.Clear();
      try
      {
        if (Directory.Exists(_dir))
        {
          foreach (var f in Directory.GetFiles(_dir)
                     .Where(f => Exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                     .OrderByDescending(File.GetLastWriteTime))
          {
            _files.Add(f);
            _list.Items.Add(Path.GetFileName(f));
          }
        }
      }
      catch { }
      _info.Text = _files.Count == 0 ? $"Aucune prise dans {_dir}" : $"{_files.Count} prise(s)";
      if (_files.Count > 0) _list.SelectedIndex = 0; else { _sel = null; _thumb?.Dispose(); _thumb = null; _preview.Invalidate(); }
    }
    private void OnSelect()
    {
      StopPlay();
      int i = _list.SelectedIndex;
      if (i < 0 || i >= _files.Count) { _sel = null; return; }
      _sel = _files[i];
      _thumb?.Dispose();
      _thumb = ExtractFrame(_sel, 0.3);
      double d = ProbeDuration(_sel);
      long sz = 0; try { sz = new FileInfo(_sel).Length; } catch { }
      _info.Text = $"{Path.GetFileName(_sel)}  ·  {d:0.0}s  ·  {sz / 1024 / 1024.0:0.0} Mo";
      _preview.Invalidate();
    }
    private void PaintPreview(object? s, PaintEventArgs e)
    {
      var g = e.Graphics;
      var cr = _preview.ClientRectangle;
      if (_player != null) { _player.DrawCover(g, cr); return; }
      if (_thumb != null)
      {
        float ar = _thumb.Width / (float)_thumb.Height;
        int w = cr.Width, h = (int)(w / ar);
        if (h > cr.Height) { h = cr.Height; w = (int)(h * ar); }
        g.DrawImage(_thumb, cr.X + (cr.Width - w) / 2, cr.Y + (cr.Height - h) / 2, w, h);
      }
      else
      {
        using var b = new SolidBrush(Color.Gray);
        g.DrawString(_files.Count == 0 ? "Aucune prise" : "Sélectionne une prise", Font, b, 12, 12);
      }
    }
    private void TogglePlay()
    {
      if (_player != null) { StopPlay(); return; }
      if (_sel == null) return;
      var pl = new VideoFileReader();
      if (!pl.Start(_sel)) { pl.Stop(); return; }
      _player = pl; _playBtn.Text = "⏸ Pause"; _timer.Start();
    }
    private void StopPlay()
    {
      _timer.Stop();
      if (_player != null) { _player.Stop(); _player = null; }
      _playBtn.Text = "▶ Lire";
      _preview.Invalidate();
    }
    private void DeleteSelected()
    {
      if (_sel == null) return;
      if (MessageBox.Show(this, $"Supprimer définitivement :\n{Path.GetFileName(_sel)} ?", "Supprimer la prise",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
      StopPlay();
      try { File.Delete(_sel); } catch (Exception ex) { MessageBox.Show(this, "Suppression impossible : " + ex.Message); return; }
      LoadTakes();
    }
    private void Assign()
    {
      if (_sel == null || !File.Exists(_sel)) return;
      StopPlay();
      SelectedPath = _sel;
      DialogResult = DialogResult.OK;
      Close();
    }
    private Bitmap? ExtractFrame(string path, double t)
    {
      try
      {
        var dir = Path.Combine(Path.GetTempPath(), "ro_takes");
        Directory.CreateDirectory(dir);
        var outp = Path.Combine(dir, "t_" + Guid.NewGuid().ToString("N") + ".png");
        var psi = new ProcessStartInfo { FileName = _ffmpegExe, Arguments = $"-y -ss {t.ToString("F2", Inv)} -i \"{path}\" -frames:v 1 -vf scale=480:-2 \"{outp}\"", UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        using var p = Process.Start(psi); if (p == null) return null;
        p.StandardError.ReadToEnd(); p.WaitForExit(12000);
        if (File.Exists(outp)) { using var s = File.OpenRead(outp); using var tmp = new Bitmap(s); var b = new Bitmap(tmp); try { File.Delete(outp); } catch { } return b; }
      }
      catch { }
      return null;
    }
    private double ProbeDuration(string path)
    {
      try
      {
        var probe = _ffmpegExe.Replace("ffmpeg.exe", "ffprobe.exe");
        var psi = new ProcessStartInfo { FileName = probe, Arguments = $"-v error -show_entries format=duration -of csv=p=0 \"{path}\"", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi); if (p == null) return 0;
        var o = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(8000);
        return double.TryParse(o, NumberStyles.Float, Inv, out var d) && d > 0 ? d : 0;
      }
      catch { return 0; }
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
      _timer.Stop();
      if (_player != null) { _player.Stop(); _player = null; }
      _thumb?.Dispose();
      base.OnFormClosed(e);
    }
  }
}