// 中国象棋核心：棋盘表示、走法生成、将军判定、中文记谱
// 坐标：x = 0..8（从红方视角的左到右），y = 0..9（y=0 是黑方底线，y=9 是红方底线）
// 索引：sq = y * 9 + x
using System;
using System.Collections.Generic;
using System.Text;

namespace ChinaChess
{
    public static class Pc
    {
        public const int None = 0;
        public const int King = 1;
        public const int Advisor = 2;
        public const int Elephant = 3;
        public const int Horse = 4;
        public const int Rook = 5;
        public const int Cannon = 6;
        public const int Pawn = 7;

        public const int Red = 0;
        public const int Black = 1;

        public static int Make(int side, int type) { return (side << 3) | type; }
        public static int Type(int p) { return p & 7; }
        public static int SideOf(int p) { return p >> 3; }

        static readonly string[] RedGlyph = { "", "帅", "仕", "相", "马", "车", "炮", "兵" };
        static readonly string[] BlackGlyph = { "", "将", "士", "象", "马", "车", "炮", "卒" };
        static readonly string[] TypeName = { "", "将/帅", "士/仕", "象/相", "马", "车", "炮", "兵/卒" };

        public static string Glyph(int p)
        {
            if (p == 0) return "";
            return (SideOf(p) == Red ? RedGlyph : BlackGlyph)[Type(p)];
        }

        public static string TypeNameOf(int t) { return TypeName[t]; }

        public static string SideName(int side) { return side == Red ? "红方" : "黑方"; }

        // 兵的“价值”，用于走法排序与评估
        public static readonly int[] Value = { 0, 10000, 200, 200, 400, 900, 450, 100 };
    }

    public struct ChessMove
    {
        public int From;
        public int To;
        public int Captured;

        public ChessMove(int from, int to, int captured) { From = from; To = to; Captured = captured; }

        public static ChessMove None { get { return new ChessMove(-1, -1, 0); } }
        public bool IsNone { get { return From < 0; } }
        public int Code { get { return From | (To << 8); } }
    }

    public class Board
    {
        public int[] Sq = new int[90];
        public int Side;                 // 轮到谁走
        public int[] KingSq = new int[2];
        public ulong Hash;

        static readonly int[] KDX = { 0, 0, 1, -1 };
        static readonly int[] KDY = { 1, -1, 0, 0 };
        static readonly int[] ADX = { 1, 1, -1, -1 };
        static readonly int[] ADY = { 1, -1, 1, -1 };
        static readonly int[] EDX = { 2, 2, -2, -2 };
        static readonly int[] EDY = { 2, -2, 2, -2 };
        static readonly int[] HDX = { 1, 2, 2, 1, -1, -2, -2, -1 };
        static readonly int[] HDY = { 2, 1, -1, -2, -2, -1, 1, 2 };

        static readonly ulong[][] Zobrist = InitZobrist();
        static readonly ulong ZobristSide = MakeRand(9973);

        static ulong MakeRand(int seed)
        {
            Random r = new Random(seed);
            byte[] b = new byte[8];
            r.NextBytes(b);
            return BitConverter.ToUInt64(b, 0);
        }

        static ulong[][] InitZobrist()
        {
            Random r = new Random(20261010);
            ulong[][] z = new ulong[16][];
            byte[] b = new byte[8];
            for (int p = 0; p < 16; p++)
            {
                z[p] = new ulong[90];
                for (int s = 0; s < 90; s++)
                {
                    r.NextBytes(b);
                    z[p][s] = BitConverter.ToUInt64(b, 0);
                }
            }
            return z;
        }

        public static int Idx(int x, int y) { return y * 9 + x; }
        public static int XOf(int sq) { return sq % 9; }
        public static int YOf(int sq) { return sq / 9; }
        public static bool OnBoard(int x, int y) { return x >= 0 && x <= 8 && y >= 0 && y <= 9; }

        public static bool InPalace(int x, int y, int side)
        {
            if (x < 3 || x > 5) return false;
            if (side == Pc.Red) return y >= 7 && y <= 9;
            return y >= 0 && y <= 2;
        }

