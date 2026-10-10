// 电脑棋手：Alpha-Beta 搜索 + 置换表 + 静态评估，分五档难度
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ChinaChess
{
    public class Level
    {
        public string Name;
        public int Depth;        // 最大搜索层数
        public int TimeMs;       // 每步思考时间上限
        public int Noise;        // 选点时允许比最优差多少“厘兵”(1兵=100)
        public int BlunderPct;   // 随机走一步（送子）的概率

        public Level(string name, int depth, int timeMs, int noise, int blunderPct)
        {
            Name = name; Depth = depth; TimeMs = timeMs; Noise = noise; BlunderPct = blunderPct;
        }
    }

    public class CancelToken
    {
        public volatile bool Cancelled;
    }

    public class TimeUpException : Exception { }

    public class Engine
    {
        const int MaxPly = 64;
        const int BufSize = 192;
        const int MateScore = 30000;
        const int Inf = 1000000;

        struct TTEntry
        {
            public ulong Key;
            public int Score;
            public int ChessMove;
            public short Depth;
            public byte Flag;    // 1=精确 2=下界 3=上界
        }

        TTEntry[] tt;
        int ttMask;
        ChessMove[][] mvBuf = new ChessMove[MaxPly][];
        int[][] ordBuf = new int[MaxPly][];
        int[] killers = new int[MaxPly * 2];
        int[] history = new int[90 * 90];

        Board b;
        Stopwatch sw;
        int timeLimit;
        long nodes;
        CancelToken token;
        Random rnd = new Random(20261010);

        public int LastScore;
        public int LastDepth;
        public long LastNodes;

        public Engine()
        {
            int bits = 19;
            ttMask = (1 << bits) - 1;
            tt = new TTEntry[1 << bits];
            for (int i = 0; i < MaxPly; i++)
            {
                mvBuf[i] = new ChessMove[BufSize];
                ordBuf[i] = new int[BufSize];
            }
        }

        // ---------- 对外入口 ----------

        public ChessMove Think(Board board, Level lv, CancelToken cancelToken)
        {
            b = board.Clone();
            token = cancelToken;
            Array.Clear(tt, 0, tt.Length);
            Array.Clear(killers, 0, killers.Length);
            Array.Clear(history, 0, history.Length);
            sw = Stopwatch.StartNew();
            timeLimit = lv.TimeMs;
            nodes = 0;
            LastScore = 0; LastDepth = 0; LastNodes = 0;

            ChessMove[] rootMoves = new ChessMove[BufSize];
            int rootCount = b.GenerateLegal(rootMoves, false);
            if (rootCount == 0) return ChessMove.None;

            ChessMove[] lastRootMoves = new ChessMove[BufSize];
            int[] lastRootScores = new int[BufSize];
            int[] curScores = new int[BufSize];
            int lastCount = 0;
            int lastScore = 0;
            int doneDepth = 0;
            ChessMove best = ChessMove.None;

            try
            {
                for (int d = 1; d <= lv.Depth; d++)
                {
                    int curBest = -Inf;
                    int curBestIdx = 0;
                    for (int i = 0; i < rootCount; i++)
                    {
                        ChessMove m = rootMoves[i];
                        b.MakeMove(m);
                        int sc = -Negamax(d - 1, -Inf, Inf, 1);
                        b.UnmakeMove(m);
                        curScores[i] = sc;
                        if (sc > curBest) { curBest = sc; curBestIdx = i; }
                    }
                    // 本层完成，记录下来
                    best = rootMoves[curBestIdx];
                    lastScore = curBest;
                    Array.Copy(rootMoves, lastRootMoves, rootCount);
                    Array.Copy(curScores, lastRootScores, rootCount);
                    lastCount = rootCount;
                    doneDepth = d;

                    // 把最优着法提到最前面，提高下一层剪枝效率
                    if (curBestIdx != 0)
                    {
                        ChessMove tm = rootMoves[0]; rootMoves[0] = rootMoves[curBestIdx]; rootMoves[curBestIdx] = tm;
                    }

                    if (Math.Abs(curBest) > MateScore - 1000) break;      // 已经算到杀棋
                    if (sw.ElapsedMilliseconds >= timeLimit) break;       // 时间用完
                }
            }
            catch (TimeUpException) { }

            LastScore = lastScore;
            LastDepth = doneDepth;
            LastNodes = nodes;

            if (lastCount == 0) return rootMoves[rnd.Next(rootCount)];

            // 按难度挑选着法：入门会“看走眼”
            if (lv.BlunderPct > 0 && rnd.Next(100) < lv.BlunderPct)
                return rootMoves[rnd.Next(rootCount)];

            if (lv.Noise > 0)
            {
                List<int> cand = new List<int>();
                for (int i = 0; i < lastCount; i++)
                    if (lastRootScores[i] >= lastScore - lv.Noise) cand.Add(i);
                if (cand.Count > 0) return lastRootMoves[cand[rnd.Next(cand.Count)]];
            }

            return best;
        }

        // ---------- 搜索 ----------

        int Negamax(int depth, int alpha, int beta, int ply)
        {
            if ((++nodes & 1023) == 0) CheckTime();
            if (ply >= MaxPly - 3) return EvalSide();

            ulong key = b.Hash;
            int idx = (int)(key & (ulong)ttMask);
            TTEntry e = tt[idx];
            int ttMove = -1;
            if (e.Key == key)
            {
                ttMove = e.ChessMove;
                if (e.Depth >= depth)
                {
                    int s = FromTT(e.Score, ply);
                    if (e.Flag == 1) return s;
                    if (e.Flag == 2 && s >= beta) return s;
                    if (e.Flag == 3 && s <= alpha) return s;
                }
            }

            if (depth <= 0) return Quiesce(alpha, beta, ply);

            ChessMove[] buf = mvBuf[ply];
            int n = b.GenerateLegal(buf, false);
            if (n == 0) return -MateScore + ply;      // 象棋里无棋可走即为负（将死或困毙）

            int[] ord = ordBuf[ply];
            OrderMoves(buf, ord, n, ttMove, ply);

            int best = -Inf;
            ChessMove bestMove = ChessMove.None;
            int origAlpha = alpha;

            for (int i = 0; i < n; i++)
            {
                SelectBest(buf, ord, n, i);
                ChessMove m = buf[i];

                b.MakeMove(m);
                int sc = -Negamax(depth - 1, -beta, -alpha, ply + 1);
                b.UnmakeMove(m);

                if (sc > best)
                {
                    best = sc;
                    bestMove = m;
                }
                if (sc > alpha) alpha = sc;
                if (alpha >= beta)
                {
                    if (m.Captured == 0)
                    {
                        int code = m.Code;
                        if (killers[ply * 2] != code)
                        {
                            killers[ply * 2 + 1] = killers[ply * 2];
                            killers[ply * 2] = code;
                        }
                        int hi = m.From * 90 + m.To;
                        history[hi] += depth * depth;
                        if (history[hi] > 200000)
                        {
                            for (int k = 0; k < history.Length; k++) history[k] >>= 1;
                        }
                    }
                    break;
                }
            }

            byte flag = (byte)(best <= origAlpha ? 3 : (best >= beta ? 2 : 1));
            StoreTT(key, best, bestMove, depth, flag, ply);
            return best;
        }

        int Quiesce(int alpha, int beta, int ply)
        {
            if ((++nodes & 1023) == 0) CheckTime();
            if (ply >= MaxPly - 3) return EvalSide();

            bool inChk = b.InCheck(b.Side);
            int stand;
            if (!inChk)
            {
                stand = EvalSide();
                if (stand >= beta) return beta;
                if (stand > alpha) alpha = stand;
            }
            else
            {
                stand = -Inf;
            }

            ChessMove[] buf = mvBuf[ply];
            int n = b.GenerateLegal(buf, !inChk);      // 被将军时考虑所有解将着法
            if (n == 0) return inChk ? (-MateScore + ply) : alpha;

            int[] ord = ordBuf[ply];
            OrderMoves(buf, ord, n, -1, ply);

            for (int i = 0; i < n; i++)
            {
                SelectBest(buf, ord, n, i);
                ChessMove m = buf[i];
                if (!inChk && m.Captured != 0)
                {
                    // 简单的前向剪枝：即使白吃也远远不够
                    if (stand + Pc.Value[Pc.Type(m.Captured)] + 180 < alpha) continue;
                }
                b.MakeMove(m);
                int sc = -Quiesce(-beta, -alpha, ply + 1);
                b.UnmakeMove(m);
                if (sc >= beta) return beta;
                if (sc > alpha) alpha = sc;
            }
            return alpha;
        }

        void CheckTime()
        {
            if (token != null && token.Cancelled) throw new TimeUpException();
            if (sw.ElapsedMilliseconds >= timeLimit) throw new TimeUpException();
        }

        // ---------- 置换表 ----------

        int ToTT(int s, int ply)
        {
            if (s > MateScore - 1000) return s + ply;
            if (s < -MateScore + 1000) return s - ply;
            return s;
        }

        int FromTT(int s, int ply)
        {
            if (s > MateScore - 1000) return s - ply;
            if (s < -MateScore + 1000) return s + ply;
            return s;
        }

        void StoreTT(ulong key, int score, ChessMove best, int depth, byte flag, int ply)
        {
            int idx = (int)(key & (ulong)ttMask);
            TTEntry old = tt[idx];
            if (old.Key == key && old.Depth > depth && flag != 1) return;
            TTEntry e = new TTEntry();
            e.Key = key;
            e.Score = ToTT(score, ply);
            e.ChessMove = best.IsNone ? -1 : best.Code;
            e.Depth = (short)depth;
            e.Flag = flag;
            tt[idx] = e;
        }

        // ---------- 着法排序 ----------

        void OrderMoves(ChessMove[] buf, int[] ord, int n, int ttMove, int ply)
        {
            for (int i = 0; i < n; i++)
            {
                ChessMove m = buf[i];
                int s;
                if (m.Code == ttMove) s = 1000000;
                else if (m.Captured != 0)
                {
                    int victim = Pc.Value[Pc.Type(m.Captured)];
                    int attacker = Pc.Value[Pc.Type(b.Sq[m.From])];
                    s = 200000 + victim * 8 - attacker;
                }
                else if (m.Code == killers[ply * 2]) s = 150000;
                else if (m.Code == killers[ply * 2 + 1]) s = 149000;
                else s = history[m.From * 90 + m.To];
                ord[i] = s;
            }
        }

        void SelectBest(ChessMove[] buf, int[] ord, int n, int i)
        {
            int bi = i;
            for (int j = i + 1; j < n; j++) if (ord[j] > ord[bi]) bi = j;
            if (bi != i)
            {
                ChessMove tm = buf[i]; buf[i] = buf[bi]; buf[bi] = tm;
                int ts = ord[i]; ord[i] = ord[bi]; ord[bi] = ts;
            }
        }

        // ---------- 局面评估 ----------

        int EvalSide()
        {
            int s = EvalRed(b);
            return (b.Side == Pc.Red) ? s + 8 : -s + 8;
        }

        public static int EvalRed(Board bd)
        {
            int score = 0;
            int rk = bd.KingSq[Pc.Red];
            int bk = bd.KingSq[Pc.Black];
            int rkx = rk % 9, rky = rk / 9;
            int bkx = bk % 9, bky = bk / 9;

            for (int s = 0; s < 90; s++)
            {
                int p = bd.Sq[s];
                if (p == 0) continue;
                int t = p & 7;
                int side = p >> 3;
                int x = s % 9, y = s / 9;
                int v = Pc.Value[t];

                if (side == Pc.Red)
                {
                    v += RedPos(t, x, y);
                    if (t == Pc.Rook || t == Pc.Horse || t == Pc.Cannon)
                    {
                        int dx = Math.Abs(x - bkx), dy = Math.Abs(y - bky);
                        int d = dx > dy ? dx : dy;
                        if (d <= 2) v += (t == Pc.Rook ? 34 : 18);
                    }
                    score += v;
                }
                else
                {
                    v += RedPos(t, 8 - x, 9 - y);
                    if (t == Pc.Rook || t == Pc.Horse || t == Pc.Cannon)
                    {
                        int dx = Math.Abs(x - rkx), dy = Math.Abs(y - rky);
                        int d = dx > dy ? dx : dy;
                        if (d <= 2) v += (t == Pc.Rook ? 34 : 18);
                    }
                    score -= v;
                }
            }
            return score;
        }

        // 以红方视角的位置加分（y=9 是红方底线）
        static int RedPos(int t, int x, int y)
        {
            int central = 4 - Math.Abs(x - 4);
            switch (t)
            {
                case Pc.Pawn:
                    {
                        int v = (6 - y) * 8 + central * 3;
                        if (y <= 4) v += 12;   // 过河
                        if (y <= 2) v += 14;   // 逼近九宫
                        if (y <= 1) v += 10;
                        return v;
                    }
                case Pc.Horse:
                    {
                        int v = central * 4;
                        if (y <= 4) v += 16;
                        if (y >= 8) v -= 6;
                        return v;
                    }
                case Pc.Cannon:
                    {
                        int v = central * 5;
                        if (y <= 4) v += 10;
                        if (y >= 8) v -= 4;
                        return v;
                    }
                case Pc.Rook:
                    {
                        int v = central * 3;
                        if (y <= 4) v += 12;
                        return v;
                    }
                case Pc.Advisor:
                    return (y >= 8) ? 8 : 0;
                case Pc.Elephant:
                    return (y >= 8) ? 8 : 0;
                case Pc.King:
                    return (y == 9) ? 4 : -10;
            }
            return 0;
        }
    }
}
