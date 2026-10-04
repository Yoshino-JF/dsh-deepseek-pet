// McScreen —— 纯代码绘制的「Minecraft 第一人称砍树」小屏幕
// 不依赖任何美术素材：所有像素由代码画进 WriteableBitmap，再用最近邻放大成像素风。
// 用法：Tick(dt) 推进时间与状态机，Source 交给 WPF Image 显示。
using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeepSeekPet
{
    class McScreen : IGameScreen
    {
        public const int W = 120;   // 游戏像素宽
        public const int H = 80;    // 游戏像素高

        /// <summary>
        /// 阶段：砍树循环（Chop→Break→Restart）+ 独立的「苦力怕探头」。
        /// v0.0.10 起**删掉了死亡演出**（用户反馈 3D 屏的 You Died 效果不好，死亡只留给 2D 横版屏），
        /// 苦力怕改成"从左边或右边露个脸"：滑进来 → 停一下 → 滑出去，不炸、不掉血、不打断砍树。
        /// </summary>
        public enum Phase { Chop, Break, Restart }

        readonly WriteableBitmap bmp;
        readonly int[] buf = new int[W * H];
        readonly Random rnd = new Random();
        readonly McHud hud = new McHud();
        readonly List<Particle> parts = new List<Particle>();

        class Particle
        {
            public double x, y, vx, vy, life, life0;
            public int color;
        }

        // ---- 运行状态 ----
        Phase phase = Phase.Chop;
        double t;                 // 当前阶段已过时间
        int hits;                 // 本棵树已砍几下
        double swing;             // 挥砍周期计时
        double clouds;            // 云飘动
        double peekT = -1;        // >=0 = 苦力怕正在走进来露脸（秒）
        int peekSide;             // 0 = 从左边来，1 = 从右边来
        double peekX;             // 停在哪个 x（随机位置）
        double peekFrom;          // 从屏外哪个 x 起步
        double peekY = 28;        // 悬在哪个 y（每次随机，范围见 TriggerCreeper）
        double peekCd;            // 冷却：防止用户狂点刷出一堆苦力怕
        public int Logs { get; set; }          // 累计原木
        public double CreeperChance { get; set; }   // 每砍倒一棵树后"探头"的概率（构造时给默认值）

        public McScreen()
        {
            bmp = new WriteableBitmap(W, H, 96, 96, PixelFormats.Bgra32, null);
            CreeperChance = 0.08;
            Reset();
        }

        public ImageSource Source { get { return bmp; } }
        public Phase Current { get { return phase; } }
        public bool ShowDiedOverlay { get { return false; } }     // 3D 屏已无死亡演出
        /// <summary>3D 屏没有会打断循环的演出，恒为"常规状态"</summary>
        public string OverlayText { get { return ""; } }
        public string[] OverlayChoices { get { return emptyChoices; } }
        public string CharacterAsset { get { return ""; } }
        public string MoodAsset { get { return ""; } }
        public string BackgroundAsset { get { return ""; } }
        public bool Click(double nx, double ny) { return false; }
        /// <summary>双击场景区：招一只苦力怕探头（保留原有彩蛋）</summary>
        public void DoubleClick() { TriggerCreeper(); }
        /// <summary>她砍树 = 按住鼠标左键（砍的阶段按住，碎裂/重生阶段松开）</summary>
        public bool KeyD { get { return false; } }
        public bool MouseLeft { get { return phase == Phase.Chop; } }
        public int ClickPulse { get { return 0; } }
        /// <summary>被叫去干活：来一只苦力怕探个头（不受冷却限制——剧情时刻该出来就出来）</summary>
        public void Panic() { peekCd = 0; TriggerCreeper(); }
        static readonly string[] emptyChoices = new string[0];

        public void Reset()
        {
            phase = Phase.Chop; t = 0; hits = 0; swing = 0; parts.Clear(); peekT = -1;
            Render();
        }

        /// <summary>
        /// 让一只苦力怕横着从左边或右边探进来。
        /// 垂直位置随机：左边从「原木」标签下方到草地上方，右边从小地图下沿到草地上方；
        /// 并且带冷却（探头 3.2s + 结束再等 1.0s），**狂点也只会有一只**。
        /// </summary>
        public void TriggerCreeper()
        {
            if (peekT >= 0 || peekCd > 0)
            {
                PetConfig.Log("peek ignored (peekT=" + peekT.ToString("F2") + " cd=" + peekCd.ToString("F2") + ")");
                return;
            }
            peekT = 0;
            peekSide = rnd.Next(2);
            // 横躺着 16 宽：画面里留 ~14px，正好是"探个脸"
            peekX = (peekSide == 0) ? -2.0 : (W - 14.0);
            peekFrom = (peekSide == 0) ? -17.0 : (W + 1.0);
            // 垂直随机：左右统一 26..34（用户要求两侧同值；块底最高 41，离草地线 13px）
            // 注：左上角「原木」标签已右移到 DIP x=44，让开左侧探头这条通道
            int lo = 26;
            int hi = 34;
            peekY = lo + rnd.Next(hi - lo + 1);
            PetConfig.Log("peek side=" + peekSide + " y=" + peekY);
        }

        public void Tick(double dt)
        {
            t += dt;
            clouds += dt * 1.6;
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                Particle p = parts[i];
                p.life -= dt;
                if (p.life <= 0) { parts.RemoveAt(i); continue; }
                p.x += p.vx * dt; p.y += p.vy * dt; p.vy += 42 * dt;
            }
            // 苦力怕探头的计时与冷却：探头 3.2s，结束后再等 2.5s 才允许下一次
            if (peekT >= 0)
            {
                peekT += dt;
                if (peekT > 3.2) { peekT = -1; peekCd = 1.0; }
            }
            else if (peekCd > 0) peekCd = Math.Max(0, peekCd - dt);

            switch (phase)
            {
                case Phase.Chop:
                    swing += dt;
                    if (swing >= 1.1)
                    {
                        swing = 0;
                        hits++;
                        SpawnChips(hits >= 3 ? 26 : 8, hits >= 3);
                        if (hits >= 3) { Logs = (Logs + 1) & 0xFF; phase = Phase.Break; t = 0; }   // 255 → 0 回绕：MC 的 byte 上限梗
                    }
                    break;
                case Phase.Break:
                    if (t >= 0.55) { phase = Phase.Restart; t = 0; hits = 0; }
                    break;
                case Phase.Restart:
                    if (t >= 0.55)
                    {
                        if (rnd.NextDouble() < CreeperChance && peekT < 0) TriggerCreeper();
                        phase = Phase.Chop; t = 0; swing = 0;
                    }
                    break;
            }
            Render();
        }

        void SpawnChips(int n, bool trunk)
        {
            for (int i = 0; i < n; i++)
            {
                Particle p = new Particle();
                p.x = 58 + rnd.NextDouble() * 10;
                p.y = 30 + rnd.NextDouble() * 18;
                p.vx = (rnd.NextDouble() - 0.5) * 70;
                p.vy = -20 - rnd.NextDouble() * 55;
                p.life0 = p.life = 0.7 + rnd.NextDouble() * 0.7;
                p.color = trunk
                    ? (rnd.Next(2) == 0 ? C(0xFF6B4A22) : C(0xFF513415))
                    : (rnd.Next(2) == 0 ? C(0xFF3E8E2A) : C(0xFF57A83A));
                parts.Add(p);
            }
        }

        static int C(uint argb) { return unchecked((int)argb); }

        // ---------------------------------------------------------------- 绘制
        void Fill(int x0, int y0, int w, int h, int color)
        {
            int x1 = x0 + w, y1 = y0 + h;
            if (x0 < 0) x0 = 0; if (y0 < 0) y0 = 0;
            if (x1 > W) x1 = W; if (y1 > H) y1 = H;
            for (int y = y0; y < y1; y++)
            {
                int row = y * W;
                for (int x = x0; x < x1; x++) buf[row + x] = color;
            }
        }

        void Put(int x, int y, int color)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            buf[y * W + x] = color;
        }

        /// <summary>把字符画放大 scale 倍画到 (ox,oy)；'.' 为透明</summary>
        void Sprite(string[] art, int ox, int oy, int scale, Dictionary<char, int> map)
        {
            for (int y = 0; y < art.Length; y++)
            {
                string line = art[y];
                for (int x = 0; x < line.Length; x++)
                {
                    char ch = line[x];
                    if (ch == '.' || ch == ' ') continue;
                    int col;
                    if (!map.TryGetValue(ch, out col)) continue;
                    Fill(ox + x * scale, oy + y * scale, scale, scale, col);
                }
            }
        }


        // 手持铁斧（第一人称右下角）：K=描边 M=金属刀身 H=木柄
        // 说明：这个尺寸下"细长的斧头"很难认，实机最终用的是下面的手臂（DrawArm），
        // 这段保留是为了以后屏幕放大后可以换回来。
        static readonly string[] AXE = new string[] {
            ".KMMMK....",
            "KMMMMMK...",
            "KMMMMMK...",
            "KMMMMMK...",
            ".KMMMK....",
            ".KHHHK....",
            "..KHK.....",
            "..KHK.....",
            "...KHK....",
            "...KHK....",
            "....KHK...",
            "....KHK...",
            ".....KHK..",
            ".....KHK..",
        };

        /// <summary>
        /// 第一人称手臂（MC 空手视角）：袖口 + 小臂 + 拳头，从右下角斜着伸出来。
        /// 默认关闭（HeldItem=false）：120×80 下认不出来，见 Render 里的说明。
        /// </summary>
        public bool HeldItem = false;

        void DrawHeldItem(double s)
        {
            int ax = 84, ay = 36;
            if (s < 0.55) { ax = 84 + (int)(s * 5); ay = 36 - (int)(s * 12); }
            else if (s < 0.85) { double k = (s - 0.55) / 0.3; ax = 87 - (int)(k * 22); ay = 29 + (int)(k * 26); }
            else { double k = (s - 0.85) / 0.25; ax = 65 + (int)(k * 19); ay = 55 - (int)(k * 19); }
            Fill(ax + 22, ay, 10, 10, C(0xFF2B3A66));        // 袖口
            Fill(ax + 16, ay + 6, 10, 10, C(0xFF2B3A66));
            Fill(ax + 11, ay + 11, 9, 9, C(0xFFF0C9A0));     // 小臂
            Fill(ax + 6, ay + 16, 9, 9, C(0xFFF0C9A0));
            Fill(ax + 1, ay + 21, 9, 9, C(0xFFF7D6B0));      // 拳头
            Fill(ax + 2, ay + 26, 6, 4, C(0xFFE8BE93));      // 指节暗部
        }

        void Render()
        {
            // 天空
            for (int y = 0; y < 46; y++)
            {
                int c = Lerp(C(0xFF6FA8FF), C(0xFFB4DCFF), y / 46.0);
                Fill(0, y, W, 1, c);
            }
            // 太阳（MC 的太阳是方的）
            Fill(92, 30, 10, 10, C(0xFFFFF4B0));
            Fill(94, 32, 6, 6, C(0xFFFFFBDA));
            // 云（慢速横移，出屏后循环）
            double cxo = clouds % (W + 40);
            Cloud((int)(cxo - 40), 8);
            Cloud((int)((cxo + 70) % (W + 40) - 40), 20);

            // 地面
            Fill(0, 46, W, H - 46, C(0xFF7A5230));
            Fill(0, 46, W, 4, C(0xFF57A83A));
            for (int x = 0; x < W; x++)            // 草皮参差的边缘
            {
                if (((x * 7) % 5) == 0) Put(x, 50, C(0xFF57A83A));
                if (((x * 5) % 7) == 0) Put(x, 51, C(0xFF4C9633));
            }
            for (int i = 0; i < 90; i++)           // 泥土里的石粒
            {
                int gx = (i * 37) % W, gy = 52 + (i * 13) % (H - 54);
                Put(gx, gy, C(0xFF6B4A22));
            }

            // 树（被砍倒的阶段不画）
            bool treeAlive = phase != Phase.Break;
            if (phase == Phase.Restart && t < 0.25) treeAlive = false;
            if (treeAlive)
            {
                // 树冠：方块状叶簇（MC 风格：2×2 色块拼出不规则轮廓 + 随机空洞透出天空）
                for (int y = 0; y < 22; y += 2)
                {
                    for (int x = 42; x < 78; x += 2)
                    {
                        int d = Math.Abs(x - 60) + Math.Abs(y - 9) * 2;
                        if (d > 32) continue;
                        if (((x * 13 + y * 7) % 19) == 0) continue;
                        int c = ((x / 2 + y / 2) % 3 == 0) ? C(0xFF2F7A22)
                              : (((x + y) % 4 == 0) ? C(0xFF63B93F) : C(0xFF46A02C));
                        Fill(x, y, 2, 2, c);
                    }
                }
                // 树干：竖纹树皮 + 左侧高光（MC 原木侧面的感觉）
                Fill(55, 18, 10, 30, C(0xFF6B4A22));
                for (int x = 56; x < 65; x += 3) Fill(x, 18, 1, 30, C(0xFF53380F));
                Fill(55, 18, 2, 30, C(0xFF7E5A2C));
                Fill(55, 18, 10, 2, C(0xFF8A6432));
                // 裂纹（每砍一下更明显）
                int cracks = phase == Phase.Chop ? hits : 0;
                for (int i = 0; i < cracks * 3; i++)
                {
                    int cy = 24 + (i * 7) % 20;
                    Fill(56 + (i % 3) * 2, cy, 2 + (i % 2), 1, C(0xFF2A1A0A));
                    if (i % 3 == 0) Fill(57, cy, 1, 3, C(0xFF2A1A0A));
                }
            }

            // 远景雾（地平线附近压一层淡蓝白，制造纵深）
            for (int y = 36; y < 52; y++)
            {
                double k = y < 46 ? (y - 36) / 10.0 * 0.35 : (1 - (y - 46) / 6.0) * 0.35;
                if (k <= 0) continue;
                for (int x = 0; x < W; x++) buf[y * W + x] = Blend(buf[y * W + x], C(0xFFCFE4FF), k);
            }

            // 粒子
            for (int i = 0; i < parts.Count; i++)
            {
                Particle p = parts[i];
                double a = p.life / p.life0;
                if (a < 0.35) continue;   // 快消失的粒子不画（省事，不用做混合）
                Fill((int)p.x, (int)p.y, 2, 2, p.color);
            }

            // 说明：第一人称"手持物"试过两版（斜举铁斧 / 空手手臂），在 120×80 这个尺寸下
            // 都只能糊成一坨灰块 —— 最后决定不画手持物：HUD + 方块世界已经足够让人认出是 MC，
            // 砍树的反馈交给树干裂纹 + 飞散木屑 + 树倒。保留 DrawHeldItem 以便将来放大屏幕时启用。
            if (HeldItem) DrawHeldItem(phase == Phase.Chop ? swing : 0.5);

            // 苦力怕「横着躺」从屏幕边探进来（用户草图：一个横着的方块 + 经典脸），停一下再缩回去
            // v0.0.13：侧身四足那版被用户说"变成乌龟了"，改成横躺的方块
            if (peekT >= 0)
            {
                double inT = 0.9, hold = 1.4, outT = 0.9;      // 共 3.2s
                double k;
                if (peekT < inT) k = peekT / inT;
                else if (peekT < inT + hold) k = 1.0;
                else k = Math.Max(0, 1.0 - (peekT - inT - hold) / outT);
                k = k * k * (3 - 2 * k);                                  // smoothstep
                int cx = (int)Math.Round(peekFrom + (peekX - peekFrom) * k);
                Px.CreeperLying(buf, W, H, cx, (int)peekY, (peekSide == 0) ? 1 : -1);   // 横着露个脸（高度随机）
            }

            // HUD（与横版屏共用 McHud）
            hud.Draw(buf, t, 7, Logs, true);

            bmp.WritePixels(new System.Windows.Int32Rect(0, 0, W, H), buf, W * 4, 0);
        }

        /// <summary>把当前画面存成 PNG（开发用：--mcdump 直接看小屏幕，不必截屏裁图）</summary>
        public void SavePng(string path)
        {
            Render();
            System.Windows.Media.Imaging.PngBitmapEncoder enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (System.IO.FileStream fs = System.IO.File.Create(path)) enc.Save(fs);
        }

        /// <summary>取一帧像素快照（供分镜表用）</summary>
        public int[] Snapshot()
        {
            Render();
            int[] copy = new int[buf.Length];
            Array.Copy(buf, copy, buf.Length);
            return copy;
        }

        /// <summary>把若干帧拼成一张分镜表 PNG（cols 列），开发/验收用</summary>
        public static void SaveSheet(System.Collections.Generic.List<int[]> frames, int cols, string path)
        {
            int rows = (frames.Count + cols - 1) / cols;
            int BW = W * cols, BH = H * rows;
            int[] big = new int[BW * BH];
            for (int i = 0; i < frames.Count; i++)
            {
                int cx = (i % cols) * W, cy = (i / cols) * H;
                for (int y = 0; y < H; y++) Array.Copy(frames[i], y * W, big, (cy + y) * BW + cx, W);
            }
            WriteableBitmap b = new WriteableBitmap(BW, BH, 96, 96, PixelFormats.Bgra32, null);
            b.WritePixels(new System.Windows.Int32Rect(0, 0, BW, BH), big, BW * 4, 0);
            System.Windows.Media.Imaging.PngBitmapEncoder enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(b));
            using (System.IO.FileStream fs = System.IO.File.Create(path)) enc.Save(fs);
        }

        void Cloud(int x, int y)
        {
            Fill(x, y, 18, 4, C(0xFFFFFFFF));
            Fill(x + 4, y - 3, 10, 3, C(0xFFFFFFFF));
            Fill(x + 2, y + 4, 13, 2, C(0xF0FFFFFF));
        }

        static int Lerp(int a, int b, double k)
        {
            int aa = (a >> 24) & 0xFF, ar = (a >> 16) & 0xFF, ag = (a >> 8) & 0xFF, ab = a & 0xFF;
            int ba = (b >> 24) & 0xFF, br = (b >> 16) & 0xFF, bg = (b >> 8) & 0xFF, bb = b & 0xFF;
            int ra = (int)(aa + (ba - aa) * k), rr = (int)(ar + (br - ar) * k);
            int rg = (int)(ag + (bg - ag) * k), rb = (int)(ab + (bb - ab) * k);
            return (ra << 24) | (rr << 16) | (rg << 8) | rb;
        }

        static int Blend(int under, int over, double k)
        {
            int ua = (under >> 24) & 0xFF, ur = (under >> 16) & 0xFF, ug = (under >> 8) & 0xFF, ub = under & 0xFF;
            int oa = (over >> 24) & 0xFF, orr = (over >> 16) & 0xFF, og = (over >> 8) & 0xFF, ob = over & 0xFF;
            int r = (int)(ur + (orr - ur) * k), g = (int)(ug + (og - ug) * k), bl = (int)(ub + (ob - ub) * k);
            int a = (int)(ua + (oa - ua) * k);
            return (a << 24) | (r << 16) | (g << 8) | bl;
        }
    }
}
