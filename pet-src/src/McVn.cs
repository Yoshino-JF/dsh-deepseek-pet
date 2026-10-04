// McVn —— galgame 屏（R2c-1）：AI 拟人角色的对话框 + 选择支
//
// 合规前提（见 DEVLOG 11.1）：**默认立绘是本项目原创的像素小人**（按配色气质区分三位角色），
// 别人画的拟人立绘只能由用户放进 assets/vn/local/（本机自用档），不随插件分发。
// 中文台词走窗口层 TextBlock（像素字体只有 ASCII），由桌宠渲染；本类只负责画面与判定。
using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeepSeekPet
{
    class McVn : IGameScreen
    {
        public const int W = 120, H = 80;
        const int BoxTop = 54;                     // 对话框顶边（像素；v0.0.10 从 56 上移，给两行台词留空间）
        static readonly int Peach = Px.C(0xFFF0A030), Panel = Px.C(0xE6201A28), White = Px.C(0xFFF4F4F8);

        readonly WriteableBitmap bmp;
        readonly int[] buf = new int[W * H];
        readonly Random rnd = new Random();

        public int Logs { get; set; }
        public double CreeperChance { get; set; }
        public bool ShowDiedOverlay { get { return false; } }
        public ImageSource Source { get { return bmp; } }
        /// <summary>用户自备立绘的 id（桌宠去 assets/vn/&lt;id&gt;.png 找）；找到就不再画内置像素小人</summary>
        public string CharacterAsset { get { return cast[who].id; } }
        /// <summary>特殊演出的专属立绘（assets/vn/love_&lt;id&gt; / angry_&lt;id&gt;）；没有则退回普通立绘</summary>
        public string MoodAsset
        {
            get
            {
                if (mood == Mood.Love) return "love_" + cast[who].id;
                if (mood == Mood.Angry) return "angry_" + cast[who].id;
                return "";
            }
        }
        /// <summary>背景图 id（assets/vn/bg_&lt;id&gt;.png|.jpg）；找到就用真背景，否则用程序化渐变</summary>
        public string BackgroundAsset { get { return "bg_" + cast[who].id; } }
        /// <summary>由桌宠设置：真背景已加载，跳过程序化渐变背景</summary>
        public bool HideBuiltinBackground = false;
        /// <summary>由桌宠设置：用户自备立绘已加载，隐藏内置像素小人</summary>
        public bool HideBuiltinCharacter = false;

        // ---- 角色（立绘由桌宠按 id 去 assets/vn/ 找同名 PNG；找不到就用下面这套原创像素小人配色）----
        class CharDef
        {
            public string id, name, line;
            public int hair, hair2, dress, accent, eye;
            public string[] choices;
            public string[] replyGood, replyBad;
            public string loveLine, angryLine;      // 满好感 / 归零时的专属台词
        }
        readonly CharDef[] cast = new CharDef[] {
            new CharDef { id = "chat", name = "CHAT",
                hair = Px.C(0xFFEDEAF7), hair2 = Px.C(0xFFCFC8E8), dress = Px.C(0xFF8E86C8),
                accent = Px.C(0xFFB9A8F0), eye = Px.C(0xFF7A5FD0),
                line = "今天的天气……很适合一起出门呢。",
                choices = new string[] { "约她一起出去玩", "自己呆在家里" },
                replyGood = new string[] { "诶！？真的吗……那、那我马上去换衣服！" },
                replyBad  = new string[] { "唔……好吧，那我在家陪你就是了。" },
                loveLine = "最喜欢你了……这句话，我练习过好多遍。",
                angryLine = "哼，我再也不理你了。（别过脸去）" },
            new CharDef { id = "cloud", name = "CLOUD",
                hair = Px.C(0xFFE8843C), hair2 = Px.C(0xFFC2611F), dress = Px.C(0xFFF6EFE0),
                accent = Px.C(0xFFD9A441), eye = Px.C(0xFF8A5A20),
                line = "这本书我读完了，要不要一起聊聊？",
                choices = new string[] { "认真和她讨论", "假装没听见" },
                replyGood = new string[] { "太好了！我就知道你也会喜欢的。" },
                replyBad  = new string[] { "……喂，我可是看见你走神了哦。" },
                loveLine = "这本书的最后一页，我写的是你的名字。",
                angryLine = "……把书还我。今天的讨论，到此为止。" },
            new CharDef { id = "gem", name = "GEM",
                hair = Px.C(0xFF8E7BE8), hair2 = Px.C(0xFF6A55C8), dress = Px.C(0xFF3A3358),
                accent = Px.C(0xFFF0D060), eye = Px.C(0xFFE060A0),
                line = "看！我把星星摘下来啦，厉害吧？",
                choices = new string[] { "夸她超级厉害", "说那只是贴纸" },
                replyGood = new string[] { "嘿嘿～那这颗就送给你啦！" },
                replyBad  = new string[] { "才、才不是贴纸呢！哼。" },
                loveLine = "整片星空都送给你！还有……我也是。",
                angryLine = "哼！星星不给你了，一颗都不给！" },
        };

        // ---- 脚本状态 ----
        int who;                       // 当前角色
        int stage;                     // 0=说开场白 1=等选择 2=说回复
        string full = "";              // 当前整句
        double typeT;                  // 打字机计时
        double waitT;                  // 自动推进计时
        string[] curChoices = new string[0];
        // 好感度**每位角色各记一份**（用户要求）：切换角色不再共享，各自从 2 起步
        readonly int[] affection = new int[] { 2, 2, 2 };
        double reactionT;              // 反应特效计时
        string reaction = "";
        double t;

        // ---- 特殊演出：满好感 / 好感归零 ----
        enum Mood { Normal, Love, Angry }
        Mood mood = Mood.Normal;
        double moodT;                  // 演出剩余时间
        readonly bool[] loveFired = new bool[3];   // 防止停在 4 / 0 上反复触发（也按角色记）
        readonly bool[] angryFired = new bool[3];

        public McVn()
        {
            bmp = new WriteableBitmap(W, H, 96, 96, PixelFormats.Bgra32, null);
            Reset();
        }

        public void Reset()
        {
            t = 0; who = rnd.Next(cast.Length); stage = 0; StartLine();
            Render();
        }

        public void TriggerCreeper() { /* galgame 屏没有苦力怕；点了也没事发生 */ }

        /// <summary>双击场景区（对话框/选项框以外）：直接切到下一位角色</summary>
        public void DoubleClick() { NextCharacter(); }

        // 她的输入：galgame 里按选项 = 单击左键；换角色 = 双击左键（都体现为左键短促闪一下）
        int clickKind, clickT;          // 2 = 双击，1 = 单击
        public bool KeyD { get { return false; } }
        public bool MouseLeft { get { return clickT > 0; } }
        public int ClickPulse { get { return clickT > 0 ? clickKind : 0; } }

        /// <summary>切到下一位角色并从头开始它的台词（好感度是全局的，保留）</summary>
        public void NextCharacter()
        {
            clickKind = 2; clickT = 8;      // 双击左键（8 帧 ≈ 0.4s）
            who = (who + 1) % cast.Length;
            mood = Mood.Normal; moodT = 0; reactionT = 0; reaction = "";
            // 好感与触发锁都是按角色的，切人不需要重置
            StartLine();
            Render();
        }

        /// <summary>被叫去干活：她吓得冒汗、赶紧糊弄一句「我这就关掉」（关屏动画由桌宠统一演）</summary>
        public void Panic()
        {
            mood = Mood.Normal; moodT = 0;
            reaction = "sweat"; reactionT = 3.0;
            full = "诶诶诶！？你要工作了？那、那我先把它关掉——";
            typeT = 0; waitT = 0; curChoices = new string[0]; stage = 2;
        }

        void StartLine()
        {
            full = cast[who].line;
            typeT = 0; waitT = 0; curChoices = new string[0];
            stage = 0;
            mood = Mood.Normal; moodT = 0;      // 特殊演出只演那一句，换人即收
        }

        /// <summary>桌宠渲染用：当前应显示的台词（打字机进度）</summary>
        public string OverlayText
        {
            get
            {
                int n = (int)(typeT / 0.045);
                if (n >= full.Length) return full;
                return full.Substring(0, Math.Max(0, n));
            }
        }

        /// <summary>桌宠渲染用：当前选项文字（无选项时为空数组）</summary>
        public string[] OverlayChoices { get { return stage == 1 ? curChoices : new string[0]; } }

        /// <summary>点屏幕：选项区域被吃掉；打字未完时点一下直接显示整句</summary>
        public bool Click(double nx, double ny)
        {
            int x = (int)(nx * W), y = (int)(ny * H);
            if (stage == 1)
            {
                // 选项框（含一点余量）：点到就选，交给屏幕自己吃
                for (int i = 0; i < curChoices.Length; i++)
                {
                    int by = ChoiceY(i);
                    if (x >= 8 && x < ChoiceX + ChoiceW + 6 && y >= by - 2 && y < by + ChoiceH + 2)
                    {
                        Pick(i == 0);
                        return true;
                    }
                }
            }
            // 对话框区域：吃掉点击（顺便跳过打字机），但**不换游戏**（用户要求：点对话框不该切游戏）
            if (y >= BoxTop - 4)
            {
                if (typeT / 0.045 < full.Length) typeT = full.Length * 0.045;
                return true;
            }
            // 其余区域（场景 / 立绘）→ 交给桌宠换游戏
            return false;
        }

        const int ChoiceX = 14, ChoiceW = 92, ChoiceH = 12;
        // 从 26 上移到 20：原来第二个框（26+16=42..54）会被下方名牌（y 51 起）压住 3px
        static int ChoiceY(int i) { return 20 + i * 16; }

        void Pick(bool goodChoice)
        {
            clickKind = 1; clickT = 8;      // 单击左键（选了选项）
            bool good = goodChoice;      // 局部即可，原来挂成字段是多余的
            affection[who] = Math.Max(0, Math.Min(4, affection[who] + (good ? 1 : -1)));
            reaction = good ? "sparkle" : (goodChoice ? "blush" : "sweat");
            reactionT = 1.6;
            typeT = 0; waitT = 0; curChoices = new string[0]; stage = 2;

            // 满好感 / 归零：切进特殊演出（各角色一句专属台词 + 满屏效果）
            if (affection[who] >= 4 && !loveFired[who])
            {
                loveFired[who] = true; angryFired[who] = false;
                mood = Mood.Love; moodT = 5.0;
                full = cast[who].loveLine;
                return;
            }
            if (affection[who] <= 0 && !angryFired[who])
            {
                angryFired[who] = true; loveFired[who] = false;
                mood = Mood.Angry; moodT = 5.0;
                full = cast[who].angryLine;
                return;
            }
            if (affection[who] > 0 && affection[who] < 4) { loveFired[who] = false; angryFired[who] = false; }
            full = good ? cast[who].replyGood[0] : cast[who].replyBad[0];
        }

        /// <summary>开发用（--vnmood love|angry）：直接看特殊演出的画面</summary>
        public void ForceMood(string m)
        {
            if (m == "love") { mood = Mood.Love; moodT = 999; affection[who] = 4; }
            else if (m == "angry") { mood = Mood.Angry; moodT = 999; affection[who] = 0; }
            else return;
            full = mood == Mood.Love ? cast[who].loveLine : cast[who].angryLine;
            typeT = 999; waitT = 0; curChoices = new string[0]; stage = 3;   // stage 3 = 停在特殊画面上不推进
            Render();
        }

        public void Tick(double dt)
        {
            t += dt;
            typeT += dt;
            if (clickT > 0) clickT--;
            if (reactionT > 0) reactionT = Math.Max(0, reactionT - dt);
            if (moodT > 0)
            {
                moodT -= dt;
                if (moodT <= 0) { mood = Mood.Normal; waitT = 0; }   // 演出结束，回到常规流程
            }
            bool typed = typeT / 0.045 >= full.Length;

            switch (stage)
            {
                case 3:
                    break;                       // 特殊画面（--vnmood 开发用）：停住不推进
                case 0:
                    if (typed)
                    {
                        waitT += dt;
                        if (waitT > 0.8) { curChoices = cast[who].choices; stage = 1; waitT = 0; }
                    }
                    break;
                case 1:
                    waitT += dt;
                    // 给用户充足时间点（原来 6 秒太短，经常"还没点就被她抢答了"）
                    if (waitT > 20.0) Pick(rnd.Next(2) == 0);
                    break;
                default:
                    if (typed)
                    {
                        waitT += dt;
                        if (waitT > 2.4)
                        {
                            who = (who + 1) % cast.Length;
                            StartLine();
                        }
                    }
                    break;
            }
            Render();
        }

        // ------------------------------------------------------------------ 绘制
        void Render()
        {
            CharDef c = cast[who];
            // 先整屏清成透明！
            // 之前不清缓冲，导致"上一帧画过、这一帧不再画"的东西留在画面上：
            // ① 换角色后看不到真背景（残留的程序化渐变把背景图盖住了）
            // ② 选完选项后选项框不消失（stage 2 不再绘框，旧框像素还留着）
            Array.Clear(buf, 0, buf.Length);
            // 程序化背景（有真背景图时留空，让窗口层的背景图透出来）
            if (!HideBuiltinBackground)
            {
                for (int y = 0; y < H; y++)
                {
                    int col = y < 40
                        ? Px.Lerp(Px.C(0xFF2A1E3A), Px.C(0xFF7A4A6A), y / 40.0)
                        : Px.Lerp(Px.C(0xFF7A4A6A), Px.C(0xFFE8A87C), (y - 40) / 40.0);
                    Px.Fill(buf, W, H, 0, y, W, 1, col);
                }
                for (int i = 0; i < 14; i++)
                {
                    int bx = (int)((i * 37 + t * (6 + i % 3)) % W);
                    int by = 10 + (i * 13) % 34;
                    Px.Put(buf, W, H, bx, by, Px.C(0x50FFF0D0));
                }
            }
            else
            {
                // 真背景上压一层**渐变**暗化（原来是硬边的一条带，看起来像"背景被切了一刀"）
                for (int y = 30; y < H; y++)
                {
                    double k = Math.Min(1.0, (y - 30) / 22.0) * 0.5;
                    for (int x = 0; x < W; x++) buf[y * W + x] = Px.Blend(buf[y * W + x], Px.C(0xFF120A16), k);
                }
            }
            DrawCharacterIfNeeded(c);
            DrawReaction();
            DrawMood();

            // 好感度：画面上只留这一处 UI。
            // 原先左上角还有「厂牌 logo + 角色名」，现在都去掉了：名字对话框上已经有，
            // 而 logo 当初只是"假装在打游戏"的占位 —— 既然游戏画面本身做得出来，就没必要再贴一个厂牌，去掉后画面清爽很多。
            for (int i = 0; i < 4; i++) Heart8(6 + i * 10, 4, i < affection[who] ? Px.C(0xFFE0507A) : Px.C(0x66FFFFFF));

            // 选项框
            if (stage == 1)
            {
                for (int i = 0; i < curChoices.Length; i++)
                {
                    int by = ChoiceY(i);
                    Px.Fill(buf, W, H, ChoiceX - 1, by - 1, ChoiceW + 2, ChoiceH + 2, Peach);
                    Px.Fill(buf, W, H, ChoiceX, by, ChoiceW, ChoiceH, Panel);
                    Px.Fill(buf, W, H, ChoiceX + 2, by + ChoiceH / 2 - 1, 3, 3, Peach);   // ▶
                }
            }

            // 对话框
            Px.Fill(buf, W, H, 3, BoxTop - 1, W - 6, H - BoxTop - 2, Peach);
            Px.Fill(buf, W, H, 4, BoxTop, W - 8, H - BoxTop - 4, Panel);
            // 名牌
            Px.Fill(buf, W, H, 6, BoxTop - 5, 44, 10, Px.C(0xFF3A2A44));
            Px.Fill(buf, W, H, 7, BoxTop - 4, 42, 8, Px.C(0xFF5A3A5E));
            Px.Text(buf, W, H, c.name, 10, BoxTop - 3, White);
            // 打字未完时右下角闪一个 ▼，打完显示 ▶
            if (t % 0.8 < 0.45)
            {
                int ax = W - 12, ay = H - 8;
                Px.Fill(buf, W, H, ax, ay, 5, 5, Peach);
                Px.Fill(buf, W, H, ax + 1, ay + 1, 3, 3, Panel);
            }

            bmp.WritePixels(new System.Windows.Int32Rect(0, 0, W, H), buf, W * 4, 0);
        }

        /// <summary>满好感 / 归零的特殊演出：满屏飘心 + 大红心；或怒气符号 + 红色脉冲</summary>
        void DrawMood()
        {
            if (mood == Mood.Love)
            {
                // 从下往上飘的粉色心（用 t 做位移，不需要额外状态）
                for (int i = 0; i < 7; i++)
                {
                    int hx = 6 + (i * 17 + (int)(t * 9)) % 108;
                    int hy = H - 24 - (int)((t * 15 + i * 11) % 52);
                    int col = (i % 3 == 0) ? Px.C(0xFFFF7AA8) : Px.C(0xFFE0507A);
                    Heart8(hx, hy, col);
                }
                // 头顶大爱心（比心）
                int bx = 92, by = 8;
                Px.Fill(buf, W, H, bx + 2, by, 4, 2, Px.C(0xFFFF9EC4));
                Px.Fill(buf, W, H, bx + 8, by, 4, 2, Px.C(0xFFFF9EC4));
                Px.Fill(buf, W, H, bx, by + 2, 14, 5, Px.C(0xFFFF6FA8));
                Px.Fill(buf, W, H, bx + 2, by + 7, 10, 3, Px.C(0xFFFF6FA8));
                Px.Fill(buf, W, H, bx + 4, by + 10, 6, 2, Px.C(0xFFE8508C));
                Px.Fill(buf, W, H, bx + 6, by + 12, 2, 2, Px.C(0xFFE8508C));
                Px.Fill(buf, W, H, bx + 4, by + 3, 3, 2, Px.C(0xFFFFD9E8));   // 高光
                // 四角星星
                for (int i = 0; i < 5; i++)
                {
                    int sx = 8 + (i * 23 + (int)(t * 6)) % 104, sy = 6 + (i * 17) % 30;
                    Px.Fill(buf, W, H, sx, sy + 1, 3, 1, Px.C(0xFFFFF0A0));
                    Px.Fill(buf, W, H, sx + 1, sy, 1, 3, Px.C(0xFFFFF0A0));
                }
            }
            else if (mood == Mood.Angry)
            {
                // 怒气符号（漫画式十字青筋）画在立绘头顶附近
                int mx = 92, my = 10;
                int red = Px.C(0xFFE03A3A);
                Px.Fill(buf, W, H, mx, my, 3, 9, red);
                Px.Fill(buf, W, H, mx + 5, my, 3, 9, red);
                Px.Fill(buf, W, H, mx + 1, my + 3, 6, 3, red);
                // 两侧冒气
                for (int i = 0; i < 3; i++)
                {
                    int px2 = 78 + i * 16, py2 = 24 - (int)((t * 12 + i * 9) % 16);
                    Px.Fill(buf, W, H, px2, py2, 4, 3, Px.C(0x66FFFFFF));
                }
                // 红色脉冲边框
                double pulse = 0.25 + 0.2 * Math.Sin(t * 6);
                for (int x = 0; x < W; x++)
                {
                    buf[x] = Px.Blend(buf[x], Px.C(0xFFFF3030), pulse);
                    buf[(H - 1) * W + x] = Px.Blend(buf[(H - 1) * W + x], Px.C(0xFFFF3030), pulse);
                }
                for (int y = 0; y < H; y++)
                {
                    buf[y * W] = Px.Blend(buf[y * W], Px.C(0xFFFF3030), pulse);
                    buf[y * W + W - 1] = Px.Blend(buf[y * W + W - 1], Px.C(0xFFFF3030), pulse);
                }
            }
        }

        /// <summary>8×7 的心形（好感度用，比 HUD 里的小心更好认）</summary>
        void Heart8(int x, int y, int color)
        {
            Px.Fill(buf, W, H, x + 1, y, 2, 1, color);
            Px.Fill(buf, W, H, x + 5, y, 2, 1, color);
            Px.Fill(buf, W, H, x, y + 1, 8, 3, color);
            Px.Fill(buf, W, H, x + 1, y + 4, 6, 1, color);
            Px.Fill(buf, W, H, x + 2, y + 5, 4, 1, color);
            Px.Fill(buf, W, H, x + 3, y + 6, 2, 1, color);
        }

        /// <summary>原创像素小人：16×22 头 + 12×14 身，按角色配色与配件区分</summary>
        void DrawCharacterIfNeeded(CharDef c)
        {
            if (HideBuiltinCharacter) return;      // 用户自备立绘时让位
            DrawCharacter(c);
        }

        void DrawCharacter(CharDef c)
        {
            int ox = 72, oy = 16;
            // 头发外轮廓
            Px.Fill(buf, W, H, ox + 2, oy - 1, 16, 3, c.hair);
            Px.Fill(buf, W, H, ox, oy + 2, 20, 12, c.hair);
            // 脸
            Px.Fill(buf, W, H, ox + 4, oy + 2, 12, 9, Px.C(0xFFF7DCC0));
            // 刘海
            Px.Fill(buf, W, H, ox + 4, oy + 2, 12, 2, c.hair);
            Px.Fill(buf, W, H, ox + 2, oy + 2, 3, 7, c.hair);
            Px.Fill(buf, W, H, ox + 15, oy + 2, 3, 7, c.hair);
            // 眼（带高光）
            Px.Fill(buf, W, H, ox + 6, oy + 6, 3, 3, White);
            Px.Fill(buf, W, H, ox + 12, oy + 6, 3, 3, White);
            Px.Fill(buf, W, H, ox + 7, oy + 7, 2, 2, c.eye);
            Px.Fill(buf, W, H, ox + 12, oy + 7, 2, 2, c.eye);
            Px.Put(buf, W, H, ox + 7, oy + 7, White);
            Px.Put(buf, W, H, ox + 12, oy + 7, White);
            // 腮红 + 嘴
            Px.Fill(buf, W, H, ox + 4, oy + 9, 2, 1, Px.C(0x80F09090));
            Px.Fill(buf, W, H, ox + 14, oy + 9, 2, 1, Px.C(0x80F09090));
            Px.Put(buf, W, H, ox + 10, oy + 10, Px.C(0xFFC05060));
            // 身体
            Px.Fill(buf, W, H, ox + 4, oy + 12, 12, 10, c.dress);
            Px.Fill(buf, W, H, ox + 7, oy + 13, 6, 5, Px.C(0xFFF6F2F8));    // 胸前装饰
            Px.Fill(buf, W, H, ox + 9, oy + 14, 2, 2, c.accent);
            // 手臂
            Px.Fill(buf, W, H, ox + 1, oy + 13, 3, 8, c.dress);
            Px.Fill(buf, W, H, ox + 16, oy + 13, 3, 8, c.dress);
            // 配件：云朵（CHAT）/ 书与雏菊（CLOUD）/ 猫耳与星星（GEM）
            switch (c.id)
            {
                case "chat":
                    Px.Fill(buf, W, H, ox - 6, oy + 4, 5, 4, White);
                    Px.Fill(buf, W, H, ox - 5, oy + 2, 4, 3, White);
                    Px.Put(buf, W, H, ox - 4, oy + 5, Px.C(0xFF6A6A8A));
                    Px.Put(buf, W, H, ox - 2, oy + 5, Px.C(0xFF6A6A8A));
                    break;
                case "cloud":
                    Px.Fill(buf, W, H, ox + 3, oy + 16, 14, 8, Px.C(0xFF6B3A1E));
                    Px.Fill(buf, W, H, ox + 4, oy + 17, 12, 6, Px.C(0xFFF0E2C8));
                    Px.Fill(buf, W, H, ox + 9, oy + 17, 2, 6, Px.C(0xFFC8A070));
                    Px.Fill(buf, W, H, ox + 13, oy - 1, 4, 4, Px.C(0xFFF0E0A0));   // 发花
                    break;
                default:
                    Px.Fill(buf, W, H, ox + 2, oy - 4, 4, 5, c.hair);              // 猫耳
                    Px.Fill(buf, W, H, ox + 14, oy - 4, 4, 5, c.hair);
                    Px.Fill(buf, W, H, ox + 3, oy - 3, 2, 3, Px.C(0xFFF0C0D0));
                    Px.Fill(buf, W, H, ox + 15, oy - 3, 2, 3, Px.C(0xFFF0C0D0));
                    Px.Fill(buf, W, H, ox + 17, oy + 4, 3, 3, Px.C(0xFFFFF0A0));   // 星星
                    Px.Fill(buf, W, H, ox - 4, oy + 8, 2, 2, Px.C(0xFFB0F0FF));
                    break;
            }
        }

        void DrawReaction()
        {
            if (reactionT <= 0) return;
            int ox = 72, oy = 16;
            if (reaction == "sparkle")
            {
                for (int i = 0; i < 4; i++)
                {
                    int sx = ox - 8 + (i % 2) * 28, sy = oy - 2 + (i / 2) * 18;
                    Px.Fill(buf, W, H, sx, sy + 1, 3, 1, Px.C(0xFFFFF0A0));
                    Px.Fill(buf, W, H, sx + 1, sy, 1, 3, Px.C(0xFFFFF0A0));
                }
            }
            else if (reaction == "sweat")
            {
                Px.Fill(buf, W, H, ox + 20, oy - 2, 3, 4, Px.C(0xFF9FD8F0));
                Px.Fill(buf, W, H, ox + 21, oy + 2, 1, 2, Px.C(0xFF9FD8F0));
            }
            else
            {
                Px.Fill(buf, W, H, ox + 3, oy + 8, 3, 2, Px.C(0xFFFF9090));
                Px.Fill(buf, W, H, ox + 14, oy + 8, 3, 2, Px.C(0xFFFF9090));
            }
        }
    }
}
