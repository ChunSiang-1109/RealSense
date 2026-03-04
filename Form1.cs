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
                        if (!isCaptured)
                        {
                            var colorFrame = alignedFrames.ColorFrame;
                            var depthFrame = alignedFrames.DepthFrame;

                            Bitmap bitmap = FrameToBitmap(colorFrame);
                            pictureBox1.Invoke(new Action(() =>
                            {
                                pictureBox1.Image?.Dispose();
                                pictureBox1.Image = bitmap;
                                lblStatus.Text = "Live - Press Capture to measure";
                            }));

                            lastDepthFrame?.Dispose();
                            lastDepthFrame = depthFrame.Clone().As<DepthFrame>();
                        }
                    }
                }
                catch { /* Handle errors */ }
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
            if (!isCaptured || lastDepthFrame == null) return;

            if (firstPoint == null)
            {
                // Set Point A
                firstPoint = e.Location;
                lblStatus.Text = "Point A set. Click Point B.";
                pictureBox1.Invalidate(); // Trigger redraw to show point A
            }
            else
            {
                // Set Point B
                secondPoint = e.Location;

                // 1. Math
                float scaleX = (float)intrinsics.width / pictureBox1.Width;
                float scaleY = (float)intrinsics.height / pictureBox1.Height;

                var p1 = Deproject(lastDepthFrame, intrinsics, firstPoint.Value.X * scaleX, firstPoint.Value.Y * scaleY);
                var p2 = Deproject(lastDepthFrame, intrinsics, secondPoint.Value.X * scaleX, secondPoint.Value.Y * scaleY);

                if (p1.Z == 0 || p2.Z == 0)
                {
                    lblStatus.Text = "Error: Invalid depth. Try clicking elsewhere.";
                    firstPoint = null; // Reset
                    secondPoint = null;
                    return;
                }

                float distance = System.Numerics.Vector3.Distance(p1, p2) * 1000;
                float height = Math.Abs(p1.Y - p2.Y) * 1000;

                // 2. Display
                lblStatus.Text = $"RESULT: Height: {height:F1}mm | Dist: {distance:F1}mm";

                // 3. Trigger Redraw to show point B
                pictureBox1.Invalidate();

                // Reset state so user can capture again (removed 'isCaptured = false' so points stay)
                // If you want points to vanish immediately, keep 'isCaptured = false'.
            }
        }

        // --- NEW METHOD: ADD THIS EVENT HANDLER ---
        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            // Only draw when we are in capture mode
            if (!isCaptured) return;

            // Use SmoothingMode for cleaner, anti-aliased circles and lines
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using (Brush redBrush = new SolidBrush(Color.Red))
            using (Pen dashedPen = new Pen(Color.Red, 2))
            {
                // Set the pen style to dashed
                dashedPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;

                int pointSize = 10;
                int offset = pointSize / 2;

                // 1. Draw Point A
                if (firstPoint != null)
                {
                    e.Graphics.FillEllipse(redBrush, firstPoint.Value.X - offset, firstPoint.Value.Y - offset, pointSize, pointSize);
                    e.Graphics.DrawString("A", this.Font, Brushes.White, firstPoint.Value.X + 5, firstPoint.Value.Y - 15);
                }

                // 2. Draw Point B and the Dashed Line
                if (secondPoint != null)
                {
                    e.Graphics.FillEllipse(redBrush, secondPoint.Value.X - offset, secondPoint.Value.Y - offset, pointSize, pointSize);
                    e.Graphics.DrawString("B", this.Font, Brushes.White, secondPoint.Value.X + 5, secondPoint.Value.Y - 15);

                    // Draw the line connecting A and B
                    if (firstPoint != null)
                    {
                        e.Graphics.DrawLine(dashedPen, firstPoint.Value, secondPoint.Value);
                    }
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