        public void SetupStart()
        {
            Array.Clear(Sq, 0, 90);
            int[] back = { Pc.Rook, Pc.Horse, Pc.Elephant, Pc.Advisor, Pc.King, Pc.Advisor, Pc.Elephant, Pc.Horse, Pc.Rook };
            for (int x = 0; x < 9; x++)
            {
                Sq[Idx(x, 0)] = Pc.Make(Pc.Black, back[x]);
                Sq[Idx(x, 9)] = Pc.Make(Pc.Red, back[x]);
            }
            Sq[Idx(1, 2)] = Pc.Make(Pc.Black, Pc.Cannon);
            Sq[Idx(7, 2)] = Pc.Make(Pc.Black, Pc.Cannon);
            Sq[Idx(1, 7)] = Pc.Make(Pc.Red, Pc.Cannon);
            Sq[Idx(7, 7)] = Pc.Make(Pc.Red, Pc.Cannon);
            for (int x = 0; x < 9; x += 2)
            {
                Sq[Idx(x, 3)] = Pc.Make(Pc.Black, Pc.Pawn);
                Sq[Idx(x, 6)] = Pc.Make(Pc.Red, Pc.Pawn);
            }
            KingSq[Pc.Black] = Idx(4, 0);
            KingSq[Pc.Red] = Idx(4, 9);
            Side = Pc.Red;
            Hash = ComputeHash();
        }

        public Board Clone()
        {
            Board b = new Board();
            Array.Copy(Sq, b.Sq, 90);
            b.Side = Side;
            b.KingSq[0] = KingSq[0];
            b.KingSq[1] = KingSq[1];
            b.Hash = Hash;
            return b;
        }

        public ulong ComputeHash()
        {
            ulong h = 0;
            for (int s = 0; s < 90; s++)
                if (Sq[s] != 0) h ^= Zobrist[Sq[s]][s];
            if (Side == Pc.Black) h ^= ZobristSide;
            return h;
        }

        public void MakeMove(ChessMove m)
        {
            int p = Sq[m.From];
            int cap = Sq[m.To];
            Sq[m.To] = p;
            Sq[m.From] = 0;
            if (Pc.Type(p) == Pc.King) KingSq[Pc.SideOf(p)] = m.To;
            Hash ^= Zobrist[p][m.From] ^ Zobrist[p][m.To];
            if (cap != 0) Hash ^= Zobrist[cap][m.To];
            Side ^= 1;
            Hash ^= ZobristSide;
        }

        public void UnmakeMove(ChessMove m)
        {
            Side ^= 1;
            Hash ^= ZobristSide;
            int p = Sq[m.To];
            Sq[m.From] = p;
            Sq[m.To] = m.Captured;
            if (Pc.Type(p) == Pc.King) KingSq[Pc.SideOf(p)] = m.From;
            Hash ^= Zobrist[p][m.From] ^ Zobrist[p][m.To];
            if (m.Captured != 0) Hash ^= Zobrist[m.Captured][m.To];
        }

        // ---------- 走法生成 ----------

        int TryAdd(ChessMove[] buf, int n, int from, int to, int us, bool capturesOnly)
        {
            int q = Sq[to];
            if (q == 0)
            {
                if (capturesOnly) return n;
            }
            else if ((q >> 3) == us) return n;
            buf[n] = new ChessMove(from, to, q);
            return n + 1;
        }

