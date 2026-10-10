// 自测程序：验证走法生成（perft）与中文记谱
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ChinaChess;

class SelfTest
{
    static int fails = 0;

    static long Perft(Board b, int depth)
    {
        if (depth == 0) return 1;
        ChessMove[] buf = new ChessMove[256];
        int n = b.GenerateLegal(buf, false);
        long c = 0;
        for (int i = 0; i < n; i++)
        {
            ChessMove m = buf[i];
            b.MakeMove(m);
            c += Perft(b, depth - 1);
            b.UnmakeMove(m);
        }
        return c;
    }

    static void Check(string what, long got, long want)
    {
        bool ok = got == want;
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what + "  got=" + got + "  want=" + want);
    }

    static void CheckStr(string what, string got, string want)
    {
        bool ok = got == want;
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what + "  got=[" + got + "]  want=[" + want + "]");
    }

    static ChessMove Find(Board b, int fx, int fy, int tx, int ty)
    {
        ChessMove[] buf = new ChessMove[256];
        int n = b.GenerateLegal(buf, false);
        int from = fy * 9 + fx, to = ty * 9 + tx;
        for (int i = 0; i < n; i++)
            if (buf[i].From == from && buf[i].To == to) return buf[i];
        return ChessMove.None;
    }

    // pcs: side, type, x, y, ...
    static Board MakeBoard(int side, params int[] pcs)
    {
        Board b = new Board();
        Array.Clear(b.Sq, 0, 90);
        for (int i = 0; i + 3 < pcs.Length; i += 4)
        {
            int sd = pcs[i], tp = pcs[i + 1], x = pcs[i + 2], y = pcs[i + 3];
            b.Sq[y * 9 + x] = Pc.Make(sd, tp);
            if (tp == Pc.King) b.KingSq[sd] = y * 9 + x;
        }
        b.Side = side;
        b.Hash = b.ComputeHash();
        return b;
    }

    static void EngineTests()
    {
        Console.WriteLine("=== 引擎测试 ===");
        Engine eng = new Engine();

        // 1. 白吃一个车，必须吃掉
        Board b = MakeBoard(Pc.Red,
            Pc.Red, Pc.King, 4, 9,
            Pc.Black, Pc.King, 4, 0,
            Pc.Black, Pc.Pawn, 4, 3,
            Pc.Black, Pc.Rook, 0, 0,
            Pc.Red, Pc.Rook, 0, 5);
        Level lv = new Level("测试", 4, 2000, 0, 0);
        ChessMove m = eng.Think(b, lv, new CancelToken());
        bool ok = (m.From == 5 * 9 + 0 && m.To == 0 * 9 + 0);
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + "白吃车: 走 " + m.From + "->" + m.To +
            " (期望 45->0), 评估=" + eng.LastScore + " 层数=" + eng.LastDepth);

        // 2. 将死局面：黑方无着可走（双车错，红兵挡住将帅照面）
        Board dead = MakeBoard(Pc.Black,
            Pc.Red, Pc.King, 4, 9,
            Pc.Red, Pc.Pawn, 4, 5,
            Pc.Red, Pc.Rook, 0, 0,
            Pc.Red, Pc.Rook, 5, 1,
            Pc.Black, Pc.King, 4, 0);
        ChessMove[] tmp = new ChessMove[256];
        int legal = dead.GenerateLegal(tmp, false);
        Check("绝杀局面无着可走", legal, 0);
        Check("绝杀局面确实被将军", dead.InCheck(Pc.Black) ? 1 : 0, 1);

        // 2b. 困毙（未被将军但无着可走）在象棋里同样判负
        Board stuck = MakeBoard(Pc.Black,
            Pc.Red, Pc.King, 4, 9,
            Pc.Red, Pc.Pawn, 4, 5,
            Pc.Red, Pc.Rook, 3, 1,
            Pc.Red, Pc.Rook, 5, 1,
            Pc.Black, Pc.King, 4, 0);
        int legal2 = stuck.GenerateLegal(tmp, false);
        Check("困毙局面无着可走", legal2, 0);

        // 3. 自我对弈 40 回合，检查全部合法、不出错
        Board g = new Board();
        g.SetupStart();
        Engine e1 = new Engine();
        Level mid = new Level("中级", 3, 800, 0, 0);
        StringBuilder sb = new StringBuilder();
        int plies = 0;
        for (int i = 0; i < 40; i++)
        {
            int side = g.Side;
            ChessMove mv = e1.Think(g, mid, new CancelToken());
            if (mv.IsNone) { sb.AppendLine("(" + Pc.SideName(side) + " 无棋可走，对局结束)"); break; }
            ChessMove[] legalBuf = new ChessMove[256];
            int ln = g.GenerateLegal(legalBuf, false);
            bool found = false;
            for (int k = 0; k < ln; k++) if (legalBuf[k].From == mv.From && legalBuf[k].To == mv.To) found = true;
            if (!found) { fails++; Console.WriteLine("FAIL  引擎给出非法着法 " + mv.From + "->" + mv.To); break; }
            sb.AppendLine((i + 1) + ". " + (side == Pc.Red ? "红 " : "黑 ") + Notation.Describe(g, mv));
            g.MakeMove(mv);
            plies++;
        }
        Check("自我对弈步数", plies, 40);
        File.WriteAllText("selftest_selfplay.txt", sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine("（自我对弈棋谱已写入 selftest_selfplay.txt）");

        // 4. 各档难度耗时
        foreach (Level l in new Level[] {
            new Level("入门", 1, 200, 200, 30),
            new Level("初级", 2, 500, 90, 12),
            new Level("中级", 3, 1200, 40, 4),
            new Level("高级", 4, 2500, 12, 0),
            new Level("大师", 6, 5000, 0, 0) })
        {
            Board t = new Board();
            t.SetupStart();
            DateTime s = DateTime.Now;
            Engine ee = new Engine();
            ChessMove mm = ee.Think(t, l, new CancelToken());
            double sec = (DateTime.Now - s).TotalSeconds;
            Console.WriteLine("  " + l.Name.PadRight(4) + " 用时 " + sec.ToString("0.00") + " 秒  层数 " + ee.LastDepth +
                "  节点 " + ee.LastNodes + "  着法 " + Notation.Describe(t, mm));
        }
    }

    static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        Board b = new Board();
        b.SetupStart();

        Console.WriteLine("=== 初始局面 ===");
        Console.Write(b.ToAscii());

        Console.WriteLine("=== perft（走法生成正确性）===");
        DateTime t0 = DateTime.Now;
        Check("perft(1)", Perft(b, 1), 44);
        Check("perft(2)", Perft(b, 2), 1920);
        Check("perft(3)", Perft(b, 3), 79666);
        Console.WriteLine("  用时 " + (DateTime.Now - t0).TotalSeconds.ToString("0.0") + " 秒");
        t0 = DateTime.Now;
        Check("perft(4)", Perft(b, 4), 3290240);
        Console.WriteLine("  用时 " + (DateTime.Now - t0).TotalSeconds.ToString("0.0") + " 秒");

        // 每次 make/unmake 后哈希必须能还原
        Board h = new Board();
        h.SetupStart();
        ulong before = h.Hash;
        ChessMove[] mv = new ChessMove[256];
        int cnt = h.GenerateLegal(mv, false);
        bool hashOk = true;
        for (int i = 0; i < cnt; i++)
        {
            h.MakeMove(mv[i]);
            h.UnmakeMove(mv[i]);
            if (h.Hash != before || h.ComputeHash() != before) hashOk = false;
        }
        Check("哈希增删一致", hashOk ? 1 : 0, 1);

        Console.WriteLine("=== 中文记谱 ===");
        Board g = new Board();
        g.SetupStart();
        StringBuilder sb = new StringBuilder();
        string[] want = { "炮二平五", "马8进7", "马二进三", "车9平8", "车一平二", "马2进3" };
        int[,] coords = {
            {7,7,4,7}, {7,0,6,2}, {7,9,6,7}, {8,0,7,0}, {8,9,7,9}, {1,0,2,2}
        };
        for (int i = 0; i < want.Length; i++)
        {
            ChessMove m = Find(g, coords[i, 0], coords[i, 1], coords[i, 2], coords[i, 3]);
            if (m.IsNone) { CheckStr("着法 " + (i + 1), "找不到合法着法", want[i]); break; }
            string s = Notation.Describe(g, m);
            CheckStr("记谱 " + (i + 1), s, want[i]);
            sb.AppendLine((i + 1) + ". " + s + "   (from " + m.From + " to " + m.To + ")");
            g.MakeMove(m);
        }

        // 局面文本落盘，便于人工核对
        File.WriteAllText("selftest_board.txt", g.ToAscii() + "\r\n" + sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine("（对局文本已写入 selftest_board.txt）");

        EngineTests();

        Console.WriteLine(fails == 0 ? "全部通过" : ("有 " + fails + " 项失败"));
        return fails == 0 ? 0 : 1;
    }
}
