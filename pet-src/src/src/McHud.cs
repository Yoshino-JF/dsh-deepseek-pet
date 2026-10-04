// McHud —— 两块游戏屏共用的 Minecraft HUD
// （F3 调试文字 / 准星 / 九宫热键栏 / 血量·饥饿·经验 / 右上小地图）
// 所有绘制都写进调用方传进来的像素缓冲（0xAARRGGBB，尺寸固定 120×80）。
using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeepSeekPet
{
    /// <summary>游戏屏的统一接口：伪 3D 与 2D 横版都实现它，桌宠只认这个。</summary>
    interface IGameScreen
    {
        ImageSource Source { get; }
        void Tick(double dt);
        void Reset();
        void TriggerCreeper();
        bool ShowDiedOverlay { get; }
        int Logs { get; }
        double CreeperChance { get; set; }
        /// <summary>需要用窗口层文字渲染的叠加内容（例如 galgame 的对话框中文台词）；无则返回空串</summary>
        string OverlayText { get; }
        /// <summary>窗口层选项文字（galgame 的选择支）；无则空数组</summary>
        string[] OverlayChoices { get; }
        /// <summary>用户自备立绘的 id（桌宠去 assets/vn/&lt;id&gt;.png 找）；空串则用屏幕自带的原创像素小人</summary>
        string CharacterAsset { get; }
        /// <summary>特殊演出专属立绘的 id（如 love_cloud）；空串或文件不存在时退回 CharacterAsset</summary>
        string MoodAsset { get; }
        /// <summary>背景图 id（桌宠去 assets/vn/&lt;id&gt;.png|.jpg 找）；空串则用屏幕自带的程序化背景</summary>
        string BackgroundAsset { get; }
        /// <summary>点屏幕：屏幕自己处理则返回 true（例如 galgame 选选项），否则由桌宠决定换游戏</summary>
        bool Click(double nx, double ny);
        /// <summary>双击屏幕的「场景区」（对话框/选项框以外）：各屏自己的彩蛋——MC 招苦力怕、galgame 切下一位角色</summary>
        void DoubleClick();
        /// <summary>
        /// **她**此刻正在按的键（不是读用户的输入！）——用于桌面键盘/鼠标的按键高亮，
        /// 让人看出"是她在操作电脑玩游戏"。各屏按自己的语义实现：
        /// 3D 砍树 = 按住鼠标左键；2D 移动 = 按住 D；2D 射箭/打怪 = 按住鼠标左键；galgame 选项/换人 = 单击/双击左键。
        /// </summary>
        bool KeyD { get; }
        bool MouseLeft { get; }
        /// <summary>离散的鼠标点击计数（galgame 选项=1、换角色=2），用于让鼠标"闪一下"；非点击帧返回 0</summary>
        int ClickPulse { get; }
        /// <summary>被叫去干活：各屏演自己的"惊一下"（收尾的关屏动画由桌宠统一负责）</summary>
        void Panic();
    }

    /// <summary>像素缓冲的小工具（两块屏共用）</summary>
    static class Px
    {
        // ---- 3×5 迷你字体（各屏共用：F3 文字 / STEAM / XBOX / DOWNLOADING…）----
        public const string FONT_CHARS = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ:.-+/% ";
        static readonly string[] FONT_BITS = new string[] {
            "111101101101111","010110010010111","111001111100111","111001111001111","101101111001001",
            "111100111001111","111100111101111","111001001001001","111101111101111","111101111001111",
            "111101111101101","110101110101110","111100100100111","110101101101110","111100111100111",
            "111100111100100","111100101101111","101101111101101","111010010010111","001001001101111",
            "101101110101101","100100100100111","101111111101101","110101101101101","111101101101111",
            "111101111100100","111101101111001","111101111110101","111100111001111","111010010010010",
            "101101101101111","101101101101010","101101111111101","101101010101101","101101111010010",
            "111001010100111","000010000010000","000000000000010","000000111000000","000010111010000",
            "001001010100100","000000000000000","000000000000000",
        };

        public static int TextWidth(string s) { return s.Length * 4; }

        /// <summary>画一行 3×5 文字（带 1px 阴影）</summary>
        public static void Text(int[] buf, int W, int H, string s, int x, int y, int color)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char ch = char.ToUpperInvariant(s[i]);
                int idx = FONT_CHARS.IndexOf(ch);
                if (idx < 0) { x += 4; continue; }
                string bits = FONT_BITS[idx];
                for (int gy = 0; gy < 5; gy++)
                    for (int gx = 0; gx < 3; gx++)
                        if (bits[gy * 3 + gx] == '1')
                        {
                            Put(buf, W, H, x + gx + 1, y + gy + 1, C(0xC0000000));
                            Put(buf, W, H, x + gx, y + gy, color);
                        }
                x += 4;
            }
        }

        public static int C(uint argb) { return unchecked((int)argb); }

        /// <summary>
        /// MC 苦力怕「**横着躺**」探进画面（按用户草图）：16 宽 × 8 高 —— 头是一整块正方形（带经典脸）、
        /// 身体横着拖在后面。facing = 1 表示从右边进来（头在右），-1 从左边进来（头在左）。
        /// 上一版我画成"侧身四足剪影"，被用户吐槽"变成乌龟了"。
        /// </summary>
        public static void CreeperLying(int[] buf, int W, int H, int x, int y, int facing)
        {
            int Hd = C(0xFF57B33F), Bo = C(0xFF4A9E36), Dk = C(0xFF3F8A2E), K = C(0xFF111111);
            int hx = facing > 0 ? x + 8 : x;                     // 头在进入方向那一端
            Fill(buf, W, H, hx, y, 8, 8, Hd);
            // 脸**跟着一起躺下**（用户："那几个黑色的眼睛嘴巴也要躺下来，不然更诡异"）：
            // 8×6 的正视脸旋转 90° 后正好塞进 8×8 的头方块里。
            string[] face = new string[] {
                ".KK..KK.", ".KK..KK.", "...KK...", "..KKKK..", "..KKKK..", "..K..K.." };
            for (int r = 0; r < face.Length; r++)
                for (int c = 0; c < 8; c++)
                {
                    if (face[r][c] != 'K') continue;
                    int fx, fy;
                    // 旋转后整张脸落在头方块的**正中间**（6 列内容放进 8 列宽 → 左右各留 1px）
                    if (facing > 0) { fx = hx + 6 - r; fy = y + c; }          // 头朝右 → 脸顺时针躺
                    else { fx = hx + 1 + r; fy = y + (7 - c); }               // 头朝左 → 脸逆时针躺
                    Put(buf, W, H, fx, fy, K);
                }
            int bx = facing > 0 ? x : x + 8;                      // 身体横拖在后面（上下各留 1px）
            Fill(buf, W, H, bx, y + 1, 8, 6, Bo);
            Fill(buf, W, H, bx + (facing > 0 ? 7 : 0), y + 1, 1, 6, Dk);   // 头身接缝
            Fill(buf, W, H, bx + 2, y + 3, 1, 1, Dk);                      // 身上两点斑纹
            Fill(buf, W, H, bx + 5, y + 4, 1, 1, Dk);
        }

        /// <summary>
        /// MC 苦力怕（**侧身/横着**，用于 3D 屏从屏幕边走进来）：12 宽 × 14 高，groundY = 脚底行。
        /// facing = 1 朝右走、-1 朝左走（头在行进方向那一侧）。
        /// 正视那版（Creeper）留给 2D 横版屏，与僵尸/骷髅的朝向习惯一致。
        /// </summary>
        public static void CreeperSide(int[] buf, int W, int H, int x, int groundY, int facing, int bob)
        {
            int Hd = C(0xFF57B33F), Bo = C(0xFF4A9E36), Lg = C(0xFF357A28), K = C(0xFF111111);
            int top = groundY - 14 - bob;
            int hx = facing > 0 ? x + 6 : x;                    // 头在前进方向那一侧
            Fill(buf, W, H, hx, top, 6, 6, Hd);
            Put(buf, W, H, hx + (facing > 0 ? 3 : 2), top + 2, K);
            Put(buf, W, H, hx + (facing > 0 ? 3 : 2), top + 3, K);
            int bx = facing > 0 ? x : x + 5;                    // 身体在头后面
            Fill(buf, W, H, bx, top + 5, 7, 6, Bo);
            Fill(buf, W, H, bx, top + 5, 7, 1, C(0xFF3F8A2E));   // 脖子暗线
            for (int i = 0; i < 3; i++) Fill(buf, W, H, x + 1 + i * 4, groundY - 3, 2, 3, Lg);
        }

        /// <summary>
        /// MC 苦力怕（正视、长方形、脚踩地）：8 宽 × 21 高（头 8 + 身 10 + 腿 3）。
        /// 两块 MC 屏共用这一份画法 —— 之前的字符画版本四角是空的，看起来"圆"，被用户指出来了。
        /// groundY = 脚底所在的那一行；bob 传 0/1 做走路起伏。
        /// </summary>
        public static void Creeper(int[] buf, int W, int H, int x, int groundY, int bob)
        {
            int HEAD = C(0xFF57B33F), BODY = C(0xFF4A9E36), LEG = C(0xFF357A28), K = C(0xFF111111);
            int top = groundY - 21 - bob;
            Fill(buf, W, H, x, top, 8, 8, HEAD);                       // 头
            string[] face = new string[] {                             // 经典脸（6 行，从第 1 行开始）
                ".KK..KK.", ".KK..KK.", "...KK...", "..KKKK..", "..KKKK..", "..K..K.." };
            for (int r = 0; r < face.Length; r++)
                for (int c = 0; c < 8; c++)
                    if (face[r][c] == 'K') Put(buf, W, H, x + c, top + 1 + r, K);
            Fill(buf, W, H, x, top + 8, 8, 8, BODY);                    // 身体（上半 8 宽）
            Fill(buf, W, H, x + 1, top + 16, 6, 2, BODY);               // 身体（下摆收一点）
            Fill(buf, W, H, x, top + 8, 8, 1, C(0xFF3F8A2E));          // 脖子处的暗线
            Fill(buf, W, H, x, groundY - 3, 2, 3, LEG);                 // 腿 ×3
            Fill(buf, W, H, x + 3, groundY - 3, 2, 3, LEG);
            Fill(buf, W, H, x + 6, groundY - 3, 2, 3, LEG);
        }

        public static void Fill(int[] buf, int W, int H, int x0, int y0, int w, int h, int color)
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

        public static void Put(int[] buf, int W, int H, int x, int y, int color)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            buf[y * W + x] = color;
        }

        public static int Blend(int under, int over, double k)
        {
            int ua = (under >> 24) & 0xFF, ur = (under >> 16) & 0xFF, ug = (under >> 8) & 0xFF, ub = under & 0xFF;
            int oa = (over >> 24) & 0xFF, orr = (over >> 16) & 0xFF, og = (over >> 8) & 0xFF, ob = over & 0xFF;
            int r = (int)(ur + (orr - ur) * k), g = (int)(ug + (og - ug) * k), bl = (int)(ub + (ob - ub) * k);
            int a = (int)(ua + (oa - ua) * k);
            return (a << 24) | (r << 16) | (g << 8) | bl;
        }

        public static int Lerp(int a, int b, double k)
        {
            int aa = (a >> 24) & 0xFF, ar = (a >> 16) & 0xFF, ag = (a >> 8) & 0xFF, ab = a & 0xFF;
            int ba = (b >> 24) & 0xFF, br = (b >> 16) & 0xFF, bg = (b >> 8) & 0xFF, bb = b & 0xFF;
            int ra = (int)(aa + (ba - aa) * k), rr = (int)(ar + (br - ar) * k);
            int rg = (int)(ag + (bg - ag) * k), rb = (int)(ab + (bb - ab) * k);
            return (ra << 24) | (rr << 16) | (rg << 8) | rb;
        }

        /// <summary>把字符画放大 scale 倍画到 (ox,oy)；'.' 与空格为透明</summary>
        public static void Sprite(int[] buf, int W, int H, string[] art, int ox, int oy, int scale, Dictionary<char, int> map)
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
                    Fill(buf, W, H, ox + x * scale, oy + y * scale, scale, scale, col);
                }
            }
        }
    }

    class McHud
    {
        public const int W = 120, H = 80;

        int fps = 60;
        double fpsAt;
        readonly Random rnd = new Random();

        // ---- 3×5 迷你字体已上移到 Px（各屏共用）----

        /// <summary>画整套 HUD；hearts = 当前血量（0–7）；crosshair = 要不要画准星（第一人称才需要）</summary>
        public void Draw(int[] buf, double t, int hearts, int logs, bool crosshair)
        {
            if (t - fpsAt > 2.0 || fpsAt == 0) { fpsAt = t; fps = 58 + rnd.Next(5); }

            // 准星（只有第一人称视角才合理；2D 横版屏不画）
            if (crosshair)
            {
                Px.Fill(buf, W, H, W / 2 - 4, 34, 9, 1, Px.C(0xB0000000));
                Px.Fill(buf, W, H, W / 2, 30, 1, 9, Px.C(0xB0000000));
                Px.Fill(buf, W, H, W / 2 - 3, 34, 7, 1, Px.C(0xE8FFFFFF));
                Px.Fill(buf, W, H, W / 2, 31, 1, 7, Px.C(0xE8FFFFFF));
            }

            // F3 的 XYZ 三行：X = 原木数量（255 回绕，老玩家一看就懂这是 byte 上限）、Y/Z = 当前时间
            // 这样就不用再占一块屏幕空间单独显示原木了（用户的主意）
            int hh = DateTime.Now.Hour, mm = DateTime.Now.Minute;
            TextLine(buf, fps + " FPS", 2, 2, Px.C(0xFFFFFFFF));
            TextLine(buf, "XYZ " + logs + " " + hh.ToString("00") + " " + mm.ToString("00"), 2, 8, Px.C(0xFFFFFFFF));
            TextLine(buf, "BIOME FOREST", 2, 14, Px.C(0xFFB8FFB8));

            Minimap(buf);
            Hotbar(buf);
            HealthHunger(buf, hearts);
        }

        void Minimap(int[] buf)
        {
            int mx = W - 34, my = 2, mw = 32, mh = 22;
            Px.Fill(buf, W, H, mx - 1, my - 1, mw + 2, mh + 2, Px.C(0xFF0A0A0A));
            Px.Fill(buf, W, H, mx, my, mw, mh, Px.C(0xFF17321B));
            Px.Fill(buf, W, H, mx + 2, my + 3, 9, 7, Px.C(0xFF2F7A22));
            Px.Fill(buf, W, H, mx + 13, my + 8, 11, 8, Px.C(0xFF2F7A22));
            Px.Fill(buf, W, H, mx + 5, my + 12, 8, 7, Px.C(0xFF3B6EA8));
            Px.Fill(buf, W, H, mx + 21, my + 2, 7, 5, Px.C(0xFF6B4A22));
            Px.Fill(buf, W, H, mx + 16, my + 11, 2, 2, Px.C(0xFFFFFFFF));
            Px.Put(buf, W, H, mx + 7, my + 6, Px.C(0xFFFFD24A));
            Px.Put(buf, W, H, mx + 25, my + 15, Px.C(0xFFFFD24A));
        }

        void Hotbar(int[] buf)
        {
            int slots = 9, sw = 12, sh = 12;
            int bx = (W - slots * sw) / 2, by = H - sh - 1;
            Px.Fill(buf, W, H, bx - 1, by - 1, slots * sw + 2, sh + 2, Px.C(0xFF1A1A1A));
            for (int i = 0; i < slots; i++)
            {
                int x = bx + i * sw;
                bool sel = (i == 0);
                Px.Fill(buf, W, H, x, by, sw - 1, sh - 1, Px.C(0xFF8B8B8B));
                Px.Fill(buf, W, H, x + 1, by + 1, sw - 3, sh - 3, Px.C(0xFF5A5A5A));
                if (sel)
                {
                    Px.Fill(buf, W, H, x - 1, by - 1, sw + 1, 1, Px.C(0xFFFFFFFF));
                    Px.Fill(buf, W, H, x - 1, by + sh - 1, sw + 1, 1, Px.C(0xFFFFFFFF));
                    Px.Fill(buf, W, H, x - 1, by, 1, sh - 1, Px.C(0xFFFFFFFF));
                    Px.Fill(buf, W, H, x + sw - 1, by, 1, sh - 1, Px.C(0xFFFFFFFF));
                }
                ItemIcon(buf, i, x + 2, by + 2);
            }
        }

        void ItemIcon(int[] buf, int slot, int x, int y)
        {
            switch (slot)
            {
                case 0:
                    Px.Fill(buf, W, H, x + 1, y + 1, 6, 2, Px.C(0xFFB0B0B0));
                    Px.Fill(buf, W, H, x + 3, y + 3, 2, 5, Px.C(0xFF8B5A2B));
                    break;
                case 1:
                    Px.Fill(buf, W, H, x + 3, y + 1, 4, 4, Px.C(0xFFB0B0B0));
                    Px.Fill(buf, W, H, x + 2, y + 4, 2, 4, Px.C(0xFF8B5A2B));
                    break;
                case 2:
                    Px.Fill(buf, W, H, x + 3, y + 1, 3, 3, Px.C(0xFFC8C8C8));
                    Px.Fill(buf, W, H, x + 3, y + 4, 2, 4, Px.C(0xFF8B5A2B));
                    break;
                case 3:
                    Px.Fill(buf, W, H, x + 3, y, 2, 5, Px.C(0xFFD8D8D8));
                    Px.Fill(buf, W, H, x + 2, y + 5, 4, 1, Px.C(0xFF8B5A2B));
                    Px.Fill(buf, W, H, x + 3, y + 6, 2, 2, Px.C(0xFF8B5A2B));
                    break;
                case 4:
                    Px.Fill(buf, W, H, x, y, 8, 8, Px.C(0xFF7A5230));
                    Px.Put(buf, W, H, x + 1, y + 2, Px.C(0xFF5E3D22));
                    Px.Put(buf, W, H, x + 5, y + 1, Px.C(0xFF5E3D22));
                    Px.Put(buf, W, H, x + 3, y + 5, Px.C(0xFF5E3D22));
                    Px.Put(buf, W, H, x + 6, y + 6, Px.C(0xFF8F6338));
                    break;
                case 5:
                    Px.Fill(buf, W, H, x, y, 8, 8, Px.C(0xFF6B4A22));
                    Px.Fill(buf, W, H, x + 2, y, 1, 8, Px.C(0xFF53380F));
                    Px.Fill(buf, W, H, x + 5, y, 1, 8, Px.C(0xFF7E5A2C));
                    break;
                case 6:
                    Px.Fill(buf, W, H, x, y, 8, 8, Px.C(0xFF8A6432));
                    Px.Fill(buf, W, H, x, y + 3, 8, 1, Px.C(0xFF53380F));
                    Px.Fill(buf, W, H, x + 3, y, 1, 3, Px.C(0xFF53380F));
                    Px.Fill(buf, W, H, x + 1, y + 5, 6, 1, Px.C(0xFF53380F));
                    break;
                case 7:
                    Px.Fill(buf, W, H, x + 3, y + 3, 2, 5, Px.C(0xFF8B5A2B));
                    Px.Fill(buf, W, H, x + 3, y + 1, 2, 2, Px.C(0xFFFFD24A));
                    Px.Put(buf, W, H, x + 3, y, Px.C(0xFFFFF0A0));
                    Px.Put(buf, W, H, x + 4, y, Px.C(0xFFFFF0A0));
                    break;
                default:
                    Px.Fill(buf, W, H, x + 2, y + 1, 4, 4, Px.C(0xFF5FE0D8));
                    Px.Fill(buf, W, H, x + 3, y + 5, 2, 2, Px.C(0xFF3FBDB5));
                    Px.Put(buf, W, H, x + 3, y + 2, Px.C(0xFFBFF8F4));
                    break;
            }
        }

        void HealthHunger(int[] buf, int hearts)
        {
            int y = H - 27;
            for (int i = 0; i < 7; i++)
            {
                int color = i < hearts ? Px.C(0xFFE02B2B) : Px.C(0xFF4A2020);   // 空心血槽
                Heart(buf, 6 + i * 7, y, color);
            }
            for (int i = 0; i < 7; i++) Drumstick(buf, W - 13 - i * 7, y);
            Px.Fill(buf, W, H, 6, y + 8, W - 12, 2, Px.C(0xFF1E3A1E));
            Px.Fill(buf, W, H, 6, y + 8, (int)((W - 12) * 0.62), 2, Px.C(0xFF7FE04A));
        }

        void Heart(int[] buf, int x, int y, int color)
        {
            Px.Fill(buf, W, H, x + 1, y, 2, 1, color); Px.Fill(buf, W, H, x + 4, y, 2, 1, color);
            Px.Fill(buf, W, H, x, y + 1, 7, 2, color);
            Px.Fill(buf, W, H, x + 1, y + 3, 5, 1, color);
            Px.Fill(buf, W, H, x + 2, y + 4, 3, 1, color);
            Px.Put(buf, W, H, x + 3, y + 5, color);
        }

        void Drumstick(int[] buf, int x, int y)
        {
            Px.Fill(buf, W, H, x + 1, y, 4, 3, Px.C(0xFFA0522D));
            Px.Fill(buf, W, H, x + 2, y + 3, 2, 1, Px.C(0xFF8B4513));
            Px.Put(buf, W, H, x + 1, y + 4, Px.C(0xFFE8E0C8));
            Px.Put(buf, W, H, x + 2, y + 4, Px.C(0xFFE8E0C8));
        }

        /// <summary>带半透明黑底的文字行（F3 风格）</summary>
        void TextLine(int[] buf, string s, int x, int y, int color)
        {
            int w = s.Length * 4 + 2;
            for (int yy = y - 1; yy < y + 6; yy++)
                for (int xx = x - 1; xx < x + w; xx++)
                    if (xx >= 0 && yy >= 0 && xx < W && yy < H)
                        buf[yy * W + xx] = Px.Blend(buf[yy * W + xx], Px.C(0xFF000000), 0.45);
            Px.Text(buf, W, H, s, x, y, color);
        }
    }
}