        public int GeneratePseudo(ChessMove[] buf, bool capturesOnly)
        {
            int n = 0;
            int us = Side;
            for (int s = 0; s < 90; s++)
            {
                int p = Sq[s];
                if (p == 0 || (p >> 3) != us) continue;
                int t = p & 7;
                int x = s % 9;
                int y = s / 9;

                switch (t)
                {
                    case Pc.King:
                        {
                            for (int d = 0; d < 4; d++)
                            {
                                int nx = x + KDX[d], ny = y + KDY[d];
                                if (!InPalace(nx, ny, us)) continue;
                                n = TryAdd(buf, n, s, ny * 9 + nx, us, capturesOnly);
                            }
                        }
                        break;
                    case Pc.Advisor:
                        {
                            for (int d = 0; d < 4; d++)
                            {
                                int nx = x + ADX[d], ny = y + ADY[d];
                                if (!InPalace(nx, ny, us)) continue;
                                n = TryAdd(buf, n, s, ny * 9 + nx, us, capturesOnly);
                            }
                        }
                        break;
                    case Pc.Elephant:
                        {
                            for (int d = 0; d < 4; d++)
                            {
                                int nx = x + EDX[d], ny = y + EDY[d];
                                if (!OnBoard(nx, ny)) continue;
                                if (us == Pc.Red ? ny < 5 : ny > 4) continue;   // 象不过河
                                int ex = x + EDX[d] / 2, ey = y + EDY[d] / 2;
                                if (Sq[ey * 9 + ex] != 0) continue;             // 塞象眼
                                n = TryAdd(buf, n, s, ny * 9 + nx, us, capturesOnly);
                            }
                        }
                        break;
                    case Pc.Horse:
                        {
                            for (int d = 0; d < 8; d++)
                            {
                                int nx = x + HDX[d], ny = y + HDY[d];
                                if (!OnBoard(nx, ny)) continue;
                                int lx, ly;
                                if (HDX[d] == 2 || HDX[d] == -2) { lx = x + HDX[d] / 2; ly = y; }
                                else { lx = x; ly = y + HDY[d] / 2; }
                                if (Sq[ly * 9 + lx] != 0) continue;             // 蹩马腿
                                n = TryAdd(buf, n, s, ny * 9 + nx, us, capturesOnly);
                            }
                        }
                        break;
                    case Pc.Rook:
                        {
                            for (int d = 0; d < 4; d++)
                            {
                                int nx = x + KDX[d], ny = y + KDY[d];
                                while (OnBoard(nx, ny))
                                {
                                    int q = Sq[ny * 9 + nx];
                                    if (q == 0)
                                    {
                                        if (!capturesOnly) n = TryAdd(buf, n, s, ny * 9 + nx, us, capturesOnly);
                                    }
                                    else
                                    {
                                        if ((q >> 3) != us) n = TryAdd(buf, n, s, ny * 9 + nx, us, capturesOnly);
                                        break;
                                    }
                                    nx += KDX[d]; ny += KDY[d];
                                }
                            }
                        }
                        break;
                    case Pc.Cannon:
                        {
                            for (int d = 0; d < 4; d++)
                            {
                                int nx = x + KDX[d], ny = y + KDY[d];
                                while (OnBoard(nx, ny) && Sq[ny * 9 + nx] == 0)
                                {
                                    if (!capturesOnly) n = TryAdd(buf, n, s, ny * 9 + nx, us, capturesOnly);
                                    nx += KDX[d]; ny += KDY[d];
                                }
                                if (!OnBoard(nx, ny)) continue;   // 没有炮架
                                nx += KDX[d]; ny += KDY[d];       // 越过炮架
                                while (OnBoard(nx, ny))
                                {
                                    int q = Sq[ny * 9 + nx];
                                    if (q != 0)
                                    {
                                        if ((q >> 3) != us) n = TryAdd(buf, n, s, ny * 9 + nx, us, capturesOnly);
                                        break;
                                    }
                                    nx += KDX[d]; ny += KDY[d];
                                }
                            }
                        }
                        break;
                    case Pc.Pawn:
                        {
                            int fy = y + (us == Pc.Red ? -1 : 1);
                            if (fy >= 0 && fy <= 9) n = TryAdd(buf, n, s, fy * 9 + x, us, capturesOnly);
                            bool crossed = (us == Pc.Red) ? (y <= 4) : (y >= 5);
                            if (crossed)
                            {
                                if (x > 0) n = TryAdd(buf, n, s, y * 9 + x - 1, us, capturesOnly);
                                if (x < 8) n = TryAdd(buf, n, s, y * 9 + x + 1, us, capturesOnly);
                            }
                        }
                        break;
                }
            }
            return n;
        }

        // 生成合法着法（过滤送将、将帅照面）
        public int GenerateLegal(ChessMove[] buf, bool capturesOnly)
        {
            int n = GeneratePseudo(buf, capturesOnly);
            int m = 0;
            int us = Side;
            for (int i = 0; i < n; i++)
            {
                ChessMove mv = buf[i];
                MakeMove(mv);
                if (!InCheck(us)) buf[m++] = mv;
                UnmakeMove(mv);
            }
            return m;
        }

        // ---------- 将军判定 ----------

        public bool InCheck(int side)
        {
            return IsAttacked(KingSq[side], 1 - side);
        }

