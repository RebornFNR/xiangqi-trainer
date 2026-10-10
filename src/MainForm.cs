// 主窗口：难度选择、悔棋、换边、棋谱记录、对局状态
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ChinaChess
{
    public class MoveRecord
    {
        public ChessMove Mv;
        public string Nota;
        public int PrevHalfMove;
    }

    public class MainForm : Form
    {
        public static readonly Level[] Levels = new Level[] {
            new Level("1 入门（刚学规则）", 1,  200, 200, 30),
            new Level("2 初级",           2,  500,  90, 12),
            new Level("3 中级",           3, 1200,  40,  4),
            new Level("4 高级",           4, 2500,  12,  0),
            new Level("5 大师（想得最久）", 6, 5000,   0,  0),
        };

        Board B = new Board();
        GameBoard view = new GameBoard();
        Engine engine = new Engine();

        int levelIdx = 2;
        int playerSide = Pc.Red;
        bool gameOver;
        bool thinking;
        string resultText = "";

        List<MoveRecord> hist = new List<MoveRecord>();
        Dictionary<ulong, int> repCount = new Dictionary<ulong, int>();
        int halfMoveClock;

        CancelToken token;
        Thread worker;
        int thinkGen;
        bool suppressAi;
        bool building = true;

        int selected = -1;
        List<int> targets = new List<int>();

        ComboBox cbLevel;
        ComboBox cbSide;
        Button btnNew, btnUndo, btnSwap;
        Label lblRed, lblBlack, lblStatus, lblTip;

        public MainForm()
        {
            Text = "中国象棋 · 新手陪练";
            ClientSize = new Size(1120, 830);
            MinimumSize = new Size(960, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(242, 239, 233);
            Font = new Font(Fonts.Ui, 9.5f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;

            BuildUi();

            building = false;
            cbLevel.SelectedIndex = levelIdx;
            cbSide.SelectedIndex = 0;
            NewGame();
        }

        // ---------------- 界面 ----------------

        void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 296f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            root.BackColor = Color.FromArgb(242, 239, 233);

            // 顶栏
            FlowLayoutPanel top = new FlowLayoutPanel();
            top.Dock = DockStyle.Fill;
            top.FlowDirection = FlowDirection.LeftToRight;
            top.WrapContents = false;
            top.Padding = new Padding(12, 14, 8, 8);
            top.BackColor = Color.FromArgb(252, 250, 245);

            top.Controls.Add(MakeLabel("难度", 42, true));
            cbLevel = new ComboBox();
            cbLevel.DropDownStyle = ComboBoxStyle.DropDownList;
            cbLevel.Width = 168;
            cbLevel.Margin = new Padding(0, 2, 18, 0);
            for (int i = 0; i < Levels.Length; i++) cbLevel.Items.Add(Levels[i].Name);
            cbLevel.SelectedIndexChanged += delegate { OnLevelChanged(); };
            top.Controls.Add(cbLevel);

            top.Controls.Add(MakeLabel("我执", 42, true));
            cbSide = new ComboBox();
            cbSide.DropDownStyle = ComboBoxStyle.DropDownList;
            cbSide.Width = 150;
            cbSide.Margin = new Padding(0, 2, 18, 0);
            cbSide.Items.Add("红方（先行）");
            cbSide.Items.Add("黑方（后行）");
            cbSide.SelectedIndexChanged += delegate { OnSideChanged(); };
            top.Controls.Add(cbSide);

            btnNew = MakeButton("新局", 76);
            btnNew.Click += delegate { NewGame(); };
            top.Controls.Add(btnNew);

            btnUndo = MakeButton("悔棋", 76);
            btnUndo.Click += delegate { Undo(); };
            top.Controls.Add(btnUndo);

            btnSwap = MakeButton("换边", 76);
            btnSwap.Click += delegate { SwapSide(); };
            top.Controls.Add(btnSwap);

            // 棋盘
            view.Dock = DockStyle.Fill;
            view.Margin = new Padding(6, 6, 3, 6);
            view.SquareClicked += OnSquareClick;

            // 右侧信息栏
            TableLayoutPanel right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.ColumnCount = 1;
            right.RowCount = 6;
            right.Padding = new Padding(10, 10, 10, 8);
            right.BackColor = Color.FromArgb(250, 248, 243);
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 96f));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 152f));

            lblRed = new Label();
            lblRed.Dock = DockStyle.Fill;
            lblRed.Font = new Font(Fonts.Ui, 10.5f, FontStyle.Bold);
            lblRed.ForeColor = Color.FromArgb(176, 32, 32);
            lblRed.TextAlign = ContentAlignment.MiddleLeft;
            right.Controls.Add(lblRed, 0, 0);

            lblBlack = new Label();
            lblBlack.Dock = DockStyle.Fill;
            lblBlack.Font = new Font(Fonts.Ui, 10.5f, FontStyle.Bold);
            lblBlack.ForeColor = Color.FromArgb(42, 42, 48);
            lblBlack.TextAlign = ContentAlignment.MiddleLeft;
            right.Controls.Add(lblBlack, 0, 1);

            lblStatus = new Label();
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.Font = new Font(Fonts.Ui, 11f, FontStyle.Bold);
            lblStatus.ForeColor = Color.FromArgb(38, 92, 168);
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Padding = new Padding(0, 6, 0, 6);
            right.Controls.Add(lblStatus, 0, 2);

            Label hdr = new Label();
            hdr.Dock = DockStyle.Fill;
            hdr.Text = "着法记录（中文记谱）";
            hdr.Font = new Font(Fonts.Ui, 9.5f, FontStyle.Bold);
            hdr.ForeColor = Color.FromArgb(110, 100, 88);
            hdr.TextAlign = ContentAlignment.MiddleLeft;
            right.Controls.Add(hdr, 0, 3);

            ListView lv = new ListView();
            lv.Dock = DockStyle.Fill;
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.MultiSelect = false;
            lv.HideSelection = true;
            lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lv.GridLines = false;
            lv.BorderStyle = BorderStyle.FixedSingle;
            lv.BackColor = Color.FromArgb(255, 254, 251);
            lv.Font = new Font(Fonts.Piece, 10.5f);
            lv.Columns.Add("#", 36, HorizontalAlignment.Right);
            lv.Columns.Add("红方", 112, HorizontalAlignment.Left);
            lv.Columns.Add("黑方", 112, HorizontalAlignment.Left);
            lv.Name = "lvMoves";
            lv.Resize += delegate
            {
                int w = lv.ClientSize.Width - 4;
                int c0 = Math.Max(28, w * 14 / 100);
                int rest = Math.Max(50, (w - c0) / 2);
                lv.Columns[0].Width = c0;
                lv.Columns[1].Width = rest;
                lv.Columns[2].Width = rest;
            };
            right.Controls.Add(lv, 0, 4);

            lblTip = new Label();
            lblTip.Dock = DockStyle.Fill;
            lblTip.Font = new Font(Fonts.Ui, 8.5f);
            lblTip.ForeColor = Color.FromArgb(120, 110, 98);
            lblTip.TextAlign = ContentAlignment.TopLeft;
            lblTip.Padding = new Padding(0, 6, 0, 0);
            lblTip.Text = "点自己的棋子：\r\n" +
                          "绿点＝能走的空位\r\n" +
                          "红圈＝能吃的子\r\n" +
                          "走错了按“悔棋”重想\r\n" +
                          "下方数字＝纵线编号";
            right.Controls.Add(lblTip, 0, 5);

            // 底部状态
            Label foot = new Label();
            foot.Dock = DockStyle.Fill;
            foot.TextAlign = ContentAlignment.MiddleLeft;
            foot.Padding = new Padding(14, 0, 0, 0);
            foot.Font = new Font(Fonts.Ui, 9.5f);
            foot.ForeColor = Color.FromArgb(96, 88, 78);
            foot.BackColor = Color.FromArgb(236, 232, 225);
            foot.Name = "foot";
            foot.Text = "准备开始：你执红先行。点击棋盘上的棋子即可走棋。";

            root.Controls.Add(top, 0, 0);
            root.SetColumnSpan(top, 2);
            root.Controls.Add(view, 0, 1);
            root.Controls.Add(right, 1, 1);
            root.Controls.Add(foot, 0, 2);
            root.SetColumnSpan(foot, 2);

            Controls.Add(root);
            _foot = foot;
        }

        Label _foot;

        Label MakeLabel(string text, int width, bool bold)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.MinimumSize = new Size(width, 26);
            l.Height = 26;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Margin = new Padding(0, 6, 6, 0);
            if (bold) l.Font = new Font(Fonts.Ui, 9.5f, FontStyle.Bold);
            return l;
        }

        Button MakeButton(string text, int width)
        {
            Button b = new Button();
            b.Text = text;
            b.Width = width;
            b.Height = 30;
            b.Margin = new Padding(0, 2, 8, 0);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.FromArgb(255, 255, 255);
            b.FlatAppearance.BorderColor = Color.FromArgb(196, 186, 172);
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
            return b;
        }

        // ---------------- 对局流程 ----------------

        public void NewGame()
        {
            StopThinking();
            gameOver = false;
            resultText = "";
            B.SetupStart();
            hist.Clear();
            repCount.Clear();
            halfMoveClock = 0;
            RepInc(B.Hash);
            selected = -1;
            targets.Clear();
            view.Flipped = (playerSide == Pc.Black);
            view.Attach(B);
            view.LastFrom = view.LastTo = -1;
            view.CheckSq = -1;
            RefreshRoles();
            UpdateMoveList();
            SetStatus(B.Side == playerSide ? "新对局开始，轮到你走。" : "新对局开始，电脑先行。", false);
            SetFoot(playerSide == Pc.Red
                ? "你执红先行。点棋子走棋，电脑执黑应战。"
                : "你执黑后行，电脑执红先行。");
            if (!suppressAi && B.Side != playerSide) StartThinking();
        }

        void SwapSide()
        {
            playerSide = 1 - playerSide;
            building = true;
            cbSide.SelectedIndex = (playerSide == Pc.Red) ? 0 : 1;
            building = false;
            NewGame();
        }

        void OnSideChanged()
        {
            if (building) return;
            playerSide = (cbSide.SelectedIndex == 0) ? Pc.Red : Pc.Black;
            NewGame();
        }

        void OnLevelChanged()
        {
            if (building) return;
            levelIdx = cbLevel.SelectedIndex;
            RefreshRoles();
            if (!gameOver && !thinking) UpdateStatus();
        }

        void RefreshRoles()
        {
            string lv = Levels[levelIdx].Name;
            lblRed.Text = "红方：" + (playerSide == Pc.Red ? "你（先行）" : "电脑 · " + lv);
            lblBlack.Text = "黑方：" + (playerSide == Pc.Black ? "你（后行）" : "电脑 · " + lv);
        }

        void OnSquareClick(int sq)
        {
            if (gameOver || thinking || suppressAi) return;
            if (B.Side != playerSide) return;

            int p = B.Sq[sq];
            if (selected >= 0)
            {
                if (targets.Contains(sq))
                {
                    ChessMove m = new ChessMove(selected, sq, B.Sq[sq]);
                    selected = -1;
                    targets.Clear();
                    view.Selected = -1;
                    view.Targets.Clear();
                    ApplyMove(m, true, AfterMove);
                    return;
                }
                if (p != 0 && Pc.SideOf(p) == playerSide)
                {
                    SelectSquare(sq);
                    return;
                }
                selected = -1;
                targets.Clear();
                view.Selected = -1;
                view.Targets = new List<int>();
                view.Invalidate();
                UpdateStatus();
                return;
            }
            if (p != 0 && Pc.SideOf(p) == playerSide) SelectSquare(sq);
        }

        void SelectSquare(int sq)
        {
            selected = sq;
            targets.Clear();
            ChessMove[] buf = new ChessMove[192];
            int n = B.GenerateLegal(buf, false);
            for (int i = 0; i < n; i++) if (buf[i].From == sq) targets.Add(buf[i].To);

            view.Selected = selected;
            view.Targets = targets;
            view.Invalidate();

            if (targets.Count == 0)
                SetStatus(Pc.Glyph(B.Sq[sq]) + " 这一步没有可走的位置（被牵制或走开就会送将）。", true);
            else
                SetStatus("已选中 " + Pc.Glyph(B.Sq[sq]) + "：绿点 " + CountEmpty() + " 个空位，红圈 " +
                          (targets.Count - CountEmpty()) + " 个可吃的子。", false);
        }

        int CountEmpty()
        {
            int c = 0;
            for (int i = 0; i < targets.Count; i++) if (B.Sq[targets[i]] == 0) c++;
            return c;
        }

        void ApplyMove(ChessMove m, bool animate, EventHandler after)
        {
            string nota = Notation.Describe(B, m);
            MoveRecord r = new MoveRecord();
            r.Mv = m;
            r.Nota = nota;
            r.PrevHalfMove = halfMoveClock;
            B.MakeMove(m);
            hist.Add(r);
            RepInc(B.Hash);
            if (m.Captured != 0) halfMoveClock = 0; else halfMoveClock++;

            selected = -1;
            targets.Clear();
            view.Selected = -1;
            view.Targets = new List<int>();
            view.LastFrom = m.From;
            view.LastTo = m.To;
            view.CheckSq = B.InCheck(B.Side) ? B.KingSq[B.Side] : -1;
            UpdateMoveList();

            if (animate) view.AnimateMove(m.From, m.To, after);
            else
            {
                view.Invalidate();
                if (after != null) after(this, EventArgs.Empty);
            }
        }

        void AfterMove(object sender, EventArgs e)
        {
            if (suppressAi) return;

            ChessMove[] buf = new ChessMove[192];
            int n = B.GenerateLegal(buf, false);
            bool chk = B.InCheck(B.Side);

            if (n == 0)
            {
                int loser = B.Side;
                int winner = 1 - loser;
                gameOver = true;
                bool playerWon = (winner == playerSide);
                resultText = playerWon ? "你赢了！" : "电脑获胜";
                view.Banner = playerWon ? "你赢了！" : "电脑获胜";
                view.Invalidate();
                SetFoot(chk
                    ? Pc.SideName(winner) + "将死" + Pc.SideName(loser) + "。" + resultText + "按“新局”再来一盘。"
                    : Pc.SideName(loser) + "无棋可走（困毙）。" + resultText + "按“新局”再来一盘。");
                SetStatus(resultText, !playerWon);
                return;
            }
            if (repCount[B.Hash] >= 3)
            {
                gameOver = true;
                resultText = "和棋";
                view.Banner = "和棋";
                view.Invalidate();
                SetFoot("同一局面出现三次，判和棋。");
                SetStatus("和棋：三次重复局面。", false);
                return;
            }
            if (halfMoveClock >= 120)
            {
                gameOver = true;
                resultText = "和棋";
                view.Banner = "和棋";
                view.Invalidate();
                SetFoot("连续 60 回合没有吃子，判和棋。");
                SetStatus("和棋：长时间没有吃子。", false);
                return;
            }

            if (B.Side != playerSide) StartThinking();
            else
            {
                UpdateStatus();
                if (chk) SetFoot("注意：你被将军了，必须马上解将（走将、垫子或吃掉将军的棋子）。");
            }
        }

        void Undo()
        {
            if (hist.Count == 0)
            {
                SetStatus("还没有走过棋。", false);
                return;
            }
            StopThinking();
            gameOver = false;
            resultText = "";
            view.Banner = null;

            UndoOne();
            while (hist.Count > 0 && B.Side != playerSide) UndoOne();

            selected = -1;
            targets.Clear();
            view.Selected = -1;
            view.Targets = new List<int>();
            view.CheckSq = B.InCheck(B.Side) ? B.KingSq[B.Side] : -1;
            if (hist.Count > 0)
            {
                MoveRecord last = hist[hist.Count - 1];
                view.LastFrom = last.Mv.From;
                view.LastTo = last.Mv.To;
            }
            else
            {
                view.LastFrom = view.LastTo = -1;
            }
            view.Invalidate();
            UpdateMoveList();

            if (!suppressAi && B.Side != playerSide) StartThinking();
            else
            {
                SetStatus("已退回上一步，重新想一想。", false);
                SetFoot("悔棋后轮到你走。");
            }
        }

        void UndoOne()
        {
            MoveRecord r = hist[hist.Count - 1];
            hist.RemoveAt(hist.Count - 1);
            RepDec(B.Hash);
            B.UnmakeMove(r.Mv);
            halfMoveClock = r.PrevHalfMove;
        }

        void RepInc(ulong h)
        {
            int c;
            if (repCount.TryGetValue(h, out c)) repCount[h] = c + 1;
            else repCount[h] = 1;
        }

        void RepDec(ulong h)
        {
            int c;
            if (repCount.TryGetValue(h, out c))
            {
                if (c <= 1) repCount.Remove(h);
                else repCount[h] = c - 1;
            }
        }

        // ---------------- 电脑思考 ----------------

        void StartThinking()
        {
            if (gameOver || suppressAi) return;
            thinking = true;
            SetStatus("电脑思考中…", false);
            SetFoot("电脑正在计算（难度：" + Levels[levelIdx].Name + "）。");
            CancelToken tok = new CancelToken();
            token = tok;
            int gen = ++thinkGen;
            Board snapshot = B.Clone();
            Level lv = Levels[levelIdx];
            int side = B.Side;

            Thread t = new Thread(delegate ()
            {
                ChessMove m = ChessMove.None;
                try { m = engine.Think(snapshot, lv, tok); }
                catch (Exception) { }
                if (gen != thinkGen) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate ()
                    {
                        if (gen != thinkGen) return;
                        thinking = false;
                        worker = null;
                        if (m.IsNone)
                        {
                            gameOver = true;
                            bool playerWon = (side != playerSide);
                            resultText = playerWon ? "你赢了！" : "电脑获胜";
                            view.Banner = resultText;
                            view.Invalidate();
                            SetStatus(resultText, !playerWon);
                            return;
                        }
                        ApplyMove(m, true, AfterMove);
                    });
                }
                catch (Exception) { }
            });
            t.IsBackground = true;
            worker = t;
            t.Start();
        }

        void StopThinking()
        {
            if (token != null) token.Cancelled = true;
            thinkGen++;
            thinking = false;
            Thread w = worker;
            worker = null;
            if (w != null && w.IsAlive)
            {
                try { w.Join(4000); }
                catch (Exception) { }
            }
            token = null;
        }

        // ---------------- 状态显示 ----------------

        void SetStatus(string s, bool warn)
        {
            lblStatus.Text = s;
            lblStatus.ForeColor = warn ? Color.FromArgb(198, 40, 40) : Color.FromArgb(38, 92, 168);
        }

        void SetFoot(string s)
        {
            if (_foot != null) _foot.Text = s;
        }

        void UpdateStatus()
        {
            if (gameOver) return;
            if (B.Side == playerSide)
            {
                string s = "轮到你走（" + Pc.SideName(playerSide) + "）";
                bool chk = B.InCheck(B.Side);
                if (chk) s += " —— 你被将军了！";
                SetStatus(s, chk);
                SetFoot(chk
                    ? "你被将军了：必须走将、垫子或吃掉将军的棋子，否则就是违规。"
                    : "轮到你走。点自己的棋子看可走位置。");
            }
            else SetStatus("电脑思考中…", false);
        }

        void UpdateMoveList()
        {
            ListView lv = (ListView)FindControl(this, "lvMoves");
            if (lv == null) return;
            lv.BeginUpdate();
            lv.Items.Clear();
            int row = 1;
            for (int i = 0; i < hist.Count; i += 2)
            {
                ListViewItem it = new ListViewItem(row.ToString());
                it.SubItems.Add(hist[i].Nota);
                if (i + 1 < hist.Count) it.SubItems.Add(hist[i + 1].Nota);
                else it.SubItems.Add("");
                lv.Items.Add(it);
                row++;
            }
            if (lv.Items.Count > 0) lv.EnsureVisible(lv.Items.Count - 1);
            lv.EndUpdate();
        }

        Control FindControl(Control root, string name)
        {
            if (root.Name == name) return root;
            for (int i = 0; i < root.Controls.Count; i++)
            {
                Control c = FindControl(root.Controls[i], name);
                if (c != null) return c;
            }
            return null;
        }

        // ---------------- 调试/截图支持 ----------------

        public bool ShotMode
        {
            set { suppressAi = value; }
        }

        public void DebugSetSide(int side)
        {
            playerSide = side;
            building = true;
            cbSide.SelectedIndex = (side == Pc.Red) ? 0 : 1;
            building = false;
            NewGame();
        }

        public void DebugSetLevel(int idx)
        {
            levelIdx = idx;
            building = true;
            cbLevel.SelectedIndex = idx;
            building = false;
            RefreshRoles();
        }

        public void DebugPlay(int plies)
        {
            suppressAi = true;
            for (int i = 0; i < plies; i++)
            {
                if (gameOver) break;
                ChessMove m = engine.Think(B, Levels[Math.Min(levelIdx, 3)], new CancelToken());
                if (m.IsNone) break;
                ApplyMove(m, false, AfterMove);
                Application.DoEvents();
            }
            suppressAi = false;
            if (!gameOver)
            {
                view.CheckSq = B.InCheck(B.Side) ? B.KingSq[B.Side] : -1;
                view.Invalidate();
                UpdateStatus();
            }
        }

        public void DebugSelect(int sq)
        {
            if (B.Sq[sq] == 0) return;
            SelectSquare(sq);
            view.Invalidate();
        }

        // 构造一个“走一步就将军”的局面，用于检查将军高亮
        public void DebugSetupCheck()
        {
            Board nb = new Board();
            Array.Clear(nb.Sq, 0, 90);
            nb.Sq[Board.Idx(4, 9)] = Pc.Make(Pc.Red, Pc.King);
            nb.Sq[Board.Idx(4, 0)] = Pc.Make(Pc.Black, Pc.King);
            nb.Sq[Board.Idx(4, 3)] = Pc.Make(Pc.Red, Pc.Pawn);     // 挡住将帅照面
            nb.Sq[Board.Idx(0, 1)] = Pc.Make(Pc.Red, Pc.Rook);
            nb.Sq[Board.Idx(0, 7)] = Pc.Make(Pc.Black, Pc.Rook);
            nb.Sq[Board.Idx(6, 6)] = Pc.Make(Pc.Red, Pc.Horse);
            nb.Sq[Board.Idx(2, 2)] = Pc.Make(Pc.Black, Pc.Cannon);
            nb.Sq[Board.Idx(8, 0)] = Pc.Make(Pc.Black, Pc.Horse);
            nb.KingSq[Pc.Red] = Board.Idx(4, 9);
            nb.KingSq[Pc.Black] = Board.Idx(4, 0);
            nb.Side = Pc.Red;
            nb.Hash = nb.ComputeHash();

            B = nb;
            hist.Clear();
            repCount.Clear();
            RepInc(B.Hash);
            halfMoveClock = 0;
            gameOver = false;
            view.Banner = null;
            view.Flipped = (playerSide == Pc.Black);
            view.Attach(B);
            RefreshRoles();
            UpdateMoveList();

            // 红车平到黑方底线，横向将军
            ChessMove m = new ChessMove(Board.Idx(0, 1), Board.Idx(0, 0), 0);
            ApplyMove(m, false, AfterMove);

            view.CheckSq = B.InCheck(B.Side) ? B.KingSq[B.Side] : -1;
            view.Invalidate();
            SetStatus("红方将军！黑方必须解将。", true);
            SetFoot("将军演示：黑方被将军，必须走将、垫子或吃掉将军的车。");
        }

        public void DebugSetBanner(string text)
        {
            view.Banner = text;
            SetStatus(text, true);
            SetFoot("（截图用：模拟对局结束的提示）");
            gameOver = true;
            view.Invalidate();
        }

        public void DumpLayout(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Form ClientSize=" + ClientSize + " Size=" + Size +
                " AutoScaleDimensions=" + AutoScaleDimensions +
                " CurrentAutoScaleDimensions=" + CurrentAutoScaleDimensions);
            sb.AppendLine("Dpi=" + CreateGraphics().DpiX.ToString("0") + " boardClient=" + view.ClientSize);
            Dump(this, 0, sb);
            System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        // 端到端交互自测：点棋子 -> 动画 -> 电脑思考 -> 电脑落子 -> 悔棋 -> 换边
        public void DebugInteractionTest(string path)
        {
            StringBuilder log = new StringBuilder();
            int fails = 0;
            suppressAi = false;

            for (int round = 0; round < 3; round++)
            {
                if (B.Side != playerSide)
                {
                    log.AppendLine("FAIL 第" + (round + 1) + "回合没轮到玩家");
                    fails++;
                    break;
                }
                ChessMove mv = engine.Think(B.Clone(), Levels[0], new CancelToken());
                OnSquareClick(mv.From);
                int tn = targets.Count;
                OnSquareClick(mv.To);
                int h0 = hist.Count;
                bool aiMoved = WaitFor(delegate { return hist.Count > h0; }, 20000);
                log.AppendLine("回合" + (round + 1) + " 玩家点 " + mv.From + "->" + mv.To +
                    " 提示点=" + tn + " 电脑应着=" + (aiMoved ? "OK" : "超时") +
                    " 总步数=" + hist.Count + " 轮到=" + (B.Side == playerSide ? "玩家" : "电脑"));
                if (!aiMoved) { fails++; break; }
            }

            int before = hist.Count;
            Undo();
            bool okUndo = (hist.Count <= before - 1) && B.Side == playerSide && !thinking;
            log.AppendLine("悔棋 " + before + "->" + hist.Count + " 轮到玩家=" + (B.Side == playerSide) +
                " " + (okUndo ? "OK" : "FAIL"));
            if (!okUndo) fails++;

            SwapSide();
            bool aiFirst = WaitFor(delegate { return hist.Count >= 1; }, 20000);
            bool okSwap = (playerSide == Pc.Black) && view.Flipped;
            log.AppendLine("换边 玩家=" + Pc.SideName(playerSide) + " 棋盘翻转=" + view.Flipped +
                " 电脑先手=" + (aiFirst ? "OK" : "超时") + " 步数=" + hist.Count);
            if (!okSwap || !aiFirst) fails++;

            if (B.Side == playerSide && !gameOver)
            {
                ChessMove mv2 = engine.Think(B.Clone(), Levels[0], new CancelToken());
                OnSquareClick(mv2.From);
                OnSquareClick(mv2.To);
                int h1 = hist.Count;
                bool ai2 = WaitFor(delegate { return hist.Count > h1; }, 20000);
                log.AppendLine("执黑应着=" + (ai2 ? "OK" : "超时") + " 步数=" + hist.Count);
                if (!ai2) fails++;
            }

            NewGame();
            bool okNew = (hist.Count == 0) && (B.Side == Pc.Red) && (B.Side != playerSide || !thinking);
            log.AppendLine("新局 步数=" + hist.Count + " 先行=" + Pc.SideName(B.Side) + " " + (okNew ? "OK" : "FAIL"));
            if (!okNew) fails++;

            log.AppendLine(fails == 0 ? "交互测试：全部通过" : ("交互测试：失败 " + fails + " 项"));
            System.IO.File.WriteAllText(path, log.ToString(), new UTF8Encoding(false));
        }

        bool WaitFor(Func<bool> pred, int ms)
        {
            DateTime t0 = DateTime.Now;
            while ((DateTime.Now - t0).TotalMilliseconds < ms)
            {
                Application.DoEvents();
                if (pred()) return true;
                Thread.Sleep(25);
            }
            Application.DoEvents();
            return pred();
        }

        void Dump(Control c, int depth, StringBuilder sb)
        {
            sb.AppendLine(new string(' ', depth * 2) + c.GetType().Name + " [" + c.Name + "] bounds=" + c.Bounds +
                " font=" + c.Font.SizeInPoints.ToString("0.0") + "pt" +
                (c is Label ? " autosize=" + c.AutoSize + " text=" + ((Label)c).Text.Substring(0, Math.Min(8, ((Label)c).Text.Length)) : ""));
            if (c is ListView)
            {
                ListView lv = (ListView)c;
                for (int i = 0; i < lv.Columns.Count; i++)
                    sb.AppendLine(new string(' ', depth * 2 + 2) + "col" + i + " w=" + lv.Columns[i].Width);
            }
            for (int i = 0; i < c.Controls.Count; i++) Dump(c.Controls[i], depth + 1, sb);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopThinking();
            base.OnFormClosing(e);
        }
    }
}
