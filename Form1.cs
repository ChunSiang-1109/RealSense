using Intel.RealSense;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RealSense1
{
    public partial class Form1 : Form
    {
        private Pipeline pipeline;
        private CancellationTokenSource tokenSource = new CancellationTokenSource();
        private Align align;
        private Intrinsics intrinsics;
        private DepthFrame lastDepthFrame = null;
        private bool isCaptured = false;

        // Stores the user clicks for drawing and math
        private Point? firstPoint = null;
        private Point? secondPoint = null; // Store second point now

        public Form1()
        {
            InitializeComponent();
            pictureBox1.Paint += pictureBox1_Paint;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                SetupRealSense();
                Task.Run(() => MainLoop(tokenSource.Token));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not initialize camera: " + ex.Message);
            }
        }

        private void SetupRealSense()
        {
            pipeline = new Pipeline();
            align = new Align(Intel.RealSense.Stream.Color);

            var config = new Config();
            config.EnableStream(Intel.RealSense.Stream.Depth, 640, 480, Intel.RealSense.Format.Z16, 30);
            config.EnableStream(Intel.RealSense.Stream.Color, 640, 480, Intel.RealSense.Format.Rgb8, 30);

            var profile = pipeline.Start(config);
            intrinsics = profile.GetStream(Intel.RealSense.Stream.Color).As<VideoStreamProfile>().GetIntrinsics();
        }

        private void MainLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (var frames = pipeline.WaitForFrames(5000))
                    using (var alignedFrames = align.Process(frames).As<FrameSet>())
                    {
                        var colorFrame = alignedFrames.ColorFrame;
                        var depthFrame = alignedFrames.DepthFrame;

                        if (colorFrame == null || depthFrame == null) continue;

                        // Determine which pixel to measure: 
                        // If the user hasn't clicked (firstPoint is null), use the center.
                        float targetX, targetY;
                        bool isLiveMode = (firstPoint == null);

                        if (isLiveMode)
                        {
                            targetX = intrinsics.width / 2f;
                            targetY = intrinsics.height / 2f;
                        }
                        else
                        {
                            // Convert the stored UI click back to camera coordinates
                            float scaleX = (float)intrinsics.width / pictureBox1.Width;
                            float scaleY = (float)intrinsics.height / pictureBox1.Height;
                            targetX = firstPoint.Value.X * scaleX;
                            targetY = firstPoint.Value.Y * scaleY;
                        }

                        var point3D = Deproject(depthFrame, intrinsics, targetX, targetY);
                        float distancemm = point3D.Z * 1000;

                        Bitmap bitmap = FrameToBitmap(colorFrame);
                        pictureBox1.Invoke(new Action(() =>
                        {
                            pictureBox1.Image?.Dispose();
                            pictureBox1.Image = bitmap;

                            if (isLiveMode)
                                lblStatus.Text = $"LIVE CENTER: {distancemm:F1}mm (Click to Lock Point A)";
                            else
                                lblStatus.Text = $"POINT A LOCKED: {distancemm:F1}mm (Click again to Reset)";
                        }));

                        lastDepthFrame?.Dispose();
                        lastDepthFrame = depthFrame.Clone().As<DepthFrame>();
                    }
                }
                catch { }
            }
        }

        private System.Numerics.Vector3 Deproject(DepthFrame frame, Intrinsics intrin, float x, float y)
        {
            float depth = frame.GetDistance((int)x, (int)y);
            var point = new System.Numerics.Vector3();
            point.X = (x - intrin.ppx) / intrin.fx * depth;
            point.Y = (y - intrin.ppy) / intrin.fy * depth;
            point.Z = depth;
            return point;
        }

        // --- UPDATED METHOD ---
        private void pictureBox1_MouseClick(object sender, MouseEventArgs e)
        {
            if (firstPoint == null)
            {
                // Lock the point you just clicked
                firstPoint = e.Location;
            }
            else
            {
                // Revert back to live mode
                firstPoint = null;
            }
            pictureBox1.Invalidate();
        }

        // --- NEW METHOD: ADD THIS EVENT HANDLER ---
        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (firstPoint == null)
            {
                // DRAW LIVE CROSSHAIR (Center)
                int cx = pictureBox1.Width / 2;
                int cy = pictureBox1.Height / 2;
                using (Pen p = new Pen(Color.LimeGreen, 2))
                {
                    e.Graphics.DrawLine(p, cx - 15, cy, cx + 15, cy);
                    e.Graphics.DrawLine(p, cx, cy - 15, cx, cy + 15);
                }
            }
            else
            {
                // DRAW LOCKED POINT A
                using (Brush redBrush = new SolidBrush(Color.Red))
                {
                    int size = 12;
                    e.Graphics.FillEllipse(redBrush, firstPoint.Value.X - (size / 2), firstPoint.Value.Y - (size / 2), size, size);
                    e.Graphics.DrawString("POINT A", this.Font, Brushes.Yellow, firstPoint.Value.X + 10, firstPoint.Value.Y - 10);
                }
            }
        }

        private Bitmap FrameToBitmap(VideoFrame frame)
        {
            Bitmap bmp = new Bitmap(frame.Width, frame.Height, PixelFormat.Format24bppRgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            int bytes = frame.Stride * frame.Height;
            byte[] managedArray = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(frame.Data, managedArray, 0, bytes);
            System.Runtime.InteropServices.Marshal.Copy(managedArray, 0, data.Scan0, bytes);
            bmp.UnlockBits(data);
            return bmp;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            tokenSource.Cancel();
            try { pipeline?.Stop(); } catch { }
            lastDepthFrame?.Dispose();
            align?.Dispose();
            pipeline?.Dispose();
            base.OnFormClosing(e);
        }

        private void btnCapture_Click(object sender, EventArgs e)
        {
            if (lastDepthFrame == null) return;
            isCaptured = true;
            firstPoint = null; // Clear old measurements
            secondPoint = null;
            lblStatus.Text = "Image Captured. Click Point A.";
            pictureBox1.Invalidate(); // Refresh the PictureBox (removes old points)
        }
    }
}