        public bool IsAttacked(int sq, int bySide)
        {
            int x = sq % 9, y = sq / 9;

            // 车、将帅照面、炮
            for (int d = 0; d < 4; d++)
            {
                int dx = KDX[d], dy = KDY[d];
                int cx = x + dx, cy = y + dy;
                int screen = 0;
                while (OnBoard(cx, cy))
                {
                    int p = Sq[cy * 9 + cx];
                    if (p != 0)
                    {
                        if (screen == 0)
                        {
                            if ((p >> 3) == bySide)
                            {
                                int t = p & 7;
                                if (t == Pc.Rook) return true;
                                if (t == Pc.King && dx == 0) return true;   // 将帅照面
                            }
                            screen = 1;
                        }
                        else
                        {
                            if ((p >> 3) == bySide && (p & 7) == Pc.Cannon) return true;
                            break;
                        }
                    }
                    cx += dx; cy += dy;
                }
            }

            // 马
            for (int i = 0; i < 8; i++)
            {
                int hx = x + HDX[i], hy = y + HDY[i];
                if (!OnBoard(hx, hy)) continue;
                int p = Sq[hy * 9 + hx];
                if (p == 0 || (p >> 3) != bySide || (p & 7) != Pc.Horse) continue;
                int lx, ly;
                if (HDX[i] == 2 || HDX[i] == -2) { lx = hx - Math.Sign(HDX[i]); ly = hy; }
                else { lx = hx; ly = hy - Math.Sign(HDY[i]); }
                if (Sq[ly * 9 + lx] == 0) return true;
            }

            // 兵/卒
            if (bySide == Pc.Red)
            {
                if (y + 1 <= 9 && Sq[(y + 1) * 9 + x] == Pc.Make(Pc.Red, Pc.Pawn)) return true;
                if (y <= 4)
                {
                    if (x > 0 && Sq[y * 9 + x - 1] == Pc.Make(Pc.Red, Pc.Pawn)) return true;
                    if (x < 8 && Sq[y * 9 + x + 1] == Pc.Make(Pc.Red, Pc.Pawn)) return true;
                }
            }
            else
            {
                if (y - 1 >= 0 && Sq[(y - 1) * 9 + x] == Pc.Make(Pc.Black, Pc.Pawn)) return true;
                if (y >= 5)
                {
                    if (x > 0 && Sq[y * 9 + x - 1] == Pc.Make(Pc.Black, Pc.Pawn)) return true;
                    if (x < 8 && Sq[y * 9 + x + 1] == Pc.Make(Pc.Black, Pc.Pawn)) return true;
                }
            }

            return false;
        }

        // ---------- 文本输出（调试用） ----------

        public string ToAscii()
        {
            StringBuilder sb = new StringBuilder();
            for (int y = 0; y < 10; y++)
            {
                for (int x = 0; x < 9; x++)
                {
                    int p = Sq[y * 9 + x];
                    sb.Append(p == 0 ? " ." : " " + Pc.Glyph(p));
                }
                sb.Append("\r\n");
            }
            return sb.ToString();
        }
    }

    // 中文记谱法：如 炮二平五、马8进7、前车进一
    // 惯例：红方用中文数字，黑方用阿拉伯数字
    public static class Notation
    {
        static readonly string[] Han = { "", "一", "二", "三", "四", "五", "六", "七", "八", "九" };

        public static string NumStr(int n, int side)
        {
            if (side == Pc.Red) return Han[n];
            return n.ToString();
        }

        public static string FileNum(int x, int side)
        {
            int n = (side == Pc.Red) ? (9 - x) : (x + 1);
            return NumStr(n, side);
        }

        public static string Describe(Board b, ChessMove m)
        {
            int p = b.Sq[m.From];
            if (p == 0) return "??";
            int t = p & 7;
            int side = p >> 3;
            int fx = m.From % 9, fy = m.From / 9;
            int tx = m.To % 9, ty = m.To / 9;

            // 同一路上有多个同种棋子时用 前/中/后 区分
            List<int> same = new List<int>();
            for (int y = 0; y < 10; y++)
            {
                int q = b.Sq[y * 9 + fx];
                if (q != 0 && (q >> 3) == side && (q & 7) == t) same.Add(y * 9 + fx);
            }
            string prefix = "";
            if (same.Count >= 2)
            {
                same.Sort(delegate(int a, int c)
                {
                    int ya = a / 9, yc = c / 9;
                    return (side == Pc.Red) ? ya.CompareTo(yc) : yc.CompareTo(ya);
                });
                int idx = same.IndexOf(m.From);
                if (idx == 0) prefix = "前";
                else if (idx == same.Count - 1) prefix = "后";
                else prefix = NumStr(idx + 1, side);
            }

            string head = (prefix.Length > 0) ? (prefix + Pc.Glyph(p)) : (Pc.Glyph(p) + FileNum(fx, side));

            bool forward = (side == Pc.Red) ? (ty < fy) : (ty > fy);
            bool straight = (t == Pc.Rook || t == Pc.Cannon || t == Pc.King || t == Pc.Pawn);
            if (ty == fy) return head + "平" + FileNum(tx, side);
            if (straight) return head + (forward ? "进" : "退") + NumStr(Math.Abs(ty - fy), side);
            return head + (forward ? "进" : "退") + FileNum(tx, side);
        }
    }
}
