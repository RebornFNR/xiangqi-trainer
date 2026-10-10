// 程序入口；带 --shot 参数时把界面渲染成 PNG（用于开发期检查版面）
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ChinaChess
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string shot = ArgVal(args, "--shot");
            if (shot != null)
            {
                Shot(args, shot);
                return;
            }

            Application.Run(new MainForm());
        }

        static string ArgVal(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        static void Shot(string[] args, string path)
        {
            MainForm f = new MainForm();
            f.ShotMode = true;                        // 截图时不要真的让电脑走棋
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(6, 6);

            string size = ArgVal(args, "--size");
            if (size != null)
            {
                string[] parts = size.Split('x');
                if (parts.Length == 2) f.ClientSize = new Size(int.Parse(parts[0]), int.Parse(parts[1]));
            }

            f.Show();
            Application.DoEvents();

            string side = ArgVal(args, "--side");
            if (side != null) f.DebugSetSide(int.Parse(side));
            string level = ArgVal(args, "--level");
            if (level != null) f.DebugSetLevel(int.Parse(level));
            string play = ArgVal(args, "--play");
            if (play != null) f.DebugPlay(int.Parse(play));
            string sel = ArgVal(args, "--select");
            if (sel != null) f.DebugSelect(int.Parse(sel));
            if (ArgVal(args, "--check") != null) f.DebugSetupCheck();
            string banner = ArgVal(args, "--banner");
            if (banner != null) f.DebugSetBanner(banner);
            if (ArgVal(args, "--dump") != null) f.DumpLayout(path + ".txt");
            string itest = ArgVal(args, "--itest");
            if (itest != null) f.DebugInteractionTest(itest);

            Application.DoEvents();
            System.Threading.Thread.Sleep(400);
            Application.DoEvents();

            using (Bitmap bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(path, ImageFormat.Png);
            }
            f.Close();
            Console.WriteLine("saved " + path);
        }
    }
}
