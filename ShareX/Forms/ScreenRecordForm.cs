﻿#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2015 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using ShareX.HelpersLib;
using ShareX.Properties;
using ShareX.ScreenCaptureLib;
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ShareX
{
    public class ScreenRecordForm : TrayForm
    {
        private static ScreenRecordForm instance;

        public static ScreenRecordForm Instance
        {
            get
            {
                if (instance == null || instance.IsDisposed)
                {
                    instance = new ScreenRecordForm();
                    instance.Show();
                }

                return instance;
            }
        }

        public bool IsRecording { get; private set; }

        private ScreenRecorder screenRecorder;
        private ScreenRegionForm regionForm;
        private ContextMenuStrip cmsMain;
        private System.ComponentModel.IContainer components;
        private ToolStripMenuItem tsmiStart;
        private ToolStripMenuItem tsmiAbort;
        private bool abortRequested;

        private ScreenRecordForm()
        {
            InitializeComponent();
        }

        private void tsmiStart_Click(object sender, EventArgs e)
        {
            StartStopRecording();
        }

        private void tsmiAbort_Click(object sender, EventArgs e)
        {
            AbortRecording();
        }

        public void StartStopRecording()
        {
            if (regionForm != null && !regionForm.IsDisposed)
            {
                regionForm.StartStop();
            }
        }

        public void AbortRecording()
        {
            if (regionForm != null && !regionForm.IsDisposed)
            {
                regionForm.Abort();
            }
        }

        private void StopRecording()
        {
            if (IsRecording && screenRecorder != null)
            {
                screenRecorder.StopRecording();
            }
        }

        private void TrayIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                StartStopRecording();
            }
        }

        public void StartRecording(ScreenRecordOutput outputType, TaskSettings taskSettings, ScreenRecordStartMethod startMethod = ScreenRecordStartMethod.Region)
        {
            string debugText;

            if (outputType == ScreenRecordOutput.FFmpeg)
            {
                debugText = string.Format("Starting FFmpeg recording. Video encoder: \"{0}\", Audio encoder: \"{1}\", FPS: {2}",
                    taskSettings.CaptureSettings.FFmpegOptions.VideoCodec.GetDescription(), taskSettings.CaptureSettings.FFmpegOptions.AudioCodec.GetDescription(),
                    taskSettings.CaptureSettings.ScreenRecordFPS);
            }
            else
            {
                debugText = string.Format("Starting Animated GIF recording. GIF encoding: \"{0}\", FPS: {1}",
                    taskSettings.CaptureSettings.GIFEncoding.GetDescription(), taskSettings.CaptureSettings.GIFFPS);
            }

            DebugHelper.WriteLine(debugText);

            if (taskSettings.CaptureSettings.RunScreencastCLI)
            {
                if (!Program.Settings.VideoEncoders.IsValidIndex(taskSettings.CaptureSettings.VideoEncoderSelected))
                {
                    MessageBox.Show(Resources.ScreenRecordForm_StartRecording_There_is_no_valid_CLI_video_encoder_selected_,
                        "ShareX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!Program.Settings.VideoEncoders[taskSettings.CaptureSettings.VideoEncoderSelected].IsValid())
                {
                    MessageBox.Show(Resources.ScreenRecordForm_StartRecording_CLI_video_encoder_file_does_not_exist__ +
                        Program.Settings.VideoEncoders[taskSettings.CaptureSettings.VideoEncoderSelected].Path,
                        "ShareX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            if (outputType == ScreenRecordOutput.GIF && taskSettings.CaptureSettings.GIFEncoding == ScreenRecordGIFEncoding.FFmpeg)
            {
                outputType = ScreenRecordOutput.FFmpeg;
                taskSettings.CaptureSettings.FFmpegOptions.VideoCodec = FFmpegVideoCodec.gif;
                taskSettings.CaptureSettings.FFmpegOptions.UseCustomCommands = false;
            }

            if (outputType == ScreenRecordOutput.FFmpeg)
            {
                if (!TaskHelpers.CheckFFmpeg(taskSettings))
                {
                    return;
                }

                if (!taskSettings.CaptureSettings.FFmpegOptions.IsSourceSelected)
                {
                    MessageBox.Show(Resources.ScreenRecordForm_StartRecording_FFmpeg_video_and_audio_source_both_can_t_be__None__,
                        "ShareX - " + Resources.ScreenRecordForm_StartRecording_FFmpeg_error, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            Rectangle captureRectangle = Rectangle.Empty;

            switch (startMethod)
            {
                case ScreenRecordStartMethod.Region:
                    TaskHelpers.SelectRegion(out captureRectangle, taskSettings);
                    break;
                case ScreenRecordStartMethod.ActiveWindow:
                    if (taskSettings.CaptureSettings.CaptureClientArea)
                    {
                        captureRectangle = CaptureHelpers.GetActiveWindowClientRectangle();
                    }
                    else
                    {
                        captureRectangle = CaptureHelpers.GetActiveWindowRectangle();
                    }
                    break;
                case ScreenRecordStartMethod.LastRegion:
                    captureRectangle = Program.Settings.ScreenRecordRegion;
                    break;
            }

            captureRectangle = CaptureHelpers.EvenRectangleSize(captureRectangle);

            if (IsRecording || !captureRectangle.IsValid() || screenRecorder != null)
            {
                return;
            }

            Program.Settings.ScreenRecordRegion = captureRectangle;

            IsRecording = true;

            Screenshot.CaptureCursor = taskSettings.CaptureSettings.ScreenRecordShowCursor;

            string trayText = "ShareX - " + Resources.ScreenRecordForm_StartRecording_Waiting___;
            TrayIcon.Text = trayText.Truncate(63);
            TrayIcon.Icon = Resources.control_record_yellow.ToIcon();
            cmsMain.Enabled = false;
            TrayIcon.Visible = true;

            string path = "";

            float duration = taskSettings.CaptureSettings.ScreenRecordFixedDuration ? taskSettings.CaptureSettings.ScreenRecordDuration : 0;

            regionForm = ScreenRegionForm.Show(captureRectangle, StopRecording, startMethod == ScreenRecordStartMethod.Region, duration);
            regionForm.RecordResetEvent = new ManualResetEvent(false);

            TaskEx.Run(() =>
            {
                try
                {
                    if (outputType == ScreenRecordOutput.FFmpeg)
                    {
                        path = Path.Combine(taskSettings.CaptureFolder, TaskHelpers.GetFilename(taskSettings, taskSettings.CaptureSettings.FFmpegOptions.Extension));
                    }
                    else
                    {
                        path = Program.ScreenRecorderCacheFilePath;
                    }

                    ScreencastOptions options = new ScreencastOptions()
                    {
                        FFmpeg = taskSettings.CaptureSettings.FFmpegOptions,
                        ScreenRecordFPS = taskSettings.CaptureSettings.ScreenRecordFPS,
                        GIFFPS = taskSettings.CaptureSettings.GIFFPS,
                        Duration = duration,
                        OutputPath = path,
                        CaptureArea = captureRectangle,
                        DrawCursor = taskSettings.CaptureSettings.ScreenRecordShowCursor
                    };

                    screenRecorder = new ScreenRecorder(outputType, options, captureRectangle);

                    if (regionForm != null && regionForm.RecordResetEvent != null)
                    {
                        trayText = "ShareX - " + Resources.ScreenRecordForm_StartRecording_Click_tray_icon_to_start_recording_;
                        TrayIcon.Text = trayText.Truncate(63);

                        this.InvokeSafe(() =>
                        {
                            tsmiStart.Text = Resources.AutoCaptureForm_Execute_Start;
                            cmsMain.Enabled = true;
                        });

                        if (taskSettings.CaptureSettings.ScreenRecordAutoStart)
                        {
                            int delay = (int)(taskSettings.CaptureSettings.ScreenRecordStartDelay * 1000);

                            if (delay > 0)
                            {
                                regionForm.InvokeSafe(() => regionForm.StartCountdown(delay));

                                regionForm.RecordResetEvent.WaitOne(delay);
                            }
                        }
                        else
                        {
                            regionForm.RecordResetEvent.WaitOne();
                        }

                        if (regionForm.AbortRequested)
                        {
                            abortRequested = true;
                        }
                    }

                    if (!abortRequested)
                    {
                        trayText = "ShareX - " + Resources.ScreenRecordForm_StartRecording_Click_tray_icon_to_stop_recording_;
                        TrayIcon.Text = trayText.Truncate(63);
                        TrayIcon.Icon = Resources.control_record.ToIcon();

                        this.InvokeSafe(() =>
                        {
                            tsmiStart.Text = Resources.AutoCaptureForm_Execute_Stop;
                        });

                        if (regionForm != null)
                        {
                            regionForm.InvokeSafe(() => regionForm.StartRecordingTimer(duration > 0, duration));
                        }

                        screenRecorder.StartRecording();

                        if (regionForm != null && regionForm.AbortRequested)
                        {
                            abortRequested = true;
                        }
                    }
                }
                catch (Exception e)
                {
                    DebugHelper.WriteException(e);
                }
                finally
                {
                    if (regionForm != null)
                    {
                        if (regionForm.RecordResetEvent != null)
                        {
                            regionForm.RecordResetEvent.Dispose();
                        }

                        regionForm.InvokeSafe(() => regionForm.Close());
                        regionForm = null;
                    }
                }

                try
                {
                    if (!abortRequested && screenRecorder != null)
                    {
                        TrayIcon.Text = "ShareX - " + Resources.ScreenRecordForm_StartRecording_Encoding___;
                        TrayIcon.Icon = Resources.camcorder_pencil.ToIcon();

                        this.InvokeSafe(() =>
                        {
                            cmsMain.Enabled = false;
                        });

                        if (outputType == ScreenRecordOutput.GIF)
                        {
                            path = Path.Combine(taskSettings.CaptureFolder, TaskHelpers.GetFilename(taskSettings, "gif"));
                            screenRecorder.EncodingProgressChanged += progress => TrayIcon.Text = string.Format("ShareX - {0} ({1}%)", Resources.ScreenRecordForm_StartRecording_Encoding___, progress);
                            GIFQuality gifQuality = taskSettings.CaptureSettings.GIFEncoding == ScreenRecordGIFEncoding.OctreeQuantizer ? GIFQuality.Bit8 : GIFQuality.Default;
                            screenRecorder.SaveAsGIF(path, gifQuality);
                        }
                        else if (outputType == ScreenRecordOutput.FFmpeg && taskSettings.CaptureSettings.FFmpegOptions.VideoCodec == FFmpegVideoCodec.gif)
                        {
                            path = Path.Combine(taskSettings.CaptureFolder, TaskHelpers.GetFilename(taskSettings, "gif"));
                            screenRecorder.FFmpegEncodeAsGIF(path);
                        }

                        if (taskSettings.CaptureSettings.RunScreencastCLI)
                        {
                            VideoEncoder encoder = Program.Settings.VideoEncoders[taskSettings.CaptureSettings.VideoEncoderSelected];
                            string sourceFilePath = path;
                            path = Path.Combine(taskSettings.CaptureFolder, TaskHelpers.GetFilename(taskSettings, encoder.OutputExtension));
                            screenRecorder.EncodeUsingCommandLine(encoder, sourceFilePath, path);
                        }
                    }
                }
                finally
                {
                    if (screenRecorder != null)
                    {
                        if ((outputType == ScreenRecordOutput.GIF || taskSettings.CaptureSettings.RunScreencastCLI ||
                            (outputType == ScreenRecordOutput.FFmpeg && taskSettings.CaptureSettings.FFmpegOptions.VideoCodec == FFmpegVideoCodec.gif)) &&
                            !string.IsNullOrEmpty(screenRecorder.CachePath) && File.Exists(screenRecorder.CachePath))
                        {
                            File.Delete(screenRecorder.CachePath);
                        }

                        screenRecorder.Dispose();
                        screenRecorder = null;

                        if (abortRequested && !string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                }
            },
            () =>
            {
                if (TrayIcon.Visible)
                {
                    TrayIcon.Visible = false;
                }

                if (!abortRequested && !string.IsNullOrEmpty(path) && File.Exists(path) && TaskHelpers.ShowAfterCaptureForm(taskSettings))
                {
                    UploadTask task = UploadTask.CreateFileJobTask(path, taskSettings);
                    TaskManager.Start(task);
                }

                abortRequested = false;
                IsRecording = false;
            });
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.cmsMain = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.tsmiStart = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiAbort = new System.Windows.Forms.ToolStripMenuItem();
            this.cmsMain.SuspendLayout();
            this.SuspendLayout();
            //
            // TrayIcon
            //
            this.TrayIcon.ContextMenuStrip = this.cmsMain;
            this.TrayIcon.Text = "ShareX";
            this.TrayIcon.MouseClick += new System.Windows.Forms.MouseEventHandler(this.TrayIcon_MouseClick);
            //
            // cmsMain
            //
            this.cmsMain.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsmiStart,
            this.tsmiAbort});
            this.cmsMain.Name = "cmsMain";
            this.cmsMain.Size = new System.Drawing.Size(153, 70);
            //
            // tsmiStart
            //
            this.tsmiStart.Image = global::ShareX.Properties.Resources.control_record;
            this.tsmiStart.Name = "tsmiStart";
            this.tsmiStart.Size = new System.Drawing.Size(152, 22);
            this.tsmiStart.Text = "Start";
            this.tsmiStart.Click += new System.EventHandler(this.tsmiStart_Click);
            //
            // tsmiAbort
            //
            this.tsmiAbort.Image = global::ShareX.Properties.Resources.cross;
            this.tsmiAbort.Name = "tsmiAbort";
            this.tsmiAbort.Size = new System.Drawing.Size(152, 22);
            this.tsmiAbort.Text = "Abort";
            this.tsmiAbort.Click += new System.EventHandler(this.tsmiAbort_Click);
            //
            // ScreenRecordForm
            //
            this.ClientSize = new System.Drawing.Size(284, 261);
            this.Name = "ScreenRecordForm";
            this.cmsMain.ResumeLayout(false);
            this.ResumeLayout(false);
        }
    }
}