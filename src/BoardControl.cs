// 棋盘绘制与鼠标交互
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ChinaChess
{
    public delegate void SquareHandler(int sq);

    public static class Fonts
    {
        public static FontFamily Pick(string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                try { return new FontFamily(names[i]); }
                catch { }
            }
            return FontFamily.GenericSansSerif;
        }

        public static readonly FontFamily Piece = Pick(new string[] { "KaiTi", "SimHei", "Microsoft YaHei", "SimSun" });
        public static readonly FontFamily Ui = Pick(new string[] { "Microsoft YaHei", "SimHei", "SimSun" });
    }

    public class GameBoard : Control
    {
        public Board B;
        public int Selected = -1;
        public List<int> Targets = new List<int>();
        public int LastFrom = -1;
        public int LastTo = -1;
        public int CheckSq = -1;
        public bool Flipped;          // 玩家执黑时翻转棋盘
        public string Banner;         // 结束时盖在棋盘上的提示

        public event SquareHandler SquareClicked;

        // 版面
        float cw = 60, ch = 64, ox = 60, oy = 60;

        // 动画
        int animFrom = -1, animTo = -1;
        float animT = 1f;
        Timer animTimer;
        EventHandler animDone;

        static readonly Color LineColor = Color.FromArgb(118, 78, 38);
        static readonly Color RedInk = Color.FromArgb(176, 32, 32);
        static readonly Color BlackInk = Color.FromArgb(42, 42, 48);
        static readonly FontFamily PieceFont = Fonts.Piece;
        static readonly FontFamily UiFont = Fonts.Ui;

        public GameBoard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(236, 231, 221);
            TabStop = true;
        }

        public void Attach(Board b)
        {
            B = b;
            Selected = -1;
            Targets.Clear();
            LastFrom = LastTo = CheckSq = -1;
            Banner = null;
            Invalidate();
        }

        // ---------- 坐标换算 ----------

        void ComputeLayout()
        {
            float W = ClientSize.Width, H = ClientSize.Height;
            const float R = 1.06f;      // 格高 / 格宽
            const float mh = 0.85f;     // 左右边距（以格宽为单位）
            const float mt = 0.75f;     // 上边距
            const float mb = 1.10f;     // 下边距（留出纵线编号的位置）
            float c = Math.Min(W / (8f + 2f * mh), H / (9f * R + mt + mb));
            if (c < 6f) c = 6f;
            cw = c;
            ch = c * R;
            float totalW = 8f * cw + 2f * mh * c;
            float totalH = 9f * ch + (mt + mb) * c;
            ox = (W - totalW) / 2f + mh * c;
            oy = (H - totalH) / 2f + mt * c;
        }

        float GX(float dx) { return ox + dx * cw; }
        float GY(float dy) { return oy + dy * ch; }

        public PointF SquareCenter(int sq)
        {
            int x = sq % 9, y = sq / 9;
            if (Flipped) { x = 8 - x; y = 9 - y; }
            return new PointF(GX(x), GY(y));
        }

        public int PointToSquare(int px, int py)
        {
            if (B == null) return -1;
            int best = -1;
            float bestD = float.MaxValue;
            for (int sq = 0; sq < 90; sq++)
            {
                PointF c = SquareCenter(sq);
                float dx = px - c.X, dy = py - c.Y;
                float d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = sq; }
            }
            float lim = cw * 0.62f;
            return (bestD <= lim * lim) ? best : -1;
        }

        // ---------- 动画 ----------

        public void AnimateMove(int from, int to, EventHandler done)
        {
            animFrom = from;
            animTo = to;
            animT = 0f;
            animDone = done;
            if (animTimer == null)
            {
                animTimer = new Timer();
                animTimer.Interval = 16;
                animTimer.Tick += AnimTick;
            }
            if (!animTimer.Enabled) animTimer.Start();
        }

        public void StopAnimation()
        {
            if (animTimer != null) animTimer.Stop();
            animFrom = animTo = -1;
            animT = 1f;
            animDone = null;
        }

        void AnimTick(object sender, EventArgs e)
        {
            animT += 0.085f;
            if (animT >= 1f)
            {
                animT = 1f;
                animTimer.Stop();
                int f = animFrom, t = animTo;
                animFrom = animTo = -1;
                EventHandler h = animDone;
                animDone = null;
                Invalidate();
                if (h != null && !IsDisposed) h(this, EventArgs.Empty);
                return;
            }
            Invalidate();
        }

        // ---------- 输入 ----------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (B == null) return;
            int sq = PointToSquare(e.X, e.Y);
            if (sq >= 0 && SquareClicked != null) SquareClicked(sq);
        }

        // ---------- 绘制 ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            ComputeLayout();

            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            if (B == null) return;

            DrawWood(g);
            DrawLines(g);
            DrawRiver(g);
            DrawMarks(g);
            DrawHighlights(g);
            DrawPieces(g);
            DrawFileLabels(g);
            DrawBanner(g);
        }

        RectangleF WoodRect()
        {
            float mm = 0.44f * cw;
            return new RectangleF(ox - mm, oy - mm, 8f * cw + 2f * mm, 9f * ch + 2f * mm);
        }

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2f;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void DrawWood(Graphics g)
        {
            RectangleF r = WoodRect();
            float rad = 0.30f * cw;
            using (GraphicsPath sp = RoundRect(new RectangleF(r.X + 2, r.Y + 4, r.Width, r.Height), rad))
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(46, 60, 44, 24)))
                g.FillPath(sb, sp);

            using (GraphicsPath wp = RoundRect(r, rad))
            {
                using (LinearGradientBrush lg = new LinearGradientBrush(r,
                    Color.FromArgb(251, 234, 202), Color.FromArgb(230, 197, 145), 35f))
                    g.FillPath(lg, wp);
                // 木纹
                using (Pen gp = new Pen(Color.FromArgb(16, 120, 78, 36), 1.2f))
                {
                    for (int i = 1; i < 26; i++)
                    {
                        float yy = r.Y + r.Height * i / 26f;
                        g.DrawLine(gp, r.X + 6, yy, r.Right - 6, yy + 0.8f);
                    }
                }
                using (Pen bp = new Pen(Color.FromArgb(150, 104, 52), Math.Max(2f, cw * 0.055f)))
                    g.DrawPath(bp, wp);
            }
            using (GraphicsPath ip = RoundRect(new RectangleF(r.X + 0.10f * cw, r.Y + 0.10f * cw, r.Width - 0.20f * cw, r.Height - 0.20f * cw), rad * 0.8f))
            using (Pen bp2 = new Pen(Color.FromArgb(110, 132, 92, 48), Math.Max(1f, cw * 0.022f)))
                g.DrawPath(bp2, ip);
        }

        void DrawLines(Graphics g)
        {
            float w = Math.Max(1f, cw * 0.030f);
            using (Pen pen = new Pen(LineColor, w))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                for (int y = 0; y < 10; y++) g.DrawLine(pen, GX(0), GY(y), GX(8), GY(y));
                for (int x = 0; x < 9; x++)
                {
                    if (x == 0 || x == 8) g.DrawLine(pen, GX(x), GY(0), GX(x), GY(9));
                    else
                    {
                        g.DrawLine(pen, GX(x), GY(0), GX(x), GY(4));
                        g.DrawLine(pen, GX(x), GY(5), GX(x), GY(9));
                    }
                }
                // 九宫斜线
                g.DrawLine(pen, GX(3), GY(0), GX(5), GY(2));
                g.DrawLine(pen, GX(5), GY(0), GX(3), GY(2));
                g.DrawLine(pen, GX(3), GY(7), GX(5), GY(9));
                g.DrawLine(pen, GX(5), GY(7), GX(3), GY(9));
            }
        }

        void DrawRiver(Graphics g)
        {
            float fs = ch * 0.60f;
            using (Font f = new Font(PieceFont, fs, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(165, 118, 76, 34)))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString("楚  河", f, b, new RectangleF(GX(0), GY(4), 4 * cw, ch), sf);
                g.DrawString("漢  界", f, b, new RectangleF(GX(5), GY(4), 4 * cw, ch), sf);
            }
        }

        void DrawMarks(Graphics g)
        {
            int[] mx = { 1, 7, 1, 7, 0, 2, 4, 6, 8, 0, 2, 4, 6, 8 };
            int[] my = { 2, 2, 7, 7, 3, 3, 3, 3, 3, 6, 6, 6, 6, 6 };
            float gap = 0.085f * cw, len = 0.20f * cw;
            using (Pen pen = new Pen(LineColor, Math.Max(1f, cw * 0.024f)))
            {
                for (int i = 0; i < mx.Length; i++)
                {
                    float cx = GX(mx[i]), cy = GY(my[i]);
                    for (int sx = -1; sx <= 1; sx += 2)
                    {
                        if (mx[i] == 0 && sx < 0) continue;
                        if (mx[i] == 8 && sx > 0) continue;
                        for (int sy = -1; sy <= 1; sy += 2)
                        {
                            g.DrawLine(pen, cx + sx * gap, cy + sy * gap, cx + sx * (gap + len), cy + sy * gap);
                            g.DrawLine(pen, cx + sx * gap, cy + sy * gap, cx + sx * gap, cy + sy * (gap + len));
                        }
                    }
                }
            }
        }

        void DrawHighlights(Graphics g)
        {
            float r = Math.Min(cw, ch) * 0.45f;

            // 上一步
            if (LastFrom >= 0 && LastTo >= 0)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(58, 255, 196, 64)))
                {
                    PointF p1 = SquareCenter(LastFrom), p2 = SquareCenter(LastTo);
                    g.FillEllipse(b, p1.X - r, p1.Y - r, 2 * r, 2 * r);
                    g.FillEllipse(b, p2.X - r, p2.Y - r, 2 * r, 2 * r);
                }
                using (Pen pen = new Pen(Color.FromArgb(200, 236, 150, 30), Math.Max(1.6f, cw * 0.032f)))
                {
                    PointF p2 = SquareCenter(LastTo);
                    float q = r * 0.92f, L = r * 0.40f;
                    g.DrawLine(pen, p2.X - q, p2.Y - q, p2.X - q + L, p2.Y - q);
                    g.DrawLine(pen, p2.X - q, p2.Y - q, p2.X - q, p2.Y - q + L);
                    g.DrawLine(pen, p2.X + q, p2.Y - q, p2.X + q - L, p2.Y - q);
                    g.DrawLine(pen, p2.X + q, p2.Y - q, p2.X + q, p2.Y - q + L);
                    g.DrawLine(pen, p2.X - q, p2.Y + q, p2.X - q + L, p2.Y + q);
                    g.DrawLine(pen, p2.X - q, p2.Y + q, p2.X - q, p2.Y + q - L);
                    g.DrawLine(pen, p2.X + q, p2.Y + q, p2.X + q - L, p2.Y + q);
                    g.DrawLine(pen, p2.X + q, p2.Y + q, p2.X + q, p2.Y + q - L);
                }
            }

            // 被将军的将/帅
            if (CheckSq >= 0)
            {
                PointF p = SquareCenter(CheckSq);
                using (Pen pen = new Pen(Color.FromArgb(210, 226, 40, 40), Math.Max(2f, cw * 0.055f)))
                    g.DrawEllipse(pen, p.X - r * 1.06f, p.Y - r * 1.06f, 2.12f * r, 2.12f * r);
                using (Pen pen = new Pen(Color.FromArgb(90, 226, 40, 40), Math.Max(4f, cw * 0.12f)))
                    g.DrawEllipse(pen, p.X - r * 1.16f, p.Y - r * 1.16f, 2.32f * r, 2.32f * r);
            }

            // 选中
            if (Selected >= 0)
            {
                PointF p = SquareCenter(Selected);
                using (Pen pen = new Pen(Color.FromArgb(235, 32, 120, 214), Math.Max(2.4f, cw * 0.055f)))
                    g.DrawEllipse(pen, p.X - r * 0.99f, p.Y - r * 0.99f, 1.98f * r, 1.98f * r);
            }

            // 可走点
            for (int i = 0; i < Targets.Count; i++)
            {
                int t = Targets[i];
                PointF p = SquareCenter(t);
                bool occupied = (B.Sq[t] != 0);
                if (occupied)
                {
                    using (Pen pen = new Pen(Color.FromArgb(215, 214, 60, 40), Math.Max(2f, cw * 0.046f)))
                        g.DrawEllipse(pen, p.X - r * 0.94f, p.Y - r * 0.94f, 1.88f * r, 1.88f * r);
                }
                else
                {
                    float dr = cw * 0.135f;
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(205, 26, 150, 70)))
                    using (Pen pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
                    {
                        g.FillEllipse(b, p.X - dr, p.Y - dr, 2 * dr, 2 * dr);
                        g.DrawEllipse(pen, p.X - dr, p.Y - dr, 2 * dr, 2 * dr);
                    }
                }
            }
        }

        void DrawPieces(Graphics g)
        {
            for (int sq = 0; sq < 90; sq++)
            {
                int p = B.Sq[sq];
                if (p == 0) continue;
                if (animFrom >= 0 && animT < 1f && sq == animTo) continue;
                PointF c = SquareCenter(sq);
                DrawPieceAt(g, c.X, c.Y, p, 1f);
            }
            if (animFrom >= 0 && animT < 1f)
            {
                int p = B.Sq[animTo];
                if (p != 0)
                {
                    PointF a = SquareCenter(animFrom), b = SquareCenter(animTo);
                    float t = animT;
                    float e = t * t * (3f - 2f * t);
                    float x = a.X + (b.X - a.X) * e;
                    float y = a.Y + (b.Y - a.Y) * e - (float)Math.Sin(Math.PI * e) * ch * 0.09f;
                    DrawPieceAt(g, x, y, p, 1.06f);
                }
            }
        }

        void DrawPieceAt(Graphics g, float cx, float cy, int piece, float scale)
        {
            float r = Math.Min(cw, ch) * 0.415f * scale;
            RectangleF rc = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);
            bool red = (piece >> 3) == Pc.Red;
            Color ink = red ? RedInk : BlackInk;

            using (SolidBrush sh = new SolidBrush(Color.FromArgb(40, 60, 40, 18)))
                g.FillEllipse(sh, rc.X + r * 0.07f, rc.Y + r * 0.13f, 2 * r, 2 * r);

            using (LinearGradientBrush bgb = new LinearGradientBrush(
                new RectangleF(rc.X - 1, rc.Y - 1, rc.Width + 2, rc.Height + 2),
                Color.FromArgb(255, 252, 244), Color.FromArgb(226, 204, 166), 62f))
                g.FillEllipse(bgb, rc);

            using (Pen pen = new Pen(ink, Math.Max(1.3f, r * 0.085f)))
                g.DrawEllipse(pen, rc);
            using (Pen pen2 = new Pen(Color.FromArgb(130, ink), Math.Max(1f, r * 0.040f)))
                g.DrawEllipse(pen2, cx - r * 0.82f, cy - r * 0.82f, 1.64f * r, 1.64f * r);

            string s = Pc.Glyph(piece);
            float fs = r * 1.20f;
            using (Font f = new Font(PieceFont, fs, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush tb = new SolidBrush(ink))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(s, f, tb, new RectangleF(cx - r, cy - r * 1.04f, 2 * r, 2 * r), sf);
            }
        }

        void DrawFileLabels(Graphics g)
        {
            int bottomSide = Flipped ? Pc.Black : Pc.Red;
            float fs = cw * 0.30f;
            float top = oy + 9f * ch + 0.44f * cw + ch * 0.06f;
            using (Font f = new Font(UiFont, fs, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(160, 108, 78, 48)))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                for (int d = 0; d < 9; d++)
                {
                    // 最下面一方的编号从自己的右手边数起
                    string t = Notation.NumStr(9 - d, bottomSide);
                    g.DrawString(t, f, b, new RectangleF(GX(d) - cw * 0.5f, top, cw, ch * 0.55f), sf);
                }
            }
        }

        void DrawBanner(Graphics g)
        {
            if (Banner == null || Banner.Length == 0) return;
            RectangleF r = WoodRect();
            float w = r.Width * 0.76f, h = r.Height * 0.20f;
            RectangleF box = new RectangleF(r.X + (r.Width - w) / 2f, r.Y + (r.Height - h) / 2f, w, h);
            using (GraphicsPath sp = RoundRect(new RectangleF(box.X + 3, box.Y + 4, box.Width, box.Height), h * 0.28f))
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(34, 0, 0, 0)))
                g.FillPath(sb, sp);
            using (GraphicsPath p = RoundRect(box, h * 0.28f))
            {
                using (SolidBrush fb = new SolidBrush(Color.FromArgb(242, 253, 249, 240)))
                    g.FillPath(fb, p);
                using (Pen pen = new Pen(Color.FromArgb(200, 150, 40, 40), Math.Max(2f, cw * 0.05f)))
                    g.DrawPath(pen, p);
            }
            using (Font f = new Font(UiFont, h * 0.42f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(198, 34, 34)))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(Banner, f, tb, box, sf);
            }
        }
    }
}
